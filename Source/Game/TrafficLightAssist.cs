// Realistic Life Simulator - shorter red-light waits for the player (0.6.0).
//
// GTA V's signal timing lives in the game's junction data and cannot be changed from a
// script. Instead, only when the player has been FIRST in line at a red light (with cross
// traffic flowing, so it really is red) for longer than MaxRedWaitSeconds:
//   1. the signal heads on the player's axis are set to green, the cross axis to red
//      (SET_ENTITY_TRAFFICLIGHT_OVERRIDE),
//   2. AI cars approaching on the cross axis are briefly held so the junction never has
//      green in every direction,
//   3. after AssistGreenSeconds the player's axis shows amber for 3 s, then every override
//      is released and GTA's own cycle continues.
// Nothing runs unless the player is stopped at a traffic-light node, so the cost is ~0.

using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public sealed class TrafficLightAssist
    {
        private const ulong SET_ENTITY_TRAFFICLIGHT_OVERRIDE = 0x57C5DB656185EAC4;
        private const int LightGreen = 0, LightRed = 1, LightAmber = 2, LightReset = 3;

        private enum Phase { None, Green, Amber }

        private static readonly Model[] LightModels = Models(
            "prop_traffic_01a", "prop_traffic_01b", "prop_traffic_01d", "prop_traffic_02a",
            "prop_traffic_02b", "prop_traffic_03a", "prop_traffic_03b", "prop_traffic_lightset_01");

        private static Model[] Models(params string[] names)
        {
            Model[] m = new Model[names.Length];
            for (int i = 0; i < names.Length; i++) m[i] = new Model(Joaat.Hash(names[i]));
            return m;
        }

        private readonly RlsMain main;
        private Phase phase = Phase.None;
        private int phaseSince;
        private int waitingSince = -1;
        private Vector3 waitPos;
        private bool crossSeen;
        private int nextCrossScan;
        private int nextHold;
        private int cooldownUntil;
        private Vector3 junction, axis;
        private readonly List<Prop> same = new List<Prop>();
        private readonly List<Prop> cross = new List<Prop>();

        public TrafficLightAssist(RlsMain main) { this.main = main; }

        // True while the player's direction has the assisted green / amber.
        public bool GreenForPlayer { get { return phase != Phase.None; } }
        public int Assists { get; private set; }

        public void Idle()
        {
            waitingSince = -1;
            if (phase != Phase.None)
            {
                Ped p = GTA.Game.Player.Character;
                if (p == null || !p.Exists() || N.Dist2D(p.Position, junction) > 60f) Reset();
            }
        }

        public void Update(Vehicle v, Vector3 pos, float kmh, int nodeFlags, int now)
        {
            RlsSettings set = main.Config.Settings;
            if (phase != Phase.None)
            {
                Step(v, pos, now, set);
                return;
            }
            if (!set.ShorterRedLights || unchecked(now - cooldownUntil) < 0) { waitingSince = -1; return; }

            bool atLight = (nodeFlags & TrafficRules.NodeTrafficLight) != 0;
            if (!atLight || kmh > 2f)
            {
                waitingSince = -1;
                return;
            }
            if (waitingSince < 0 || N.Dist2D(pos, waitPos) > 4f)
            {
                waitingSince = now;
                waitPos = pos;
                crossSeen = false;
                return;
            }
            if (unchecked(now - nextCrossScan) >= 0)
            {
                nextCrossScan = now + 1000;
                if (!crossSeen) crossSeen = CrossTrafficMoving(v, pos);
            }
            if (!crossSeen) return; // nothing crossing: not a red light, or nobody to wait for
            if (TrafficLightRules.ShouldAssist(true, unchecked(now - waitingSince), set.MaxRedWaitSeconds, FirstInLine(v), EmergencyNearby(pos)))
                Begin(v, pos, now);
        }

        private void Begin(Vehicle v, Vector3 pos, int now)
        {
            waitingSince = -1;
            Vector3 f = v.ForwardVector;
            float len = (float)Math.Sqrt(f.X * f.X + f.Y * f.Y);
            if (len < 0.1f) return;
            axis = new Vector3(f.X / len, f.Y / len, 0f);
            junction = pos + axis * 12f;
            same.Clear();
            cross.Clear();
            foreach (Prop pr in World.GetNearbyProps(junction, 32f, LightModels))
            {
                if (pr == null || !pr.Exists()) continue;
                Vector3 pf = pr.ForwardVector;
                float pl = (float)Math.Sqrt(pf.X * pf.X + pf.Y * pf.Y);
                if (pl < 0.1f) continue;
                LightAxis a = TrafficLightRules.Axis((pf.X * axis.X + pf.Y * axis.Y) / pl);
                if (a == LightAxis.Same) same.Add(pr);
                else if (a == LightAxis.Cross) cross.Add(pr);
            }
            if (same.Count == 0)
            {
                cooldownUntil = now + 30000; // no signal heads found: leave this junction alone
                Log.Debug("Traffic light assist: no signal heads found");
                return;
            }
            foreach (Prop pr in cross) N.Call(SET_ENTITY_TRAFFICLIGHT_OVERRIDE, pr, LightRed);
            foreach (Prop pr in same) N.Call(SET_ENTITY_TRAFFICLIGHT_OVERRIDE, pr, LightGreen);
            phase = Phase.Green;
            phaseSince = now;
            nextHold = now;
            Assists++;
            Log.Debug("Traffic light assist: green for the player (" + same.Count + " heads, " + cross.Count + " cross)");
        }

        private void Step(Vehicle v, Vector3 pos, int now, RlsSettings set)
        {
            int t = unchecked(now - phaseSince);
            if (N.Dist2D(pos, junction) > 60f) { Reset(); return; }
            if (unchecked(now - nextHold) >= 0)
            {
                nextHold = now + 500;
                HoldCrossTraffic(v);
            }
            if (phase == Phase.Green && t >= set.AssistGreenSeconds * 1000)
            {
                foreach (Prop pr in same) if (pr.Exists()) N.Call(SET_ENTITY_TRAFFICLIGHT_OVERRIDE, pr, LightAmber);
                phase = Phase.Amber;
                phaseSince = now;
            }
            else if (phase == Phase.Amber && t >= TrafficLightRules.AmberMs)
            {
                Reset();
            }
        }

        // Cross-axis AI cars approaching the junction wait until the assisted phase ends.
        private void HoldCrossTraffic(Vehicle player)
        {
            foreach (Vehicle o in World.GetNearbyVehicles(junction, 40f))
            {
                if (o == null || !o.Exists() || o == player || o.Speed < 1f) continue;
                Ped d = o.Driver;
                if (d == null || !d.Exists() || d.IsPlayer || N.IsMissionEntity(o)) continue;
                if ((int)o.ClassType == 18 && N.Call<bool>(N.IS_VEHICLE_SIREN_ON, o)) continue; // sirens always pass
                Vector3 of = o.ForwardVector;
                if (Math.Abs(of.X * axis.X + of.Y * axis.Y) > 0.5f) continue;  // same axis: may go
                Vector3 toJ = junction - o.Position;
                float dist = (float)Math.Sqrt(toJ.X * toJ.X + toJ.Y * toJ.Y);
                if (dist < 7f || dist > 32f) continue;                          // already in the junction / far away
                if ((toJ.X * of.X + toJ.Y * of.Y) / Math.Max(0.1f, dist) < 0.5f) continue; // moving away
                N.Call(N.TASK_VEHICLE_TEMP_ACTION, d, o, 6, 1200);
            }
        }

        private bool CrossTrafficMoving(Vehicle player, Vector3 pos)
        {
            Vector3 f = player.ForwardVector;
            foreach (Vehicle o in World.GetNearbyVehicles(pos + f * 12f, 25f))
            {
                if (o == null || !o.Exists() || o == player || o.Speed < 3f) continue;
                Vector3 of = o.ForwardVector;
                if (Math.Abs(of.X * f.X + of.Y * f.Y) < 0.45f) return true;
            }
            return false;
        }

        private static bool FirstInLine(Vehicle player)
        {
            Vector3 f = player.ForwardVector, pos = player.Position;
            foreach (Vehicle o in World.GetNearbyVehicles(pos, 14f))
            {
                if (o == null || !o.Exists() || o == player) continue;
                Vector3 rel = o.Position - pos;
                float along = rel.X * f.X + rel.Y * f.Y;
                float side = Math.Abs(rel.X * f.Y - rel.Y * f.X);
                if (along > 2f && along < 12f && side < 2.5f) return false;
            }
            return true;
        }

        private static bool EmergencyNearby(Vector3 pos)
        {
            foreach (Vehicle o in World.GetNearbyVehicles(pos, 60f))
                if (o != null && o.Exists() && (int)o.ClassType == 18 && N.Call<bool>(N.IS_VEHICLE_SIREN_ON, o)) return true;
            return false;
        }

        public void Reset()
        {
            foreach (Prop pr in same) try { if (pr.Exists()) N.Call(SET_ENTITY_TRAFFICLIGHT_OVERRIDE, pr, LightReset); } catch (Exception) { }
            foreach (Prop pr in cross) try { if (pr.Exists()) N.Call(SET_ENTITY_TRAFFICLIGHT_OVERRIDE, pr, LightReset); } catch (Exception) { }
            same.Clear();
            cross.Clear();
            if (phase != Phase.None) cooldownUntil = Environment.TickCount + 20000;
            phase = Phase.None;
            waitingSince = -1;
        }
    }
}
