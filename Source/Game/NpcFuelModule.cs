// Realistic Life Simulator - NPC fuel simulation.
// Nearby ambient drivers get a fuel level that drops while they drive. When it runs low,
// a few of them (limited by the performance profile) drive to the nearest gas station,
// step out, wait while "refuelling", get back in and continue wandering.
// Only a small number of NPCs is examined per update, and nothing is ever spawned.

using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public sealed class NpcFuelModule : IModule
    {
        private sealed class NpcFuel
        {
            public float Fraction;     // 0..1
            public float Rate;         // personal consumption factor
            public int LastTick;
            public bool Done;          // already refuelled this lifetime
        }

        private enum TripState { Driving, Leaving, Refuelling, Entering }

        private sealed class Trip
        {
            public Ped Driver;
            public Vehicle Vehicle;
            public GasStation Station;
            public TripState State;
            public int StateSince;
            public int RefuelMs;
        }

        // A full tank lasts about 25-60 minutes of NPC driving.
        private const float BaseDrainPerSecond = 1f / (40f * 60f);
        private const float LowThreshold = 0.12f;
        private const int GlobalCooldownMs = 45000;
        private const float MaxStationDistance = 900f;

        private readonly RlsMain main;
        private readonly FuelSystem fuel;
        private readonly Random rng = new Random();
        private readonly Dictionary<int, NpcFuel> records = new Dictionary<int, NpcFuel>();
        private readonly List<Trip> trips = new List<Trip>();
        private readonly List<int> scratch = new List<int>();
        private Vehicle[] candidates = new Vehicle[0];
        private int cursor;
        private int nextScan;
        private int nextTripAllowed;
        private int nextDistant;

        public NpcFuelModule(RlsMain main, FuelSystem fuel)
        {
            this.main = main;
            this.fuel = fuel;
        }

        public string Name { get { return "NpcFuel"; } }
        public int IntervalMs { get { return main.Performance.Profile.NpcUpdateIntervalMs; } }
        public int ActiveTrips { get { return trips.Count; } }
        public int Tracked { get { return records.Count; } }

        private int MaxTrips
        {
            get
            {
                switch (main.Performance.Profile.Kind)
                {
                    case PerformanceProfileKind.High: return 3;
                    case PerformanceProfileKind.Balanced: return 2;
                }
                return 1;
            }
        }

        public void Start() { }

        public void Update()
        {
            RlsSettings set = main.Config.Settings;
            if (!set.FuelEnabled || !set.NpcRefuelling)
            {
                if (trips.Count > 0) ReleaseAll();
                return;
            }

            Ped player = GTA.Game.Player.Character;
            if (player == null || !player.Exists()) return;
            PerformanceProfile prof = main.Performance.Profile;
            int now = Environment.TickCount;

            UpdateTrips(player, now);

            // Refresh the candidate list every few seconds (one native query), then examine
            // only a handful per update.
            if (unchecked(now - nextScan) >= 0)
            {
                nextScan = now + 3000;
                candidates = World.GetNearbyVehicles(player.Position, prof.NpcFullSimRadius);
                cursor = 0;
            }

            int budget = prof.MaxNpcChecksPerUpdate;
            while (budget > 0 && cursor < candidates.Length)
            {
                Vehicle v = candidates[cursor++];
                budget--;
                ExamineVehicle(v, player, now);
            }

            if (prof.DistantNpcSimulation && unchecked(now - nextDistant) >= 0)
            {
                nextDistant = now + prof.DistantSimIntervalMs;
                SimulateDistant(now);
            }

            if (records.Count > prof.MaxTrackedNpcs) PruneRecords(prof.MaxTrackedNpcs);
        }

        private void ExamineVehicle(Vehicle v, Ped player, int now)
        {
            if (v == null || !v.Exists() || v.IsDead) return;
            Ped d = v.Driver;
            if (d == null || !d.Exists() || d.IsDead || d == player || d.IsPlayer) return;
            if (N.IsMissionEntity(v) || N.IsMissionEntity(d) || N.IsCop(d)) return;
            int cls = (int)v.ClassType;
            if (cls == 18 || cls == 19 || cls == 21) return; // emergency, military, trains
            FuelSpec spec = FuelModel.ForVehicle(cls, v.Model.Hash);
            if (!spec.Supported || spec.Electric) return;

            NpcFuel f;
            if (!records.TryGetValue(v.Handle, out f))
            {
                f = new NpcFuel();
                f.Fraction = 0.08f + (float)rng.NextDouble() * 0.87f;
                f.Rate = 0.6f + (float)rng.NextDouble() * 1.0f; // careful vs. thirsty drivers
                f.LastTick = now;
                records[v.Handle] = f;
                return;
            }

            float dt = Math.Min(30f, unchecked(now - f.LastTick) / 1000f);
            f.LastTick = now;
            float moving = v.Speed > 1f ? 1f : 0.2f;
            f.Fraction = Math.Max(0f, f.Fraction - BaseDrainPerSecond * f.Rate * moving * dt * main.Config.Settings.FuelConsumption);

            if (f.Done || f.Fraction > LowThreshold) return;
            if (trips.Count >= MaxTrips || unchecked(now - nextTripAllowed) < 0) return;
            if (rng.NextDouble() > 0.5) return; // not every low driver reacts at once

            GasStation st = Nearest(v.Position);
            if (st == null) return;
            StartTrip(d, v, st, now);
            f.Done = true;
            f.Fraction = 1f; // they will refuel; keep the record consistent
        }

        // Cheap time-based drain for tracked vehicles that are no longer being examined.
        private void SimulateDistant(int now)
        {
            foreach (NpcFuel f in records.Values)
            {
                float dt = Math.Min(60f, unchecked(now - f.LastTick) / 1000f);
                if (dt < 5f) continue;
                f.LastTick = now;
                f.Fraction = Math.Max(0f, f.Fraction - BaseDrainPerSecond * f.Rate * 0.6f * dt);
            }
        }

        private void PruneRecords(int max)
        {
            scratch.Clear();
            foreach (KeyValuePair<int, NpcFuel> kv in records)
            {
                Entity e = Entity.FromHandle(kv.Key);
                if (e == null || !e.Exists()) scratch.Add(kv.Key);
            }
            foreach (int h in scratch) records.Remove(h);
            if (records.Count <= max) return;

            // Still too many: drop the ones not updated for the longest time.
            List<KeyValuePair<int, NpcFuel>> all = new List<KeyValuePair<int, NpcFuel>>(records);
            all.Sort((a, b) => a.Value.LastTick.CompareTo(b.Value.LastTick));
            for (int i = 0; i < all.Count - max; i++) records.Remove(all[i].Key);
        }

        private GasStation Nearest(Vector3 pos)
        {
            GasStation best = null;
            float bestD = MaxStationDistance * MaxStationDistance;
            foreach (GasStation s in fuel.Stations)
            {
                float d = N.DistSq(pos, s.Position);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        // ------------------------------------------------------------ trips

        private void StartTrip(Ped d, Vehicle v, GasStation st, int now)
        {
            Trip t = new Trip();
            t.Driver = d;
            t.Vehicle = v;
            t.Station = st;
            t.State = TripState.Driving;
            t.StateSince = now;
            t.RefuelMs = 15000 + rng.Next(20000);
            d.IsPersistent = true;
            v.IsPersistent = true;
            N.Call(N.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE, d, v, st.Position.X, st.Position.Y, st.Position.Z, 14f, 786603, 8f);
            trips.Add(t);
            nextTripAllowed = now + GlobalCooldownMs;
            Log.Debug("NPC heading to " + st.Name + " to refuel");
        }

        private void UpdateTrips(Ped player, int now)
        {
            for (int i = trips.Count - 1; i >= 0; i--)
            {
                Trip t = trips[i];
                if (!StepTrip(t, player, now))
                {
                    Release(t);
                    trips.RemoveAt(i);
                }
            }
        }

        // Returns false when the trip is over (finished or aborted).
        private bool StepTrip(Trip t, Ped player, int now)
        {
            Ped d = t.Driver;
            Vehicle v = t.Vehicle;
            if (d == null || v == null || !d.Exists() || !v.Exists() || d.IsDead || v.IsDead) return false;
            if (player.Position.DistanceTo(v.Position) > 350f) return false;          // left the area: let the game clean up
            if (v.Driver == player || player.CurrentVehicle == v) return false;        // player took the car
            int elapsed = unchecked(now - t.StateSince);

            switch (t.State)
            {
                case TripState.Driving:
                    if (elapsed > 180000) return false;
                    if (N.Dist2D(v.Position, t.Station.Position) < 14f && v.Speed < 1f)
                    {
                        N.Call(N.TASK_LEAVE_VEHICLE, d, v, 0);
                        SetState(t, TripState.Leaving, now);
                    }
                    return true;

                case TripState.Leaving:
                    if (!d.IsInVehicle())
                    {
                        N.Call(N.TASK_START_SCENARIO_IN_PLACE, d, "WORLD_HUMAN_STAND_IMPATIENT", 0, true);
                        SetState(t, TripState.Refuelling, now);
                    }
                    else if (elapsed > 8000) return false;
                    return true;

                case TripState.Refuelling:
                    if (elapsed >= t.RefuelMs)
                    {
                        N.Call(N.TASK_ENTER_VEHICLE, d, v, 20000, -1, 1.0f, 1, 0);
                        SetState(t, TripState.Entering, now);
                    }
                    return true;

                case TripState.Entering:
                    if (d.IsInVehicle(v))
                    {
                        N.Call(N.TASK_VEHICLE_DRIVE_WANDER, d, v, 14f, 786603);
                        main.Saves.Data.Driving.NpcRefuelsSeen++;
                        Log.Debug("NPC finished refuelling at " + t.Station.Name);
                        return false;
                    }
                    return elapsed < 25000;
            }
            return false;
        }

        private static void SetState(Trip t, TripState s, int now)
        {
            t.State = s;
            t.StateSince = now;
        }

        private static void Release(Trip t)
        {
            try
            {
                if (t.Driver != null && t.Driver.Exists())
                {
                    t.Driver.IsPersistent = false;
                    t.Driver.MarkAsNoLongerNeeded();
                }
                if (t.Vehicle != null && t.Vehicle.Exists())
                {
                    t.Vehicle.IsPersistent = false;
                    t.Vehicle.MarkAsNoLongerNeeded();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Releasing NPC failed", ex);
            }
        }

        private void ReleaseAll()
        {
            foreach (Trip t in trips) Release(t);
            trips.Clear();
        }

        public void Shutdown()
        {
            ReleaseAll();
            records.Clear();
        }
    }
}
