// Realistic Life Simulator - extra police patrols for a busier, more believable city (0.6.0).
//
// Budget first: at most 1-4 extra units (Settings: Police > PatrolDensity, capped by the
// performance profile). GTA's own ambient police within 300 m are counted, so no unit is
// created while enough police are already around. Every 15 s at most one unit is created,
// always 200-500 m away at a road node the player cannot see (never next to the player).
//
// Behaviours: cruising patrol, slow neighbourhood patrol (residential areas), stationary
// speed check at the roadside, and a two-officer foot patrol in the city by day.
// Units are released to the game (it cleans them up) when they are far away, out of sight,
// or have patrolled for a while. Patrol cars are ordinary police: they witness violations,
// pull the player over (handed to PoliceStopSystem) and can be sent to accidents (Dispatch).

using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public sealed class PolicePatrolSystem : IModule
    {
        private const ulong TASK_WANDER_STANDARD = 0xBB9CE077274F6A1B;
        private const ulong GET_SAFE_COORD_FOR_PED = 0xB61C8E878A4199CA;
        private const ulong GET_INTERIOR_FROM_ENTITY = 0x2107BA504071A6BB;

        private sealed class Unit
        {
            public PatrolBehavior Behavior;
            public Vehicle Vehicle;
            public Ped Driver, Partner;
            public int Created;
            public bool Rural;
        }

        private readonly RlsMain main;
        private readonly Random rng = new Random();
        private readonly List<Unit> units = new List<Unit>();
        private int nextSpawn;
        private int seed = Environment.TickCount;

        public PolicePatrolSystem(RlsMain main) { this.main = main; }

        public string Name { get { return "Patrols"; } }
        public int IntervalMs { get { return 2000; } }
        public int Count { get { return units.Count; } }
        public int Created { get; private set; }

        public void Start() { nextSpawn = Environment.TickCount + 20000; } // let the world settle first

        public void Shutdown()
        {
            foreach (Unit u in units) ReleaseUnit(u);
            units.Clear();
        }

        public bool Owns(Entity e)
        {
            if (e == null) return false;
            foreach (Unit u in units)
                if (u.Vehicle == e || u.Driver == e || u.Partner == e) return true;
            return false;
        }

        // A patrol car is taken over by another system (traffic stop, NPC stop, dispatch).
        // The patrol forgets it; the new owner takes responsibility for releasing it.
        public bool HandOver(Vehicle v)
        {
            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i];
                if (u.Vehicle != v) continue;
                units.RemoveAt(i);
                N.Release(u.Partner);  // the partner stays seated as an ambient officer
                return true;
            }
            return false;
        }

        // Nearest patrol car (with its driver) within radius, for Dispatch.
        public Vehicle Nearest(Vector3 target, float radius)
        {
            Vehicle best = null;
            float bestD = radius * radius;
            foreach (Unit u in units)
            {
                if (u.Behavior == PatrolBehavior.FootPatrol || u.Vehicle == null || !u.Vehicle.Exists() || u.Vehicle.IsDead) continue;
                if (u.Driver == null || !u.Driver.Exists() || u.Driver.IsDead || !u.Driver.IsInVehicle(u.Vehicle)) continue;
                float d = N.DistSq(u.Vehicle.Position, target);
                if (d < bestD) { bestD = d; best = u.Vehicle; }
            }
            return best;
        }

        public string Summary()
        {
            int cars = 0, foot = 0, traps = 0;
            foreach (Unit u in units)
            {
                if (u.Behavior == PatrolBehavior.FootPatrol) foot++;
                else { cars++; if (u.Behavior == PatrolBehavior.SpeedTrap) traps++; }
            }
            return L.F("{0} car(s) ({1} speed check), {2} foot patrol(s)", "машин: {0} (контроль скорости: {1}), пеших: {2}", cars, traps, foot);
        }

        // ------------------------------------------------------------ update

        public void Update()
        {
            int now = Environment.TickCount;
            Ped p = GTA.Game.Player.Character;
            if (p == null || !p.Exists()) return;
            Cleanup(p, now);

            RlsSettings set = main.Config.Settings;
            if (set.PolicePatrols == PoliceDensity.Off)
            {
                if (units.Count > 0) Shutdown();
                return;
            }
            if (unchecked(now - nextSpawn) < 0) return;
            nextSpawn = now + 15000;
            if (p.IsDead || N.InMission() || N.Wanted() > 0 || GTA.Game.IsPaused) return;
            if (N.Call<int>(GET_INTERIOR_FROM_ENTITY, p) != 0) return;            // inside a building
            Vehicle pv = p.CurrentVehicle;
            if (pv != null && pv.Exists())
            {
                int cls = (int)pv.ClassType;
                if (cls == 14 || cls == 15 || cls == 16) return;                   // boat / aircraft
            }

            Vector3 pos = p.Position;
            bool rural = main.Traffic != null && main.Traffic.IsRuralZone(pos);
            bool residential = main.Traffic != null && main.Traffic.IsResidentialZone(pos);
            int ambient = AmbientPoliceNearby(pos);
            if (!PatrolRules.ShouldSpawn(set.PolicePatrols, main.Performance.Profile.Kind, !rural, ambient, units.Count)) return;

            int hour = N.Call<int>(N.GET_CLOCK_HOURS);
            bool night = hour >= 22 || hour < 6;
            PatrolBehavior b = PatrolRules.PickBehavior(seed++, !rural, residential, night, set.PolicePatrols);
            try
            {
                Unit u = b == PatrolBehavior.FootPatrol ? SpawnFoot(pos, now) : SpawnCar(pos, b, rural, now);
                if (u != null)
                {
                    units.Add(u);
                    Created++;
                    Log.Debug("Patrol created: " + b + (rural ? " (county)" : ""));
                }
            }
            catch (Exception ex) { Log.Error("Patrol spawn failed", ex); }
        }

        private int AmbientPoliceNearby(Vector3 pos)
        {
            int n = 0;
            foreach (Vehicle v in World.GetNearbyVehicles(pos, 300f))
            {
                if (v == null || !v.Exists() || v.IsDead || (int)v.ClassType != 18) continue;
                Ped d = v.Driver;
                if (d == null || !d.Exists() || !N.IsCop(d) || Owns(v)) continue;
                n++;
            }
            return n;
        }

        private void Cleanup(Ped p, int now)
        {
            for (int i = units.Count - 1; i >= 0; i--)
            {
                Unit u = units[i];
                Entity anchor = u.Behavior == PatrolBehavior.FootPatrol ? (Entity)u.Driver : u.Vehicle;
                bool valid = anchor != null && anchor.Exists() && u.Driver != null && u.Driver.Exists() && !u.Driver.IsDead
                             && (u.Behavior == PatrolBehavior.FootPatrol || !u.Vehicle.IsDead);
                int age = unchecked(now - u.Created);
                float dist = valid ? N.Dist2D(anchor.Position, p.Position) : 9999f;
                bool far = dist > 450f && !anchor.IsOnScreen;
                bool done = age > 480000 && dist > 200f && !anchor.IsOnScreen;   // 8 min of patrolling
                if (!valid || far || done || N.Wanted() > 2)
                {
                    ReleaseUnit(u);
                    units.RemoveAt(i);
                    continue;
                }
                // A speed check lasts about 4 minutes, then the unit drives on.
                if (u.Behavior == PatrolBehavior.SpeedTrap && age > 240000 && u.Driver.IsInVehicle(u.Vehicle))
                {
                    u.Behavior = PatrolBehavior.Cruise;
                    N.Call(N.TASK_VEHICLE_DRIVE_WANDER, u.Driver, u.Vehicle, PatrolRules.CruiseSpeed(PatrolBehavior.Cruise), N.StyleNormal);
                }
            }
        }

        // ------------------------------------------------------------ spawning

        private Unit SpawnCar(Vector3 around, PatrolBehavior b, bool rural, int now)
        {
            Vector3 spawn;
            float heading;
            if (main.Dispatch == null || !main.Dispatch.FindSpawnNode(around, out spawn, out heading)) return null;
            if (b == PatrolBehavior.SpeedTrap)
            {
                // Park at the right-hand roadside, in the direction of traffic.
                double rad = heading * Math.PI / 180.0;
                Vector3 right = new Vector3((float)Math.Cos(rad), (float)Math.Sin(rad), 0f);
                spawn += right * 3.5f;
            }
            string[] cars = rural ? new[] { "sheriff", "sheriff2" } : new[] { "police", "police2", "police3" };
            string[] peds = rural ? new[] { "s_m_y_sheriff_01", "s_f_y_sheriff_01" } : new[] { "s_m_y_cop_01", "s_f_y_cop_01" };
            Model vm = new Model(cars[rng.Next(cars.Length)]);
            Model pm = new Model(peds[rng.Next(peds.Length)]);
            if (!vm.IsInCdImage || !pm.IsInCdImage || !vm.Request(1500) || !pm.Request(1500)) return null;
            Vehicle v = World.CreateVehicle(vm, spawn, heading);
            if (v == null || !v.Exists()) { vm.MarkAsNoLongerNeeded(); pm.MarkAsNoLongerNeeded(); return null; }
            Ped d = v.CreatePedOnSeat(VehicleSeat.Driver, pm);
            bool partner = main.Performance.Profile.Kind != PerformanceProfileKind.Low && rng.NextDouble() < 0.5;
            Ped p2 = partner ? v.CreatePedOnSeat(VehicleSeat.Passenger, pm) : null;
            vm.MarkAsNoLongerNeeded();
            pm.MarkAsNoLongerNeeded();
            if (d == null || !d.Exists()) { v.Delete(); return null; }

            Arm(d);
            if (p2 != null && p2.Exists()) Arm(p2);
            N.Call(N.SET_VEHICLE_ENGINE_ON, v, true, true, false);
            Unit u = new Unit();
            u.Behavior = b;
            u.Vehicle = v;
            u.Driver = d;
            u.Partner = p2;
            u.Created = now;
            u.Rural = rural;
            if (b == PatrolBehavior.SpeedTrap)
                N.Call(N.SET_PED_KEEP_TASK, d, true);     // sits and watches traffic
            else
            {
                N.Call(N.TASK_VEHICLE_DRIVE_WANDER, d, v, PatrolRules.CruiseSpeed(b), N.StyleNormal);
                N.Call(N.SET_PED_KEEP_TASK, d, true);
            }
            return u;
        }

        private Unit SpawnFoot(Vector3 around, int now)
        {
            Vector3 node;
            float heading;
            if (main.Dispatch == null || !main.Dispatch.FindSpawnNode(around, out node, out heading)) return null;
            OutputArgument safe = new OutputArgument();
            if (!N.Call<bool>(GET_SAFE_COORD_FOR_PED, node.X, node.Y, node.Z, true, safe, 16)) return null;
            Vector3 at = safe.GetResult<Vector3>();
            if (N.Dist2D(at, around) < 150f || N.Call<bool>(N.IS_SPHERE_VISIBLE, at.X, at.Y, at.Z, 2f)) return null;
            Model pm = new Model("s_m_y_cop_01");
            if (!pm.Request(1500)) return null;
            Ped a = World.CreatePed(pm, at, heading);
            Ped b = World.CreatePed(pm, at + new Vector3(1.2f, 0.6f, 0f), heading);
            pm.MarkAsNoLongerNeeded();
            if (a == null || !a.Exists()) { if (b != null && b.Exists()) b.Delete(); return null; }
            Arm(a);
            N.Call(TASK_WANDER_STANDARD, a, 10f, 10);
            N.Call(N.SET_PED_KEEP_TASK, a, true);
            if (b != null && b.Exists())
            {
                Arm(b);
                N.Call(N.TASK_GO_TO_ENTITY, b, a, -1, 2.0f, 1.0f, 0f, 0); // walks with the first officer
                N.Call(N.SET_PED_KEEP_TASK, b, true);
            }
            Unit u = new Unit();
            u.Behavior = PatrolBehavior.FootPatrol;
            u.Driver = a;
            u.Partner = b;
            u.Created = now;
            return u;
        }

        private static void Arm(Ped p)
        {
            try { p.Weapons.Give(WeaponHash.Pistol, 60, false, true); } catch (Exception) { }
        }

        private static void ReleaseUnit(Unit u)
        {
            N.Release(u.Driver);
            N.Release(u.Partner);
            N.Release(u.Vehicle);
        }
    }
}
