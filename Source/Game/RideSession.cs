// Realistic Life Simulator - one journey as a passenger in an NPC-driven vehicle.
// Shared by ordinary rides (PassengerSystem) and the taxi service (TaxiService).
//
// The NPC always stays the driver. While the ride lasts the driver and the vehicle are
// marked persistent, which is how every other system of this mod recognises "taken over"
// entities (N.IsAmbientDriver / IsMissionEntity), so NPC traffic, NPC refuelling and NPC
// police stops leave this car alone instead of fighting over the driver's task.
// Crashes are handed to the existing NpcAccidentSystem (no second accident system); when
// its scene is over the driver is taken back and the journey continues.
// Safety: the player is never moved out of a moving vehicle, the automatic shuffle into
// the driver's seat is blocked for the ride, and every exit path restores the player.

using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public enum RidePhase { Riding, PullingOver, Stopped, InAccident }

    public enum RideEvent { None, PlayerLeft, TookWheel, VehicleLost, DriverLost, AccidentScene, AccidentOver, MinorCrash, SevereCrash, Stopped, Arrived }

    public static class RideNatives
    {
        public const ulong IS_VEHICLE_SEAT_FREE = 0x22AC59A870E6A669;
        public const ulong GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS = 0xA7C4F2C6E744A550;
        public const ulong GET_PED_IN_VEHICLE_SEAT = 0xBB40DD2270B65366;
        public const ulong SET_PED_CONFIG_FLAG = 0x1913FE4CBF41C463;
        public const ulong GET_PED_CONFIG_FLAG = 0x7EE53118C892B513;
        public const ulong PLAY_PED_AMBIENT_SPEECH_NATIVE = 0x8E04FEDD28D42462;
        public const ulong START_VEHICLE_HORN = 0x9C8C6504B5B63D2C;
        public const ulong GET_POINT_ON_ROAD_SIDE = 0x16F46FB18C8009E4; // GET_POSITION_BY_SIDE_OF_ROAD in newer SHVDN
        public const ulong IS_PED_SHOOTING = 0x34616828CD07F1A1;
        public const ulong IS_PLAYER_FREE_AIMING = 0x2E397FD2ECD37C87;
        public const ulong IS_PED_DOING_DRIVEBY = 0xB2C086CC1BF8F2BF;
        public const ulong IS_ENTITY_UPSIDEDOWN = 0x1DBD58820FA61D71;
        public const ulong IS_PED_FLEEING = 0xBBCCE00B381F8482;
        public const ulong GET_GROUND_Z_FOR_3D_COORD = 0xC906A7DAB05C8D2B;

        public const int FlagPreventShuffleToDriver = 184; // CPED_CONFIG_FLAG_PreventAutoShuffleToDriversSeat
    }

    public sealed class RideSession
    {
        public const int SafeStyle = NpcRules.Normal | NpcRules.UseBlinkers;

        private readonly RlsMain main;
        public readonly Vehicle Vehicle;
        public readonly Ped Driver;
        public int Seat;
        public readonly DriverPersonality Personality;
        public readonly PassengerAttitude Attitude;
        public readonly int Seed;

        public RidePhase Phase = RidePhase.Riding;
        public bool HasDestination;
        public Vector3 Destination;
        public float ArriveRadius = 35f;
        public bool Arrived;
        public bool CanContinue = true;      // false after a crash that leaves the car undriveable
        public bool DriverWantsOut;
        public RideSituation? OutReason;
        public bool ExitWhenStopped;
        public double Km;
        public double WaitingSeconds;
        public int StartedAt;
        public bool Begun;

        private int phaseSince;
        private Vector3 pullTarget;
        private bool pullBrakeOnly;
        private int pullAt = -1;             // delayed pull-over (unfriendly drivers)
        private int autoResumeAt = -1;       // short stop after a minor bump
        private int insistAt = -1;           // driver insists the passenger gets out (vehicle stopped)
        private float lastBody, lastEngine, lastSpeed;
        private Vector3 lastPos;
        private int lastTick;
        private int nextLimit;
        private float cruise = 12f;
        private int slowSince = -1;
        private int accidentSince;
        private bool savedFlag, flagSet;
        private readonly HashSet<RideSituation> warned = new HashSet<RideSituation>();
        private readonly HashSet<RideSituation> handled = new HashSet<RideSituation>();

        public RideSession(RlsMain main, Vehicle v, Ped d, int seat, DriverPersonality p, PassengerAttitude a, int seed)
        {
            this.main = main;
            Vehicle = v;
            Driver = d;
            Seat = seat;
            Personality = p;
            Attitude = a;
            Seed = seed;
            StartedAt = Environment.TickCount;
        }

        public bool DriverValid
        {
            get { return Driver != null && Driver.Exists() && !Driver.IsDead && Vehicle != null && Vehicle.Exists() && Driver.IsInVehicle(Vehicle); }
        }

        // ------------------------------------------------------------ control

        // Mark both as taken over (persistent + no ambient reactions) and drive safely.
        public void Take()
        {
            if (Vehicle != null && Vehicle.Exists()) Vehicle.IsPersistent = true;
            if (Driver == null || !Driver.Exists()) return;
            Driver.IsPersistent = true;
            N.Block(Driver);
            PersonalityTraits t = NpcRules.Traits(Personality);
            N.Call(N.SET_DRIVER_ABILITY, Driver, Math.Max(t.Ability, 0.85f));
            N.Call(N.SET_DRIVER_AGGRESSIVENESS, Driver, Math.Min(t.Aggressiveness, 0.35f));
        }

        // The player is seated: start the journey.
        public void Begin()
        {
            Ped player = GTA.Game.Player.Character;
            if (!flagSet)
            {
                savedFlag = N.Call<bool>(RideNatives.GET_PED_CONFIG_FLAG, player, RideNatives.FlagPreventShuffleToDriver, true);
                N.Call(RideNatives.SET_PED_CONFIG_FLAG, player, RideNatives.FlagPreventShuffleToDriver, true);
                flagSet = true;
            }
            Begun = true;
            StartedAt = Environment.TickCount;
            lastTick = StartedAt;
            lastPos = Vehicle.Position;
            lastBody = Vehicle.BodyHealth;
            lastEngine = Vehicle.EngineHealth;
            lastSpeed = Vehicle.Speed;
            UpdateCruise();
            Phase = RidePhase.Riding;
            phaseSince = StartedAt;
        }

        // (Re)issues the driving task: the driver's own route, or the agreed destination.
        public void Drive()
        {
            if (!DriverValid || Phase == RidePhase.InAccident) return;
            N.Call(N.CLEAR_PED_TASKS, Driver);
            if (HasDestination)
                N.Call(N.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE, Driver, Vehicle, Destination.X, Destination.Y, Destination.Z, cruise, SafeStyle, 10f);
            else
                N.Call(N.TASK_VEHICLE_DRIVE_WANDER, Driver, Vehicle, cruise, SafeStyle);
            N.Call(N.SET_DRIVE_TASK_CRUISE_SPEED, Driver, cruise);
            SetPhase(RidePhase.Riding);
            slowSince = -1;
        }

        public void SetDestination(Vector3 pos)
        {
            Destination = pos;
            HasDestination = true;
            Arrived = false;
        }

        public void Hold()
        {
            if (!DriverValid) return;
            N.Call(N.TASK_VEHICLE_TEMP_ACTION, Driver, Vehicle, 6, 600000);
        }

        // Stay parked until told otherwise (no traffic-wait time, no auto re-route).
        public void HoldHere()
        {
            Hold();
            pullAt = -1;
            autoResumeAt = -1;
            SetPhase(RidePhase.Stopped);
        }

        // The passenger asked to stay stopped: cancel the automatic restart after a bump.
        public void StayStopped()
        {
            autoResumeAt = -1;
        }

        // Brake in the lane (only used when the driver agrees it is safe, or in an emergency).
        public void StopNow()
        {
            if (!DriverValid || Phase == RidePhase.InAccident) return;
            pullAt = -1;
            pullBrakeOnly = true;
            pullTarget = Vehicle.Position;
            N.Call(N.TASK_VEHICLE_TEMP_ACTION, Driver, Vehicle, 6, 600000);
            SetPhase(RidePhase.PullingOver);
        }

        // Pull over at the roadside a little ahead. delayMs > 0: the driver does it later.
        public void PullOver(int delayMs)
        {
            if (!DriverValid || Phase == RidePhase.InAccident) return;
            if (Phase == RidePhase.Stopped) return;
            if (delayMs > 0) { pullAt = Environment.TickCount + delayMs; return; }
            pullAt = -1;
            Vector3 spot;
            if (Vehicle.Speed < 1f) { StopNow(); return; }
            if (RoadsideAhead(Vehicle, out spot))
            {
                pullBrakeOnly = false;
                pullTarget = spot;
                N.Call(N.TASK_VEHICLE_DRIVE_TO_COORD, Driver, Vehicle, spot.X, spot.Y, spot.Z,
                    Math.Min(9f, Math.Max(5f, Vehicle.Speed)), 0, Vehicle.Model.Hash, SafeStyle, 3f, 0f);
                SetPhase(RidePhase.PullingOver);
            }
            else StopNow();
        }

        // Continue after a stop. False when the driver will not (or cannot) carry on.
        public bool Continue()
        {
            if (!DriverValid || !CanContinue || DriverWantsOut || Phase == RidePhase.InAccident) return false;
            pullAt = -1;
            autoResumeAt = -1;
            ExitWhenStopped = false;
            if (HasDestination && Arrived) HasDestination = false;
            Drive();
            return true;
        }

        // Exit only when stopped; otherwise the driver pulls over first.
        public bool RequestExit()
        {
            if (Vehicle == null || !Vehicle.Exists()) return false;
            if (Vehicle.Speed < 1f)
            {
                LeaveNow();
                return true;
            }
            ExitWhenStopped = true;
            PullOver(0);
            return false;
        }

        private void LeaveNow()
        {
            Ped player = GTA.Game.Player.Character;
            if (Phase != RidePhase.InAccident && DriverValid && Phase != RidePhase.Stopped) { Hold(); SetPhase(RidePhase.Stopped); }
            N.Call(N.TASK_LEAVE_VEHICLE, player, Vehicle, 0);
        }

        // Ends the ride: restores the player and hands driver and vehicle back to the game.
        public void Release(bool driveOn, float speed)
        {
            try
            {
                Ped player = GTA.Game.Player.Character;
                if (flagSet && player != null && player.Exists())
                    N.Call(RideNatives.SET_PED_CONFIG_FLAG, player, RideNatives.FlagPreventShuffleToDriver, savedFlag);
                flagSet = false;
                if (Vehicle != null && Vehicle.Exists() && main.NpcAccidents != null && main.NpcAccidents.IsInvolved(Vehicle))
                    return; // the accident scene still owns them and releases them itself
                if (driveOn && DriverValid)
                {
                    N.Call(N.CLEAR_PED_TASKS, Driver);
                    N.Call(N.TASK_VEHICLE_DRIVE_WANDER, Driver, Vehicle, speed > 0 ? speed : Math.Max(cruise, 10f), N.StyleNormal);
                }
                N.Release(Driver);
                N.Release(Vehicle);
            }
            catch (Exception ex) { Log.Error("Ride release failed", ex); }
        }

        // ------------------------------------------------------------ update

        public RideEvent Update(int now)
        {
            Ped player = GTA.Game.Player.Character;
            if (Vehicle == null || !Vehicle.Exists()) return RideEvent.VehicleLost;
            if (!player.IsInVehicle(Vehicle)) return RideEvent.PlayerLeft;
            if (SeatOf(Vehicle, player) == -1) return RideEvent.TookWheel;
            if (Vehicle.IsDead) return RideEvent.VehicleLost;

            float dt = Math.Min(5f, Math.Max(0f, unchecked(now - lastTick) / 1000f));
            lastTick = now;
            Vector3 pos = Vehicle.Position;
            float moved = N.Dist2D(pos, lastPos);
            if (moved < 80f) Km += moved / 1000.0; // ignore teleports
            lastPos = pos;
            float speed = Vehicle.Speed;

            if (Phase == RidePhase.InAccident) return UpdateAccident(now);

            if (Driver == null || !Driver.Exists() || Driver.IsDead || !Driver.IsInVehicle(Vehicle) || SeatOf(Vehicle, Driver) != -1)
                return RideEvent.DriverLost;

            // Collision since the last look? Body damage is latched, so the 500 ms step still sees it.
            float body = Vehicle.BodyHealth, engine = Vehicle.EngineHealth;
            float drop = Math.Max(0f, lastBody - body) + Math.Max(0f, lastEngine - engine);
            float speedBefore = lastSpeed;
            lastBody = body;
            lastEngine = engine;
            lastSpeed = speed;
            if (drop > 20f)
            {
                RideEvent crash = OnCrash(speedBefore, drop, now);
                if (crash != RideEvent.None) return crash;
            }

            if (Phase == RidePhase.Riding)
            {
                if (speed < 1.5f) WaitingSeconds += dt;
                if (unchecked(now - nextLimit) >= 0) UpdateCruise();

                // Congestion: AI waits behind traffic on its own; if nothing has moved for a long
                // time (blocked junction, stuck behind a parked car) the route is re-issued.
                if (speed < 0.5f)
                {
                    if (slowSince < 0) slowSince = now;
                    else if (unchecked(now - slowSince) > 45000) { Log.Debug("Ride: driver stuck, re-issuing route"); Drive(); }
                }
                else slowSince = -1;

                if (pullAt >= 0 && unchecked(now - pullAt) >= 0) PullOver(0);

                if (HasDestination && !Arrived && N.Dist2D(pos, Destination) < ArriveRadius)
                {
                    Arrived = true;
                    PullOver(0);
                    return RideEvent.Arrived;
                }
            }
            else if (Phase == RidePhase.PullingOver)
            {
                int t = unchecked(now - phaseSince);
                bool there = pullBrakeOnly || N.Dist2D(pos, pullTarget) < 14f || t > 12000;
                if (speed < 0.6f && there)
                {
                    Hold();
                    SetPhase(RidePhase.Stopped);
                    return RideEvent.Stopped;
                }
                if (t > 30000) StopNow(); // could not reach the kerb: stop where it is
            }
            else if (Phase == RidePhase.Stopped)
            {
                if (ExitWhenStopped && speed < 1f)
                {
                    ExitWhenStopped = false;
                    N.Call(N.TASK_LEAVE_VEHICLE, player, Vehicle, 0);
                }
                if (autoResumeAt >= 0 && unchecked(now - autoResumeAt) >= 0)
                {
                    autoResumeAt = -1;
                    if (!ExitWhenStopped) Continue();
                }
                if (insistAt >= 0 && unchecked(now - insistAt) >= 0 && speed < 1f)
                {
                    insistAt = -1;
                    PoliceStopSystem.Say(L.S("I said get out!", "Я сказал - выходи!"));
                    N.Call(N.TASK_LEAVE_VEHICLE, player, Vehicle, 0);
                }
            }
            return RideEvent.None;
        }

        private RideEvent UpdateAccident(int now)
        {
            if (main.NpcAccidents != null && main.NpcAccidents.IsInvolved(Vehicle))
            {
                accidentSince = now;
                return RideEvent.None;
            }
            // Scene closed: wait for the driver to get back behind the wheel.
            if (Driver != null && Driver.Exists() && !Driver.IsDead && Driver.IsInVehicle(Vehicle) && SeatOf(Vehicle, Driver) == -1)
            {
                Take();
                lastBody = Vehicle.BodyHealth;
                lastEngine = Vehicle.EngineHealth;
                lastSpeed = Vehicle.Speed;
                CanContinue = Vehicle.IsDriveable && Vehicle.EngineHealth > 250f;
                Hold();
                SetPhase(RidePhase.Stopped);
                return RideEvent.AccidentOver;
            }
            if (Driver == null || !Driver.Exists() || Driver.IsDead || unchecked(now - accidentSince) > 45000) return RideEvent.DriverLost;
            return RideEvent.None;
        }

        private RideEvent OnCrash(float speedBefore, float drop, int now)
        {
            Vehicle other = OtherVehicleHit(Vehicle);
            double kmh = speedBefore * 3.6;
            if (other != null && main.NpcAccidents != null)
            {
                main.NpcAccidents.Report(Vehicle, other, kmh, other.Speed * 3.6, drop);
                if (main.NpcAccidents.IsInvolved(Vehicle))
                {
                    SetPhase(RidePhase.InAccident);
                    accidentSince = now;
                    pullAt = -1;
                    autoResumeAt = -1;
                    Log.Info("Ride: vehicle in an NPC accident scene (" + N.VehicleName(Vehicle) + " vs " + N.VehicleName(other) + ")");
                    return RideEvent.AccidentScene;
                }
            }

            // Not a two-car scene (wall, pole, pedestrian, scene limit reached): the driver
            // stops, reacts and carries on if the car still drives.
            if (kmh < main.Config.Settings.AccidentMinSpeedKmh && drop < 80f) return RideEvent.None; // parking bump
            bool severe = !Vehicle.IsDriveable || Vehicle.EngineHealth < 250f || drop > 300f;
            Ped victim = PedHit(Vehicle);
            string what = other != null ? N.VehicleName(other) : victim != null ? L.S("a pedestrian", "пешехода") : L.S("an obstacle", "препятствие");
            main.Saves.Data.AddIncident("Accident", main.Traffic.DescribeLocation(Vehicle.Position),
                L.F("The {0} you were riding in hit {1} at about {2:0} km/h.", "{0}, в которой вы ехали пассажиром, столкнулась с: {1} (около {2:0} км/ч).", N.VehicleName(Vehicle), what, kmh),
                severe ? L.S("Vehicle could not continue", "Машина не смогла продолжить путь") : L.S("Driver checked the car and drove on", "Водитель осмотрел машину и поехал дальше"));
            Speech(Driver, severe ? "GENERIC_SHOCKED_HIGH" : "GENERIC_CURSE_MED");
            StopNow();
            if (severe)
            {
                CanContinue = false;
                PoliceStopSystem.Say(L.S("Damn it! The car's wrecked. We're not going anywhere.", "Чёрт! Машина разбита. Дальше не поедем."));
                return RideEvent.SevereCrash;
            }
            autoResumeAt = now + 8000;
            PoliceStopSystem.Say(L.S("Whoa! Is everyone okay? ...Okay, let's keep going.", "Ох! Все целы? ...Ладно, едем дальше."));
            return RideEvent.MinorCrash;
        }

        // ------------------------------------------------------------ passenger behaviour

        // How the driver reacts to what the passenger does (wanted level, weapons, fire).
        public void WatchPassenger(int now)
        {
            if (Phase == RidePhase.InAccident || !DriverValid) return;
            Ped player = GTA.Game.Player.Character;
            RideSituation? s = null;
            if (Vehicle.IsOnFire) s = RideSituation.VehicleOnFire;
            else if (N.Call<bool>(RideNatives.IS_PED_SHOOTING, player)) s = RideSituation.Shooting;
            else if (N.Wanted() > 0) s = RideSituation.Wanted;
            else if (N.Call<bool>(RideNatives.IS_PLAYER_FREE_AIMING, GTA.Game.Player.Handle) || N.Call<bool>(RideNatives.IS_PED_DOING_DRIVEBY, player)) s = RideSituation.WeaponOut;

            if (s.HasValue && !handled.Contains(s.Value))
            {
                RideSituation sit = s.Value;
                DriverReaction r = PassengerRules.React(Attitude, sit, warned.Contains(sit));
                switch (r)
                {
                    case DriverReaction.Warn:
                        warned.Add(sit);
                        PoliceStopSystem.Say(PassengerRules.WarnLine(sit));
                        Speech(Driver, "GENERIC_FRIGHTENED_MED");
                        break;
                    case DriverReaction.PullOverAskOut:
                    case DriverReaction.StopNowAskOut:
                        handled.Add(sit);
                        DriverWantsOut = true;
                        OutReason = sit;
                        CanContinue = false;
                        PoliceStopSystem.Say(PassengerRules.AskOutLine(sit));
                        Speech(Driver, sit == RideSituation.VehicleOnFire ? "GENERIC_SHOCKED_HIGH" : "GENERIC_FRIGHTENED_HIGH");
                        if (r == DriverReaction.StopNowAskOut) StopNow(); else PullOver(0);
                        Log.Info("Ride: driver (" + Attitude + ") wants the passenger out: " + sit);
                        break;
                }
            }

            // Once stopped, a driver who wants the passenger out insists after a while.
            if (DriverWantsOut && Phase == RidePhase.Stopped && insistAt < 0)
                insistAt = now + (OutReason == RideSituation.VehicleOnFire ? 1500 : 10000);
        }

        // ------------------------------------------------------------ info

        public string StatusText()
        {
            switch (Phase)
            {
                case RidePhase.InAccident: return L.S("Dealing with an accident", "Разбирается с ДТП");
                case RidePhase.PullingOver: return L.S("Pulling over", "Останавливается у обочины");
                case RidePhase.Stopped:
                    if (DriverWantsOut) return L.S("Stopped - driver wants you out", "Стоит - водитель просит выйти");
                    if (!CanContinue) return L.S("Stopped - cannot continue", "Стоит - дальше не поедет");
                    return L.S("Stopped", "Стоит");
            }
            if (Vehicle != null && Vehicle.Exists() && Vehicle.Speed < 1.5f) return L.S("Waiting in traffic", "Стоит в потоке");
            return HasDestination ? L.S("Driving to the agreed stop", "Едет к оговорённому месту") : L.S("Driving its own route", "Едет своим маршрутом");
        }

        public int MinutesSoFar { get { return Math.Max(0, unchecked(Environment.TickCount - StartedAt) / 60000); } }

        // ------------------------------------------------------------ helpers

        private void SetPhase(RidePhase p)
        {
            Phase = p;
            phaseSince = Environment.TickCount;
        }

        private void UpdateCruise()
        {
            nextLimit = Environment.TickCount + 6000;
            if (Vehicle == null || !Vehicle.Exists()) return;
            int limit = main.Traffic != null ? main.Traffic.LimitAt(Vehicle.Position) : 50;
            float weather = main.NpcTraffic != null ? main.NpcTraffic.WeatherFactor : 1f;
            // Passengers on board: nobody drives above the limit.
            cruise = Math.Min(NpcRules.CruiseSpeed(limit, Personality, weather), Math.Max(4f, limit / 3.6f));
            if (Attitude == PassengerAttitude.Cautious) cruise *= 0.95f;
            if (DriverValid && Phase == RidePhase.Riding) N.Call(N.SET_DRIVE_TASK_CRUISE_SPEED, Driver, cruise);
        }

        public float Cruise { get { return cruise; } }

        public static int SeatOf(Vehicle v, Ped p)
        {
            if (v == null || !v.Exists() || p == null || !p.Exists()) return -2;
            int max = Math.Min(16, N.Call<int>(RideNatives.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, v));
            for (int s = -1; s < max; s++)
            {
                int h = N.Call<int>(RideNatives.GET_PED_IN_VEHICLE_SEAT, v, s, false);
                if (h == p.Handle) return s;
            }
            return -2;
        }

        public static List<int> FreePassengerSeats(Vehicle v)
        {
            List<int> list = new List<int>();
            int max = Math.Min(16, N.Call<int>(RideNatives.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, v));
            for (int s = 0; s < max; s++)
                if (N.Call<bool>(RideNatives.IS_VEHICLE_SEAT_FREE, v, s, false)) list.Add(s);
            return list;
        }

        public static void Speech(Ped p, string line)
        {
            try
            {
                if (p != null && p.Exists() && !p.IsDead)
                    N.Call(RideNatives.PLAY_PED_AMBIENT_SPEECH_NATIVE, p, line, "SPEECH_PARAMS_FORCE_NORMAL", 0);
            }
            catch (Exception) { }
        }

        public static void Horn(Vehicle v, int ms)
        {
            try { if (v != null && v.Exists()) N.Call(RideNatives.START_VEHICLE_HORN, v, ms, 0, false); }
            catch (Exception) { }
        }

        public static bool UpsideDown(Vehicle v)
        {
            return N.Call<bool>(RideNatives.IS_ENTITY_UPSIDEDOWN, v);
        }

        // Nearest drivable road point (for destinations and pick-ups).
        public static Vector3 RoadPoint(Vector3 pos)
        {
            OutputArgument o = new OutputArgument();
            if (N.Call<bool>(N.GET_CLOSEST_VEHICLE_NODE, pos.X, pos.Y, pos.Z, o, 1, 3f, 0))
            {
                Vector3 r = o.GetResult<Vector3>();
                if (r != Vector3.Zero) return r;
            }
            return pos;
        }

        // Waypoint on the map, with a ground height if the area is loaded.
        public static bool Waypoint(out Vector3 pos)
        {
            pos = Vector3.Zero;
            if (!GTA.Game.IsWaypointActive) return false;
            Vector3 w = World.WaypointPosition;
            float z = 40f;
            OutputArgument oz = new OutputArgument();
            if (N.Call<bool>(RideNatives.GET_GROUND_Z_FOR_3D_COORD, w.X, w.Y, 1000f, oz, false, false)) z = oz.GetResult<float>();
            pos = new Vector3(w.X, w.Y, z);
            return true;
        }

        // A kerb-side point some way ahead, so the driver has room to slow down.
        private static bool RoadsideAhead(Vehicle v, out Vector3 spot)
        {
            float ahead = 25f + v.Speed * 2.5f;
            Vector3 probe = v.Position + v.ForwardVector * ahead;
            OutputArgument o = new OutputArgument();
            if (N.Call<bool>(RideNatives.GET_POINT_ON_ROAD_SIDE, probe.X, probe.Y, probe.Z, 1, o))
            {
                spot = o.GetResult<Vector3>();
                Vector3 rel = spot - v.Position;
                if (spot != Vector3.Zero && rel.Length() < 120f && Vector3.Dot(rel, v.ForwardVector) > 5f) return true;
            }
            // Fallback: road node ahead, offset to the right-hand kerb.
            OutputArgument p = new OutputArgument(), h = new OutputArgument(), lanes = new OutputArgument();
            if (N.Call<bool>(N.GET_NTH_CLOSEST_VEHICLE_NODE_WITH_HEADING, probe.X, probe.Y, probe.Z, 1, p, h, lanes, 0, 3f, 0f))
            {
                Vector3 node = p.GetResult<Vector3>();
                Vector3 right = Vector3.Cross(v.ForwardVector, Vector3.WorldUp);
                right.Normalize();
                spot = node + right * 3.5f;
                if (Vector3.Dot(spot - v.Position, v.ForwardVector) > 5f) return true;
            }
            spot = Vector3.Zero;
            return false;
        }

        private static Vehicle OtherVehicleHit(Vehicle v)
        {
            Vehicle best = null;
            float bestD = 81f;
            foreach (Vehicle o in World.GetNearbyVehicles(v.Position, 9f))
            {
                if (o == null || !o.Exists() || o == v) continue;
                bool hit = N.Call<bool>(N.IS_ENTITY_TOUCHING_ENTITY, v, o)
                           || N.Call<bool>(N.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, o, v, true)
                           || N.Call<bool>(N.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, v, o, true);
                if (!hit) continue;
                float d = N.DistSq(o.Position, v.Position);
                if (d < bestD) { bestD = d; best = o; }
            }
            return best;
        }

        private static Ped PedHit(Vehicle v)
        {
            foreach (Ped ped in World.GetNearbyPeds(v.Position, 8f))
            {
                if (ped == null || !ped.Exists() || ped.IsPlayer || ped.IsInVehicle()) continue;
                if (N.Call<bool>(N.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, ped, v, true)) return ped;
            }
            return null;
        }
    }
}
