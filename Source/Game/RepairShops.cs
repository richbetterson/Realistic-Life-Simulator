// Realistic Life Simulator - repair shops (mechanic menu).
// Park within 25 m, talk to the mechanic on foot: inspection, service, damage repair.
// Owned vehicles get insurance cover and a service history; any other car can still be
// repaired at full price.

using System;
using GTA;
using GTA.Math;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public sealed class RepairShops
    {
        private readonly RlsMain main;

        public RepairShops(RlsMain main) { this.main = main; }

        private Vehicle FindCar(Shop s)
        {
            Vehicle last = GTA.Game.Player.LastVehicle;
            if (last != null && last.Exists() && !last.IsDead && last.Position.DistanceTo(s.Position) < 25f) return last;
            return null;
        }

        public void Open(Shop s)
        {
            Vehicle v = FindCar(s);
            if (v == null)
            {
                main.Notify(L.S("Park your car within 25 m of the shop first.", "Сначала поставьте машину в пределах 25 м от сервиса."));
                return;
            }
            main.Menu.Open(Build(s, v));
        }

        private Menu Build(Shop s, Vehicle v)
        {
            OwnedVehicle o = main.Ownership.RecordFor(v);
            int cls = (int)v.ClassType;
            double factor = OwnershipRules.ClassFactor(cls);
            Menu m = new Menu(s.Name, N.VehicleName(v) + "  " + N.Plate(v));

            m.Add(new MenuItem(L.S("Body", "Кузов"), null).WithValue(() => Pct(v.BodyHealth)));
            m.Add(new MenuItem(L.S("Engine (damage)", "Двигатель (повреждения)"), null).WithValue(() => Pct(v.EngineHealth)));
            if (o != null)
            {
                m.Add(new MenuItem(L.S("Engine condition (wear)", "Состояние двигателя (износ)"), null).WithValue(() => o.Condition.ToString("0", L.Culture) + "%"));
                m.Add(new MenuItem(L.S("Mileage", "Пробег"), null).WithValue(() =>
                    L.F("{0:0} km, {1:0} since service", "{0:0} км, {1:0} после ТО", o.MileageKm, o.MileageKm - o.KmAtService)));
                m.Add(new MenuItem(L.S("Insurance", "Страховка"), null).WithValue(() => InsuranceName(o.Insurance)));
            }
            else
            {
                m.Add(new MenuItem(L.S("Not registered to you", "Не зарегистрирована на вас"), null)
                    .WithDescription(L.S("Repairs are possible at full price. Register the car (Life Hub > My vehicles) for history and insurance.",
                                         "Ремонт возможен по полной цене. Зарегистрируйте машину (центр управления > Мои машины) для истории и страховки.")));
            }

            m.Add(new MenuItem(L.S("Inspection", "Диагностика"), () =>
            {
                if (!Pay(OwnershipRules.InspectionFee, L.S("Inspection", "Диагностика"), v)) return;
                string advice = Advice(v, o);
                PoliceStopSystem.Say(advice);
                if (o != null) Record(o, "Inspection", OwnershipRules.InspectionFee, 0);
                main.Menu.Replace(Build(s, v));
            }).WithValue(() => L.Money(OwnershipRules.InspectionFee))
              .WithDescription(L.S("The mechanic checks the car and tells you what it needs.", "Механик проверит машину и скажет, что ей нужно.")));

            double service = OwnershipRules.ServiceCost(cls);
            m.Add(new MenuItem(L.S("Service (oil, filters, check-up)", "ТО (масло, фильтры, проверка)"), () =>
            {
                if (!Pay(service, L.S("Service", "ТО"), v)) return;
                Work(1);
                o.Condition = Math.Max(o.Condition, OwnershipRules.ServiceCeiling(o.MileageKm));
                o.KmAtService = o.MileageKm;
                Record(o, "Service", service, 0);
                main.Notify(L.F("Service done. Engine condition {0:0}%.", "ТО выполнено. Состояние двигателя {0:0}%.", o.Condition));
                main.Menu.Replace(Build(s, v));
            }).WithEnabled(() => o != null)
              .WithValue(() => L.Money(service))
              .WithDescription(o == null ? L.S("Only for vehicles registered to you.", "Только для зарегистрированных на вас машин.")
                  : L.F("Restores engine condition up to {0:0}% (high mileage leaves some permanent wear).",
                        "Восстанавливает двигатель до {0:0}% (большой пробег оставляет необратимый износ).", OwnershipRules.ServiceCeiling(o.MileageKm))));

            m.Add(new MenuItem(L.S("Repair all damage", "Устранить все повреждения"), () =>
            {
                double cost = PoliceRules.DamageEstimate(v.BodyHealth, v.EngineHealth, factor);
                double own, ins;
                OwnershipRules.SplitRepairCost(cost, o != null ? o.Insurance : InsuranceLevel.None, out own, out ins);
                if (!Pay(own, L.S("Repair", "Ремонт"), v)) return;
                Work(1);
                VehicleLooks.Repair(v);
                if (o != null)
                {
                    o.BodyHealth = 1000f;
                    o.EngineHealth = 1000f;
                    Record(o, "Repair", own, ins);
                }
                main.Notify(ins > 0
                    ? L.F("Repaired. You paid {0}, insurance paid {1}.", "Отремонтировано. Вы заплатили {0}, страховка - {1}.", L.Money(own), L.Money(ins))
                    : L.F("Repaired for {0}.", "Отремонтировано за {0}.", L.Money(own)));
                main.Menu.Replace(Build(s, v));
            }).WithEnabled(() => v.BodyHealth < 995 || v.EngineHealth < 995)
              .WithValue(() =>
              {
                  double cost = PoliceRules.DamageEstimate(v.BodyHealth, v.EngineHealth, factor);
                  double own, ins;
                  OwnershipRules.SplitRepairCost(cost, o != null ? o.Insurance : InsuranceLevel.None, out own, out ins);
                  return ins > 0 ? L.Money(own) + L.F(" (+{0} ins.)", " (+{0} страх.)", L.Money(ins)) : L.Money(own);
              }));

            if (o != null)
                m.Add(new MenuItem(L.S("Service history", "История обслуживания"), () => main.Menu.Push(main.Menus.BuildServiceHistory(o))).WithValue(() => ">"));
            m.Add(new MenuItem(L.S("Close", "Закрыть"), () => main.Menu.Close()));
            return m;
        }

        private static string Pct(float health)
        {
            return Math.Max(0, Math.Min(100, health / 10f)).ToString("0", L.Culture) + "%";
        }

        private string Advice(Vehicle v, OwnedVehicle o)
        {
            string text = L.F("Body {0}, engine {1}.", "Кузов {0}, двигатель {1}.", Pct(v.BodyHealth), Pct(v.EngineHealth));
            if (o != null)
            {
                double since = o.MileageKm - o.KmAtService;
                text += " " + (since > main.Config.Settings.ServiceIntervalKm
                    ? L.F("Service overdue by {0:0} km.", "ТО просрочено на {0:0} км.", since - main.Config.Settings.ServiceIntervalKm)
                    : L.F("Next service in {0:0} km.", "Следующее ТО через {0:0} км.", main.Config.Settings.ServiceIntervalKm - since));
                if (o.Condition < 50) text += " " + L.S("The engine is worn and losing power.", "Двигатель изношен и теряет мощность.");
            }
            return text;
        }

        private bool Pay(double amount, string what, Vehicle v)
        {
            int cost = (int)Math.Round(amount);
            if (GTA.Game.Player.Money < cost)
            {
                main.Notify(L.S("Not enough cash.", "Недостаточно наличных."));
                return false;
            }
            GTA.Game.Player.Money -= cost;
            main.Saves.Data.AddTransaction("Repair", what + " - " + N.VehicleName(v), -cost);
            return true;
        }

        private static void Work(int hours)
        {
            Transition.FadeOut();
            Transition.AdvanceClock(hours);
            Script.Wait(600);
            Transition.FadeIn();
        }

        private void Record(OwnedVehicle o, string kind, double cost, double insurer)
        {
            ServiceEntry e = new ServiceEntry();
            e.TimeUtc = DateTime.UtcNow.ToString("o");
            e.Km = Math.Round(o.MileageKm, 1);
            e.Kind = kind;
            e.Cost = cost;
            e.InsurancePaid = insurer;
            o.Services.Add(e);
            if (o.Services.Count > 40) o.Services.RemoveAt(0);
            main.Saves.Save();
        }

        public static string InsuranceName(InsuranceLevel l)
        {
            switch (l)
            {
                case InsuranceLevel.Basic: return L.S("Basic", "Базовая");
                case InsuranceLevel.Full: return L.S("Full", "Полная");
            }
            return L.S("None", "Нет");
        }
    }
}
