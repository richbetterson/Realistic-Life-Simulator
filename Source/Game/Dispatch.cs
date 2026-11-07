// Realistic Life Simulator - emergency responder dispatch.
// Police: first tries a nearby existing police car; otherwise creates one at a road node
// 200-500 m away that the player cannot see. Ambulances are always created that way.
// Responders always DRIVE in on the road network; nothing appears beside the player.

using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public enum UnitKind { Police, Ambulance }

    public sealed class Responder
    {
        public UnitKind Kind;
        public Vehicle Vehicle;
        public Ped Driver;
        public Ped Partner;
        public Blip Blip;
        public bool Created;       // we created it (vs. borrowed an ambient unit)
        public bool Urgent;
        public Vector3 Target;
        public int DispatchedAt;
        public string Name;        // "Officer J. Ramirez, badge 4721"

        public bool Valid
        {
            get { return Vehicle != null && Vehicle.Exists() && !Vehicle.IsDead && Driver != null && Driver.Exists() && !Driver.IsDead; }
        }
    }

    public sealed class Dispatch
    {
        private readonly RlsMain main;
        private readonly Random rng = new Random();
        private readonly List<Responder> active = new List<Responder>();

        public Dispatch(RlsMain main) { this.main = main; }

        public bool IsInUse(Entity e)
        {
            if (e == null) return false;
            foreach (Responder r in active)
                if (r.Vehicle == e || r.Driver == e || r.Partner == e) return true;
            return false;
        }

        public Responder SendPolice(Vector3 target, bool urgent)
        {
            Responder r = BorrowPolice(target);
            if (r == null)
            {
                bool county = target.Y > 1200f;
                r = Create(UnitKind.Police, county ? "sheriff" : "police", county ? "s_m_y_sheriff_01" : "s_m_y_cop_01", target, false);
            }
            if (r == null) return null;
            r.Name = OfficerName();
            Go(r, target, urgent);
            return r;
        }

        public Responder SendAmbulance(Vector3 target, bool urgent)
        {
            Responder r = Create(UnitKind.Ambulance, "ambulance", "s_m_m_paramedic_01", target, true);
            if (r == null) return null;
            r.Name = L.S("Paramedics", "Медики");
            Go(r, target, urgent);
            return r;
        }

        public string OfficerName()
        {
            string[] last = { "Ramirez", "Johnson", "Nguyen", "Walker", "Morales", "Bennett", "Kim", "Carter", "Ortiz", "Reed" };
            return L.F("Officer {0}. {1}, badge {2}", "Офицер {0}. {1}, жетон {2}",
                (char)('A' + rng.Next(26)), last[rng.Next(last.Length)], PoliceRules.Badge(rng));
        }

        // Adopt an existing police car (with driver) for a stop: used for the witnessing officer.
        public Responder Adopt(Vehicle v, Ped driver)
        {
            Responder r = new Responder();
            r.Kind = UnitKind.Police;
            r.Vehicle = v;
            r.Driver = driver;
            r.DispatchedAt = Environment.TickCount;
            r.Name = OfficerName();
            Prepare(r);
            active.Add(r);
            return r;
        }

        private Responder BorrowPolice(Vector3 target)
        {
            // A patrol unit of this mod nearby is the most natural responder.
            if (main.Patrols != null)
            {
                Vehicle pv = main.Patrols.Nearest(target, 350f);
                if (pv != null && !IsInUse(pv))
                {
                    Ped pd = pv.Driver;
                    main.Patrols.HandOver(pv);
                    if (pd != null && pd.Exists()) return Adopt(pv, pd);
                }
            }
            Vehicle best = null;
            float bestD = 350f * 350f;
            foreach (Vehicle v in World.GetNearbyVehicles(target, 350f))
            {
                if (v == null || !v.Exists() || v.IsDead || (int)v.ClassType != 18) continue;
                Ped d = v.Driver;
                if (d == null || !d.Exists() || d.IsDead || !N.IsCop(d) || IsInUse(v)) continue;
                if (N.IsMissionEntity(v) || N.IsMissionEntity(d)) continue;
                float dist = N.DistSq(v.Position, target);
                if (dist < bestD) { bestD = dist; best = v; }
            }
            if (best == null) return null;
            return Adopt(best, best.Driver);
        }

        private Responder Create(UnitKind kind, string vehModel, string pedModel, Vector3 target, bool partner)
        {
            Vector3 spawn;
            float heading;
            if (!FindSpawnNode(target, out spawn, out heading))
            {
                Log.Warn("Dispatch: no road node found for " + kind);
                return null;
            }
            Model vm = new Model(vehModel), pm = new Model(pedModel);
            if (!vm.Request(1500) || !pm.Request(1500))
            {
                Log.Warn("Dispatch: models not loaded for " + kind);
                return null;
            }
            Vehicle v = World.CreateVehicle(vm, spawn, heading);
            if (v == null || !v.Exists()) return null;
            Ped d = v.CreatePedOnSeat(VehicleSeat.Driver, pm);
            Ped p2 = partner ? v.CreatePedOnSeat(VehicleSeat.Passenger, pm) : null;
            vm.MarkAsNoLongerNeeded();
            pm.MarkAsNoLongerNeeded();
            if (d == null || !d.Exists()) { v.Delete(); return null; }

            Responder r = new Responder();
            r.Kind = kind;
            r.Vehicle = v;
            r.Driver = d;
            r.Partner = p2;
            r.Created = true;
            r.DispatchedAt = Environment.TickCount;
            Prepare(r);
            active.Add(r);
            Log.Info(string.Format("Dispatch: created {0} {1:0} m away", kind, N.Dist2D(spawn, target)));
            return r;
        }

        private void Prepare(Responder r)
        {
            r.Vehicle.IsPersistent = true;
            r.Driver.IsPersistent = true;
            N.Block(r.Driver);
            if (r.Partner != null && r.Partner.Exists())
            {
                r.Partner.IsPersistent = true;
                N.Block(r.Partner);
            }
            r.Blip = r.Vehicle.AddBlip();
            r.Blip.Color = r.Kind == UnitKind.Police ? BlipColor.Blue : BlipColor.Red;
            r.Blip.Scale = 0.7f;
            r.Blip.Name = r.Kind == UnitKind.Police ? L.S("Police", "Полиция") : L.S("Ambulance", "Скорая помощь");
        }

        public void Go(Responder r, Vector3 target, bool urgent)
        {
            r.Target = target;
            r.Urgent = urgent;
            N.Siren(r.Vehicle, urgent);
            N.Call(N.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE, r.Driver, r.Vehicle, target.X, target.Y, target.Z,
                urgent ? 24f : 14f, urgent ? N.StyleUrgent : N.StyleNormal, 18f);
        }

        public bool Arrived(Responder r)
        {
            if (!r.Valid) return false;
            float d = N.Dist2D(r.Vehicle.Position, r.Target);
            if (d < 25f && r.Vehicle.Speed < 2f) return true;
            // Stuck close by for a long time: treat as arrived and walk the rest.
            return d < 60f && unchecked(Environment.TickCount - r.DispatchedAt) > 120000;
        }

        public int EtaSeconds(Responder r)
        {
            if (!r.Valid) return -1;
            float d = N.Dist2D(r.Vehicle.Position, r.Target);
            return (int)(d / (r.Urgent ? 18f : 11f)) + 5;
        }

        // Back to the car (if out), drive away, then release everything to the game.
        public void SendAway(Responder r)
        {
            if (r == null) return;
            try
            {
                if (r.Blip != null && r.Blip.Exists()) r.Blip.Delete();
                r.Blip = null;
                if (r.Valid)
                {
                    N.Siren(r.Vehicle, false);
                    if (r.Driver.IsInVehicle(r.Vehicle))
                        N.Call(N.TASK_VEHICLE_DRIVE_WANDER, r.Driver, r.Vehicle, 14f, N.StyleNormal);
                    else
                        N.Call(N.TASK_ENTER_VEHICLE, r.Driver, r.Vehicle, 20000, -1, 1.0f, 1, 0);
                    if (r.Partner != null && r.Partner.Exists() && !r.Partner.IsDead && !r.Partner.IsInVehicle(r.Vehicle))
                        N.Call(N.TASK_ENTER_VEHICLE, r.Partner, r.Vehicle, 20000, 0, 1.0f, 1, 0);
                }
            }
            catch (Exception ex) { Log.Error("SendAway failed", ex); }
            leaving.Add(new KeyValuePair<Responder, int>(r, Environment.TickCount));
            active.Remove(r);
        }

        private readonly List<KeyValuePair<Responder, int>> leaving = new List<KeyValuePair<Responder, int>>();

        // Called every second: once crews are back in their vehicle, let them drive off and release.
        public void Update()
        {
            int now = Environment.TickCount;
            for (int i = leaving.Count - 1; i >= 0; i--)
            {
                Responder r = leaving[i].Key;
                bool timeout = unchecked(now - leaving[i].Value) > 40000;
                bool seated = r.Valid && r.Driver.IsInVehicle(r.Vehicle)
                              && (r.Partner == null || !r.Partner.Exists() || r.Partner.IsDead || r.Partner.IsInVehicle(r.Vehicle));
                if (!r.Valid || seated || timeout)
                {
                    if (r.Valid && seated) N.Call(N.TASK_VEHICLE_DRIVE_WANDER, r.Driver, r.Vehicle, 14f, N.StyleNormal);
                    N.Release(r.Driver);
                    N.Release(r.Partner);
                    N.Release(r.Vehicle);
                    leaving.RemoveAt(i);
                }
            }
        }

        public void ReleaseAll()
        {
            foreach (Responder r in active.ToArray()) SendAway(r);
            foreach (KeyValuePair<Responder, int> kv in leaving)
            {
                if (kv.Key.Blip != null && kv.Key.Blip.Exists()) kv.Key.Blip.Delete();
                N.Release(kv.Key.Driver);
                N.Release(kv.Key.Partner);
                N.Release(kv.Key.Vehicle);
            }
            leaving.Clear();
        }

        // Road node 200-500 m from target, preferably out of sight (also used by the taxi service
        // and the patrols).
        // 0.6.0 fix: the old search walked the Nth-closest nodes 30..220. In dense city areas the
        // 220th node is still closer than 200 m, and in the countryside the 30th is already past
        // 500 m, so "no road node found" was logged and no unit came. Now points on rings around
        // the target are snapped to the nearest road node instead (16 probes at most).
        private const ulong GET_CLOSEST_VEHICLE_NODE_WITH_HEADING = 0xFF071FB798B803B0;

        public bool FindSpawnNode(Vector3 target, out Vector3 pos, out float heading)
        {
            Vector3 fallback = Vector3.Zero;
            float fallbackHeading = 0f;
            float[] radii = { 260f, 340f, 420f, 200f };
            int start = rng.Next(8);
            foreach (float r in radii)
            {
                for (int k = 0; k < 8; k += 2)   // 4 directions per ring, rotated randomly
                {
                    double ang = (start + k + (r == 340f ? 1 : 0)) * Math.PI / 4.0;
                    Vector3 probe = new Vector3(target.X + (float)Math.Cos(ang) * r, target.Y + (float)Math.Sin(ang) * r, target.Z);
                    OutputArgument p = new OutputArgument(), h = new OutputArgument();
                    if (!N.Call<bool>(GET_CLOSEST_VEHICLE_NODE_WITH_HEADING, probe.X, probe.Y, probe.Z, p, h, 1, 3f, 0f)) continue;
                    Vector3 c = p.GetResult<Vector3>();
                    float d = N.Dist2D(c, target);
                    if (d < 180f || d > 550f || Math.Abs(c.Z - target.Z) > 120f) continue;
                    float hd = h.GetResult<float>();
                    if (fallback == Vector3.Zero) { fallback = c; fallbackHeading = hd; }
                    if (!N.Call<bool>(N.IS_SPHERE_VISIBLE, c.X, c.Y, c.Z, 4f))
                    {
                        pos = c;
                        heading = hd;
                        return true;
                    }
                }
            }
            // Old method as a last resort.
            for (int n = 30; n <= 220; n += 20)
            {
                OutputArgument p = new OutputArgument(), h = new OutputArgument(), lanes = new OutputArgument();
                if (!N.Call<bool>(N.GET_NTH_CLOSEST_VEHICLE_NODE_WITH_HEADING, target.X, target.Y, target.Z, n, p, h, lanes, 1, 3f, 0f))
                    continue;
                Vector3 c = p.GetResult<Vector3>();
                float d = N.Dist2D(c, target);
                if (d < 150f) continue;
                if (d > 600f) break;
                if (fallback == Vector3.Zero) { fallback = c; fallbackHeading = h.GetResult<float>(); }
                break;
            }
            pos = fallback;
            heading = fallbackHeading;
            return fallback != Vector3.Zero;
        }
    }
}
