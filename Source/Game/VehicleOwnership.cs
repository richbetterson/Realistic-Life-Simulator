// Realistic Life Simulator - owned vehicles.
//
// * Buy at the dealership (Premium Deluxe Motorsport) or register a car you already have
//   (your character's own car, or one you bought - not stolen ones).
// * Mileage, engine condition (wear by distance, overdue service, crashes), damage history.
// * A worn engine loses top speed and uses more fuel.
// * Owned cars stay where you parked them. Far away they are saved and removed from the
//   world (no memory leak); when you come back they reappear with their colours and mods.
// * Recovery service brings a lost car to you; destroyed insured cars pay out.
// * GTA's own personal vehicles can be registered as "managed": tracked (mileage, wear,
//   insurance, service) while GTA keeps spawning them, so nothing is ever duplicated.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GTA;
using GTA.Math;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public sealed class Shop
    {
        public string Id, Name;
        public bool Dealership;
        public Vector3 Position;
        public Blip Blip;
    }

    public sealed class VehicleOwnership : IModule, IFrameModule
    {
        private readonly RlsMain main;
        private readonly Random rng = new Random();
        private readonly Dictionary<string, Vehicle> live = new Dictionary<string, Vehicle>();
        private List<CatalogEntry> catalog = new List<CatalogEntry>();
        private readonly List<Shop> shops = new List<Shop>();

        // current vehicle tracking
        private OwnedVehicle cur;
        private Vehicle curVeh;
        private float curEngine, curBody;
        private double appliedPenalty = -1;
        private bool serviceWarned;
        private int lastUpdate, nextStream;

        public VehicleOwnership(RlsMain main) { this.main = main; }

        public string Name { get { return "Vehicles"; } }
        public int IntervalMs { get { return 500; } }
        public IList<CatalogEntry> Catalog { get { return catalog; } }
        public IList<Shop> Shops { get { return shops; } }

        private SaveData Data { get { return main.Saves.Data; } }
        private string Owner { get { return main.CurrentProfile().Character; } }

        public List<OwnedVehicle> Mine()
        {
            List<OwnedVehicle> l = new List<OwnedVehicle>();
            foreach (OwnedVehicle o in Data.OwnedVehicles)
                if (o.Owner == Owner && o.State != VehicleState.Sold) l.Add(o);
            return l;
        }

        public void Start()
        {
            LoadData();
            lastUpdate = Environment.TickCount;
        }

        public void Shutdown()
        {
            // Remember where cars were left; keep entities in place (GTA cleans them up).
            foreach (KeyValuePair<string, Vehicle> kv in live)
            {
                OwnedVehicle o = Find(kv.Key);
                if (o != null && kv.Value != null && kv.Value.Exists()) Snapshot(o, kv.Value);
                N.Release(kv.Value);
            }
            live.Clear();
            foreach (Shop s in shops) if (s.Blip != null && s.Blip.Exists()) s.Blip.Delete();
        }

        public OwnedVehicle Find(string id)
        {
            foreach (OwnedVehicle o in Data.OwnedVehicles) if (o.Id == id) return o;
            return null;
        }

        // Owned record for a world vehicle (current character), or null.
        public OwnedVehicle RecordFor(Vehicle v)
        {
            if (v == null || !v.Exists()) return null;
            foreach (KeyValuePair<string, Vehicle> kv in live)
                if (kv.Value == v) { OwnedVehicle o = Find(kv.Key); if (o != null && o.Owner == Owner) return o; }
            string plate = N.Plate(v);
            foreach (OwnedVehicle o in Data.OwnedVehicles)
            {
                if (o.Owner != Owner || o.ModelHash != v.Model.Hash || o.State == VehicleState.Sold || o.State == VehicleState.Destroyed) continue;
                if (string.Equals(o.Plate, plate, StringComparison.OrdinalIgnoreCase))
                {
                    if (!o.Managed) live[o.Id] = v;
                    return o;
                }
            }
            return null;
        }

        public double FuelFactor(Vehicle v)
        {
            return cur != null && v == curVeh ? OwnershipRules.FuelPenaltyFactor(cur.Condition) : 1.0;
        }

        // ------------------------------------------------------------ update

        public void Update()
        {
            int now = Environment.TickCount;
            double dt = Math.Min(2.0, Math.Max(0, unchecked(now - lastUpdate) / 1000.0));
            lastUpdate = now;
            Ped p = GTA.Game.Player.Character;
            if (p == null || !p.Exists() || p.IsDead) return;

            Vehicle v = p.CurrentVehicle;
            if (v != null && v.Exists() && v.Driver == p)
            {
                if (v != curVeh)
                {
                    curVeh = v;
                    cur = RecordFor(v);
                    curEngine = v.EngineHealth;
                    curBody = v.BodyHealth;
                    appliedPenalty = -1;
                    serviceWarned = false;
                }
                if (cur != null) TrackDriving(cur, v, dt);
            }
            else
            {
                if (cur != null && curVeh != null && curVeh.Exists()) Snapshot(cur, curVeh);
                cur = null;
                curVeh = null;
            }

            if (unchecked(now - nextStream) >= 0)
            {
                nextStream = now + 2000;
                Stream(p);
            }
        }

        private void TrackDriving(OwnedVehicle o, Vehicle v, double dt)
        {
            RlsSettings set = main.Config.Settings;
            double km = v.Speed * dt / 1000.0;
            o.MileageKm += km;

            if (set.VehicleWear)
            {
                double wear = OwnershipRules.WearPerKm(o.MileageKm - o.KmAtService, set.ServiceIntervalKm, set.WearRate) * km;
                if (v.CurrentRPM > 0.9f) wear *= 1.5; // revving hard
                o.Condition = Math.Max(0, o.Condition - wear);
            }

            // Crash damage: engine / body loss since the last update.
            float engine = v.EngineHealth, body = v.BodyHealth;
            float eDrop = curEngine - engine, bDrop = curBody - body;
            if (eDrop > 40f || bDrop > 60f)
            {
                DamageEntry d = new DamageEntry();
                d.TimeUtc = DateTime.UtcNow.ToString("o");
                d.Km = Math.Round(o.MileageKm, 1);
                d.Location = main.Traffic.DescribeLocation(v.Position);
                d.Estimate = PoliceRules.DamageEstimate(Math.Max(0, 1000 - bDrop), Math.Max(0, 1000 - eDrop), OwnershipRules.ClassFactor((int)v.ClassType));
                o.Damage.Add(d);
                if (o.Damage.Count > 30) o.Damage.RemoveAt(0);
                if (set.VehicleWear && eDrop > 0) o.Condition = Math.Max(0, o.Condition - eDrop / 10.0);
            }
            curEngine = engine;
            curBody = body;
            o.EngineHealth = engine;
            o.BodyHealth = body;

            double penalty = set.VehicleWear ? OwnershipRules.TopSpeedPenaltyPercent(o.Condition) : 0;
            if (Math.Abs(penalty - appliedPenalty) > 0.9)
            {
                N.Call(VehicleLooks.MODIFY_VEHICLE_TOP_SPEED, v, (float)-penalty);
                appliedPenalty = penalty;
            }

            if (set.VehicleWear && !serviceWarned && o.MileageKm - o.KmAtService > set.ServiceIntervalKm)
            {
                serviceWarned = true;
                main.Notify(L.F("Service due for your {0} ({1:0} km since the last service). Visit a repair shop.",
                                "Пора на ТО: {0} ({1:0} км с последнего обслуживания). Заезжайте в автосервис.",
                    o.DisplayName, o.MileageKm - o.KmAtService));
            }
        }

        private void Snapshot(OwnedVehicle o, Vehicle v)
        {
            Vector3 pos = v.Position;
            o.LastX = pos.X; o.LastY = pos.Y; o.LastZ = pos.Z; o.LastHeading = v.Heading;
            o.BodyHealth = v.BodyHealth;
            o.EngineHealth = v.EngineHealth;
            o.Look = VehicleLooks.Read(v);
        }

        // Keep owned cars near the player in the world, remove far ones, detect destruction.
        private void Stream(Ped p)
        {
            Vector3 pp = p.Position;
            foreach (OwnedVehicle o in Data.OwnedVehicles)
            {
                if (o.State != VehicleState.Out || o.Managed) continue;
                Vehicle v;
                if (live.TryGetValue(o.Id, out v))
                {
                    if (v == null || !v.Exists()) { live.Remove(o.Id); continue; }
                    if (v.IsDead) { Destroyed(o, v); continue; }
                    if (p.CurrentVehicle == v) continue;
                    if (pp.DistanceTo(v.Position) > 500f)
                    {
                        Snapshot(o, v);
                        v.Delete();
                        live.Remove(o.Id);
                    }
                    continue;
                }
                if (o.Owner != Owner || (o.LastX == 0 && o.LastY == 0)) continue;
                Vector3 at = new Vector3(o.LastX, o.LastY, o.LastZ);
                if (pp.DistanceTo(at) > 150f) continue;

                // Already there (e.g. after a script reload)? Adopt it instead of spawning a copy.
                Vehicle found = null;
                foreach (Vehicle n in World.GetNearbyVehicles(at, 12f))
                    if (n.Model.Hash == o.ModelHash && string.Equals(N.Plate(n), o.Plate, StringComparison.OrdinalIgnoreCase)) { found = n; break; }
                if (found == null) found = Spots.SpawnOwned(o, at, o.LastHeading);
                if (found != null)
                {
                    found.IsPersistent = true;
                    live[o.Id] = found;
                }
            }
        }

        private void Destroyed(OwnedVehicle o, Vehicle v)
        {
            o.State = VehicleState.Destroyed;
            live.Remove(o.Id);
            N.Release(v);
            double payout = OwnershipRules.TotalLossPayout(o.Value, o.Insurance, o.Condition);
            if (payout > 0)
            {
                GTA.Game.Player.Money += (int)payout;
                Data.AddTransaction("Insurance", L.F("Total loss payout: {0}", "Выплата за полную гибель: {0}", o.DisplayName), payout);
            }
            main.Notify(payout > 0
                ? L.F("Your {0} was destroyed. Insurance paid {1}.", "Ваш {0} уничтожен. Страховка выплатила {1}.", o.DisplayName, L.Money(payout))
                : L.F("Your {0} was destroyed. It was not insured.", "Ваш {0} уничтожен. Он не был застрахован.", o.DisplayName));
            main.Saves.Save();
        }

        // ------------------------------------------------------------ actions

        private string NewPlate()
        {
            const string letters = "ABCDEFGHJKLMNPRSTUVWXYZ";
            return rng.Next(10, 99) + "" + letters[rng.Next(letters.Length)] + letters[rng.Next(letters.Length)]
                   + letters[rng.Next(letters.Length)] + rng.Next(100, 999);
        }

        public bool Buy(CatalogEntry c, Vector3 near)
        {
            if (GTA.Game.Player.Money < c.Price)
            {
                main.Notify(L.S("Not enough cash for this vehicle.", "Недостаточно наличных для этой машины."));
                return false;
            }
            Vector3 pos;
            float heading;
            Spots.RoadSpot(near, 8f, 80f, out pos, out heading);
            OwnedVehicle o = new OwnedVehicle();
            o.Id = OwnershipRules.NewId(rng);
            o.Owner = Owner;
            o.ModelHash = Joaat.Hash(c.Model);
            o.ModelName = c.Model;
            o.DisplayName = c.Name;
            o.Plate = NewPlate();
            o.Value = c.Price;
            o.PricePaid = c.Price;
            o.AcquiredUtc = DateTime.UtcNow.ToString("o");
            o.MileageKm = 3 + rng.Next(8);
            o.KmAtService = 0;
            o.Condition = 100;
            o.State = VehicleState.Out;
            Vehicle v = Spots.SpawnOwned(o, pos, heading);
            if (v == null)
            {
                main.Notify(L.S("This model is not available in your game.", "Эта модель недоступна в вашей игре."));
                return false;
            }
            o.Look = VehicleLooks.Read(v);
            Snapshot(o, v);
            GTA.Game.Player.Money -= (int)c.Price;
            Data.OwnedVehicles.Add(o);
            live[o.Id] = v;
            Data.AddTransaction("Vehicle", L.F("Bought {0} ({1})", "Покупка {0} ({1})", c.Name, o.Plate), -c.Price);
            // A new car leaves the dealer with a full tank.
            FuelSpec spec = FuelModel.ForVehicle(c.Class, o.ModelHash);
            if (spec.Supported && !spec.Electric) main.Fuel.GetRecord(v, spec).Liters = spec.TankLiters;
            Blip b = v.AddBlip();
            b.Sprite = BlipSprite.PersonalVehicleCar;
            b.Color = BlipColor.Green;
            b.Name = c.Name;
            b.IsShortRange = false;
            tempBlips.Add(new KeyValuePair<Blip, Vehicle>(b, v));
            main.Saves.Save();
            main.Notify(L.F("You bought a {0} for {1}. Plate {2}. It is waiting outside.", "Вы купили {0} за {1}. Номер {2}. Машина ждёт у салона.",
                c.Name, L.Money(c.Price), o.Plate));
            Log.Info("Bought " + c.Model + " for " + c.Price);
            return true;
        }

        private readonly List<KeyValuePair<Blip, Vehicle>> tempBlips = new List<KeyValuePair<Blip, Vehicle>>();

        public bool RegisterCurrent()
        {
            Ped p = GTA.Game.Player.Character;
            Vehicle v = p.CurrentVehicle;
            if (v == null || !v.Exists() || v.Driver != p)
            {
                main.Notify(L.S("Sit in the driver's seat of the car you want to register.", "Сядьте за руль машины, которую хотите зарегистрировать."));
                return false;
            }
            if (RecordFor(v) != null) { main.Notify(L.S("This vehicle is already registered to you.", "Эта машина уже зарегистрирована на вас.")); return false; }
            if (N.Call<bool>(N.IS_VEHICLE_STOLEN, v))
            {
                main.Notify(L.S("This vehicle is reported stolen and cannot be registered.", "Эта машина числится в угоне, зарегистрировать её нельзя."));
                return false;
            }
            int cls = (int)v.ClassType;
            if (cls == 13 || cls == 14 || cls == 15 || cls == 16 || cls == 21 || cls == 18) { main.Notify(L.S("This type of vehicle cannot be registered here.", "Такой транспорт здесь не регистрируется.")); return false; }
            if (GTA.Game.Player.Money < OwnershipRules.RegistrationFee) { main.Notify(L.S("Not enough cash for the registration fee.", "Недостаточно наличных для пошлины.")); return false; }

            OwnedVehicle o = new OwnedVehicle();
            o.Id = OwnershipRules.NewId(rng);
            o.Owner = Owner;
            o.ModelHash = v.Model.Hash;
            o.ModelName = "";
            o.DisplayName = N.VehicleName(v);
            o.Plate = N.Plate(v);
            o.Value = OwnershipRules.EstimateValue(cls);
            o.AcquiredUtc = DateTime.UtcNow.ToString("o");
            o.MileageKm = 15000 + rng.Next(70000) / 10.0 * 10;
            o.KmAtService = o.MileageKm - rng.Next(0, main.Config.Settings.ServiceIntervalKm);
            o.Condition = 60 + rng.Next(30);
            o.Managed = N.IsMissionEntity(v);   // GTA's own personal vehicles
            o.State = VehicleState.Out;
            Snapshot(o, v);
            GTA.Game.Player.Money -= (int)OwnershipRules.RegistrationFee;
            Data.OwnedVehicles.Add(o);
            Data.AddTransaction("Vehicle", L.F("Registration: {0} ({1})", "Регистрация: {0} ({1})", o.DisplayName, o.Plate), -OwnershipRules.RegistrationFee);
            if (!o.Managed) { v.IsPersistent = true; live[o.Id] = v; }
            cur = o;
            curVeh = v;
            main.Saves.Save();
            main.Notify(L.F("{0} ({1}) registered to {2}.", "{0} ({1}) зарегистрирован на {2}.", o.DisplayName, o.Plate, o.Owner));
            return true;
        }

        public bool SellNear(OwnedVehicle o, Vector3 shopPos)
        {
            Vehicle v;
            if (o.Managed || !live.TryGetValue(o.Id, out v) || v == null || !v.Exists() || v.Position.DistanceTo(shopPos) > 30f)
            {
                main.Notify(L.S("Park the vehicle at the dealership to sell it.", "Чтобы продать машину, оставьте её у автосалона."));
                return false;
            }
            if (GTA.Game.Player.Character.CurrentVehicle == v) { main.Notify(L.S("Get out of the vehicle first.", "Сначала выйдите из машины.")); return false; }
            double price = OwnershipRules.CurrentValue(o.Value, o.MileageKm, o.Condition);
            if (v.BodyHealth < 900 || v.EngineHealth < 900) price = Math.Round(price * 0.8);
            v.Delete();
            live.Remove(o.Id);
            o.State = VehicleState.Sold;
            GTA.Game.Player.Money += (int)price;
            Data.AddTransaction("Vehicle", L.F("Sold {0} ({1})", "Продажа {0} ({1})", o.DisplayName, o.Plate), price);
            main.Saves.Save();
            main.Notify(L.F("Sold your {0} for {1}.", "Вы продали {0} за {1}.", o.DisplayName, L.Money(price)));
            return true;
        }

        public double SalePrice(OwnedVehicle o)
        {
            return OwnershipRules.CurrentValue(o.Value, o.MileageKm, o.Condition);
        }

        public bool IsNearby(OwnedVehicle o, Vector3 pos, float radius)
        {
            Vehicle v;
            return live.TryGetValue(o.Id, out v) && v != null && v.Exists() && v.Position.DistanceTo(pos) < radius;
        }

        public bool Recover(OwnedVehicle o)
        {
            if (o.Managed) { main.Notify(L.S("GTA keeps track of this personal vehicle itself.", "Этой личной машиной управляет сама игра.")); return false; }
            if (o.State != VehicleState.Out) return false;
            if (GTA.Game.Player.Money < OwnershipRules.RecoveryFee) { main.Notify(L.S("Not enough cash for the recovery fee.", "Недостаточно наличных для эвакуации.")); return false; }
            Vehicle v;
            if (live.TryGetValue(o.Id, out v) && v != null && v.Exists())
            {
                if (GTA.Game.Player.Character.CurrentVehicle == v) return false;
                Snapshot(o, v);
                v.Delete();
                live.Remove(o.Id);
            }
            Vector3 pos;
            float heading;
            Spots.RoadSpot(GTA.Game.Player.Character.Position, 25f, 120f, out pos, out heading);
            Vehicle n = Spots.SpawnOwned(o, pos, heading);
            if (n == null) return false;
            live[o.Id] = n;
            Snapshot(o, n);
            GTA.Game.Player.Money -= (int)OwnershipRules.RecoveryFee;
            Data.AddTransaction("Vehicle", L.F("Recovery: {0}", "Эвакуация: {0}", o.DisplayName), -OwnershipRules.RecoveryFee);
            Blip b = n.AddBlip();
            b.Sprite = BlipSprite.PersonalVehicleCar;
            b.Color = BlipColor.Green;
            b.Name = o.DisplayName;
            tempBlips.Add(new KeyValuePair<Blip, Vehicle>(b, n));
            main.Notify(L.F("Your {0} was delivered nearby.", "Ваш {0} доставлен неподалёку.", o.DisplayName));
            return true;
        }

        public void SetWaypoint(OwnedVehicle o)
        {
            Vehicle v;
            Vector3 at = live.TryGetValue(o.Id, out v) && v != null && v.Exists() ? v.Position : new Vector3(o.LastX, o.LastY, o.LastZ);
            if (at.X == 0 && at.Y == 0) { main.Notify(L.S("Location unknown. Use recovery.", "Местоположение неизвестно. Воспользуйтесь эвакуацией.")); return; }
            N.Call(N.SET_NEW_WAYPOINT, at.X, at.Y);
            main.Notify(L.S("Waypoint set.", "Точка отмечена на карте."));
        }

        // Garage support (PropertySystem).
        public bool Store(Vehicle v, OwnedVehicle o, string garageId)
        {
            if (o.Managed) { main.Notify(L.S("GTA personal vehicles stay in GTA's own garages.", "Личные машины персонажей хранятся в гаражах самой игры.")); return false; }
            Snapshot(o, v);
            Ped p = GTA.Game.Player.Character;
            if (p.CurrentVehicle == v)
            {
                Transition.FadeOut();
                p.Position = v.Position + v.RightVector * -2.5f;
                v.Delete();
                Script.Wait(200);
                Transition.FadeIn();
            }
            else v.Delete();
            live.Remove(o.Id);
            o.State = VehicleState.Stored;
            o.GarageId = garageId;
            if (cur == o) { cur = null; curVeh = null; }
            main.Saves.Save();
            return true;
        }

        public Vehicle TakeOut(OwnedVehicle o, Vector3 near)
        {
            Vector3 pos;
            float heading;
            Spots.RoadSpot(near, 3f, 45f, out pos, out heading);
            Vehicle v = Spots.SpawnOwned(o, pos, heading);
            if (v == null) return null;
            o.State = VehicleState.Out;
            o.GarageId = null;
            live[o.Id] = v;
            Snapshot(o, v);
            GTA.Game.Player.Character.SetIntoVehicle(v, VehicleSeat.Driver);
            main.Saves.Save();
            return v;
        }

        // ------------------------------------------------------------ shops (dealer / repair)

        private void LoadData()
        {
            string cat = main.Paths.CatalogFile;
            if (!File.Exists(cat)) FileUtil.WriteAllTextAtomic(cat, OwnershipRules.DefaultCatalogIni());
            try { catalog = OwnershipRules.ParseCatalog(IniFile.Load(cat)); }
            catch (Exception ex) { Log.Error("VehicleCatalog.ini unreadable", ex); }

            string sf = main.Paths.ShopsFile;
            if (!File.Exists(sf)) FileUtil.WriteAllTextAtomic(sf, DefaultShops());
            shops.Clear();
            try
            {
                IniFile ini = IniFile.Load(sf);
                foreach (string id in ini.SectionNames)
                {
                    if (id.Length == 0) continue;
                    float x, y, z;
                    if (!float.TryParse(ini.Get(id, "X", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                        || !float.TryParse(ini.Get(id, "Y", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                        || !float.TryParse(ini.Get(id, "Z", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out z)) continue;
                    Shop s = new Shop();
                    s.Id = id;
                    s.Name = ini.Get(id, "Name", id);
                    s.Dealership = string.Equals(ini.Get(id, "Type", "Repair"), "Dealership", StringComparison.OrdinalIgnoreCase);
                    s.Position = new Vector3(x, y, z);
                    if (s.Dealership)
                    {
                        s.Blip = World.CreateBlip(s.Position);
                        s.Blip.Sprite = BlipSprite.CarShowroom;
                        s.Blip.Name = s.Name;
                        s.Blip.IsShortRange = true;
                    }
                    shops.Add(s);
                }
            }
            catch (Exception ex) { Log.Error("Shops.ini unreadable", ex); }
            Log.Info("Vehicle catalogue: " + catalog.Count + " models, shops: " + shops.Count);
        }

        private static string DefaultShops()
        {
            return "; Realistic Life Simulator - dealership and repair shops / автосалон и автосервисы.\r\n"
                 + "; Repair shops: talk to the mechanic on foot, with your car parked within 25 m.\r\n\r\n"
                 + "[PDM]\r\nName=Premium Deluxe Motorsport\r\nType=Dealership\r\nX=-56.6\r\nY=-1096.7\r\nZ=26.4\r\n\r\n"
                 + "[RepairBurton]\r\nName=Burton Auto Repair\r\nType=Repair\r\nX=-337.4\r\nY=-136.9\r\nZ=39.0\r\n\r\n"
                 + "[RepairLaMesa]\r\nName=La Mesa Auto Repair\r\nType=Repair\r\nX=731.8\r\nY=-1088.8\r\nZ=22.2\r\n\r\n"
                 + "[RepairAirport]\r\nName=LSIA Auto Repair\r\nType=Repair\r\nX=-1155.5\r\nY=-2007.2\r\nZ=13.2\r\n\r\n"
                 + "[RepairHarmony]\r\nName=Harmony Garage\r\nType=Repair\r\nX=1175.1\r\nY=2640.0\r\nZ=37.8\r\n\r\n"
                 + "[RepairPaleto]\r\nName=Paleto Bay Garage\r\nType=Repair\r\nX=110.9\r\nY=6626.5\r\nZ=31.8\r\n";
        }

        public void OnFrame()
        {
            // Purchase / recovery map markers disappear once the player has reached the car.
            Ped p = GTA.Game.Player.Character;
            for (int i = tempBlips.Count - 1; i >= 0; i--)
            {
                KeyValuePair<Blip, Vehicle> kv = tempBlips[i];
                if (kv.Value == null || !kv.Value.Exists() || p.CurrentVehicle == kv.Value)
                {
                    if (kv.Key != null && kv.Key.Exists()) kv.Key.Delete();
                    tempBlips.RemoveAt(i);
                }
            }

            if (main.Menu.IsOpen || p.IsInVehicle() || p.IsDead) return;
            foreach (Shop s in shops)
            {
                float d = p.Position.DistanceTo(s.Position);
                if (d > 30f) continue;
                World.DrawMarker(MarkerType.Cylinder, s.Position - new Vector3(0, 0, 0.95f), Vector3.Zero, Vector3.Zero,
                    new Vector3(1.2f, 1.2f, 0.4f), System.Drawing.Color.FromArgb(110, 92, 148, 214));
                if (d > 1.6f) continue;
                GTA.UI.Screen.ShowHelpTextThisFrame(s.Dealership
                    ? L.S("Press ~INPUT_CONTEXT~ to talk to a salesperson.", "Нажмите ~INPUT_CONTEXT~, чтобы поговорить с продавцом.")
                    : L.S("Press ~INPUT_CONTEXT~ to talk to the mechanic.", "Нажмите ~INPUT_CONTEXT~, чтобы поговорить с механиком."));
                if (NativeInput.JustPressed(GTA.Control.Context))
                {
                    if (s.Dealership) main.Menu.Open(BuildDealerMenu(s));
                    else main.Repairs.Open(s);
                }
                return;
            }
        }

        // ------------------------------------------------------------ dealership menus

        private Menu BuildDealerMenu(Shop s)
        {
            Menu m = new Menu(s.Name, L.S("New and used vehicles", "Новые и подержанные автомобили"));
            m.Add(new MenuItem(L.S("Cash", "Наличные"), null).WithValue(() => L.Money(GTA.Game.Player.Money)));
            m.Add(new MenuItem(L.S("Buy a vehicle", "Купить машину"), () => main.Menu.Push(BuildCatalogMenu(s))).WithValue(() => ">"));
            m.Add(new MenuItem(L.S("Sell a vehicle parked here", "Продать машину, стоящую здесь"), () => main.Menu.Push(BuildSellMenu(s))).WithValue(() => ">"));
            m.Add(new MenuItem(L.S("Close", "Закрыть"), () => main.Menu.Close()));
            return m;
        }

        private Menu BuildCatalogMenu(Shop s)
        {
            Menu m = new Menu(L.S("Catalogue", "Каталог"), L.S("Prices include registration", "Цены включают регистрацию"));
            foreach (CatalogEntry c in catalog)
            {
                CatalogEntry entry = c;
                m.Add(new MenuItem(c.Name, () =>
                {
                    if (Buy(entry, s.Position)) main.Menu.Close();
                }).WithValue(() => L.Money(entry.Price))
                  .WithEnabled(() => GTA.Game.Player.Money >= entry.Price)
                  .WithDescription(L.F("Insurance from {0} per bill (basic). Choose insurance later in the Life Hub under My vehicles.",
                                       "Страховка от {0} за период (базовая). Выбрать страховку можно в центре управления, раздел «Мои машины».",
                                       L.Money(OwnershipRules.Premium(entry.Price, InsuranceLevel.Basic)))));
            }
            m.Add(new MenuItem(L.S("Back", "Назад"), () => main.Menu.Back()));
            return m;
        }

        private Menu BuildSellMenu(Shop s)
        {
            Menu m = new Menu(L.S("Sell a vehicle", "Продажа машины"), L.S("The car must be parked here", "Машина должна стоять у салона"));
            int count = 0;
            foreach (OwnedVehicle o in Mine())
            {
                if (o.Managed || o.State != VehicleState.Out || !IsNearby(o, s.Position, 30f)) continue;
                OwnedVehicle rec = o;
                count++;
                m.Add(new MenuItem(o.DisplayName + "  " + o.Plate, () =>
                {
                    if (SellNear(rec, s.Position)) main.Menu.Close();
                }).WithValue(() => L.Money(SalePrice(rec)))
                  .WithDescription(L.F("{0:0} km, engine {1:0}%. Damaged cars sell for 20% less.", "{0:0} км, двигатель {1:0}%. Битые машины дешевле на 20%.", rec.MileageKm, rec.Condition)));
            }
            if (count == 0) m.Add(new MenuItem(L.S("None of your vehicles is parked here.", "Ни одна ваша машина не стоит у салона."), null));
            m.Add(new MenuItem(L.S("Back", "Назад"), () => main.Menu.Back()));
            return m;
        }
    }
}
