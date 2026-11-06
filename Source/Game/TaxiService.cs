// Realistic Life Simulator - taxi service (separate from ordinary passenger rides).
//
// Call a taxi from the menu (or the optional unassigned "Call a taxi" key). A free taxi
// nearby takes the job; otherwise the company sends one from a road 200-500 m away that
// you cannot see (the same off-screen spawn rule as police dispatch). It drives to you on
// the roads, waits at the kerb, you get in a rear seat (game animation), pick a
// destination, and it drives you there obeying the traffic rules. The meter counts
// distance actually driven plus waiting time in traffic; the fare uses the [Taxi] prices
// in Settings.ini. Payment comes from cash; whatever cash does not cover is added to the
// unpaid bills (Debt) that the property/ownership system already settles.

using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public sealed class TaxiDestination
    {
        public string Name;
        public Vector3 Position;
    }

    public sealed class TaxiService : IModule
    {
        private enum State { None, EnRoute, Waiting, Boarding, Choosing, Driving, Finished }

        private static readonly int TaxiModel = Joaat.Hash("taxi");
        private static readonly string[] DriverModels = { "a_m_m_eastsa_01", "a_m_y_stlat_01", "a_m_y_genstreet_01", "a_m_m_socenlat_01" };

        private readonly RlsMain main;
        private State state;
        private int since;
        private Vehicle taxi;
        private Ped driver;
        private Blip blip;
        private bool created;
        private Vector3 pickup;
        private int lastPickupCheck, slowSince = -1, lastRetask;
        private RideSession ride;
        private string destName;
        private int quote;
        private bool payOnStop;
        private int finishedAt;

        public TaxiService(RlsMain main) { this.main = main; }

        public string Name { get { return "Taxi"; } }
        public int IntervalMs { get { return 500; } }
        public bool Active { get { return state != State.None; } }
        public bool Waiting { get { return state == State.Waiting; } }
        public bool OnBoard { get { return state == State.Choosing || state == State.Driving || state == State.Finished; } }
        public bool Choosing { get { return state == State.Choosing; } }
        public bool Driving { get { return state == State.Driving; } }
        public RideSession Ride { get { return ride; } }
        public string DestinationName { get { return destName; } }
        public int Quote { get { return quote; } }

        public bool IsTaxiVehicle(Vehicle v)
        {
            return v != null && taxi != null && v == taxi;
        }

        public TaxiPricing Pricing
        {
            get
            {
                TaxiPricing p = new TaxiPricing();
                RlsSettings s = main.Config.Settings;
                p.BaseFare = s.TaxiBaseFare;
                p.PerKm = s.TaxiPricePerKm;
                p.PerMinuteWaiting = s.TaxiPricePerMinute;
                return p;
            }
        }

        public int MeterNow
        {
            get { return ride == null ? 0 : TaxiRules.Fare(Pricing, ride.Km, ride.WaitingSeconds / 60.0); }
        }

        public void Start() { }

        public void Shutdown()
        {
            Cleanup(true);
        }

        // ------------------------------------------------------------ booking

        public void Request()
        {
            if (!main.Config.Settings.TaxiEnabled) { main.Notify(L.S("The taxi service is switched off in Settings.", "Служба такси отключена в настройках.")); return; }
            if (state != State.None) { main.Notify(L.S("A taxi is already booked.", "Такси уже заказано.")); return; }
            if (main.Passenger != null && main.Passenger.Busy) { main.Notify(L.S("You are already riding with someone.", "Вы уже едете с кем-то.")); return; }
            Ped player = GTA.Game.Player.Character;
            if (player == null || !player.Exists() || player.IsDead) return;
            if (player.IsInVehicle()) { main.Notify(L.S("Get out of your vehicle to call a taxi.", "Чтобы вызвать такси, выйдите из машины.")); return; }
            if (N.InMission()) { main.Notify(L.S("Not available during a mission.", "Недоступно во время миссии.")); return; }
            if (N.Wanted() > 0) { main.Notify(L.S("Dispatcher: \"Sorry, no cars available.\" (Nobody comes while the police are after you.)", "Диспетчер: «Извините, свободных машин нет». (Пока вас ищет полиция, никто не приедет.)")); return; }

            pickup = RideSession.RoadPoint(player.Position);
            if (!Borrow(player.Position) && !Create(player.Position))
            {
                main.Notify(L.S("Dispatcher: \"Sorry, no taxi can reach you right now.\"", "Диспетчер: «Извините, сейчас ни одна машина не может к вам приехать»."));
                return;
            }
            taxi.IsPersistent = true;
            driver.IsPersistent = true;
            N.Block(driver);
            blip = taxi.AddBlip();
            blip.Color = BlipColor.Yellow;
            blip.Scale = 0.75f;
            blip.Name = L.S("Your taxi", "Ваше такси");
            GoToPickup();
            state = State.EnRoute;
            since = Environment.TickCount;
            int eta = (int)(N.Dist2D(taxi.Position, pickup) / 9f) + 10;
            main.Notify(L.F("Taxi booked. ETA about {0} min. It will stop at the nearest road.", "Такси заказано. Будет примерно через {0} мин. Остановится у ближайшей дороги.", Math.Max(1, (eta + 30) / 60)));
            Log.Info(string.Format("Taxi booked ({0}), {1:0} m away", created ? "created" : "nearby taxi", N.Dist2D(taxi.Position, pickup)));
        }

        // A free ambient taxi within 400 m takes the job.
        private bool Borrow(Vector3 near)
        {
            Vehicle best = null;
            float bestD = 400f * 400f;
            foreach (Vehicle v in World.GetNearbyVehicles(near, 400f))
            {
                if (v == null || !v.Exists() || v.IsDead || v.Model.Hash != TaxiModel) continue;
                Ped d = v.Driver;
                if (!N.IsAmbientDriver(d) || N.IsMissionEntity(v) || main.Dispatch.IsInUse(v)) continue;
                if (main.NpcAccidents != null && main.NpcAccidents.IsInvolved(v)) continue;
                if (RideSession.FreePassengerSeats(v).Count < 3) continue; // already carrying someone
                if (!v.IsDriveable || v.EngineHealth < 300f) continue;
                float dist = N.DistSq(v.Position, near);
                if (dist < bestD) { bestD = dist; best = v; }
            }
            if (best == null) return false;
            taxi = best;
            driver = best.Driver;
            created = false;
            return true;
        }

        private bool Create(Vector3 near)
        {
            Vector3 spawn;
            float heading;
            if (!main.Dispatch.FindSpawnNode(near, out spawn, out heading)) { Log.Warn("Taxi: no road node to send a taxi from"); return false; }
            Model vm = new Model(TaxiModel);
            Model pm = null;
            foreach (string name in DriverModels)
            {
                Model m = new Model(name);
                if (m.IsInCdImage) { pm = m; break; }
            }
            if (pm == null || !vm.IsInCdImage || !vm.Request(1500) || !pm.Request(1500)) { Log.Warn("Taxi: models not loaded"); return false; }
            Vehicle v = World.CreateVehicle(vm, spawn, heading);
            if (v == null || !v.Exists()) return false;
            Ped d = v.CreatePedOnSeat(VehicleSeat.Driver, pm);
            vm.MarkAsNoLongerNeeded();
            pm.MarkAsNoLongerNeeded();
            if (d == null || !d.Exists()) { v.Delete(); return false; }
            taxi = v;
            driver = d;
            created = true;
            return true;
        }

        private void GoToPickup()
        {
            N.Call(N.CLEAR_PED_TASKS, driver);
            N.Call(N.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE, driver, taxi, pickup.X, pickup.Y, pickup.Z, 14f, RideSession.SafeStyle, 12f);
            lastRetask = Environment.TickCount;
        }

        public void Cancel()
        {
            if (state == State.EnRoute || state == State.Waiting || state == State.Boarding)
            {
                Cleanup(true);
                main.Notify(L.S("Taxi cancelled.", "Такси отменено."));
            }
        }

        // ------------------------------------------------------------ boarding and destination

        public void GetIn()
        {
            if (state != State.Waiting || !Valid) return;
            Ped player = GTA.Game.Player.Character;
            if (player.IsInVehicle()) return;
            List<int> seats = RideSession.FreePassengerSeats(taxi);
            int seat = seats.Contains(1) ? 1 : seats.Contains(2) ? 2 : seats.Count > 0 ? seats[0] : -2;
            if (seat < 0) { main.Notify(L.S("No free seat in this taxi.", "В этом такси нет свободных мест.")); return; }
            N.Call(N.TASK_ENTER_VEHICLE, player, taxi, 20000, seat, 1.0f, 1, 0);
            state = State.Boarding;
            since = Environment.TickCount;
        }

        public List<TaxiDestination> Destinations()
        {
            List<TaxiDestination> list = new List<TaxiDestination>();
            Vector3 wp;
            if (RideSession.Waypoint(out wp)) list.Add(Dest(L.S("Map waypoint", "Метка на карте"), wp));
            if (main.Properties != null)
                foreach (PropertyDef d in main.Properties.Defs)
                    if (main.Properties.OwnedByMe(d)) list.Add(Dest(d.Name, new Vector3(d.X, d.Y, d.Z)));
            return list;
        }

        private static TaxiDestination Dest(string name, Vector3 pos)
        {
            TaxiDestination t = new TaxiDestination();
            t.Name = name;
            t.Position = pos;
            return t;
        }

        public int EstimateTo(Vector3 pos)
        {
            if (!Valid) return 0;
            return TaxiRules.Estimate(Pricing, N.Dist2D(taxi.Position, pos) / 1000.0) + (ride != null ? MeterNow - TaxiRules.Fare(Pricing, 0, 0) : 0);
        }

        public void SetDestination(TaxiDestination d)
        {
            if (ride == null || (state != State.Choosing && state != State.Driving)) return; // a paid trip is over
            if (ride.Phase == RidePhase.InAccident || !ride.CanContinue || ride.DriverWantsOut) return;
            destName = d.Name;
            quote = EstimateTo(d.Position);
            ride.SetDestination(RideSession.RoadPoint(d.Position));
            ride.ArriveRadius = 30f;
            ride.Continue();
            payOnStop = false;
            state = State.Driving;
            PoliceStopSystem.Say(L.F("{0}? No problem. About {1}.", "{0}? Без проблем. Примерно {1}.", d.Name, L.Money(quote)));
        }

        // Pull over now and pay what the meter shows.
        public void StopHere()
        {
            if (ride == null || state != State.Driving) return;
            payOnStop = true;
            PoliceStopSystem.Say(L.S("Sure, I'll pull over here.", "Конечно, остановлюсь здесь."));
            ride.PullOver(0);
        }

        public void Exit()
        {
            if (ride == null) return;
            if (state == State.Driving && ride.Vehicle.Speed >= 1f)
            {
                payOnStop = true;
                ride.ExitWhenStopped = true;
                ride.PullOver(0);
                PoliceStopSystem.Say(L.S("Okay, pulling over.", "Хорошо, останавливаюсь."));
                return;
            }
            ride.RequestExit();
        }

        // ------------------------------------------------------------ update

        private bool Valid
        {
            get { return taxi != null && taxi.Exists() && !taxi.IsDead && driver != null && driver.Exists() && !driver.IsDead; }
        }

        public void Update()
        {
            if (state == State.None) return;
            int now = Environment.TickCount;
            Ped player = GTA.Game.Player.Character;
            if (player == null || !player.Exists()) return;
            try
            {
                switch (state)
                {
                    case State.EnRoute: UpdateEnRoute(player, now); break;
                    case State.Waiting:
                    case State.Boarding: UpdateWaiting(player, now); break;
                    default: UpdateOnBoard(player, now); break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Taxi update failed; ending the booking", ex);
                Cleanup(true);
            }
        }

        private void UpdateEnRoute(Ped player, int now)
        {
            if (!Valid || player.IsDead) { Fail(L.S("Your taxi didn't make it. Booking cancelled.", "Ваше такси не доехало. Заказ отменён.")); return; }
            if (N.Wanted() > 0) { Fail(L.S("Dispatcher: \"The driver has turned back.\" (The police are after you.)", "Диспетчер: «Водитель развернулся». (Вас ищет полиция.)")); return; }
            if (main.NpcAccidents != null && main.NpcAccidents.IsInvolved(taxi)) { since = now; return; } // waits for the scene

            // The player walked off: follow to the new nearest road (checked every 8 s).
            if (unchecked(now - lastPickupCheck) > 8000)
            {
                lastPickupCheck = now;
                if (N.Dist2D(player.Position, pickup) > 60f) { pickup = RideSession.RoadPoint(player.Position); GoToPickup(); }
            }

            float d = N.Dist2D(taxi.Position, pickup);
            if (d < 20f && taxi.Speed < 2f)
            {
                N.Call(N.TASK_VEHICLE_TEMP_ACTION, driver, taxi, 6, 600000);
                RideSession.Horn(taxi, 500);
                state = State.Waiting;
                since = now;
                main.Notify(L.S("~y~Your taxi is here.~s~ Get in: Life Hub > Rides and taxi > Get in the taxi (or walk up and enter a rear seat).",
                                "~y~Ваше такси приехало.~s~ Сесть: Центр управления > Поездки и такси > Сесть в такси (или подойдите и сядьте назад)."));
                return;
            }

            // Stuck in traffic for long: re-issue the route; give up after 5 minutes.
            if (taxi.Speed < 0.5f)
            {
                if (slowSince < 0) slowSince = now;
                else if (unchecked(now - slowSince) > 25000 && unchecked(now - lastRetask) > 25000) { GoToPickup(); slowSince = now; }
            }
            else slowSince = -1;
            if (unchecked(now - since) > 300000) Fail(L.S("Dispatcher: \"Sorry, the driver couldn't get through.\"", "Диспетчер: «Извините, водитель не смог проехать»."));
        }

        private void UpdateWaiting(Ped player, int now)
        {
            if (!Valid) { Fail(L.S("Your taxi is gone. Booking cancelled.", "Ваше такси уехало. Заказ отменён.")); return; }
            if (player.IsInVehicle(taxi))
            {
                int seat = RideSession.SeatOf(taxi, player);
                if (seat == -1 || !driver.IsInVehicle(taxi))
                {
                    // Took the driver's seat (car-jacked the taxi): the booking is over.
                    Cleanup(false);
                    main.Notify(L.S("You took the taxi's wheel. Booking cancelled.", "Вы сели за руль такси. Заказ отменён."));
                    return;
                }
                ride = new RideSession(main, taxi, driver, seat, DriverPersonality.Professional, PassengerAttitude.Neutral, unchecked(driver.Handle * 31));
                ride.Take();
                ride.Begin();
                ride.HoldHere(); // parked while the destination is chosen (meter shows base fare only)
                state = State.Choosing;
                since = now;
                PoliceStopSystem.Say(L.S("Hi! Where to?", "Здравствуйте! Куда едем?"));
                RideSession.Speech(driver, "GENERIC_HI");
                main.OpenRideMenu();
                return;
            }
            if (state == State.Boarding && unchecked(now - since) > 22000)
            {
                if (!player.IsInVehicle()) N.Call(N.CLEAR_PED_TASKS, player);
                state = State.Waiting;
                since = now;
            }
            if (state == State.Waiting && unchecked(now - since) > 150000)
                Fail(L.S("Your taxi waited and left.", "Такси подождало и уехало."));
        }

        private void UpdateOnBoard(Ped player, int now)
        {
            RideEvent e = ride.Update(now);
            switch (e)
            {
                case RideEvent.PlayerLeft:
                    if (state == State.Driving || (state == State.Finished && !paid)) Pay(L.S("partial trip", "часть пути"));
                    Finish(state == State.Choosing ? L.S("You left the taxi.", "Вы вышли из такси.") : null);
                    return;
                case RideEvent.TookWheel:
                case RideEvent.VehicleLost:
                case RideEvent.DriverLost:
                    Finish(L.S("The taxi ride was interrupted. No charge.", "Поездка на такси прервана. Платить не нужно."));
                    return;
                case RideEvent.SevereCrash:
                    main.Notify(L.S("The taxi is too damaged to continue. No charge - get out when it has stopped.", "Такси слишком повреждено. Платить не нужно - выходите, когда оно остановится."));
                    paid = true; // nothing is charged for this trip
                    state = State.Finished;
                    finishedAt = now;
                    return;
                case RideEvent.AccidentScene:
                    main.Notify(L.S("~o~Your taxi was in an accident.~s~ The meter is paused.", "~o~Ваше такси попало в ДТП.~s~ Счётчик остановлен."));
                    return;
                case RideEvent.AccidentOver:
                    if (ride.CanContinue && ride.HasDestination && state == State.Driving)
                    {
                        PoliceStopSystem.Say(L.S("Sorry about that. Let's get you there.", "Извините. Едем дальше."));
                        ride.Continue();
                    }
                    else if (!ride.CanContinue)
                    {
                        paid = true;
                        state = State.Finished;
                        finishedAt = now;
                        main.Notify(L.S("The taxi can't continue. No charge.", "Такси дальше не поедет. Платить не нужно."));
                    }
                    return;
                case RideEvent.Arrived:
                    PoliceStopSystem.Say(L.S("Here we are.", "Приехали."));
                    return;
                case RideEvent.Stopped:
                    if (state == State.Driving && (ride.Arrived || payOnStop))
                    {
                        Pay(ride.Arrived ? destName : L.S("stopped on request", "остановка по просьбе"));
                        state = State.Finished;
                        finishedAt = now;
                        autoExit = ride.Arrived && !ride.ExitWhenStopped;
                    }
                    return;
            }

            if (state == State.Driving && ride.DriverWantsOut && ride.Phase == RidePhase.Stopped && !paid)
            {
                Pay(L.S("trip ended by the driver", "поездка прервана водителем"));
                state = State.Finished;
                finishedAt = now;
            }

            // Arrived at the destination and paid: the passenger gets out of the stopped taxi.
            if (state == State.Finished && autoExit && unchecked(now - finishedAt) > 3500 && ride.Vehicle.Speed < 1f)
            {
                autoExit = false; // once
                ride.RequestExit();
            }
            ride.WatchPassenger(now);
        }

        private bool paid;
        private bool autoExit;

        private void Pay(string what)
        {
            if (paid || ride == null) return;
            paid = true;
            int fare = MeterNow;
            int cash = GTA.Game.Player.Money;
            int paidNow, debt;
            TaxiRules.Settle(fare, cash, out paidNow, out debt);
            GTA.Game.Player.Money = cash - paidNow;
            SaveData data = main.Saves.Data;
            if (debt > 0) data.Debt += debt;
            data.AddTransaction("Taxi", L.F("Taxi: {0}, {1:0.0} km", "Такси: {0}, {1:0.0} км", what ?? "", ride.Km), -fare);
            data.Passenger.TaxiRides++;
            data.Passenger.TaxiSpent += fare;
            data.Passenger.DistanceKm += ride.Km;
            main.Notify(L.F("~y~Taxi receipt~s~~n~{0:0.0} km, {1} min~n~Fare: {2}{3}", "~y~Чек за такси~s~~n~{0:0.0} км, {1} мин~n~Стоимость: {2}{3}",
                ride.Km, ride.MinutesSoFar, L.Money(fare),
                debt > 0 ? L.F("~n~~r~Not enough cash: {0} added to unpaid bills.~s~", "~n~~r~Не хватило наличных: {0} добавлено к неоплаченным счетам.~s~", L.Money(debt)) : ""));
            Log.Info(string.Format("Taxi fare {0} ({1:0.00} km, {2:0} s waiting), cash paid {3}, debt {4}", fare, ride.Km, ride.WaitingSeconds, paidNow, debt));
        }

        private void Fail(string message)
        {
            Cleanup(true);
            main.Notify(message);
        }

        private void Finish(string message)
        {
            if (message != null) main.Notify(message);
            Cleanup(true);
        }

        private void Cleanup(bool driveOn)
        {
            try
            {
                if (blip != null && blip.Exists()) blip.Delete();
                blip = null;
                if (ride != null) ride.Release(driveOn, 0f);
                else if (taxi != null && taxi.Exists() && (main.NpcAccidents == null || !main.NpcAccidents.IsInvolved(taxi)))
                {
                    if (driveOn && Valid && driver.IsInVehicle(taxi))
                    {
                        N.Call(N.CLEAR_PED_TASKS, driver);
                        N.Call(N.TASK_VEHICLE_DRIVE_WANDER, driver, taxi, 12f, N.StyleNormal);
                    }
                    N.Release(driver);
                    N.Release(taxi);
                }
            }
            catch (Exception ex) { Log.Error("Taxi cleanup failed", ex); }
            ride = null;
            taxi = null;
            driver = null;
            state = State.None;
            paid = false;
            autoExit = false;
            payOnStop = false;
            destName = null;
            quote = 0;
            slowSince = -1;
            finishedAt = 0;
        }

        public string StatusText()
        {
            switch (state)
            {
                case State.EnRoute:
                    return Valid ? L.F("On the way, {0:0} m", "В пути, {0:0} м", N.Dist2D(taxi.Position, pickup)) : "-";
                case State.Waiting: return L.S("Waiting for you at the kerb", "Ждёт вас у обочины");
                case State.Boarding: return L.S("Getting in", "Посадка");
                case State.Choosing: return L.S("Choose a destination", "Выберите, куда ехать");
                case State.Driving: return ride != null ? ride.StatusText() : "-";
                case State.Finished: return L.S("Arrived", "Приехали");
            }
            return L.S("Not booked", "Не заказано");
        }
    }
}
