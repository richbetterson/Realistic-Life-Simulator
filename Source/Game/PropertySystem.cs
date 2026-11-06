// Realistic Life Simulator - properties, garages and bills.
//
// For sale: walk to the door marker (map icon), see details, tour the interior, buy.
// Owned: enter (interior), rest (sleep 8 h), garage (store / take out owned cars),
// rent it out, sell (85 % of price).
// Garages: drive an owned car to the door marker of a property with garage slots and press
// the Context key to store it; on foot, open the garage to take a car out.
// Bills every N minutes of play: maintenance and insurance premiums out, rent in.
// Unpaid amounts become debt (settled automatically when cash allows; bank in Phase 6).
// Interiors are GTA Online interiors present in story mode; if one does not load, the
// player is returned outside immediately.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using GTA;
using GTA.Math;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public sealed class PropertySystem : IModule, IFrameModule
    {
        private sealed class Interior
        {
            public Vector3 Entry;
            public float Heading;
            public Interior(float x, float y, float z, float h) { Entry = new Vector3(x, y, z); Heading = h; }
        }

        private static readonly Dictionary<string, Interior> Interiors = new Dictionary<string, Interior>(StringComparer.OrdinalIgnoreCase)
        {
            { "LowApt", new Interior(261.4586f, -998.8196f, -99.00863f, 0f) },
            { "MidApt", new Interior(347.2686f, -999.2955f, -99.19622f, 0f) },
            { "HighApt", new Interior(-18.07856f, -583.6725f, 79.46569f, 160f) },
            { "Garage2", new Interior(173.2903f, -1003.6f, -99.65707f, 0f) },
            { "Garage6", new Interior(197.8153f, -1002.293f, -99.65749f, 0f) },
            { "Garage10", new Interior(229.9559f, -981.7928f, -99.66071f, 0f) }
        };

        private readonly RlsMain main;
        private List<PropertyDef> defs = new List<PropertyDef>();
        private readonly Dictionary<string, Blip> blips = new Dictionary<string, Blip>();
        private PropertyDef near;
        private float nearDist;
        private PropertyDef inside;
        private bool touring;
        private int nextBilling;

        public PropertySystem(RlsMain main) { this.main = main; }

        public string Name { get { return "Property"; } }
        public int IntervalMs { get { return 400; } }
        public IList<PropertyDef> Defs { get { return defs; } }
        private SaveData Data { get { return main.Saves.Data; } }
        private string Owner { get { return main.CurrentProfile().Character; } }

        public PropertyDef Def(string id)
        {
            foreach (PropertyDef d in defs) if (d.Id == id) return d;
            return null;
        }

        public OwnedProperty Owned(string id)
        {
            foreach (OwnedProperty p in Data.Properties) if (p.Id == id) return p;
            return null;
        }

        public bool OwnedByMe(PropertyDef d)
        {
            OwnedProperty p = Owned(d.Id);
            return p != null && p.Owner == Owner;
        }

        public int StoredCount(string id)
        {
            int n = 0;
            foreach (OwnedVehicle v in Data.OwnedVehicles) if (v.State == VehicleState.Stored && v.GarageId == id) n++;
            return n;
        }

        public void Start()
        {
            string f = main.Paths.PropertiesFile;
            if (!File.Exists(f)) FileUtil.WriteAllTextAtomic(f, OwnershipRules.DefaultPropertiesIni());
            List<string> problems = new List<string>();
            try { defs = OwnershipRules.ParseProperties(IniFile.Load(f), problems); }
            catch (Exception ex) { Log.Error("Properties.ini unreadable", ex); }
            foreach (string p in problems) Log.Warn("Properties.ini: " + p);
            if (Data.LastBillingPlaySeconds <= 0) Data.LastBillingPlaySeconds = Data.Stats.TotalPlayTimeSeconds;
            RefreshBlips();
            Log.Info("Properties loaded: " + defs.Count);
        }

        public void Shutdown()
        {
            foreach (Blip b in blips.Values) if (b != null && b.Exists()) b.Delete();
            blips.Clear();
        }

        public void RefreshBlips()
        {
            foreach (Blip b in blips.Values) if (b != null && b.Exists()) b.Delete();
            blips.Clear();
            foreach (PropertyDef d in defs)
            {
                bool mine = OwnedByMe(d);
                if (!mine && (!main.Config.Settings.ShowPropertiesOnMap || Owned(d.Id) != null)) continue;
                Blip b = World.CreateBlip(new Vector3(d.X, d.Y, d.Z));
                b.Sprite = d.Type == PropertyType.Garage
                    ? (mine ? BlipSprite.Garage : BlipSprite.GarageForSale)
                    : (mine ? BlipSprite.Safehouse : BlipSprite.SafehouseForSale);
                b.Color = mine ? BlipColor.Green : BlipColor.White;
                b.Scale = mine ? 0.85f : 0.7f;
                b.IsShortRange = !mine;
                b.Name = mine ? d.Name : L.F("For sale: {0}", "Продаётся: {0}", d.Name);
                blips[d.Id] = b;
            }
        }

        // ------------------------------------------------------------ update

        public void Update()
        {
            Ped p = GTA.Game.Player.Character;
            if (p == null || !p.Exists()) return;
            near = null;
            nearDist = 999f;
            Vector3 pos = p.Position;
            foreach (PropertyDef d in defs)
            {
                float dist = N.Dist2D(pos, new Vector3(d.X, d.Y, d.Z));
                if (dist < 40f && dist < nearDist && Math.Abs(pos.Z - d.Z) < 15f) { near = d; nearDist = dist; }
            }
            if (inside != null)
            {
                Interior i;
                if (!Interiors.TryGetValue(inside.Interior, out i) || pos.DistanceTo(i.Entry) > 80f) { inside = null; touring = false; }
            }

            int now = Environment.TickCount;
            if (unchecked(now - nextBilling) >= 0)
            {
                nextBilling = now + 5000;
                Billing();
            }
        }

        private void Billing()
        {
            double interval = main.Config.Settings.BillingIntervalMinutes * 60.0;
            double play = Data.Stats.TotalPlayTimeSeconds;
            if (play - Data.LastBillingPlaySeconds < interval) return;
            Data.LastBillingPlaySeconds = play;

            double maint, rent, prem;
            double balance = OwnershipRules.CycleBalance(Data.Properties, Def, Data.OwnedVehicles, Owner, out maint, out rent, out prem);
            if (maint == 0 && rent == 0 && prem == 0 && Data.Debt <= 0) return;

            if (rent > 0) Data.AddTransaction("Rent", L.S("Rental income", "Доход от аренды"), rent);
            if (maint > 0) Data.AddTransaction("Property", L.S("Property maintenance", "Содержание недвижимости"), -maint);
            if (prem > 0) Data.AddTransaction("Insurance", L.S("Vehicle insurance premiums", "Страховые взносы за машины"), -prem);

            int cash = GTA.Game.Player.Money;
            double due = Data.Debt - balance;             // positive = must pay
            if (due <= 0)
            {
                GTA.Game.Player.Money = cash + (int)Math.Round(-due);
                Data.Debt = 0;
            }
            else
            {
                int pay = (int)Math.Min(cash, Math.Round(due));
                GTA.Game.Player.Money = cash - pay;
                Data.Debt = Math.Max(0, due - pay);
            }
            main.Notify(L.F("~b~Bills~s~ rent +{0}, maintenance -{1}, insurance -{2}.{3}",
                            "~b~Счета~s~ аренда +{0}, содержание -{1}, страховка -{2}.{3}",
                L.Money(rent), L.Money(maint), L.Money(prem),
                Data.Debt > 0 ? L.F("~n~~r~Unpaid: {0}~s~", "~n~~r~Долг: {0}~s~", L.Money(Data.Debt)) : ""));
            main.Saves.Save();
        }

        // ------------------------------------------------------------ frame: markers and prompts

        public void OnFrame()
        {
            if (main.Menu.IsOpen) return;
            Ped p = GTA.Game.Player.Character;
            if (p == null || !p.Exists() || p.IsDead) return;

            if (inside != null)
            {
                Interior i;
                if (Interiors.TryGetValue(inside.Interior, out i))
                {
                    World.DrawMarker(MarkerType.Cylinder, i.Entry - new Vector3(0, 0, 0.95f), Vector3.Zero, Vector3.Zero,
                        new Vector3(1f, 1f, 0.4f), Color.FromArgb(110, 92, 148, 214));
                    if (p.Position.DistanceTo(i.Entry) < 1.4f)
                    {
                        GTA.UI.Screen.ShowHelpTextThisFrame(L.S("Press ~INPUT_CONTEXT~ for the exit and options.", "Нажмите ~INPUT_CONTEXT~: выход и действия."));
                        if (NativeInput.JustPressed(GTA.Control.Context)) main.Menu.Open(BuildInsideMenu(inside));
                    }
                }
                return;
            }

            PropertyDef d = near;
            if (d == null) return;
            Vector3 door = new Vector3(d.X, d.Y, d.Z);
            bool mine = OwnedByMe(d);
            World.DrawMarker(MarkerType.Cylinder, door - new Vector3(0, 0, 0.95f), Vector3.Zero, Vector3.Zero,
                new Vector3(1.2f, 1.2f, 0.4f), mine ? Color.FromArgb(110, 90, 180, 110) : Color.FromArgb(110, 230, 230, 230));

            Vehicle v = p.CurrentVehicle;
            if (v != null)
            {
                if (!mine || d.GarageSlots == 0 || v.Speed > 1f || nearDist > 9f || v.Driver != p) return;
                OwnedVehicle o = main.Ownership.RecordFor(v);
                if (o == null || o.Managed) return;
                GTA.UI.Screen.ShowHelpTextThisFrame(L.F("Press ~INPUT_CONTEXT~ to store the vehicle in the garage ({0}/{1}).",
                    "Нажмите ~INPUT_CONTEXT~, чтобы поставить машину в гараж ({0}/{1}).", StoredCount(d.Id), d.GarageSlots));
                if (NativeInput.JustPressed(GTA.Control.Context))
                {
                    if (StoredCount(d.Id) >= d.GarageSlots) main.Notify(L.S("The garage is full.", "Гараж заполнен."));
                    else if (main.Ownership.Store(v, o, d.Id)) main.Notify(L.F("{0} stored in {1}.", "{0} поставлен в гараж: {1}.", o.DisplayName, d.Name));
                }
                return;
            }

            if (nearDist > 1.6f) return;
            GTA.UI.Screen.ShowHelpTextThisFrame(mine
                ? L.S("Press ~INPUT_CONTEXT~ for your property.", "Нажмите ~INPUT_CONTEXT~: ваша недвижимость.")
                : L.S("Press ~INPUT_CONTEXT~ to view this property.", "Нажмите ~INPUT_CONTEXT~, чтобы посмотреть объект."));
            if (NativeInput.JustPressed(GTA.Control.Context))
                main.Menu.Open(mine ? BuildOwnedMenu(d) : BuildForSaleMenu(d, false));
        }

        // ------------------------------------------------------------ menus

        public static string TypeName(PropertyType t)
        {
            switch (t)
            {
                case PropertyType.Apartment: return L.S("Apartment", "Квартира");
                case PropertyType.Garage: return L.S("Garage", "Гараж");
            }
            return L.S("House", "Дом");
        }

        public void AddDetails(Menu m, PropertyDef d)
        {
            m.Add(new MenuItem(L.S("Type", "Тип"), null).WithValue(() => TypeName(d.Type)));
            m.Add(new MenuItem(L.S("Price", "Цена"), null).WithValue(() => L.Money(d.Price)));
            m.Add(new MenuItem(L.S("Luxury", "Класс"), null).WithValue(() => d.Luxury + " / 5"));
            m.Add(new MenuItem(L.S("Garage spaces", "Мест в гараже"), null).WithValue(() => d.GarageSlots.ToString()));
            m.Add(new MenuItem(L.S("Maintenance / rent per bill", "Содержание / аренда за период"), null)
                .WithValue(() => L.Money(d.Maintenance) + " / " + L.Money(d.Rent)));
        }

        public Menu BuildForSaleMenu(PropertyDef d, bool fromInside)
        {
            OwnedProperty owner = Owned(d.Id);
            Menu m = new Menu(d.Name, owner != null ? L.F("Owned by {0}", "Владелец: {0}", owner.Owner) : L.S("For sale", "Продаётся"));
            AddDetails(m, d);
            if (!fromInside && d.Interior != "None" && Interiors.ContainsKey(d.Interior))
                m.Add(new MenuItem(L.S("Tour the interior", "Осмотреть изнутри"), () =>
                {
                    main.Menu.Close();
                    Enter(d, true);
                }).WithEnabled(() => owner == null));
            m.Add(new MenuItem(L.F("Buy for {0}", "Купить за {0}", L.Money(d.Price)), () => Buy(d))
                .WithEnabled(() => owner == null && GTA.Game.Player.Money >= d.Price)
                .WithDescription(L.F("Cash: {0}. Paid from the character's cash (bank arrives in Phase 6).",
                                     "Наличные: {0}. Оплата наличными персонажа (банк появится на этапе 6).", L.Money(GTA.Game.Player.Money))));
            m.Add(new MenuItem(L.S("Close", "Закрыть"), () => main.Menu.Close()));
            return m;
        }

        private Menu BuildOwnedMenu(PropertyDef d)
        {
            OwnedProperty o = Owned(d.Id);
            Menu m = new Menu(d.Name, L.S("Your property", "Ваша недвижимость"));
            if (d.Interior != "None" && Interiors.ContainsKey(d.Interior))
                m.Add(new MenuItem(L.S("Enter", "Войти"), () => { main.Menu.Close(); Enter(d, false); })
                    .WithEnabled(() => !o.RentedOut)
                    .WithDescription(o.RentedOut ? L.S("Tenants live here while it is rented out.", "Пока объект сдан, здесь живут арендаторы.") : ""));
            if (d.GarageSlots > 0)
                m.Add(new MenuItem(L.S("Garage", "Гараж"), () => main.Menu.Push(BuildGarageMenu(d)))
                    .WithValue(() => StoredCount(d.Id) + " / " + d.GarageSlots + "  >"));
            m.Add(new MenuItem(L.S("Rent it out", "Сдавать в аренду"), () =>
            {
                o.RentedOut = !o.RentedOut;
                main.Saves.Save();
                main.Menu.Replace(BuildOwnedMenu(d));
            }).WithValue(() => L.OnOff(o.RentedOut))
              .WithDescription(L.F("Earns {0} per bill. You cannot live here while it is rented.", "Приносит {0} за период. Пока сдано, жить здесь нельзя.", L.Money(d.Rent))));
            m.Add(new MenuItem(L.F("Sell for {0}", "Продать за {0}", L.Money(Math.Round(d.Price * OwnershipRules.SellPropertyFactor))), () => main.Menu.Push(BuildSellConfirm(d)))
                .WithDescription(L.S("85% of the price after agent fees. Stored vehicles must be taken out first.",
                                     "85% цены за вычетом комиссии. Сначала заберите машины из гаража.")));
            m.Add(new MenuItem(L.S("Close", "Закрыть"), () => main.Menu.Close()));
            return m;
        }

        private Menu BuildSellConfirm(PropertyDef d)
        {
            Menu m = new Menu(L.S("Sell property?", "Продать недвижимость?"), d.Name);
            m.Add(new MenuItem(L.S("Yes, sell it", "Да, продать"), () =>
            {
                if (StoredCount(d.Id) > 0) { main.Notify(L.S("Take your vehicles out of the garage first.", "Сначала заберите машины из гаража.")); return; }
                double price = Math.Round(d.Price * OwnershipRules.SellPropertyFactor);
                Data.Properties.Remove(Owned(d.Id));
                GTA.Game.Player.Money += (int)price;
                Data.AddTransaction("Property", L.F("Sold {0}", "Продажа: {0}", d.Name), price);
                main.Saves.Save();
                RefreshBlips();
                main.Menu.Close();
                main.Notify(L.F("Sold {0} for {1}.", "{0} продан за {1}.", d.Name, L.Money(price)));
            }));
            m.Add(new MenuItem(L.S("No", "Нет"), () => main.Menu.Back()));
            return m;
        }

        private Menu BuildGarageMenu(PropertyDef d)
        {
            Menu m = new Menu(L.S("Garage", "Гараж"), d.Name);
            int count = 0;
            foreach (OwnedVehicle v in Data.OwnedVehicles)
            {
                if (v.State != VehicleState.Stored || v.GarageId != d.Id) continue;
                OwnedVehicle rec = v;
                count++;
                m.Add(new MenuItem(v.DisplayName + "  " + v.Plate, () =>
                {
                    main.Menu.Close();
                    if (rec.Owner != Owner) { main.Notify(L.F("This vehicle belongs to {0}.", "Эта машина принадлежит: {0}.", rec.Owner)); return; }
                    Transition.FadeOut();
                    Vehicle n = main.Ownership.TakeOut(rec, new Vector3(d.X, d.Y, d.Z));
                    Script.Wait(300);
                    Transition.FadeIn();
                    if (n == null) main.Notify(L.S("Could not bring the vehicle out (model unavailable).", "Не удалось выгнать машину (модель недоступна)."));
                }).WithValue(() => L.F("{0:0} km, {1:0}%", "{0:0} км, {1:0}%", rec.MileageKm, rec.Condition)));
            }
            if (count == 0) m.Add(new MenuItem(L.S("Empty. Drive an owned car to the door marker to store it.", "Пусто. Подъедьте на своей машине к маркеру, чтобы поставить её."), null));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => main.Menu.Back()));
            return m;
        }

        private Menu BuildInsideMenu(PropertyDef d)
        {
            Menu m = new Menu(d.Name, touring ? L.S("Viewing", "Осмотр") : L.S("Home", "Дом"));
            m.Add(new MenuItem(L.S("Leave", "Выйти"), () => { main.Menu.Close(); Leave(); }));
            if (!touring)
            {
                m.Add(new MenuItem(L.S("Rest (sleep 8 hours)", "Отдохнуть (поспать 8 часов)"), () =>
                {
                    main.Menu.Close();
                    Transition.FadeOut();
                    Transition.AdvanceClock(8);
                    Ped p = GTA.Game.Player.Character;
                    p.Health = p.MaxHealth;
                    main.Saves.Save();
                    Script.Wait(800);
                    Transition.FadeIn();
                    main.Notify(L.S("You feel rested. Progress saved.", "Вы отдохнули. Прогресс сохранён."));
                }));
            }
            else
            {
                m.Add(new MenuItem(L.F("Buy for {0}", "Купить за {0}", L.Money(d.Price)), () => { Buy(d); })
                    .WithEnabled(() => Owned(d.Id) == null && GTA.Game.Player.Money >= d.Price));
            }
            return m;
        }

        // ------------------------------------------------------------ actions

        private void Enter(PropertyDef d, bool tour)
        {
            Interior i;
            if (!Interiors.TryGetValue(d.Interior, out i)) return;
            if (Transition.MovePlayer(i.Entry, i.Heading, true))
            {
                inside = d;
                touring = tour;
            }
            else main.Notify(L.S("This interior is not available in your game version.", "Этот интерьер недоступен в вашей версии игры."));
        }

        private void Leave()
        {
            PropertyDef d = inside;
            inside = null;
            touring = false;
            if (d == null) return;
            Transition.MovePlayer(new Vector3(d.X, d.Y, d.Z), 0f, false);
        }

        public void Buy(PropertyDef d)
        {
            if (Owned(d.Id) != null) return;
            if (GTA.Game.Player.Money < d.Price) { main.Notify(L.S("Not enough cash.", "Недостаточно наличных.")); return; }
            GTA.Game.Player.Money -= (int)d.Price;
            OwnedProperty o = new OwnedProperty();
            o.Id = d.Id;
            o.Owner = Owner;
            o.PurchasedUtc = DateTime.UtcNow.ToString("o");
            o.PricePaid = d.Price;
            Data.Properties.Add(o);
            Data.AddTransaction("Property", L.F("Bought {0}", "Покупка: {0}", d.Name), -d.Price);
            main.Saves.Save();
            touring = false;
            RefreshBlips();
            if (main.Menu.IsOpen) main.Menu.Close();
            main.Notify(L.F("Congratulations! {0} is yours.", "Поздравляем! {0} теперь ваш.", d.Name));
            Log.Info("Bought property " + d.Id + " for " + d.Price);
        }

        public void SetWaypoint(PropertyDef d)
        {
            N.Call(N.SET_NEW_WAYPOINT, d.X, d.Y);
            main.Notify(L.S("Waypoint set.", "Точка отмечена на карте."));
        }
    }
}
