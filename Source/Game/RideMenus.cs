// Realistic Life Simulator - mouse menu pages for passenger rides and the taxi service.
// Reached from Life Hub > Rides and taxi, or the optional (unassigned) "Passenger menu" key.
// The page adapts to the situation: on foot, riding with an NPC, or in a taxi.

using System;
using System.Collections.Generic;
using GTA;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public sealed class RideMenus
    {
        private readonly RlsMain main;

        public RideMenus(RlsMain main) { this.main = main; }

        private MenuController Menu { get { return main.Menu; } }
        private PassengerSystem P { get { return main.Passenger; } }
        private TaxiService T { get { return main.Taxi; } }
        private RlsSettings Settings { get { return main.Config.Settings; } }

        private static string More() { return ">"; }

        public Menu BuildPage()
        {
            if (T != null && T.OnBoard) return BuildTaxiRide();
            if (P != null && P.Riding) return BuildRide();
            return BuildOnFoot();
        }

        private void Refresh() { Menu.Replace(BuildPage()); }

        // ------------------------------------------------------------ on foot

        private Menu BuildOnFoot()
        {
            Menu m = new Menu(L.S("Rides and taxi", "Поездки и такси"), L.S("Travel as a passenger", "Поездки пассажиром"));
            m.Add(new MenuItem(L.S("Ask a nearby driver for a ride", "Попросить водителя подвезти"), () => Menu.Push(BuildCandidates()))
                .WithValue(More)
                .WithEnabled(() => Settings.PassengerEnabled && !P.Busy && (T == null || !T.Active))
                .WithDescription(L.S("Stand next to a stopped or slow car with a driver. Each driver decides for themselves.",
                                     "Подойдите к стоящей или медленно едущей машине с водителем. Каждый водитель решает сам.")));

            if (T != null && T.Active)
            {
                m.Add(new MenuItem(L.S("Taxi", "Такси"), null).WithValue(() => T.StatusText()));
                m.Add(new MenuItem(L.S("Get in the taxi", "Сесть в такси"), () => { Menu.Close(); T.GetIn(); })
                    .WithEnabled(() => T.Waiting)
                    .WithDescription(L.S("Walk to a rear seat and get in.", "Подойти и сесть на заднее сиденье.")));
                m.Add(new MenuItem(L.S("Cancel the taxi", "Отменить такси"), () => { T.Cancel(); Refresh(); })
                    .WithDescription(L.S("No charge before you get in.", "До посадки платить не нужно.")));
            }
            else
            {
                m.Add(new MenuItem(L.S("Call a taxi", "Вызвать такси"), () => { T.Request(); Refresh(); })
                    .WithEnabled(() => Settings.TaxiEnabled && !P.Busy)
                    .WithDescription(L.F("A taxi drives to the nearest road. Fare: {0} + {1}/km + {2}/min waiting.",
                                         "Такси приедет к ближайшей дороге. Тариф: {0} + {1}/км + {2}/мин ожидания.",
                                         L.Money(Settings.TaxiBaseFare), L.Money(Settings.TaxiPricePerKm), L.Money(Settings.TaxiPricePerMinute))));
            }
            AddStats(m);
            m.Add(new MenuItem(L.S("Settings", "Настройки"), () => Menu.Push(BuildSettings())).WithValue(More));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private Menu BuildCandidates()
        {
            Menu m = new Menu(L.S("Nearby drivers", "Водители рядом"), L.S("Within about 14 m", "В радиусе около 14 м"));
            List<RideCandidate> list = P.NearbyCandidates();
            foreach (RideCandidate c in list)
            {
                RideCandidate cand = c;
                string label = N.SafeName(c.Vehicle) + "  " + c.Distance.ToString("0") + L.S(" m", " м");
                if (c.Problem != null)
                {
                    m.Add(new MenuItem(label, null).WithValue(() => cand.Problem));
                    continue;
                }
                m.Add(new MenuItem(label, () => Menu.Push(BuildSeats(cand)))
                    .WithValue(() => L.F("{0} free  >", "свободно: {0}  >", cand.Seats.Count))
                    .WithDescription(L.S("Choose a seat, then the driver decides.", "Выберите место, затем водитель решит.")));
            }
            if (list.Count == 0) m.Add(new MenuItem(L.S("No cars with drivers nearby.", "Рядом нет машин с водителями."), null));
            m.Add(new MenuItem(L.S("Refresh", "Обновить"), () => Menu.Replace(BuildCandidates())));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private Menu BuildSeats(RideCandidate c)
        {
            Menu m = new Menu(N.SafeName(c.Vehicle), L.S("Free passenger seats", "Свободные пассажирские места"));
            foreach (int s in c.Seats)
            {
                int seat = s;
                m.Add(new MenuItem(PassengerRules.SeatName(seat), () =>
                {
                    string problem = P.RequestRide(c.Vehicle, seat);
                    Menu.Close();
                    if (problem != null) main.Notify(problem);
                }).WithDescription(L.S("Ask the driver for a ride in this seat.", "Попросить водителя подвезти на этом месте.")));
            }
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        // ------------------------------------------------------------ riding with an NPC

        private Menu BuildRide()
        {
            RideSession r = P.Ride;
            Menu m = new Menu(L.S("Passenger", "Пассажир"), N.SafeName(r.Vehicle) + "  -  " + PassengerRules.SeatName(r.Seat));
            m.Add(new MenuItem(L.S("Journey information", "Информация о поездке"), () => Menu.Push(BuildInfo(r, false))).WithValue(More));
            m.Add(new MenuItem(L.S("Request a stop", "Попросить остановиться"), () => { P.RequestStop(true); Refresh(); })
                .WithEnabled(() => r.Phase == RidePhase.Riding || r.Phase == RidePhase.Stopped)
                .WithDescription(L.S("Asks the driver to stop now. If it isn't safe here, they pull over a bit further on.",
                                     "Просит водителя остановиться сейчас. Если тут небезопасно, он остановится чуть дальше.")));
            m.Add(new MenuItem(L.S("Stop at a safe place", "Остановиться в безопасном месте"), () => { P.RequestStop(false); Refresh(); })
                .WithEnabled(() => r.Phase == RidePhase.Riding)
                .WithDescription(L.S("The driver pulls over at the kerb ahead.", "Водитель остановится у обочины впереди.")));
            m.Add(new MenuItem(L.S("Exit the vehicle", "Выйти из машины"), () => { Menu.Close(); P.Exit(); })
                .WithValue(() => r.Vehicle.Exists() && r.Vehicle.Speed >= 1f ? L.S("stops first", "сначала остановка") : "")
                .WithDescription(L.S("Only when the car has stopped. While moving, the driver pulls over first.",
                                     "Только когда машина стоит. На ходу водитель сначала остановится.")));
            m.Add(new MenuItem(L.S("Continue the journey", "Продолжить поездку"), () => { P.ContinueJourney(); Refresh(); })
                .WithEnabled(() => r.Phase == RidePhase.Stopped || r.Phase == RidePhase.PullingOver));
            m.Add(new MenuItem(L.S("Ask to drop me at my waypoint", "Попросить довезти до метки"), () => { P.AskForWaypoint(); Refresh(); })
                .WithEnabled(() => !r.HasDestination && r.Phase != RidePhase.InAccident)
                .WithDescription(L.S("Changes the driver's route only if they agree (depends on the driver and the distance).",
                                     "Водитель изменит маршрут, только если согласится (зависит от водителя и расстояния).")));
            m.Add(new MenuItem(L.S("Let the driver go their own way", "Пусть едет своим маршрутом"), () => { P.FollowOwnRoute(); Refresh(); })
                .WithEnabled(() => r.HasDestination && r.Phase == RidePhase.Riding));
            m.Add(new MenuItem(L.S("Close", "Закрыть"), () => Menu.Close()));
            return m;
        }

        private Menu BuildInfo(RideSession r, bool taxi)
        {
            Menu m = new Menu(L.S("Journey information", "Информация о поездке"), N.SafeName(r.Vehicle));
            if (!taxi)
            {
                m.Add(new MenuItem(L.S("Driver", "Водитель"), null).WithValue(() => PassengerRules.AttitudeName(r.Attitude)));
                m.Add(new MenuItem(L.S("Driving style", "Стиль вождения"), null).WithValue(() => NpcRules.Name(r.Personality)));
            }
            m.Add(new MenuItem(L.S("Your seat", "Ваше место"), null).WithValue(() => PassengerRules.SeatName(r.Seat)));
            m.Add(new MenuItem(L.S("Status", "Состояние"), null).WithValue(() => r.StatusText()));
            m.Add(new MenuItem(L.S("Speed / limit", "Скорость / ограничение"), null).WithValue(() =>
                r.Vehicle.Exists() ? L.F("{0:0} / {1} km/h", "{0:0} / {1} км/ч", r.Vehicle.Speed * 3.6f, main.Traffic.LimitAt(r.Vehicle.Position)) : "-"));
            m.Add(new MenuItem(L.S("Location", "Где"), null).WithValue(() => r.Vehicle.Exists() ? main.Traffic.DescribeLocation(r.Vehicle.Position) : "-"));
            m.Add(new MenuItem(L.S("Destination", "Куда"), null).WithValue(() =>
                taxi ? (T.DestinationName ?? L.S("not chosen", "не выбрано"))
                     : r.HasDestination ? L.S("Your waypoint", "Ваша метка") : L.S("The driver's own route", "Маршрут водителя")));
            m.Add(new MenuItem(L.S("Distance left", "Осталось"), null).WithValue(() =>
                r.HasDestination && r.Vehicle.Exists() ? L.F("{0:0.0} km", "{0:0.0} км", N.Dist2D(r.Vehicle.Position, r.Destination) / 1000f) : "-"));
            m.Add(new MenuItem(L.S("Travelled", "Проехали"), null).WithValue(() => L.F("{0:0.0} km, {1} min", "{0:0.0} км, {1} мин", r.Km, r.MinutesSoFar)));
            if (taxi) m.Add(new MenuItem(L.S("Meter", "Счётчик"), null).WithValue(() => L.Money(T.MeterNow)));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        // ------------------------------------------------------------ taxi

        private Menu BuildTaxiRide()
        {
            RideSession r = T.Ride;
            Menu m = new Menu(L.S("Taxi", "Такси"), L.S("Meter: ", "Счётчик: ") + L.Money(T.MeterNow));
            m.Add(new MenuItem(T.Choosing ? L.S("Choose destination", "Выбрать, куда ехать") : L.S("Change destination", "Изменить пункт назначения"),
                () => Menu.Push(BuildDestinations()))
                .WithValue(() => (T.DestinationName ?? "") + "  >")
                .WithEnabled(() => (T.Choosing || T.Driving) && r.CanContinue && !r.DriverWantsOut && r.Phase != RidePhase.InAccident));
            m.Add(new MenuItem(L.S("Journey information", "Информация о поездке"), () => Menu.Push(BuildInfo(r, true))).WithValue(More));
            m.Add(new MenuItem(L.S("Stop here and pay", "Остановиться здесь и оплатить"), () => { T.StopHere(); Refresh(); })
                .WithEnabled(() => T.Driving && r.Phase == RidePhase.Riding));
            m.Add(new MenuItem(L.S("Exit the taxi", "Выйти из такси"), () => { Menu.Close(); T.Exit(); })
                .WithDescription(L.S("When stopped. While moving, the driver pulls over and you pay the meter.",
                                     "Когда машина стоит. На ходу водитель остановится, и вы оплатите по счётчику.")));
            m.Add(new MenuItem(L.S("Close", "Закрыть"), () => Menu.Close()));
            return m;
        }

        private Menu BuildDestinations()
        {
            Menu m = new Menu(L.S("Destination", "Куда ехать"), L.S("Estimated fare on the right", "Справа - примерная стоимость"));
            List<TaxiDestination> list = T.Destinations();
            foreach (TaxiDestination d in list)
            {
                TaxiDestination dest = d;
                m.Add(new MenuItem(d.Name, () => { T.SetDestination(dest); Menu.Replace(BuildPage()); })
                    .WithValue(() => L.S("about ", "около ") + L.Money(T.EstimateTo(dest.Position))));
            }
            if (list.Count == 0)
                m.Add(new MenuItem(L.S("Set a waypoint on the map (pause menu > Map), then refresh.", "Поставьте метку на карте (пауза > Карта) и обновите."), null));
            m.Add(new MenuItem(L.S("Refresh", "Обновить"), () => Menu.Replace(BuildDestinations())));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        // ------------------------------------------------------------ stats and settings

        private void AddStats(Menu m)
        {
            PassengerStats s = main.Saves.Data.Passenger;
            m.Add(new MenuItem(L.S("Rides / refusals", "Поездок / отказов"), null).WithValue(() => s.Rides + " / " + s.Refusals));
            m.Add(new MenuItem(L.S("Taxi rides / spent", "Такси: поездок / потрачено"), null).WithValue(() => s.TaxiRides + " / " + L.Money(s.TaxiSpent)));
            m.Add(new MenuItem(L.S("Distance as passenger", "Проехано пассажиром"), null).WithValue(() => L.F("{0:0.0} km", "{0:0.0} км", s.DistanceKm)));
        }

        public Menu BuildSettings()
        {
            Menu m = new Menu(L.S("Passengers and taxi", "Пассажиры и такси"), L.S("Changes are saved immediately", "Изменения сохраняются сразу"));
            m.Add(new MenuItem(L.S("Rides with NPC drivers", "Поездки с водителями NPC"), () => { Settings.PassengerEnabled = !Settings.PassengerEnabled; Save(); })
                .WithValue(() => L.OnOff(Settings.PassengerEnabled)));
            m.Add(new MenuItem(L.S("Taxi service", "Служба такси"), () => { Settings.TaxiEnabled = !Settings.TaxiEnabled; Save(); })
                .WithValue(() => L.OnOff(Settings.TaxiEnabled)));
            m.Add(new MenuItem(L.S("Taxi base fare", "Посадка в такси"), () => { Settings.TaxiBaseFare = Next(RlsSettings.TaxiBaseChoices, Settings.TaxiBaseFare); Save(); })
                .WithValue(() => L.Money(Settings.TaxiBaseFare)));
            m.Add(new MenuItem(L.S("Taxi price per km", "Цена такси за км"), () => { Settings.TaxiPricePerKm = Next(RlsSettings.TaxiPerKmChoices, Settings.TaxiPricePerKm); Save(); })
                .WithValue(() => L.Money(Settings.TaxiPricePerKm)));
            m.Add(new MenuItem(L.S("Taxi waiting per minute", "Ожидание такси за минуту"), () => { Settings.TaxiPricePerMinute = Next(RlsSettings.TaxiPerMinuteChoices, Settings.TaxiPricePerMinute); Save(); })
                .WithValue(() => L.Money(Settings.TaxiPricePerMinute))
                .WithDescription(L.S("Charged while the taxi stands still in traffic with you on board.", "Начисляется, пока такси с вами стоит в пробке.")));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private static float Next(float[] choices, float current)
        {
            int idx = -1;
            for (int i = 0; i < choices.Length; i++) if (Math.Abs(choices[i] - current) < 0.001f) idx = i;
            return choices[(idx + 1) % choices.Length];
        }

        private void Save()
        {
            try { main.Config.SaveSettings(); }
            catch (Exception ex) { Log.Error("Could not save settings", ex); }
        }
    }
}
