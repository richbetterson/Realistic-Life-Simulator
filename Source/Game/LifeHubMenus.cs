// Realistic Life Simulator - Life Hub menu pages (English / Russian).
// Only pages backed by working features are listed.

using System;
using System.Collections.Generic;
using RealisticLifeSimulator.Core;
using Keys = System.Windows.Forms.Keys;

namespace RealisticLifeSimulator.Game
{
    public sealed class LifeHubMenus
    {
        private readonly RlsMain main;

        public LifeHubMenus(RlsMain main)
        {
            this.main = main;
        }

        private MenuController Menu { get { return main.Menu; } }
        private RlsSettings Settings { get { return main.Config.Settings; } }
        private SaveData Data { get { return main.Saves.Data; } }

        private static string More() { return ">"; }

        public void OpenMain()
        {
            Menu.Open(BuildMain());
        }

        public void OpenSettingsDirect()
        {
            if (Menu.IsOpen) return;
            Menu.Open(BuildMain());
            Menu.Push(BuildSettings());
        }

        private Menu BuildMain()
        {
            Menu m = new Menu(L.S("Life Hub", "Центр управления"), "Realistic Life Simulator " + RlsMain.Version);
            m.Add(new MenuItem(L.S("Traffic & fines", "Штрафы и ПДД"), () => Menu.Push(BuildDrivingRecord()))
                .WithValue(() =>
                {
                    double owed = Data.OutstandingFines();
                    return owed > 0 ? L.Money(owed) + "  >" : More();
                })
                .WithDescription(L.S("Traffic violations, fines to pay, how enforcement works and driving statistics.",
                                     "Нарушения ПДД, неоплаченные штрафы, как работает контроль и статистика вождения.")));
            m.Add(new MenuItem(L.S("Police & legal status", "Полиция и правовой статус"), () => Menu.Push(BuildLicence()))
                .WithValue(() =>
                {
                    LicenceStatus st = LicenceRules.Status(main.CurrentProfile(), Data.Stats.TotalPlayTimeSeconds);
                    if (st == LicenceStatus.Revoked) return L.S("REVOKED  >", "ОТОЗВАНЫ  >");
                    if (st == LicenceStatus.Suspended) return L.S("SUSPENDED  >", "ПРИОСТАНОВЛЕНЫ  >");
                    return More();
                })
                .WithDescription(L.S("Driving licence status, points, reinstatement, insurance and your record with the police.",
                                     "Статус прав, баллы, восстановление, страховка и ваша история с полицией.")));
            m.Add(new MenuItem(L.S("Vehicle controls", "Управление машиной"), () => Menu.Push(BuildVehicleControls()))
                .WithValue(() => main.Limiter != null && main.Limiter.Enabled
                    ? L.F("limiter {0}  >", "огранич. {0}  >", main.Config.Settings.SpeedLimiterKmh) : More())
                .WithDescription(L.S("Speed limiter for the vehicle you drive.", "Ограничитель скорости для машины, которой вы управляете.")));
            m.Add(new MenuItem(L.S("My vehicles", "Мои машины"), () => Menu.Push(BuildMyVehicles()))
                .WithValue(() => main.Ownership == null ? More() : main.Ownership.Mine().Count + "  >")
                .WithDescription(L.S("Owned cars: condition, mileage, insurance, service history, recovery.",
                                     "Ваши машины: состояние, пробег, страховка, история обслуживания, эвакуация.")));
            m.Add(new MenuItem(L.S("Properties", "Недвижимость"), () => Menu.Push(BuildRealEstate())).WithValue(More)
                .WithDescription(L.S("Homes, apartments and garages: your properties and listings for sale.",
                                     "Дома, квартиры и гаражи: ваша недвижимость и объекты на продажу.")));
            m.Add(new MenuItem(L.S("Accident reports", "Протоколы ДТП"), () => Menu.Push(BuildAccidents()))
                .WithValue(() => Data.Accidents.Count > 0 ? Data.Accidents.Count + "  >" : More()));
            m.Add(new MenuItem(L.S("City incidents", "Происшествия в городе"), () => Menu.Push(BuildIncidents()))
                .WithValue(More)
                .WithDescription(L.S("Accidents between other drivers and police stops you witnessed nearby.",
                                     "ДТП между другими водителями и остановки полицией, которые произошли рядом с вами.")));
            m.Add(new MenuItem(L.S("Rides and taxi", "Поездки и такси"), () => { if (main.RideMenus != null) Menu.Push(main.RideMenus.BuildPage()); })
                .WithValue(() =>
                {
                    if (main.Taxi != null && main.Taxi.Active) return L.S("taxi  >", "такси  >");
                    if (main.Passenger != null && main.Passenger.Riding) return L.S("riding  >", "в пути  >");
                    return More();
                })
                .WithDescription(L.S("Ask NPC drivers for a ride, control your journey as a passenger, or call a taxi.",
                                     "Попросить водителя NPC подвезти, управлять поездкой пассажиром или вызвать такси.")));
            m.Add(new MenuItem(L.S("Call police (911)", "Вызвать полицию (911)"), () =>
            {
                Menu.Close();
                if (main.Police != null) main.Police.CallForAssistance();
            }).WithDescription(L.S("A unit drives to you. At an accident scene this calls police to the accident.",
                                   "Патруль приедет к вам. На месте ДТП вызывает полицию на ДТП.")));
            m.Add(new MenuItem(L.S("Receipts", "Чеки и расходы"), () => Menu.Push(BuildReceipts())).WithValue(More)
                .WithDescription(L.S("Fuel purchases and paid fines.", "Покупки топлива и оплаченные штрафы.")));
            m.Add(new MenuItem(L.S("Settings", "Настройки"), () => Menu.Push(BuildSettings())).WithValue(More)
                .WithDescription(L.S("Language, performance, fuel, traffic rules and display.",
                                     "Язык, производительность, топливо, правила движения и отображение.")));
            m.Add(new MenuItem(L.S("Key bindings", "Клавиши"), () => Menu.Push(BuildBindings()))
                .WithValue(() => L.F("{0} assigned  >", "назначено: {0}  >", main.Config.Bindings.AssignedCount))
                .WithDescription(L.S("Optional keyboard shortcuts. Every action starts unassigned.",
                                     "Необязательные горячие клавиши. По умолчанию ничего не назначено.")));
            m.Add(new MenuItem(L.S("Save and status", "Сохранение и состояние"), () => Menu.Push(BuildStatus())).WithValue(More)
                .WithDescription(L.S("Save file, play time and how much frame time the mod uses.",
                                     "Файл сохранения, время игры и нагрузка мода на кадр.")));
            m.Add(new MenuItem(L.S("Close", "Закрыть"), () => Menu.Close()));
            return m;
        }

        // ------------------------------------------------------------ driving record

        private Menu BuildDrivingRecord()
        {
            Menu m = new Menu(L.S("Traffic & fines", "Штрафы и ПДД"), L.S("Mode: ", "Режим: ") + FineModeName(Settings.Fines));
            m.Add(new MenuItem(L.S("Outstanding fines", "Неоплаченные штрафы"), null).WithValue(() => L.Money(Data.OutstandingFines())));
            m.Add(new MenuItem(L.S("Pay outstanding fines", "Оплатить штрафы"), PayFines)
                .WithEnabled(() => Data.OutstandingFines() > 0)
                .WithValue(() => L.F("cash {0}", "наличные {0}", L.Money(GTA.Game.Player.Money)))
                .WithDescription(L.S("Paid from cash, oldest fines first. Unpaid fines make officers less lenient and block licence reinstatement.",
                                     "Оплата наличными, начиная со старых. Неоплаченные штрафы делают полицию строже и мешают восстановить права.")));
            m.Add(new MenuItem(L.S("Enforcement and fine amounts", "Контроль и размер штрафов"), () => Menu.Push(BuildTrafficSettings())).WithValue(More)
                .WithDescription(FineModeHelp(Settings.Fines)));
            m.Add(new MenuItem(L.S("Distance driven", "Пройдено"), null)
                .WithValue(() => Data.Driving.DistanceKm.ToString("0.0", L.Culture) + L.S(" km", " км")));
            m.Add(new MenuItem(L.S("Violations recorded", "Записано нарушений"), null).WithValue(() => Data.Driving.Violations.ToString()));
            m.Add(new MenuItem(L.S("Fines issued", "Выписано штрафов"), null).WithValue(() => Data.Driving.FinesIssued.ToString()));
            m.Add(new MenuItem(L.S("NPC refuels seen", "Заправки NPC рядом"), null).WithValue(() => Data.Driving.NpcRefuelsSeen.ToString())
                .WithDescription(L.S("Nearby drivers who drove to a gas station to refuel.", "Водители поблизости, заехавшие на АЗС.")));

            List<ViolationRecord> list = Data.Violations;
            int shown = 0;
            for (int i = list.Count - 1; i >= 0 && shown < 20; i--, shown++)
            {
                ViolationRecord v = list[i];
                DateTime t;
                string when = DateTime.TryParse(v.TimeUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out t) ? L.DateTimeLocal(t) : "";
                string status = v.Warning ? L.S("warning", "предупреждение")
                              : v.Fine <= 0 ? L.S("recorded", "запись")
                              : v.Paid ? L.F("{0} paid", "{0} оплачен", L.Money(v.Fine))
                              : L.F("{0} unpaid", "{0} не оплачен", L.Money(v.Fine));
                string source = v.Fine > 0 ? "  (" + EnforcementRules.SourceName(v.Witnessed) + (v.Points > 0 ? L.F(", {0} pt", ", {0} б.", v.Points) : "") + ")" : "";
                string desc = when + "  " + v.Location + (string.IsNullOrEmpty(v.Details) ? "" : "  -  " + v.Details) + source;
                m.Add(new MenuItem(TrafficRules.NameFromString(v.Type), null).WithValue(() => status).WithDescription(desc));
            }
            if (shown == 0) m.Add(new MenuItem(L.S("No violations - well driven.", "Нарушений нет - отличное вождение."), null));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private void PayFines()
        {
            int cash = GTA.Game.Player.Money;
            double paid = 0;
            int count = 0;
            foreach (ViolationRecord v in Data.Violations)
            {
                if (v.Paid || v.Fine <= 0) continue;
                int amount = (int)Math.Round(v.Fine);
                if (amount > cash) break;
                cash -= amount;
                paid += amount;
                v.Paid = true;
                count++;
                Data.AddTransaction("Fine", TrafficRules.NameFromString(v.Type) + " (" + v.Location + ")", -amount);
            }
            if (count == 0)
            {
                main.Notify(L.S("Not enough cash to pay the oldest fine.", "Недостаточно наличных, чтобы оплатить самый старый штраф."));
                return;
            }
            GTA.Game.Player.Money = cash;
            Data.Driving.FinesPaid += paid;
            main.Saves.Save();
            main.Notify(L.F("Paid {0} fine(s): {1}.", "Оплачено штрафов: {0}, сумма {1}.", count, L.Money(paid)));
            Menu.Replace(BuildDrivingRecord());
        }

        // Opens a page directly (assigned keys).
        public void OpenDirect(Menu page)
        {
            if (Menu.IsOpen) return;
            Menu.Open(BuildMain());
            Menu.Push(page);
        }

        // ------------------------------------------------------------ vehicles

        private static string StateText(OwnedVehicle o, PropertySystem props)
        {
            if (o.Managed) return L.S("Personal (GTA)", "Личная (GTA)");
            switch (o.State)
            {
                case VehicleState.Stored:
                    PropertyDef d = props != null ? props.Def(o.GarageId) : null;
                    return L.S("In garage", "В гараже") + (d != null ? ": " + d.Name : "");
                case VehicleState.Destroyed: return L.S("Destroyed", "Уничтожена");
                case VehicleState.Sold: return L.S("Sold", "Продана");
            }
            return L.S("Parked / in use", "Припаркована / в пути");
        }

        public Menu BuildMyVehicles()
        {
            Menu m = new Menu(L.S("My vehicles", "Мои машины"), main.CurrentProfile().Character);
            List<OwnedVehicle> list = main.Ownership.Mine();
            foreach (OwnedVehicle o in list)
            {
                OwnedVehicle rec = o;
                m.Add(new MenuItem(o.DisplayName + "  " + o.Plate, () => Menu.Push(BuildVehicleDetail(rec)))
                    .WithValue(() => rec.State == VehicleState.Destroyed ? L.S("destroyed", "уничтожена") : rec.Condition.ToString("0", L.Culture) + "%  >")
                    .WithDescription(StateText(rec, main.Properties)));
            }
            if (list.Count == 0)
                m.Add(new MenuItem(L.S("No vehicles yet. Buy one at the dealership or register your car.", "Машин пока нет. Купите в автосалоне или зарегистрируйте свою."), null));
            m.Add(new MenuItem(L.F("Register the car I'm driving ({0})", "Зарегистрировать машину, в которой я сижу ({0})", L.Money(OwnershipRules.RegistrationFee)), () =>
            {
                if (main.Ownership.RegisterCurrent()) Menu.Replace(BuildMyVehicles());
            }).WithDescription(L.S("Your character's own car or one you bought. Stolen vehicles cannot be registered.",
                                   "Собственная машина персонажа или купленная. Угнанные машины регистрировать нельзя.")));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private Menu BuildVehicleDetail(OwnedVehicle o)
        {
            RlsSettings set = Settings;
            Menu m = new Menu(o.DisplayName, o.Plate + "  -  " + StateText(o, main.Properties));
            m.Add(new MenuItem(L.S("Mileage", "Пробег"), null).WithValue(() => o.MileageKm.ToString("0", L.Culture) + L.S(" km", " км")));
            m.Add(new MenuItem(L.S("Engine condition", "Состояние двигателя"), null).WithValue(() => o.Condition.ToString("0", L.Culture) + "%")
                .WithDescription(L.S("Below 50% the car loses top speed; below 25% it uses 20% more fuel. A service restores it.",
                                     "Ниже 50% падает максимальная скорость, ниже 25% расход топлива растёт на 20%. ТО восстанавливает двигатель.")));
            m.Add(new MenuItem(L.S("Next service", "Следующее ТО"), null).WithValue(() =>
            {
                double left = set.ServiceIntervalKm - (o.MileageKm - o.KmAtService);
                return left >= 0 ? L.F("in {0:0} km", "через {0:0} км", left) : L.F("overdue by {0:0} km", "просрочено на {0:0} км", -left);
            }));
            m.Add(new MenuItem(L.S("Market value", "Рыночная стоимость"), null).WithValue(() => L.Money(OwnershipRules.CurrentValue(o.Value, o.MileageKm, o.Condition))));
            m.Add(new MenuItem(L.S("Insurance", "Страховка"), () =>
            {
                o.Insurance = (InsuranceLevel)(((int)o.Insurance + 1) % 3);
                main.Saves.Save();
            }).WithValue(() => RepairShops.InsuranceName(o.Insurance) + "  " + L.F("{0} / bill", "{0} / период", L.Money(OwnershipRules.Premium(o.Value, o.Insurance))))
              .WithEnabled(() => o.State != VehicleState.Destroyed && o.State != VehicleState.Sold)
              .WithDescription(L.S("Click to change. Basic: 50% of repairs over $500, 40% of value if destroyed. Full: 80% over $250, 80% if destroyed.",
                                   "Нажмите, чтобы сменить. Базовая: 50% ремонта сверх $500, 40% стоимости при гибели. Полная: 80% сверх $250, 80% при гибели.")));
            m.Add(new MenuItem(L.S("Show on map", "Показать на карте"), () => main.Ownership.SetWaypoint(o))
                .WithEnabled(() => o.State == VehicleState.Out && !o.Managed));
            m.Add(new MenuItem(L.F("Recovery: deliver it to me ({0})", "Эвакуация ко мне ({0})", L.Money(OwnershipRules.RecoveryFee)), () =>
            {
                if (main.Ownership.Recover(o)) Menu.Close();
            }).WithEnabled(() => o.State == VehicleState.Out && !o.Managed)
              .WithDescription(L.S("A recovery truck brings the car to a road near you.", "Эвакуатор привезёт машину на дорогу рядом с вами.")));
            m.Add(new MenuItem(L.S("Service history", "История обслуживания"), () => Menu.Push(BuildServiceHistory(o))).WithValue(() => o.Services.Count + "  >"));
            m.Add(new MenuItem(L.S("Damage history", "История повреждений"), () => Menu.Push(BuildDamageHistory(o))).WithValue(() => o.Damage.Count + "  >"));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        public Menu BuildServiceHistory(OwnedVehicle o)
        {
            Menu m = new Menu(L.S("Service history", "История обслуживания"), o.DisplayName + "  " + o.Plate);
            for (int i = o.Services.Count - 1; i >= 0; i--)
            {
                ServiceEntry e = o.Services[i];
                DateTime t;
                string when = DateTime.TryParse(e.TimeUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out t) ? L.DateTimeLocal(t) : "";
                string kind = e.Kind == "Service" ? L.S("Service", "ТО") : e.Kind == "Repair" ? L.S("Repair", "Ремонт") : L.S("Inspection", "Диагностика");
                m.Add(new MenuItem(when + "  " + kind, null)
                    .WithValue(() => L.Money(e.Cost) + (e.InsurancePaid > 0 ? L.F(" (+{0} ins.)", " (+{0} страх.)", L.Money(e.InsurancePaid)) : ""))
                    .WithDescription(L.F("At {0:0} km", "При пробеге {0:0} км", e.Km)));
            }
            if (o.Services.Count == 0) m.Add(new MenuItem(L.S("No records yet.", "Записей пока нет."), null));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private Menu BuildDamageHistory(OwnedVehicle o)
        {
            Menu m = new Menu(L.S("Damage history", "История повреждений"), o.DisplayName + "  " + o.Plate);
            for (int i = o.Damage.Count - 1; i >= 0; i--)
            {
                DamageEntry e = o.Damage[i];
                DateTime t;
                string when = DateTime.TryParse(e.TimeUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out t) ? L.DateTimeLocal(t) : "";
                m.Add(new MenuItem(when, null).WithValue(() => L.Money(e.Estimate))
                    .WithDescription(e.Location + L.F("  -  {0:0} km", "  -  {0:0} км", e.Km)));
            }
            if (o.Damage.Count == 0) m.Add(new MenuItem(L.S("No damage recorded.", "Повреждений не зафиксировано."), null));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        // ------------------------------------------------------------ real estate

        public Menu BuildRealEstate()
        {
            PropertySystem ps = main.Properties;
            Menu m = new Menu(L.S("Properties", "Недвижимость"), L.S("Your properties first, then listings", "Сначала ваши объекты, затем предложения"));
            m.Add(new MenuItem(L.S("Unpaid bills", "Неоплаченные счета"), null).WithValue(() => L.Money(Data.Debt))
                .WithDescription(L.F("Bills every {0} minutes of play: maintenance and insurance out, rent in.",
                                     "Счета каждые {0} минут игры: содержание и страховка списываются, аренда начисляется.", Settings.BillingIntervalMinutes)));
            m.Add(new MenuItem(L.S("Pay unpaid bills", "Оплатить долг"), () =>
            {
                int pay = (int)Math.Min(GTA.Game.Player.Money, Math.Round(Data.Debt));
                if (pay <= 0) return;
                GTA.Game.Player.Money -= pay;
                Data.Debt = Math.Max(0, Data.Debt - pay);
                Data.AddTransaction("Bills", L.S("Paid unpaid bills", "Оплата долга по счетам"), -pay);
                main.Saves.Save();
            }).WithEnabled(() => Data.Debt > 0 && GTA.Game.Player.Money > 0));

            foreach (PropertyDef d in ps.Defs)
            {
                if (!ps.OwnedByMe(d)) continue;
                PropertyDef def = d;
                OwnedProperty op = ps.Owned(d.Id);
                m.Add(new MenuItem(d.Name, () => { ps.SetWaypoint(def); })
                    .WithValue(() => (ps.Inside == def ? L.S("you are here", "вы здесь") : op.RentedOut ? L.S("rented out", "сдан") : L.S("owned", "ваш"))
                                     + (def.GarageSlots > 0 ? "  " + ps.StoredCount(def.Id) + "/" + def.GarageSlots : ""))
                    .WithDescription(PropertySystem.HasInterior(def)
                        ? L.S("Click to set a waypoint. At the door marker: enter, garage, rent out, sell.", "Нажмите, чтобы отметить на карте. У маркера двери: войти, гараж, аренда, продажа.")
                        : L.S("Click to set a waypoint. Manage it at its door.", "Нажмите, чтобы отметить на карте. Управление - у двери объекта.")));
            }
            foreach (PropertyDef d in ps.Defs)
            {
                if (ps.Owned(d.Id) != null) continue;
                PropertyDef def = d;
                m.Add(new MenuItem(PropertySystem.TypeName(d.Type) + ": " + d.Name, () => Menu.Push(BuildListing(def)))
                    .WithValue(() => L.Money(def.Price) + "  >"));
            }
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private Menu BuildListing(PropertyDef d)
        {
            Menu m = new Menu(d.Name, L.S("For sale", "Продаётся"));
            main.Properties.AddDetails(m, d);
            m.Add(new MenuItem(L.S("Set a waypoint to view it", "Отметить на карте для осмотра"), () => main.Properties.SetWaypoint(d))
                .WithDescription(L.S("Visit the door marker to tour the interior and buy.", "У двери можно осмотреть объект изнутри и купить его.")));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        // ------------------------------------------------------------ licence

        private Menu BuildLicence()
        {
            DriverProfile p = main.CurrentProfile();
            Menu m = new Menu(L.S("Police & legal status", "Полиция и правовой статус"), p.Character);
            m.Add(new MenuItem(L.S("Licence status", "Статус прав"), null)
                .WithValue(() => LicenceRules.StatusName(LicenceRules.Status(p, Data.Stats.TotalPlayTimeSeconds)))
                .WithDescription(L.F("Suspended at {0} active points (ends when points expire, after 2 h of play). Revoked at {1} points, after driving while suspended twice, or after a serious pedestrian collision.",
                                     "Приостанавливаются при {0} баллах (до сгорания баллов, 2 ч игры). Отзываются при {1} баллах, после повторной езды с приостановленными правами или после тяжёлого наезда на пешехода.",
                                     PoliceRules.SuspensionPoints, LicenceRules.RevokePoints)));
            m.Add(new MenuItem(L.S("Reason / time left", "Причина / осталось"), null).WithValue(() =>
            {
                double play = Data.Stats.TotalPlayTimeSeconds;
                LicenceStatus st = LicenceRules.Status(p, play);
                if (st == LicenceStatus.Valid) return "-";
                int min = (int)Math.Ceiling(LicenceRules.SecondsRemaining(p, play) / 60.0);
                return min > 0 ? L.F("{0} min of play", "{0} мин игры", min) : L.S("can apply now", "можно подать заявление");
            }).WithDescription(string.IsNullOrEmpty(p.StatusReason) ? "" : p.StatusReason));
            m.Add(new MenuItem(L.F("Apply for reinstatement ({0})", "Восстановить права ({0})", L.Money(LicenceRules.ReinstatementFee)), () =>
            {
                double play = Data.Stats.TotalPlayTimeSeconds;
                string reason;
                if (!LicenceRules.CanReinstate(p, play, Data.OutstandingFines(), GTA.Game.Player.Money, out reason))
                {
                    main.Notify(reason);
                    return;
                }
                LicenceStatus before = LicenceRules.Status(p, play);
                GTA.Game.Player.Money -= (int)LicenceRules.ReinstatementFee;
                Data.AddTransaction("Licence", L.S("Licence reinstatement fee", "Пошлина за восстановление прав"), -LicenceRules.ReinstatementFee);
                LicenceRules.Reinstate(p, play);
                main.AnnounceLicenceChange(before, p);
                main.Saves.Save();
                Menu.Replace(BuildLicence());
            }).WithEnabled(() => LicenceRules.Status(p, Data.Stats.TotalPlayTimeSeconds) == LicenceStatus.Revoked)
              .WithDescription(L.S("Possible after the waiting period, with all fines paid. Clears your points.",
                                   "Возможно после окончания срока лишения и оплаты всех штрафов. Баллы обнуляются.")));
            m.Add(new MenuItem(L.S("Licence number", "Номер прав"), null).WithValue(() => p.LicenceNumber));
            m.Add(new MenuItem(L.S("Active points", "Активные баллы"), null).WithValue(() =>
                p.ActivePoints(Data.Stats.TotalPlayTimeSeconds) + " / " + PoliceRules.SuspensionPoints));
            m.Add(new MenuItem(L.S("Outstanding fines", "Неоплаченные штрафы"), null).WithValue(() => L.Money(Data.OutstandingFines())));
            m.Add(new MenuItem(L.S("Wanted level", "Уровень розыска"), null).WithValue(() => N.Wanted() > 0 ? N.Wanted() + " / 5" : L.S("none", "нет")));
            m.Add(new MenuItem(L.S("Driver reputation", "Репутация водителя"), null).WithValue(() => ((int)p.Reputation) + " / 100")
                .WithDescription(L.S("Higher reputation makes officers more likely to give a warning. Clean driving slowly restores it.",
                                     "Чем выше репутация, тем чаще полиция ограничивается предупреждением. Аккуратная езда её постепенно восстанавливает.")));
            m.Add(new MenuItem(L.S("Insurance", "Страховка"), null).WithValue(() => p.Insurer).WithDescription(L.S("Policy ", "Полис ") + p.PolicyNumber));
            m.Add(new MenuItem(L.S("Traffic stops", "Остановок полицией"), null).WithValue(() => p.Stops.ToString()));
            m.Add(new MenuItem(L.S("Warnings received", "Предупреждений"), null).WithValue(() => p.Warnings.ToString()));
            m.Add(new MenuItem(L.S("Suspensions / revocations", "Приостановок / лишений"), null).WithValue(() => p.Suspensions + " / " + p.Revocations));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        // ------------------------------------------------------------ vehicle controls (0.6.0)

        private Menu BuildVehicleControls()
        {
            Menu m = new Menu(L.S("Vehicle controls", "Управление машиной"), CurrentVehicleText());
            m.Add(new MenuItem(L.S("Speed limiter", "Ограничитель скорости"), () => Menu.Push(BuildSpeedLimiter()))
                .WithValue(() => main.Limiter.StatusText() + "  >")
                .WithDescription(L.S("Set a maximum speed for the vehicle you drive.", "Задайте максимальную скорость для машины, которой управляете.")));
            m.Add(new MenuItem(L.S("My vehicles", "Мои машины"), () => Menu.Push(BuildMyVehicles())).WithValue(More));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private static string CurrentVehicleText()
        {
            GTA.Ped p = GTA.Game.Player.Character;
            GTA.Vehicle v = p != null && p.Exists() ? p.CurrentVehicle : null;
            if (v == null || !v.Exists()) return L.S("Not in a vehicle", "Вы не в машине");
            if (v.Driver != p) return L.S("Passenger", "Пассажир") + ": " + N.VehicleName(v);
            return N.VehicleName(v);
        }

        private Menu BuildSpeedLimiter()
        {
            SpeedLimiter lim = main.Limiter;
            Menu m = new Menu(L.S("Speed limiter", "Ограничитель скорости"), CurrentVehicleText());
            m.Add(new MenuItem(L.S("Limiter", "Ограничитель"), () =>
            {
                lim.Toggle();
                Menu.Replace(BuildSpeedLimiter());
            }).WithValue(() => L.OnOff(lim.Enabled))
              .WithDescription(L.S("When on, the vehicle you drive cannot go faster than the set speed. Braking and steering are not affected.",
                                   "Когда включён, машина не разгоняется выше заданной скорости. Тормоз и руль работают как обычно.")));
            m.Add(new MenuItem(L.S("Maximum speed", "Максимальная скорость"), null)
                .WithValue(() => Settings.SpeedLimiterKmh + L.S(" km/h", " км/ч")));
            m.Add(new MenuItem(L.S("Enter a speed...", "Ввести скорость..."), () =>
            {
                SpeedLimiter.TextInputActive = true;
                string text;
                try { text = SpeedLimiter.AskNumber(Settings.SpeedLimiterKmh.ToString()); }
                finally { SpeedLimiter.TextInputActive = false; }
                if (text == null) return; // cancelled
                int kmh;
                string error;
                if (!SpeedLimiterRules.TryParse(text, out kmh, out error))
                {
                    main.Notify(L.S("Speed limiter: ", "Ограничитель: ") + error);
                    return;
                }
                lim.SetLimit(kmh);
                main.Notify(L.F("Speed limiter set to {0} km/h{1}.", "Ограничитель: {0} км/ч{1}.", kmh,
                    lim.Enabled ? "" : L.S(" (switch it on to use it)", " (включите, чтобы он работал)")));
                if (Menu.IsOpen) Menu.Replace(BuildSpeedLimiter());
            }).WithDescription(L.F("Type a whole number from {0} to {1} km/h and press Enter.", "Введите целое число от {0} до {1} км/ч и нажмите Enter.",
                SpeedLimiterRules.MinKmh, SpeedLimiterRules.MaxKmh)));
            m.Add(new MenuItem(L.F("Lower by {0} km/h", "Уменьшить на {0} км/ч", SpeedLimiterRules.StepKmh), () =>
                lim.SetLimit(Settings.SpeedLimiterKmh - SpeedLimiterRules.StepKmh)).WithEnabled(() => Settings.SpeedLimiterKmh > SpeedLimiterRules.MinKmh));
            m.Add(new MenuItem(L.F("Raise by {0} km/h", "Увеличить на {0} км/ч", SpeedLimiterRules.StepKmh), () =>
                lim.SetLimit(Settings.SpeedLimiterKmh + SpeedLimiterRules.StepKmh)).WithEnabled(() => Settings.SpeedLimiterKmh < SpeedLimiterRules.MaxKmh));
            m.Add(new MenuItem(L.S("Applies to", "Действует на"), null).WithValue(() =>
            {
                GTA.Ped p = GTA.Game.Player.Character;
                GTA.Vehicle v = p.CurrentVehicle;
                if (v == null || !v.Exists() || v.Driver != p) return L.S("the vehicle you drive", "машину, которой вы управляете");
                if (!SpeedLimiterRules.Supports((int)v.ClassType)) return L.S("not this vehicle (aircraft / train)", "не эту машину (авиа / поезд)");
                return lim.ActiveNow ? L.S("this vehicle (active)", "эту машину (работает)") : L.S("this vehicle", "эту машину");
            }));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        // ------------------------------------------------------------ accident reports

        private Menu BuildAccidents()
        {
            Menu m = new Menu(L.S("Accident reports", "Протоколы ДТП"), L.S("Latest 20", "Последние 20"));
            List<AccidentReport> list = Data.Accidents;
            int shown = 0;
            for (int i = list.Count - 1; i >= 0 && shown < 20; i--, shown++)
            {
                AccidentReport r = list[i];
                DateTime t;
                string when = DateTime.TryParse(r.TimeUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out t) ? L.DateTimeLocal(t) : "";
                m.Add(new MenuItem(when + "  " + r.OtherVehicle, () => Menu.Push(BuildAccidentDetail(r)))
                    .WithValue(() => SeverityText(r.Severity) + "  >")
                    .WithDescription(r.Location));
            }
            if (shown == 0) m.Add(new MenuItem(L.S("No accidents recorded.", "ДТП не зафиксировано."), null));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private static string SeverityText(string s)
        {
            try { return PoliceRules.SeverityName((AccidentSeverity)Enum.Parse(typeof(AccidentSeverity), s)); }
            catch (Exception) { return s; }
        }

        private static string FaultText(string f)
        {
            try { return PoliceRules.FaultName((Fault)Enum.Parse(typeof(Fault), f)); }
            catch (Exception) { return f; }
        }

        private static string StatementText(string s)
        {
            switch (s)
            {
                case "OtherAtFault": return L.S("Other driver caused it", "Виноват другой водитель");
                case "MyFault": return L.S("My fault", "Моя вина");
                case "DontKnow": return L.S("Don't know", "Не знаю");
            }
            return L.S("None", "Нет");
        }

        private Menu BuildAccidentDetail(AccidentReport r)
        {
            Menu m = new Menu(L.S("Accident report", "Протокол ДТП"), r.Id + "  -  " + r.Character);
            Add(m, L.S("Location", "Место"), r.Location);
            Add(m, L.S("Severity", "Тяжесть"), SeverityText(r.Severity));
            Add(m, L.S("Other party", "Другой участник"), r.OtherVehicle + (string.IsNullOrEmpty(r.OtherPlate) ? "" : "  " + r.OtherPlate));
            Add(m, L.S("Other driver", "Другой водитель"), string.IsNullOrEmpty(r.OtherDriver) ? "-" : r.OtherDriver);
            if (r.InfoExchanged) Add(m, L.S("Their insurance", "Его страховка"), r.OtherInsurer + "  " + r.OtherPolicy);
            Add(m, L.S("Speeds (you / other)", "Скорость (вы / другой)"), r.PlayerSpeedKmh + " / " + r.OtherSpeedKmh + L.S(" km/h", " км/ч"));
            Add(m, L.S("Your statement", "Ваши показания"), StatementText(r.Statement));
            Add(m, L.S("Fault", "Виновник"), FaultText(r.Fault));
            Add(m, L.S("Fines", "Штрафы"), r.Fine > 0 ? L.Money(r.Fine) : "-");
            Add(m, L.S("Damage estimate", "Оценка ущерба"), r.PlayerDamageEstimate > 0 ? L.Money(r.PlayerDamageEstimate) : "-");
            Add(m, L.S("Police / ambulance", "Полиция / скорая"), L.OnOff(r.PoliceAttended) + " / " + L.OnOff(r.AmbulanceAttended));
            if (r.LeftScene) Add(m, L.S("Left the scene", "Покинул место ДТП"), L.S("Yes", "Да"));
            if (!string.IsNullOrEmpty(r.Officer)) Add(m, L.S("Officer", "Офицер"), r.Officer);
            if (!string.IsNullOrEmpty(r.Findings))
                m.Add(new MenuItem(L.S("Findings (details below)", "Выводы (подробности ниже)"), null).WithDescription(r.Findings));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private static void Add(Menu m, string label, string value)
        {
            string v = value ?? "";
            m.Add(new MenuItem(label, null).WithValue(() => v));
        }

        // ------------------------------------------------------------ city incidents

        private Menu BuildIncidents()
        {
            Menu m = new Menu(L.S("City incidents", "Происшествия в городе"), L.S("Latest 25", "Последние 25"));
            m.Add(new MenuItem(L.S("NPC accidents seen", "ДТП NPC рядом"), null).WithValue(() => Data.Driving.NpcAccidentsSeen.ToString()));
            m.Add(new MenuItem(L.S("NPC police stops seen", "Остановок NPC полицией"), null).WithValue(() => Data.Driving.NpcStopsSeen.ToString()));
            List<CityIncident> list = Data.Incidents;
            int shown = 0;
            for (int i = list.Count - 1; i >= 0 && shown < 25; i--, shown++)
            {
                CityIncident c = list[i];
                DateTime t;
                string when = DateTime.TryParse(c.TimeUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out t) ? L.DateTimeLocal(t) : "";
                string kind = c.Kind == "Accident" ? L.S("Accident", "ДТП") : L.S("Police stop", "Остановка");
                string outcome = c.Outcome;
                m.Add(new MenuItem(when + "  " + kind, null).WithValue(() => c.Location)
                    .WithDescription(c.Description + "  " + outcome));
            }
            if (shown == 0) m.Add(new MenuItem(L.S("Nothing has happened nearby yet.", "Рядом пока ничего не происходило."), null));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        // ------------------------------------------------------------ receipts

        private Menu BuildReceipts()
        {
            Menu m = new Menu(L.S("Receipts", "Чеки и расходы"), L.S("Latest 25 entries", "Последние 25 записей"));
            m.Add(new MenuItem(L.S("Fuel bought", "Куплено топлива"), null).WithValue(() =>
                L.Liters(Data.Driving.FuelLitersBought) + "  /  " + L.Money(Data.Driving.FuelMoneySpent)));
            m.Add(new MenuItem(L.S("Fines paid", "Оплачено штрафов"), null).WithValue(() => L.Money(Data.Driving.FinesPaid)));

            List<Transaction> list = Data.Transactions;
            int shown = 0;
            for (int i = list.Count - 1; i >= 0 && shown < 25; i--, shown++)
            {
                Transaction t = list[i];
                DateTime when;
                string w = DateTime.TryParse(t.TimeUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out when) ? L.DateTimeLocal(when) : "";
                string cat = CategoryName(t.Category);
                double amount = t.Amount;
                m.Add(new MenuItem(cat + "  " + w, null).WithValue(() => L.Money(amount)).WithDescription(t.Description));
            }
            if (shown == 0) m.Add(new MenuItem(L.S("No receipts yet.", "Чеков пока нет."), null));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        // 0.6.0 fix: categories other than fuel, fines and taxi were shown in English only.
        private static string CategoryName(string c)
        {
            switch (c)
            {
                case "Fuel": return L.S("Fuel", "Топливо");
                case "Fine": return L.S("Fine", "Штраф");
                case "Taxi": return L.S("Taxi", "Такси");
                case "Insurance": return L.S("Insurance", "Страховка");
                case "Vehicle": return L.S("Vehicle", "Машина");
                case "Bills": return L.S("Bills", "Счета");
                case "Licence": return L.S("Licence", "Права");
                case "Medical": return L.S("Medical", "Медицина");
                case "Repair": return L.S("Repair", "Ремонт");
                case "Rent": return L.S("Rent", "Аренда");
                case "Property": return L.S("Property", "Недвижимость");
            }
            return c ?? "";
        }

        // ------------------------------------------------------------ settings

        private Menu BuildSettings()
        {
            Menu m = new Menu(L.S("Settings", "Настройки"), L.S("Changes are saved immediately", "Изменения сохраняются сразу"));

            m.Add(new MenuItem(L.S("Language", "Язык"), () =>
            {
                Settings.Language = (LanguageSetting)(((int)Settings.Language + 1) % 3);
                SaveSettings();
                main.ApplyLanguage();
                Menu.Replace(BuildSettings());
            }).WithValue(() => L.LanguageName(Settings.Language))
              .WithDescription(L.S("Automatic = Russian if the game or Windows uses Russian.",
                                   "Автоматически = русский, если игра или Windows на русском.")));

            m.Add(new MenuItem(L.S("Performance profile", "Профиль производительности"), () =>
            {
                Settings.Profile = (PerformanceProfileKind)(((int)Settings.Profile + 1) % 3);
                SaveSettings();
                main.ApplyProfile();
            }).WithValue(() => ProfileName(Settings.Profile))
              .WithDescription(L.S("Low keeps FPS stable on weaker PCs: fewer NPCs are simulated in detail.",
                                   "Низкий сохраняет стабильный FPS на слабых ПК: меньше NPC моделируется подробно.")));

            m.Add(new MenuItem(L.S("Fuel", "Топливо"), () => Menu.Push(BuildFuelSettings())).WithValue(More));
            m.Add(new MenuItem(L.S("Traffic & fines", "Штрафы и ПДД"), () => Menu.Push(BuildTrafficSettings())).WithValue(More));
            m.Add(new MenuItem(L.S("Police and accidents", "Полиция и ДТП"), () => Menu.Push(BuildPoliceSettings())).WithValue(More));
            m.Add(new MenuItem(L.S("NPC traffic", "Трафик NPC"), () => Menu.Push(BuildNpcSettings())).WithValue(More));
            m.Add(new MenuItem(L.S("Property and vehicles", "Недвижимость и машины"), () => Menu.Push(BuildOwnershipSettings())).WithValue(More));
            m.Add(new MenuItem(L.S("Passengers and taxi", "Пассажиры и такси"), () => { if (main.RideMenus != null) Menu.Push(main.RideMenus.BuildSettings()); }).WithValue(More));

            m.Add(new MenuItem(L.S("Autosave every", "Автосохранение"), () =>
            {
                Settings.AutosaveMinutes = Next(RlsSettings.AutosaveChoices, Settings.AutosaveMinutes);
                SaveSettings();
            }).WithValue(() => L.F("{0} min", "{0} мин", Settings.AutosaveMinutes)));

            m.Add(new MenuItem(L.S("Life Hub map icons", "Значки центра на карте"), () =>
            {
                Settings.ShowHubBlips = !Settings.ShowHubBlips;
                SaveSettings();
                if (main.Hubs != null) main.Hubs.RefreshBlips();
            }).WithValue(() => L.OnOff(Settings.ShowHubBlips)));

            m.Add(new MenuItem(L.S("Debug logging", "Подробный журнал"), () =>
            {
                Settings.DebugLogging = !Settings.DebugLogging;
                Log.DebugEnabled = Settings.DebugLogging;
                SaveSettings();
            }).WithValue(() => L.OnOff(Settings.DebugLogging))
              .WithDescription(L.S("Extra detail in Logs\\RealisticLifeSimulator.log. Leave off for normal play.",
                                   "Больше подробностей в Logs\\RealisticLifeSimulator.log. Для обычной игры не нужно.")));

            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private Menu BuildFuelSettings()
        {
            Menu m = new Menu(L.S("Fuel", "Топливо"), L.S("Fuel economy settings", "Настройки расхода топлива"));
            m.Add(new MenuItem(L.S("Fuel system", "Система топлива"), () => { Settings.FuelEnabled = !Settings.FuelEnabled; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.FuelEnabled)));
            m.Add(new MenuItem(L.S("Price per liter", "Цена за литр"), () =>
            {
                Settings.FuelPricePerLiter = Next(RlsSettings.FuelPriceChoices, Settings.FuelPricePerLiter);
                SaveSettings();
            }).WithValue(() => L.Money(Settings.FuelPricePerLiter)));
            m.Add(new MenuItem(L.S("Consumption", "Расход"), () =>
            {
                Settings.FuelConsumption = Next(RlsSettings.ConsumptionChoices, Settings.FuelConsumption);
                SaveSettings();
            }).WithValue(() => "x" + Settings.FuelConsumption.ToString("0.00", L.Culture))
              .WithDescription(L.S("x1.00: a full sedan tank lasts about 1-1.5 hours of driving.",
                                   "x1.00: полного бака седана хватает примерно на 1-1,5 часа езды.")));
            m.Add(new MenuItem(L.S("Fuel gauge", "Указатель топлива"), () =>
            {
                Settings.FuelGauge = (FuelGaugeMode)(((int)Settings.FuelGauge + 1) % 3);
                SaveSettings();
            }).WithValue(() => GaugeName(Settings.FuelGauge))
              .WithDescription(L.S("Shown above your speedometer while driving.", "Показывается над спидометром во время езды.")));
            m.Add(new MenuItem(L.S("NPCs refuel at stations", "NPC заправляются на АЗС"), () =>
            {
                Settings.NpcRefuelling = !Settings.NpcRefuelling;
                SaveSettings();
            }).WithValue(() => L.OnOff(Settings.NpcRefuelling)));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private Menu BuildTrafficSettings()
        {
            Menu m = new Menu(L.S("Traffic & fines", "Штрафы и ПДД"), L.S("Detection, enforcement and fines", "Обнаружение, контроль и штрафы"));
            m.Add(new MenuItem(L.S("Traffic laws", "Правила движения"), () => { Settings.TrafficEnabled = !Settings.TrafficEnabled; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.TrafficEnabled)));
            m.Add(new MenuItem(L.S("Fines", "Штрафы"), () =>
            {
                int idx = Array.IndexOf(RlsSettings.FineModeOrder, Settings.Fines);
                Settings.Fines = RlsSettings.FineModeOrder[(idx + 1) % RlsSettings.FineModeOrder.Length];
                SaveSettings();
                Menu.Replace(BuildTrafficSettings());
            }).WithValue(() => FineModeName(Settings.Fines))
              .WithDescription(FineModeHelp(Settings.Fines)));
            m.Add(new MenuItem(L.S("Automatic enforcement", "Автоматический контроль"), () => Menu.Push(BuildAutoEnforcement())).WithValue(More)
                .WithDescription(L.S("Which violations cameras and patrol radar fine without an officer stopping you.",
                                     "Какие нарушения камеры и радары штрафуют без остановки полицией.")));
            m.Add(new MenuItem(L.S("Fine amounts", "Суммы штрафов"), () => Menu.Push(BuildFineAmounts())).WithValue(More)
                .WithDescription(L.S("Base amount for each violation, before the multiplier below.", "Базовая сумма за каждое нарушение до множителя ниже.")));
            m.Add(new MenuItem(L.S("Speed tolerance", "Допуск скорости"), () =>
            {
                Settings.SpeedToleranceKmh = Next(RlsSettings.ToleranceChoices, Settings.SpeedToleranceKmh);
                SaveSettings();
            }).WithValue(() => "+" + Settings.SpeedToleranceKmh + L.S(" km/h", " км/ч")));
            m.Add(new MenuItem(L.S("Fine amounts", "Размер штрафов"), () =>
            {
                Settings.FineMultiplier = Next(RlsSettings.FineMultiplierChoices, Settings.FineMultiplier);
                SaveSettings();
            }).WithValue(() => "x" + Settings.FineMultiplier.ToString("0.0", L.Culture)));
            m.Add(new MenuItem(L.S("Show speed limit", "Показывать ограничение"), () => { Settings.ShowSpeedLimit = !Settings.ShowSpeedLimit; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.ShowSpeedLimit)));
            m.Add(new MenuItem(L.S("Speeding", "Превышение скорости"), () => { Settings.DetectSpeeding = !Settings.DetectSpeeding; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.DetectSpeeding)));
            m.Add(new MenuItem(L.S("Red lights", "Красный свет"), () => { Settings.DetectRedLights = !Settings.DetectRedLights; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.DetectRedLights))
                .WithDescription(L.S("Detected when cars beside you are waiting at the light.", "Определяется по машинам, стоящим рядом на светофоре.")));
            m.Add(new MenuItem(L.S("Stop / give way", "Стоп / уступи дорогу"), () => { Settings.DetectStopSigns = !Settings.DetectStopSigns; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.DetectStopSigns)));
            m.Add(new MenuItem(L.S("Wrong way", "Встречная полоса"), () => { Settings.DetectWrongWay = !Settings.DetectWrongWay; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.DetectWrongWay)));
            m.Add(new MenuItem(L.S("Headlights at night", "Фары ночью"), () => { Settings.DetectHeadlights = !Settings.DetectHeadlights; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.DetectHeadlights)));
            m.Add(new MenuItem(L.S("Shorter red lights", "Короче красный свет"), () => { Settings.ShorterRedLights = !Settings.ShorterRedLights; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.ShorterRedLights))
                .WithDescription(L.S("First in line at a red light for too long: your direction gets a short green, cross traffic waits. GTA's own cycle is unchanged.",
                                     "Если вы первым долго стоите на красный: ваше направление получает короткий зелёный, поперечный поток ждёт. Цикл GTA не меняется.")));
            m.Add(new MenuItem(L.S("Longest red-light wait", "Максимальное ожидание на красный"), () =>
            {
                Settings.MaxRedWaitSeconds = Next(TrafficLightRules.MaxWaitChoices, Settings.MaxRedWaitSeconds);
                SaveSettings();
            }).WithValue(() => L.F("{0} s", "{0} с", Settings.MaxRedWaitSeconds)).WithEnabled(() => Settings.ShorterRedLights));
            m.Add(new MenuItem(L.S("Assisted green phase", "Длительность зелёного"), () =>
            {
                Settings.AssistGreenSeconds = Next(TrafficLightRules.GreenChoices, Settings.AssistGreenSeconds);
                SaveSettings();
            }).WithValue(() => L.F("{0} s + 3 s amber", "{0} с + 3 с жёлтый", Settings.AssistGreenSeconds)).WithEnabled(() => Settings.ShorterRedLights));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private Menu BuildAutoEnforcement()
        {
            AutoEnforcement a = Settings.Auto;
            Menu m = new Menu(L.S("Automatic enforcement", "Автоматический контроль"), L.S("Used in the Automatic fine mode", "Действует в режиме «Автоматически»"));
            AddToggle(m, L.S("Speeding (camera / radar)", "Превышение (камера / радар)"), () => a.Speeding, v => a.Speeding = v,
                L.S("Fined after 3 s above the limit plus tolerance; once per episode.", "Штраф после 3 с выше лимита с допуском; один раз за эпизод."));
            AddToggle(m, L.S("Red light (camera)", "Красный свет (камера)"), () => a.RedLight, v => a.RedLight = v,
                L.S("Only when two or more cars were waiting at the light. One waiting car is recorded, not fined.",
                    "Только если на светофоре стояли две и более машины. Одна машина - только запись."));
            AddToggle(m, L.S("Wrong way", "Встречная полоса"), () => a.WrongWay, v => a.WrongWay = v,
                L.S("Only when road geometry and oncoming traffic agree for 4 s.", "Только если геометрия дороги и встречный поток совпадают 4 с."));
            AddToggle(m, L.S("Not yielding to sirens", "Не уступил спецтранспорту"), () => a.FailureToYield, v => a.FailureToYield = v, "");
            AddToggle(m, L.S("Stop / give way", "Стоп / уступи дорогу"), () => a.StopSign, v => a.StopSign = v,
                L.S("Off by default: GTA's junction data is too unreliable for camera fines.", "По умолчанию выкл: данные перекрёстков GTA ненадёжны для камер."));
            AddToggle(m, L.S("Headlights at night", "Фары ночью"), () => a.Headlights, v => a.Headlights = v, "");
            AddToggle(m, L.S("Pay camera fines from cash", "Оплачивать штрафы сразу наличными"), () => a.AutoPayFromCash, v => a.AutoPayFromCash = v,
                L.S("Off: fines wait in Traffic & fines until you pay them.", "Выкл: штрафы ждут оплаты в разделе «Штрафы и ПДД»."));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private void AddToggle(Menu m, string label, Func<bool> get, Action<bool> set, string description)
        {
            m.Add(new MenuItem(label, () => { set(!get()); SaveSettings(); }).WithValue(() => L.OnOff(get())).WithDescription(description));
        }

        private Menu BuildFineAmounts()
        {
            FineTable f = Settings.FineAmounts;
            Menu m = new Menu(L.S("Fine amounts", "Суммы штрафов"), L.F("Multiplier x{0}", "Множитель x{0}", Settings.FineMultiplier.ToString("0.0", L.Culture)));
            AddAmount(m, ViolationType.Speeding, L.S("Speeding up to 20 km/h", "Превышение до 20 км/ч"), () => f.SpeedingMinor, v => f.SpeedingMinor = v);
            AddAmount(m, ViolationType.Speeding, L.S("Speeding 21-40 km/h", "Превышение 21-40 км/ч"), () => f.SpeedingMajor, v => f.SpeedingMajor = v);
            AddAmount(m, ViolationType.Speeding, L.S("Speeding over 40 km/h", "Превышение более 40 км/ч"), () => f.SpeedingExtreme, v => f.SpeedingExtreme = v);
            AddAmount(m, ViolationType.RedLight, null, () => f.RedLight, v => f.RedLight = v);
            AddAmount(m, ViolationType.StopSign, null, () => f.StopSign, v => f.StopSign = v);
            AddAmount(m, ViolationType.WrongWay, null, () => f.WrongWay, v => f.WrongWay = v);
            AddAmount(m, ViolationType.NoHeadlights, null, () => f.NoHeadlights, v => f.NoHeadlights = v);
            AddAmount(m, ViolationType.FailureToYieldEmergency, null, () => f.FailureToYield, v => f.FailureToYield = v);
            AddAmount(m, ViolationType.Reckless, null, () => f.Reckless, v => f.Reckless = v);
            AddAmount(m, ViolationType.LeavingScene, null, () => f.LeavingScene, v => f.LeavingScene = v);
            AddAmount(m, ViolationType.HitPedestrian, null, () => f.HitPedestrian, v => f.HitPedestrian = v);
            AddAmount(m, ViolationType.SuspendedLicence, L.S("Driving without a valid licence", "Езда без действующих прав"), () => f.UnlicensedDriving, v => f.UnlicensedDriving = v);
            AddAmount(m, ViolationType.NoDocuments, null, () => f.NoDocuments, v => f.NoDocuments = v);
            AddAmount(m, ViolationType.EvadingPolice, null, () => f.EvadingPolice, v => f.EvadingPolice = v);
            AddAmount(m, ViolationType.FalseStatement, null, () => f.FalseStatement, v => f.FalseStatement = v);
            m.Add(new MenuItem(L.S("Reset to defaults", "Сбросить по умолчанию"), () =>
            {
                Settings.FineAmounts = new FineTable();
                TrafficRules.Fines = Settings.FineAmounts;
                SaveSettings();
                Menu.Replace(BuildFineAmounts());
            }));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private void AddAmount(Menu m, ViolationType t, string label, Func<int> get, Action<int> set)
        {
            m.Add(new MenuItem(label ?? TrafficRules.Name(t), () =>
            {
                set(NextAmount(get()));
                SaveSettings();
            }).WithValue(() => L.Money(get())).WithDescription(L.S("Click to cycle through amounts. Any value can be set in Config\\Settings.ini [FineAmounts].",
                                                                   "Нажмите, чтобы выбрать сумму. Любое значение можно задать в Config\\Settings.ini [FineAmounts].")));
        }

        private static int NextAmount(int current)
        {
            foreach (int c in FineTable.Choices) if (c > current) return c;
            return FineTable.Choices[0];
        }

        private static string FineModeHelp(FineMode f)
        {
            switch (f)
            {
                case FineMode.Witnessed: return L.S("Fined only when a police officer sees the violation.", "Штраф только если нарушение видел полицейский.");
                case FineMode.Always: return L.S("Every detected violation is fined, even uncertain detections.", "Штраф за каждое обнаруженное нарушение, даже сомнительное.");
                case FineMode.Off: return L.S("Violations are recorded, never fined.", "Нарушения только записываются, без штрафов.");
            }
            return L.S("Police stop you when they see it; otherwise cameras fine reliable detections (speeding, red lights...). Uncertain ones are only recorded.",
                       "Полиция останавливает, если видит; иначе камеры штрафуют за надёжно обнаруженные нарушения (скорость, красный...). Сомнительные - только запись.");
        }

        private Menu BuildPoliceSettings()
        {
            Menu m = new Menu(L.S("Police and accidents", "Полиция и ДТП"), L.S("Changes are saved immediately", "Изменения сохраняются сразу"));
            m.Add(new MenuItem(L.S("Police pull you over", "Полиция останавливает вас"), () => { Settings.PoliceStops = !Settings.PoliceStops; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.PoliceStops))
                .WithDescription(L.S("Off: witnessed violations become a ticket by notification instead of a roadside stop.",
                                     "Выкл: вместо остановки штраф приходит уведомлением.")));
            m.Add(new MenuItem(L.S("Police stop NPC drivers", "Полиция останавливает NPC"), () => { Settings.NpcPoliceStops = !Settings.NpcPoliceStops; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.NpcPoliceStops)));
            m.Add(new MenuItem(L.S("Accident system", "Система ДТП"), () => { Settings.AccidentsEnabled = !Settings.AccidentsEnabled; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.AccidentsEnabled)));
            m.Add(new MenuItem(L.S("Ignore impacts below", "Игнорировать удары ниже"), () =>
            {
                Settings.AccidentMinSpeedKmh = Next(new[] { 8, 12, 20, 30 }, Settings.AccidentMinSpeedKmh);
                SaveSettings();
            }).WithValue(() => Settings.AccidentMinSpeedKmh + L.S(" km/h", " км/ч"))
              .WithDescription(L.S("Light bumps below this speed are not treated as accidents.", "Лёгкие касания ниже этой скорости не считаются ДТП.")));
            m.Add(new MenuItem(L.S("Other drivers and witnesses call 911", "Водители и свидетели звонят в 911"), () => { Settings.NpcCallEmergency = !Settings.NpcCallEmergency; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.NpcCallEmergency)));
            m.Add(new MenuItem(L.S("Pedestrian accidents", "ДТП с пешеходами"), () => { Settings.PedestrianAccidents = !Settings.PedestrianAccidents; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.PedestrianAccidents))
                .WithDescription(L.S("Hitting a pedestrian opens an accident scene: ambulance, police investigation, fines and licence consequences.",
                                     "Наезд на пешехода открывает ДТП: скорая, разбирательство полиции, штрафы и последствия для прав.")));
            m.Add(new MenuItem(L.S("Police patrols", "Полицейские патрули"), () =>
            {
                Settings.PolicePatrols = (PoliceDensity)(((int)Settings.PolicePatrols + 1) % 4);
                SaveSettings();
            }).WithValue(() => PatrolRules.DensityName(Settings.PolicePatrols))
              .WithDescription(L.S("Extra patrol cars and officers (1-4 at most, fewer on the Low profile). They appear far away and drive in on the roads.",
                                   "Дополнительные патрули (не более 1-4, меньше на профиле «Низкий»). Появляются далеко и приезжают по дорогам.")));
            m.Add(new MenuItem(L.S("Patrol licence checks", "Проверка прав патрулями"), () => { Settings.PatrolLicenceChecks = !Settings.PatrolLicenceChecks; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.PatrolLicenceChecks))
                .WithDescription(L.S("A patrol that sees you driving with a suspended or revoked licence may pull you over.",
                                     "Патруль, увидевший вас за рулём без действующих прав, может вас остановить.")));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private Menu BuildNpcSettings()
        {
            Menu m = new Menu(L.S("NPC traffic", "Трафик NPC"), L.S("How other drivers behave", "Как ведут себя другие водители"));
            m.Add(new MenuItem(L.S("Driver personalities and speed limits", "Характеры водителей и ограничения"), () => { Settings.NpcTraffic = !Settings.NpcTraffic; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.NpcTraffic))
                .WithDescription(L.S("Careful, average, aggressive, slow, professional and distracted drivers, each with its own speed and reactions.",
                                     "Аккуратные, обычные, агрессивные, медленные, профессиональные и рассеянные водители со своей скоростью и реакцией.")));
            m.Add(new MenuItem(L.S("Occasional mistakes", "Редкие ошибки"), () => { Settings.NpcMistakes = !Settings.NpcMistakes; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.NpcMistakes))
                .WithDescription(L.S("Running a light, speeding for a while or braking late. Police can stop them for it.",
                                     "Проезд на красный, превышение или позднее торможение. Полиция может их за это остановить.")));
            m.Add(new MenuItem(L.S("Make way for sirens", "Уступать машинам с сиреной"), () => { Settings.NpcYieldEmergency = !Settings.NpcYieldEmergency; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.NpcYieldEmergency)));
            m.Add(new MenuItem(L.S("NPC accident scenes", "ДТП между NPC"), () => { Settings.NpcAccidents = !Settings.NpcAccidents; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.NpcAccidents)));
            m.Add(new MenuItem(L.S("Detect: you not yielding to sirens", "Нарушение: не уступили спецтранспорту"), () => { Settings.DetectFailureToYield = !Settings.DetectFailureToYield; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.DetectFailureToYield)));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private Menu BuildOwnershipSettings()
        {
            Menu m = new Menu(L.S("Property and vehicles", "Недвижимость и машины"), L.S("Changes are saved immediately", "Изменения сохраняются сразу"));
            m.Add(new MenuItem(L.S("Bills every", "Счета каждые"), () =>
            {
                Settings.BillingIntervalMinutes = Next(RlsSettings.BillingChoices, Settings.BillingIntervalMinutes);
                SaveSettings();
            }).WithValue(() => L.F("{0} min of play", "{0} мин игры", Settings.BillingIntervalMinutes)));
            m.Add(new MenuItem(L.S("Vehicle wear", "Износ машин"), () => { Settings.VehicleWear = !Settings.VehicleWear; SaveSettings(); })
                .WithValue(() => L.OnOff(Settings.VehicleWear)));
            m.Add(new MenuItem(L.S("Wear speed", "Скорость износа"), () =>
            {
                Settings.WearRate = Next(RlsSettings.WearChoices, Settings.WearRate);
                SaveSettings();
            }).WithValue(() => "x" + Settings.WearRate.ToString("0.0", L.Culture)));
            m.Add(new MenuItem(L.S("Service interval", "Интервал ТО"), () =>
            {
                Settings.ServiceIntervalKm = Next(new[] { 200, 400, 800, 1500 }, Settings.ServiceIntervalKm);
                SaveSettings();
            }).WithValue(() => Settings.ServiceIntervalKm + L.S(" km", " км")));
            m.Add(new MenuItem(L.S("Properties for sale on map", "Объекты на продажу на карте"), () =>
            {
                Settings.ShowPropertiesOnMap = !Settings.ShowPropertiesOnMap;
                SaveSettings();
                if (main.Properties != null) main.Properties.RefreshBlips();
            }).WithValue(() => L.OnOff(Settings.ShowPropertiesOnMap)));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private static int Next(int[] choices, int current)
        {
            int idx = Array.IndexOf(choices, current);
            return choices[(idx + 1) % choices.Length];
        }

        private static float Next(float[] choices, float current)
        {
            int idx = -1;
            for (int i = 0; i < choices.Length; i++) if (Math.Abs(choices[i] - current) < 0.001f) idx = i;
            return choices[(idx + 1) % choices.Length];
        }

        private static string ProfileName(PerformanceProfileKind k)
        {
            switch (k)
            {
                case PerformanceProfileKind.High: return L.S("High", "Высокий");
                case PerformanceProfileKind.Balanced: return L.S("Balanced", "Средний");
            }
            return L.S("Low", "Низкий");
        }

        private static string GaugeName(FuelGaugeMode g)
        {
            switch (g)
            {
                case FuelGaugeMode.LowOnly: return L.S("Only when low", "Только при низком");
                case FuelGaugeMode.Off: return L.S("Off", "Выкл");
            }
            return L.S("Always", "Всегда");
        }

        private static string FineModeName(FineMode f)
        {
            switch (f)
            {
                case FineMode.Always: return L.S("Always", "Всегда");
                case FineMode.Off: return L.S("Off (record only)", "Выкл (только запись)");
                case FineMode.Witnessed: return L.S("Witnessed", "При свидетелях");
            }
            return L.S("Automatic", "Автоматически");
        }

        private void SaveSettings()
        {
            try { main.Config.SaveSettings(); }
            catch (Exception ex) { Log.Error("Could not save settings", ex); }
        }

        // ------------------------------------------------------------ key bindings

        private Menu BuildBindings()
        {
            Menu m = new Menu(L.S("Key bindings", "Клавиши"), L.S("Nothing is assigned until you choose a key", "Ничего не назначено, пока вы сами не выберете"));
            foreach (RlsAction a in ActionCatalog.All)
            {
                RlsAction action = a;
                bool available = main.IsActionAvailable(action);
                m.Add(new MenuItem(ActionCatalog.DisplayName(action), () => Menu.Push(BuildBindingDetail(action)))
                    .WithValue(() => KeyText(action))
                    .WithDescription(available
                        ? L.S("Click to assign, change or remove a key.", "Нажмите, чтобы назначить, изменить или убрать клавишу.")
                        : L.S("This feature arrives in a later phase. You can assign a key now.",
                              "Эта функция появится на следующих этапах. Клавишу можно назначить уже сейчас.")));
            }
            m.Add(new MenuItem(L.S("Re-check other mods for keys", "Перепроверить клавиши других модов"), () =>
            {
                main.Conflicts.ScanExternal(main.Paths.GameRoot);
                main.Notify(L.F("Checked {0} config file(s) for key conflicts.", "Проверено файлов настроек: {0}.", main.Conflicts.ScannedFiles.Count));
            }).WithDescription(L.S("Reads ScriptHookVDotNet.ini and the INI files in your scripts folder (read only).",
                                   "Читает ScriptHookVDotNet.ini и INI-файлы в папке scripts (только чтение).")));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private string KeyText(RlsAction a)
        {
            Keys k = main.Config.Bindings.Get(a);
            return k == Keys.None ? L.S("Unassigned", "Не назначено") : k.ToString();
        }

        private Menu BuildBindingDetail(RlsAction action)
        {
            Menu m = new Menu(ActionCatalog.DisplayName(action), L.S("Current key: ", "Текущая клавиша: ") + KeyText(action));
            m.Add(new MenuItem(L.S("Assign a key", "Назначить клавишу"), () => StartCapture(action))
                .WithDescription(L.S("Click, then press the key you want. Esc cancels.", "Нажмите, затем нажмите нужную клавишу. Esc - отмена.")));
            m.Add(new MenuItem(L.S("Remove key", "Убрать клавишу"), () =>
            {
                main.Config.Bindings.Clear(action);
                SaveBindings();
                Menu.Replace(BuildBindingDetail(action));
            }).WithEnabled(() => main.Config.Bindings.IsAssigned(action))
              .WithDescription(L.S("Leaves this action without a key.", "Действие останется без клавиши.")));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }

        private void StartCapture(RlsAction action)
        {
            Menu.BeginKeyCapture(L.F("Press a key for '{0}'.", "Нажмите клавишу для «{0}».", ActionCatalog.DisplayName(action)), key =>
            {
                string reason;
                if (!KeyRules.IsAssignable(key, out reason))
                {
                    main.Notify(reason);
                    return;
                }
                List<string> conflicts = main.Conflicts.FindConflicts(key, action, main.Config.Bindings);
                if (conflicts.Count == 0)
                {
                    Assign(action, key);
                    return;
                }
                Menu.Push(BuildConflict(action, key, conflicts));
            });
        }

        private Menu BuildConflict(RlsAction action, Keys key, List<string> conflicts)
        {
            string first = conflicts[0] + (conflicts.Count > 1 ? " (+" + (conflicts.Count - 1) + ")" : "");
            Menu m = new Menu(L.S("Key already in use", "Клавиша уже занята"), L.F("{0} is used by: {1}", "{0} используется: {1}", key, first));
            string all = string.Join("; ", conflicts.ToArray());
            bool own = main.Conflicts.HasOwnConflict(key, action, main.Config.Bindings);
            m.Add(new MenuItem(L.F("Use {0} anyway", "Всё равно использовать {0}", key), () =>
            {
                Menu.Back();
                Assign(action, key);
            }).WithDescription(L.S("Conflicts: ", "Конфликты: ") + all + (own
                ? L.S(". The other action in this mod will lose this key.", ". Другое действие этого мода потеряет эту клавишу.")
                : L.S(". Both will react to this key.", ". Обе функции будут реагировать на клавишу."))));
            m.Add(new MenuItem(L.S("Choose a different key", "Выбрать другую клавишу"), () =>
            {
                Menu.Back();
                StartCapture(action);
            }));
            m.Add(new MenuItem(L.S("Cancel", "Отмена"), () => Menu.Back()));
            return m;
        }

        private void Assign(RlsAction action, Keys key)
        {
            foreach (RlsAction other in main.Config.Bindings.ActionsUsing(key))
                if (other != action) main.Config.Bindings.Clear(other);
            main.Config.Bindings.Set(action, key);
            SaveBindings();
            Menu.Replace(BuildBindingDetail(action));
        }

        private void SaveBindings()
        {
            try { main.Config.SaveBindings(); }
            catch (Exception ex) { Log.Error("Could not save key bindings", ex); }
        }

        // ------------------------------------------------------------ status

        private Menu BuildStatus()
        {
            Menu m = new Menu(L.S("Save and status", "Сохранение и состояние"), "Realistic Life Simulator " + RlsMain.Version);
            m.Add(new MenuItem(L.S("Save file", "Файл сохранения"), null).WithValue(() => main.Saves.StatusText)
                .WithDescription(L.S("Saves\\profile.xml, with 5 rolling backups and daily snapshots in Saves\\Backups.",
                                     "Saves\\profile.xml, 5 резервных копий и ежедневные снимки в Saves\\Backups.")));
            m.Add(new MenuItem(L.S("Last saved", "Последнее сохранение"), null).WithValue(() =>
                main.Saves.LastSaveLocal == DateTime.MinValue ? L.S("Not yet", "Ещё нет") : main.Saves.LastSaveLocal.ToString("HH:mm:ss")));
            m.Add(new MenuItem(L.S("Play time with mod", "Время игры с модом"), null).WithValue(() =>
            {
                TimeSpan t = TimeSpan.FromSeconds(Data.Stats.TotalPlayTimeSeconds);
                return L.F("{0} h {1} min", "{0} ч {1} мин", (int)t.TotalHours, t.Minutes);
            }));
            m.Add(new MenuItem(L.S("Sessions", "Сеансов"), null).WithValue(() => Data.Stats.SessionsStarted.ToString()));
            m.Add(new MenuItem(L.S("Mod frame cost", "Нагрузка мода на кадр"), null).WithValue(() =>
                L.F("{0:0.00} ms avg, {1:0.00} peak", "в среднем {0:0.00} мс, пик {1:0.00}", main.Performance.AverageMs, main.Performance.PeakMs))
                .WithDescription(L.S("Time this mod uses per frame (measured over 2 s), including this menu.",
                                     "Время, которое мод тратит на кадр (за 2 с), включая это меню.")));
            m.Add(new MenuItem(L.S("NPC fuel simulation", "Топливо NPC"), null).WithValue(() =>
                main.NpcFuel == null ? "-" : L.F("{0} tracked, {1} refuelling", "отслеживается {0}, заправляются {1}", main.NpcFuel.Tracked, main.NpcFuel.ActiveTrips)));
            m.Add(new MenuItem(L.S("NPC drivers nearby", "Водители NPC рядом"), null).WithValue(() =>
                main.NpcTraffic == null ? "-" : main.NpcTraffic.Tracked.ToString())
                .WithDescription(main.NpcTraffic == null ? "" : main.NpcTraffic.Summary()));
            m.Add(new MenuItem(L.S("Active scenes", "Активные события"), null).WithValue(() =>
                L.F("{0} NPC accident(s), {1} NPC stop(s)", "ДТП NPC: {0}, остановок: {1}",
                    main.NpcAccidents == null ? 0 : main.NpcAccidents.Active, main.Police == null ? 0 : main.Police.NpcStopsActive)));
            m.Add(new MenuItem(L.S("Extra police patrols", "Дополнительные патрули"), null).WithValue(() =>
                main.Patrols == null ? "-" : main.Patrols.Count + " / " + PatrolRules.ExtraUnitsCap(Settings.PolicePatrols, main.Performance.Profile.Kind))
                .WithDescription(main.Patrols == null ? "" : main.Patrols.Summary()));
            m.Add(new MenuItem(L.S("Red-light assists this session", "Помощь на светофорах за сеанс"), null).WithValue(() =>
                main.Traffic == null || main.Traffic.Lights == null ? "-" : main.Traffic.Lights.Assists.ToString()));
            m.Add(new MenuItem(L.S("Profile budget", "Бюджет профиля"), null).WithValue(() => ProfileName(main.Performance.Profile.Kind))
                .WithDescription(main.Performance.Profile.Summary));
            m.Add(new MenuItem(L.S("Save now", "Сохранить сейчас"), () =>
            {
                bool ok = main.Saves.Save();
                main.Notify(ok ? L.S("Saved.", "Сохранено.") : L.S("Save failed: ", "Ошибка сохранения: ") + main.Saves.LastError);
            }).WithEnabled(() => !main.Saves.ReadOnly));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => Menu.Back()));
            return m;
        }
    }
}
