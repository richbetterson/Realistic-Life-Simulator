// Realistic Life Simulator - NPC traffic behaviour.
// Every nearby ambient driver gets a personality (careful, average, aggressive, slow,
// professional, distracted) that sets its reaction ability, aggressiveness, driving style
// and target speed derived from the ACTUAL speed limit of the road it is on. Drivers make
// occasional believable mistakes, slow down for emergency vehicles with sirens, and
// collisions between NPCs are handed to the NPC accident system.
// Budget: only MaxNpcChecksPerUpdate vehicles are examined per update (performance profile).

using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public sealed class NpcTrafficModule : IModule
    {
        private sealed class DriverState
        {
            public Vehicle Vehicle;
            public DriverPersonality Personality;
            public PersonalityTraits Traits;
            public float Cruise;
            public int NextLimitCheck;
            public NpcMistake Mistake;
            public int MistakeUntil;
            public int MistakeStarted;
            public int YieldUntil;
            public float LastBody;
            public float LastSpeed;
            public int LastCheck;
        }

        private readonly RlsMain main;
        private readonly Random rng = new Random();
        private readonly Dictionary<int, DriverState> drivers = new Dictionary<int, DriverState>();
        private readonly List<int> scratch = new List<int>();
        private Vehicle[] candidates = new Vehicle[0];
        private int cursor;
        private int nextScan, nextSirenScan;
        private readonly int[] personalityCount = new int[6];

        public NpcTrafficModule(RlsMain main) { this.main = main; }

        public string Name { get { return "NpcTraffic"; } }
        public int IntervalMs { get { return Math.Min(500, main.Performance.Profile.NpcUpdateIntervalMs); } }
        public int Tracked { get { return drivers.Count; } }

        // Weather hook for Phase 8 (1 = dry).
        public float WeatherFactor = 1f;

        public void Start() { }

        public void Shutdown() { drivers.Clear(); }

        public string Summary()
        {
            Array.Clear(personalityCount, 0, personalityCount.Length);
            foreach (DriverState s in drivers.Values) personalityCount[(int)s.Personality]++;
            List<string> parts = new List<string>();
            for (int i = 0; i < personalityCount.Length; i++)
                if (personalityCount[i] > 0) parts.Add(NpcRules.Name((DriverPersonality)i) + " " + personalityCount[i]);
            return parts.Count == 0 ? "-" : string.Join(", ", parts.ToArray());
        }

        public DriverPersonality PersonalityOf(Vehicle v)
        {
            DriverState s;
            if (v != null && drivers.TryGetValue(v.Handle, out s)) return s.Personality;
            return DriverPersonality.Average;
        }

        // For police: did this NPC just do something a patrol would pull it over for?
        public bool RecentViolation(Vehicle v, out string reason)
        {
            reason = null;
            DriverState s;
            if (v == null || !drivers.TryGetValue(v.Handle, out s)) return false;
            if (s.Mistake == NpcMistake.RunRedLight) { reason = TrafficRules.Name(ViolationType.RedLight); return true; }
            if (s.Mistake == NpcMistake.SpeedingBurst) { reason = TrafficRules.Name(ViolationType.Speeding); return true; }
            return false;
        }

        public void Update()
        {
            RlsSettings set = main.Config.Settings;
            if (!set.NpcTraffic && !set.NpcAccidents) { drivers.Clear(); return; }
            Ped player = GTA.Game.Player.Character;
            if (player == null || !player.Exists()) return;
            PerformanceProfile prof = main.Performance.Profile;
            int now = Environment.TickCount;

            if (unchecked(now - nextScan) >= 0)
            {
                nextScan = now + 3000;
                candidates = World.GetNearbyVehicles(player.Position, prof.NpcFullSimRadius);
                cursor = 0;
                Prune(prof.MaxTrackedNpcs);
            }

            int budget = prof.MaxNpcChecksPerUpdate;
            while (budget > 0 && cursor < candidates.Length)
            {
                Vehicle v = candidates[cursor++];
                budget--;
                try { Examine(v, player, now, set); }
                catch (Exception ex) { Log.Error("NPC traffic check failed", ex); }
            }

            if (set.NpcTraffic && set.NpcYieldEmergency && unchecked(now - nextSirenScan) >= 0)
            {
                nextSirenScan = now + 2000;
                YieldToSirens(now);
            }
        }

        private void Examine(Vehicle v, Ped player, int now, RlsSettings set)
        {
            if (v == null || !v.Exists() || v.IsDead) return;
            int cls = (int)v.ClassType;
            if (cls == 13 || cls == 14 || cls == 15 || cls == 16 || cls == 18 || cls == 21) return;
            Ped d = v.Driver;
            if (!N.IsAmbientDriver(d) || N.IsMissionEntity(v)) return;

            DriverState s;
            if (!drivers.TryGetValue(v.Handle, out s))
            {
                if (drivers.Count >= main.Performance.Profile.MaxTrackedNpcs) return;
                s = new DriverState();
                s.Vehicle = v;
                s.Personality = NpcRules.Pick(rng, cls);
                s.Traits = NpcRules.Traits(s.Personality);
                s.LastBody = v.BodyHealth;
                s.LastSpeed = v.Speed;
                s.LastCheck = now;
                s.NextLimitCheck = now;
                drivers[v.Handle] = s;
                if (set.NpcTraffic) ApplyTraits(d, s);
            }

            float seconds = Math.Min(30f, unchecked(now - s.LastCheck) / 1000f);
            s.LastCheck = now;

            // Collision since the last look? Body damage is latched, so sparse checks still catch it.
            float body = v.BodyHealth;
            if (set.NpcAccidents && s.LastBody - body > 25f) DetectCollision(v, s, player);
            s.LastBody = body;

            if (!set.NpcTraffic) { s.LastSpeed = v.Speed; return; }

            if (s.Mistake != NpcMistake.None && unchecked(now - s.MistakeUntil) >= 0) EndMistake(d, s, now);

            if (unchecked(now - s.YieldUntil) >= 0 && unchecked(now - s.NextLimitCheck) >= 0)
            {
                s.NextLimitCheck = now + 6000 + rng.Next(2000);
                int limit = main.Traffic.LimitAt(v.Position);
                s.Cruise = NpcRules.CruiseSpeed(limit, s.Personality, WeatherFactor);
                float target = s.Mistake == NpcMistake.SpeedingBurst ? s.Cruise * 1.35f : s.Cruise;
                N.Call(N.SET_DRIVE_TASK_CRUISE_SPEED, d, target);
            }

            if (set.NpcMistakes && s.Mistake == NpcMistake.None && v.Speed > 3f)
            {
                NpcMistake m = NpcRules.RollMistake(rng, s.Personality, seconds);
                if (m != NpcMistake.None) StartMistake(d, s, m, now);
            }
            s.LastSpeed = v.Speed;
        }

        private static void ApplyTraits(Ped d, DriverState s)
        {
            N.Call(N.SET_DRIVER_ABILITY, d, s.Traits.Ability);
            N.Call(N.SET_DRIVER_AGGRESSIVENESS, d, s.Traits.Aggressiveness);
            N.Call(N.SET_DRIVE_TASK_DRIVING_STYLE, d, s.Traits.DrivingStyle);
        }

        private void StartMistake(Ped d, DriverState s, NpcMistake m, int now)
        {
            s.Mistake = m;
            s.MistakeStarted = now;
            switch (m)
            {
                case NpcMistake.RunRedLight:
                    N.Call(N.SET_DRIVE_TASK_DRIVING_STYLE, d, s.Traits.DrivingStyle & ~NpcRules.StopAtLights);
                    s.MistakeUntil = now + 12000;
                    break;
                case NpcMistake.SpeedingBurst:
                    N.Call(N.SET_DRIVE_TASK_CRUISE_SPEED, d, Math.Max(s.Cruise, 10f) * 1.35f);
                    s.MistakeUntil = now + 20000;
                    break;
                case NpcMistake.LateBraking:
                    N.Call(N.SET_DRIVER_ABILITY, d, 0.05f);
                    s.MistakeUntil = now + 15000;
                    break;
            }
            Log.Debug("NPC " + s.Personality + " mistake: " + m);
        }

        private static void EndMistake(Ped d, DriverState s, int now)
        {
            s.Mistake = NpcMistake.None;
            ApplyTraits(d, s);
            if (s.Cruise > 0) N.Call(N.SET_DRIVE_TASK_CRUISE_SPEED, d, s.Cruise);
        }

        // Cars ahead of an emergency vehicle with its siren on slow right down for a few seconds.
        private void YieldToSirens(int now)
        {
            foreach (Vehicle e in candidates)
            {
                if (e == null || !e.Exists() || (int)e.ClassType != 18 || e.Speed < 5f) continue;
                if (!N.Call<bool>(N.IS_VEHICLE_SIREN_ON, e)) continue;
                Vector3 fwd = e.ForwardVector;
                foreach (DriverState s in drivers.Values)
                {
                    Vehicle v = s.Vehicle;
                    if (v == null || !v.Exists() || unchecked(now - s.YieldUntil) < 0) continue;
                    Vector3 rel = v.Position - e.Position;
                    float along = rel.X * fwd.X + rel.Y * fwd.Y;
                    float side = Math.Abs(rel.X * fwd.Y - rel.Y * fwd.X);
                    Vector3 vf = v.ForwardVector;
                    if (along < 5f || along > 45f || side > 8f || vf.X * fwd.X + vf.Y * fwd.Y < 0.7f) continue;
                    Ped d = v.Driver;
                    if (!N.IsAmbientDriver(d)) continue;
                    N.Call(N.SET_DRIVE_TASK_CRUISE_SPEED, d, 2f);
                    s.YieldUntil = now + 5000;
                    s.NextLimitCheck = s.YieldUntil; // speed restored by the next limit check
                }
            }
        }

        private void DetectCollision(Vehicle v, DriverState s, Ped player)
        {
            Vehicle pv = player.CurrentVehicle ?? GTA.Game.Player.LastVehicle;
            foreach (Vehicle o in World.GetNearbyVehicles(v.Position, 7f))
            {
                if (o == null || !o.Exists() || o == v) continue;
                bool hit = N.Call<bool>(N.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, v, o, true)
                           || N.Call<bool>(N.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, o, v, true)
                           || N.Call<bool>(N.IS_ENTITY_TOUCHING_ENTITY, v, o);
                if (!hit) continue;
                if (o == pv || (o.Driver != null && o.Driver.Exists() && o.Driver.IsPlayer)) return; // player's accident system handles it
                if (N.Call<bool>(N.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, v, player, true)) return;
                N.Call(N.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, v);
                N.Call(N.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, o);
                DriverState os;
                float otherSpeed = drivers.TryGetValue(o.Handle, out os) ? os.LastSpeed : o.Speed;
                main.NpcAccidents.Report(v, o, s.LastSpeed * 3.6, otherSpeed * 3.6, s.LastBody - v.BodyHealth);
                drivers.Remove(v.Handle);
                drivers.Remove(o.Handle);
                return;
            }
        }

        private void Prune(int max)
        {
            scratch.Clear();
            Ped player = GTA.Game.Player.Character;
            float radius = main.Performance.Profile.NpcFullSimRadius * 1.3f;
            foreach (KeyValuePair<int, DriverState> kv in drivers)
            {
                Vehicle v = kv.Value.Vehicle;
                if (v == null || !v.Exists() || v.IsDead || player.Position.DistanceTo(v.Position) > radius) scratch.Add(kv.Key);
            }
            foreach (int h in scratch) drivers.Remove(h);
        }
    }
}
