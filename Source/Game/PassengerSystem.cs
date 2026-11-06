// Realistic Life Simulator - riding as a passenger with ordinary NPC drivers.
//
// The player asks a nearby NPC driver for a ride through the mouse menu (Life Hub >
// Rides and taxi, or the optional unassigned "Passenger menu" key). The driver decides
// from their attitude and the situation, then holds the car while the player walks to
// the chosen free passenger seat with the game's own enter animation. The NPC stays the
// driver and keeps following its own route; it changes route only if the player asks
// (drop me at my waypoint) and the driver agrees.
//
// Cost when idle: one state check every 500 ms. Nearby cars are only scanned when the
// menu page is built.

using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public sealed class RideCandidate
    {
        public Vehicle Vehicle;
        public Ped Driver;
        public float Distance;
        public List<int> Seats = new List<int>();
        public string Problem;   // null = can be asked
    }

    public sealed class PassengerSystem : IModule
    {
        private enum State { Idle, Entering, Riding }

        private const int EnterTimeoutMs = 20000;
        private const float AskRange = 14f;

        private readonly RlsMain main;
        private readonly Dictionary<int, int> refusedAt = new Dictionary<int, int>();
        private readonly Dictionary<int, PassengerAttitude> attitudes = new Dictionary<int, PassengerAttitude>();
        private State state;
        private int stateSince;
        private RideSession ride;
        private static readonly int TaxiModel = Joaat.Hash("taxi");

        public PassengerSystem(RlsMain main) { this.main = main; }

        public string Name { get { return "Passenger"; } }
        public int IntervalMs { get { return 500; } }
        public bool Riding { get { return state == State.Riding && ride != null; } }
        public bool Busy { get { return state != State.Idle; } }
        public RideSession Ride { get { return ride; } }

        public bool IsRideVehicle(Vehicle v)
        {
            return ride != null && v != null && ride.Vehicle == v;
        }

        public void Start() { }

        public void Shutdown()
        {
            if (ride != null) ride.Release(true, 0f);
            ride = null;
            state = State.Idle;
        }

        // ------------------------------------------------------------ asking for a ride

        public List<RideCandidate> NearbyCandidates()
        {
            List<RideCandidate> list = new List<RideCandidate>();
            Ped player = GTA.Game.Player.Character;
            if (player == null || !player.Exists()) return list;
            foreach (Vehicle v in World.GetNearbyVehicles(player.Position, AskRange))
            {
                if (v == null || !v.Exists() || v.Driver == null || !v.Driver.Exists()) continue;
                RideCandidate c = new RideCandidate();
                c.Vehicle = v;
                c.Driver = v.Driver;
                c.Distance = player.Position.DistanceTo(v.Position);
                c.Problem = Check(v, out c.Seats);
                list.Add(c);
            }
            list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            if (list.Count > 6) list.RemoveRange(6, list.Count - 6);
            return list;
        }

        // Null when this vehicle can be asked; otherwise why not.
        public string Check(Vehicle v, out List<int> seats)
        {
            seats = new List<int>();
            if (v == null || !v.Exists() || v.IsDead) return L.S("Vehicle is gone", "Машины уже нет");
            int cls = (int)v.ClassType;
            if (cls == 13 || cls == 14 || cls == 15 || cls == 16 || cls == 21) return L.S("Not a passenger vehicle", "Не для пассажиров");
            if (cls == 18 || cls == 19) return L.S("Emergency / military vehicle", "Спецтранспорт");
            if (v.Model.Hash == TaxiModel || (main.Taxi != null && main.Taxi.IsTaxiVehicle(v))) return L.S("A taxi: use the taxi service", "Это такси: вызовите через службу такси");
            Ped d = v.Driver;
            if (!N.IsAmbientDriver(d) || N.IsMissionEntity(v)) return L.S("Driver is busy", "Водитель занят");
            if (main.Dispatch.IsInUse(v) || (main.NpcAccidents != null && main.NpcAccidents.IsInvolved(v))) return L.S("Driver is busy", "Водитель занят");
            if (v.IsOnFire || !v.IsDriveable || v.EngineHealth < 200f || RideSession.UpsideDown(v)) return L.S("Vehicle is damaged", "Машина повреждена");
            if (N.Call<bool>(RideNatives.IS_PED_FLEEING, d) || d.IsInCombat) return L.S("Driver is panicking", "Водитель в панике");
            if (v.Speed > 9f) return L.S("Moving too fast", "Едет слишком быстро");
            seats = RideSession.FreePassengerSeats(v);
            if (seats.Count == 0) return L.S("No free seats", "Нет свободных мест");
            return null;
        }

        public PassengerAttitude AttitudeOf(Vehicle v, Ped d)
        {
            PassengerAttitude a;
            if (attitudes.TryGetValue(d.Handle, out a)) return a;
            DriverPersonality p = main.NpcTraffic != null ? main.NpcTraffic.PersonalityOf(v) : DriverPersonality.Average;
            a = PassengerRules.AttitudeFor(p, Seed(d));
            if (attitudes.Count > 64) attitudes.Clear();
            attitudes[d.Handle] = a;
            return a;
        }

        private static int Seed(Ped d)
        {
            return unchecked(d.Handle * 31 + d.Model.Hash);
        }

        // Returns a message for the menu when the request could not even be made.
        public string RequestRide(Vehicle v, int seat)
        {
            if (!main.Config.Settings.PassengerEnabled) return L.S("Passenger rides are switched off in Settings.", "Поездки пассажиром отключены в настройках.");
            if (state != State.Idle) return L.S("You are already riding with someone.", "Вы уже едете с кем-то.");
            if (main.Taxi != null && main.Taxi.Active) return L.S("You have a taxi booked. Cancel it first.", "У вас заказано такси. Сначала отмените его.");
            Ped player = GTA.Game.Player.Character;
            if (player == null || !player.Exists() || player.IsDead) return null;
            if (player.IsInVehicle()) return L.S("Get out of your current vehicle first.", "Сначала выйдите из машины.");
            if (N.InMission()) return L.S("Not available during a mission.", "Недоступно во время миссии.");

            List<int> seats;
            string problem = Check(v, out seats);
            if (problem != null) return problem;
            if (!PassengerRules.IsPassengerSeat(seat) || !seats.Contains(seat)) return L.S("That seat is taken.", "Это место занято.");
            if (player.Position.DistanceTo(v.Position) > AskRange) return L.S("Too far from the vehicle.", "Слишком далеко от машины.");

            Ped d = v.Driver;
            PassengerAttitude att = AttitudeOf(v, d);
            DriverPersonality pers = main.NpcTraffic != null ? main.NpcTraffic.PersonalityOf(v) : DriverPersonality.Average;
            int now = Environment.TickCount;

            RideContext ctx = new RideContext();
            ctx.Attitude = att;
            int hour = N.Call<int>(N.GET_CLOCK_HOURS);
            ctx.Night = hour >= 22 || hour < 6;
            ctx.PlayerWanted = N.Wanted() > 0;
            ctx.PlayerArmed = Armed(player);
            ctx.PlayerInjured = player.Health < Math.Max(100, player.MaxHealth) * 0.6f;
            ctx.Reputation = main.CurrentProfile().Reputation;
            ctx.VehicleClass = (int)v.ClassType;
            ctx.BadWeather = BadWeather();
            int last;
            ctx.AskedRecently = refusedAt.TryGetValue(d.Handle, out last) && unchecked(now - last) < 120000;
            ctx.VehicleMoving = v.Speed > 2f;

            RideRefusal why;
            if (!PassengerRules.Accepts(ctx, Seed(d), out why))
            {
                Refuse(v, d, att, why, now, ctx.AskedRecently);
                return null;
            }

            ride = new RideSession(main, v, d, seat, pers, att, Seed(d));
            ride.Take();
            N.Call(N.TASK_VEHICLE_TEMP_ACTION, d, v, 6, 30000); // wait for the passenger
            RideSession.Speech(d, "GENERIC_HI");
            PoliceStopSystem.Say(PassengerRules.AcceptLine(att));
            N.Call(N.TASK_ENTER_VEHICLE, player, v, EnterTimeoutMs, seat, 1.0f, 1, 0);
            state = State.Entering;
            stateSince = now;
            Log.Info(string.Format("Ride accepted: {0} driver ({1}) in {2}, seat {3}", att, pers, N.VehicleName(v), seat));
            return null;
        }

        private void Refuse(Vehicle v, Ped d, PassengerAttitude att, RideRefusal why, int now, bool repeated)
        {
            refusedAt[d.Handle] = now;
            if (refusedAt.Count > 40) refusedAt.Clear();
            main.Saves.Data.Passenger.Refusals++;
            PoliceStopSystem.Say(PassengerRules.RefuseLine(att, why));
            bool scared = why == RideRefusal.Wanted || why == RideRefusal.Armed;
            if (scared)
            {
                RideSession.Speech(d, "GENERIC_FRIGHTENED_HIGH");
                N.Call(N.TASK_VEHICLE_DRIVE_WANDER, d, v, 22f, N.StyleNormal); // drives off quickly (still obeys lights)
            }
            else if (att == PassengerAttitude.Unfriendly || repeated)
            {
                RideSession.Speech(d, "GENERIC_INSULT_HIGH");
                RideSession.Horn(v, 900);
            }
            else if (att == PassengerAttitude.Suspicious)
            {
                RideSession.Speech(d, "GENERIC_FRIGHTENED_MED");
                N.Call(N.TASK_VEHICLE_DRIVE_WANDER, d, v, 16f, N.StyleNormal);
            }
            else RideSession.Speech(d, "GENERIC_BYE");
            Log.Info("Ride refused: " + att + " driver, reason " + why);
        }

        private static bool Armed(Ped p)
        {
            const uint Unarmed = 0xA2719263;
            try { return (uint)p.Weapons.Current.Hash != Unarmed; }
            catch (Exception) { return false; }
        }

        private static bool BadWeather()
        {
            try
            {
                Weather w = World.Weather;
                return w == Weather.Raining || w == Weather.ThunderStorm || w == Weather.Snowing || w == Weather.Blizzard || w == Weather.Foggy;
            }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------ update

        public void Update()
        {
            if (state == State.Idle) return;
            Ped player = GTA.Game.Player.Character;
            if (player == null || !player.Exists()) return;
            int now = Environment.TickCount;
            if (player.IsDead) { End(L.S("Ride ended.", "Поездка окончена."), true); return; }

            if (state == State.Entering) UpdateEntering(player, now);
            else if (state == State.Riding) UpdateRiding(now);
        }

        private void UpdateEntering(Ped player, int now)
        {
            Vehicle v = ride.Vehicle;
            if (v == null || !v.Exists() || v.IsDead || !ride.DriverValid)
            {
                Abort(L.S("The driver left.", "Водитель уехал."));
                return;
            }
            if (player.IsInVehicle(v))
            {
                int seat = RideSession.SeatOf(v, player);
                if (seat == -1)
                {
                    // Should not happen (the driver is in that seat), but never let the ride continue from it.
                    Abort(L.S("Ride cancelled.", "Поездка отменена."));
                    return;
                }
                ride.Seat = seat;
                ride.Begin();
                ride.Drive();
                state = State.Riding;
                stateSince = now;
                main.Saves.Data.Passenger.Rides++;
                main.Notify(L.F("You're riding with a {0} driver. Ride options: Life Hub > Rides and taxi.",
                                "Вы едете с водителем ({0}). Действия: Центр управления > Поездки и такси.",
                                PassengerRules.AttitudeName(ride.Attitude).ToLowerInvariant()));
                return;
            }
            bool walkedAway = player.Position.DistanceTo(v.Position) > AskRange + 6f;
            if (walkedAway || unchecked(now - stateSince) > EnterTimeoutMs + 2000)
            {
                if (!walkedAway && !player.IsInVehicle()) N.Call(N.CLEAR_PED_TASKS, player); // stop a stuck enter attempt
                Abort(walkedAway ? null : L.S("Couldn't get in. The driver moved on.", "Не получилось сесть. Водитель поехал дальше."));
            }
        }

        private void UpdateRiding(int now)
        {
            RideEvent e = ride.Update(now);
            switch (e)
            {
                case RideEvent.PlayerLeft:
                    End(null, !ride.DriverWantsOut);
                    return;
                case RideEvent.TookWheel:
                    End(L.S("Ride over.", "Поездка окончена."), false);
                    return;
                case RideEvent.VehicleLost:
                    End(L.S("The vehicle was wrecked. Ride over.", "Машина разбита. Поездка окончена."), false);
                    return;
                case RideEvent.DriverLost:
                    // No forced exit: the player stays in the car and decides.
                    End(L.S("Your driver can't continue. You're on your own.", "Ваш водитель не может ехать дальше. Дальше вы сами."), false);
                    return;
                case RideEvent.AccidentScene:
                    main.Notify(L.S("~o~Your driver was in an accident.~s~ You can stay inside, or get out once the car has stopped.",
                                    "~o~Ваш водитель попал в ДТП.~s~ Можно остаться в машине или выйти, когда она остановится."));
                    return;
                case RideEvent.AccidentOver:
                    if (ride.CanContinue && !ride.DriverWantsOut)
                    {
                        PoliceStopSystem.Say(L.S("Sorry about that. Let's go.", "Извините за это. Поехали."));
                        ride.Continue();
                    }
                    else main.Notify(L.S("The car can't continue. Get out when you're ready.", "Машина дальше не поедет. Выходите, когда будете готовы."));
                    return;
                case RideEvent.SevereCrash:
                    main.Notify(L.S("The car is too damaged to continue. Get out when it has stopped.", "Машина слишком повреждена. Выходите, когда она остановится."));
                    return;
                case RideEvent.Arrived:
                    PoliceStopSystem.Say(L.S("Here we are. I'll pull over.", "Приехали. Сейчас остановлюсь."));
                    return;
                case RideEvent.Stopped:
                    if (ride.Arrived && !ride.DriverWantsOut)
                        main.Notify(L.S("You've arrived. Get out through Life Hub > Rides and taxi, or continue.", "Вы на месте. Выйти: Центр управления > Поездки и такси, или поехать дальше."));
                    return;
            }
            ride.WatchPassenger(now);
        }

        private void Abort(string message)
        {
            if (ride != null) ride.Release(true, 0f);
            ride = null;
            state = State.Idle;
            if (message != null) main.Notify(message);
        }

        private void End(string message, bool driveOn)
        {
            RideSession r = ride;
            ride = null;
            state = State.Idle;
            if (r == null) return;
            main.Saves.Data.Passenger.DistanceKm += r.Km;
            bool flee = r.OutReason.HasValue && r.OutReason.Value != RideSituation.VehicleOnFire;
            r.Release(driveOn || flee, flee ? 20f : 0f);
            string summary = L.F("Ride over: {0:0.0} km, {1} min.", "Поездка окончена: {0:0.0} км, {1} мин.", r.Km, r.MinutesSoFar);
            main.Notify(message != null ? message + "~n~" + summary : summary);
            Log.Info(string.Format("Ride ended: {0:0.00} km, attitude {1}", r.Km, r.Attitude));
        }

        // ------------------------------------------------------------ passenger actions (menu)

        public void RequestStop(bool urgent)
        {
            if (!Riding) return;
            RideSession r = ride;
            if (r.Phase == RidePhase.InAccident) { PoliceStopSystem.Say(L.S("Hold on, I'm dealing with this.", "Подожди, я тут разбираюсь.")); return; }
            if (r.Phase == RidePhase.Stopped) { r.StayStopped(); return; }
            float kmh = r.Vehicle.Speed * 3.6f;
            int limit = main.Traffic != null ? main.Traffic.LimitAt(r.Vehicle.Position) : 50;
            StopResponse resp = PassengerRules.OnStopRequest(r.Attitude, urgent, kmh, limit);
            PoliceStopSystem.Say(PassengerRules.StopLine(r.Attitude, resp));
            switch (resp)
            {
                case StopResponse.StopNow: r.StopNow(); break;
                case StopResponse.PullOverNow: r.PullOver(0); break;
                case StopResponse.PullOverLater: r.PullOver(PassengerRules.GrumbleDelayMs(r.Seed)); break;
            }
        }

        public void ContinueJourney()
        {
            if (!Riding) return;
            if (ride.Continue()) PoliceStopSystem.Say(L.S("Alright, moving on.", "Ладно, едем дальше."));
            else if (ride.DriverWantsOut) PoliceStopSystem.Say(PassengerRules.AskOutLine(ride.OutReason ?? RideSituation.Wanted));
            else main.Notify(L.S("The driver can't continue right now.", "Водитель сейчас не может ехать дальше."));
        }

        public void Exit()
        {
            if (!Riding) return;
            if (!ride.RequestExit())
                PoliceStopSystem.Say(L.S("Okay, let me stop first.", "Хорошо, сначала остановлюсь."));
        }

        public void AskForWaypoint()
        {
            if (!Riding) return;
            Vector3 wp;
            if (!RideSession.Waypoint(out wp)) { main.Notify(L.S("Set a waypoint on the map first.", "Сначала поставьте метку на карте.")); return; }
            if (ride.DriverWantsOut || !ride.CanContinue || ride.Phase == RidePhase.InAccident) return;
            double km = N.Dist2D(ride.Vehicle.Position, wp) / 1000.0 * TaxiRules.RoadFactor;
            if (!PassengerRules.AgreesToDetour(ride.Attitude, km))
            {
                PoliceStopSystem.Say(km > 4
                    ? L.S("That's way out of my way. I'm not a taxi.", "Это мне совсем не по пути. Я не такси.")
                    : L.S("Sorry, that's not on my way.", "Извините, мне не по пути."));
                return;
            }
            ride.SetDestination(RideSession.RoadPoint(wp));
            ride.Continue();
            PoliceStopSystem.Say(L.S("Sure, I can drop you there.", "Конечно, могу вас туда подбросить."));
        }

        public void FollowOwnRoute()
        {
            if (!Riding || !ride.HasDestination) return;
            ride.HasDestination = false;
            ride.Continue();
        }
    }
}
