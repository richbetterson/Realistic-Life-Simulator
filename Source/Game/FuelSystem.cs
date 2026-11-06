// Realistic Life Simulator - player fuel system.
//  * every vehicle the player drives gets a fuel level (saved by plate + model)
//  * consumption depends on vehicle class, distance, throttle, RPM and idling
//  * empty tank: the engine stops and will not restart
//  * refuel on foot at a gas station pump (menu, animation, receipt, payment from cash)
//  * jerry can (GTA's petrol can weapon) can top up a vehicle anywhere

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using GTA;
using GTA.Math;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public sealed class GasStation
    {
        public string Name;
        public Vector3 Position;
    }

    public sealed class FuelSystem : IModule, IFrameModule
    {
        private const string AnimDict = "timetable@gardener@filling_can";
        private const string AnimName = "gar_ig_5_filling_can";
        private const float StationRadius = 60f;

        private sealed class RefuelSession
        {
            public Vehicle Vehicle;
            public VehicleFuelRecord Record;
            public FuelSpec Spec;
            public double Target;     // liters to add
            public double Added;
            public bool JerryCan;
            public float Price;
            public string StationName;
            public bool AnimPlaying;
            public int RequestedAt;
            public int LastFrame;
        }

        private readonly RlsMain main;
        private readonly Random rng = new Random();
        private readonly List<GasStation> stations = new List<GasStation>();
        private readonly Model[] pumpModels;

        // current driven vehicle
        private Vehicle curVeh;
        private VehicleFuelRecord curRec;
        private FuelSpec curSpec;
        private int lastUpdate;
        private int lastStamp;
        private bool warnedLow, warnedCritical, outOfFuel, outOfFuelNotified;
        private int nextEngineKill;

        // gas station proximity (on foot)
        private GasStation nearStation;
        private bool nearPump;
        private RefuelSession session;

        // progress bar
        private readonly GTA.UI.ContainerElement barBack, barFill, panel;
        private readonly GTA.UI.TextElement barText;

        public FuelSystem(RlsMain main)
        {
            this.main = main;
            string[] pumps = { "prop_gas_pump_1a", "prop_gas_pump_1b", "prop_gas_pump_1c", "prop_gas_pump_1d",
                               "prop_gas_pump_old2", "prop_gas_pump_old3", "prop_vintage_pump" };
            pumpModels = new Model[pumps.Length];
            for (int i = 0; i < pumps.Length; i++) pumpModels[i] = new Model(Joaat.Hash(pumps[i]));

            panel = new GTA.UI.ContainerElement(PointF.Empty, new SizeF(300f, 44f), Color.FromArgb(225, 22, 24, 29));
            barBack = new GTA.UI.ContainerElement(PointF.Empty, new SizeF(276f, 5f), Color.FromArgb(70, 255, 255, 255));
            barFill = new GTA.UI.ContainerElement(PointF.Empty, new SizeF(0f, 5f), Color.FromArgb(255, 92, 148, 214));
            barText = new GTA.UI.TextElement("", PointF.Empty, 0.3f, Color.FromArgb(255, 236, 238, 241),
                GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Left, false, false);
        }

        public string Name { get { return "Fuel"; } }
        public int IntervalMs { get { return 250; } }

        public IList<GasStation> Stations { get { return stations; } }

        // For the HUD: fraction 0..1, or -1 when there is nothing to show.
        public double CurrentFraction
        {
            get
            {
                if (curVeh == null || curRec == null || curSpec == null || !curSpec.Supported || curSpec.Electric) return -1;
                return curRec.Liters / curSpec.TankLiters;
            }
        }

        public bool CurrentIsElectric { get { return curVeh != null && curSpec != null && curSpec.Electric; } }

        public void Start()
        {
            LoadStations();
            lastUpdate = Environment.TickCount;
        }

        public void Shutdown()
        {
            if (session != null) FinishSession(false);
        }

        // ------------------------------------------------------------ periodic update

        public void Update()
        {
            int now = Environment.TickCount;
            double dt = Math.Min(1.0, Math.Max(0, unchecked(now - lastUpdate) / 1000.0));
            lastUpdate = now;

            RlsSettings set = main.Config.Settings;
            Ped p = GTA.Game.Player.Character;
            if (!set.FuelEnabled || p == null || !p.Exists() || p.IsDead)
            {
                DetachVehicle();
                return;
            }

            Vehicle v = p.CurrentVehicle;
            if (v != null && v.Exists() && v.Driver == p)
            {
                if (v != curVeh) AttachVehicle(v);
                if (curRec != null && curSpec.Supported && !curSpec.Electric && !GTA.Game.IsPaused)
                {
                    double meters = v.Speed * dt;
                    float throttle = N.Call<float>(N.GET_CONTROL_NORMAL, 0, N.ControlVehicleAccelerate);
                    double used = FuelModel.Consumption(curSpec, meters, dt, throttle, v.CurrentRPM,
                        v.IsEngineRunning, set.FuelConsumption * (float)(main.Ownership != null ? main.Ownership.FuelFactor(v) : 1.0));
                    curRec.Liters = Math.Max(0, curRec.Liters - used);
                    main.Saves.Data.Driving.DistanceKm += meters / 1000.0;
                    CheckWarnings(set);
                    outOfFuel = curRec.Liters <= 0.01;
                    if (unchecked(now - lastStamp) > 30000)
                    {
                        lastStamp = now;
                        curRec.LastUsedUtc = DateTime.UtcNow.ToString("o");
                    }
                }
                else outOfFuel = false;
            }
            else
            {
                DetachVehicle();
            }

            // Gas station proximity (only matters on foot)
            nearStation = null;
            nearPump = false;
            if (v == null)
            {
                Vector3 pos = p.Position;
                float best = StationRadius * StationRadius;
                foreach (GasStation s in stations)
                {
                    float d = N.DistSq(pos, s.Position);
                    if (d < best) { best = d; nearStation = s; }
                }
                if (nearStation != null)
                {
                    Prop[] props = World.GetNearbyProps(pos, 3.5f, pumpModels);
                    nearPump = props.Length > 0 || best < 15f * 15f;
                }
            }
        }

        private void AttachVehicle(Vehicle v)
        {
            curVeh = v;
            curSpec = FuelModel.ForVehicle((int)v.ClassType, v.Model.Hash);
            curRec = curSpec.Supported && !curSpec.Electric ? GetRecord(v, curSpec) : null;
            warnedLow = warnedCritical = outOfFuelNotified = false;
            outOfFuel = curRec != null && curRec.Liters <= 0.01;
            lastStamp = Environment.TickCount;
        }

        private void DetachVehicle()
        {
            curVeh = null;
            curRec = null;
            curSpec = null;
            outOfFuel = false;
        }

        public VehicleFuelRecord GetRecord(Vehicle v, FuelSpec spec)
        {
            SaveData data = main.Saves.Data;
            string plate = N.Plate(v);
            VehicleFuelRecord rec = data.FindVehicle(plate, v.Model.Hash);
            if (rec == null)
            {
                rec = new VehicleFuelRecord();
                rec.Plate = plate;
                rec.ModelHash = v.Model.Hash;
                rec.Liters = FuelModel.InitialLiters(spec, rng);
                data.Vehicles.Add(rec);
            }
            if (rec.Liters > spec.TankLiters) rec.Liters = spec.TankLiters;
            rec.LastUsedUtc = DateTime.UtcNow.ToString("o");
            data.TrimVehicles();
            return rec;
        }

        private void CheckWarnings(RlsSettings set)
        {
            double pct = curRec.Liters / curSpec.TankLiters * 100.0;
            if (set.LowFuelWarningPercent > 0 && pct <= set.LowFuelWarningPercent && !warnedLow)
            {
                warnedLow = true;
                N.Beep();
                main.Notify(L.F("Low fuel: {0:0}% ({1}). Find a gas station.", "Мало топлива: {0:0}% ({1}). Найдите АЗС.",
                    pct, L.Liters(curRec.Liters)));
            }
            else if (pct <= 5 && !warnedCritical && curRec.Liters > 0.01)
            {
                warnedCritical = true;
                N.Beep();
                main.Notify(L.S("Fuel almost empty!", "Топливо почти закончилось!"));
            }
            if (pct > set.LowFuelWarningPercent + 5) warnedLow = false;
            if (pct > 10) warnedCritical = false;
        }

        // ------------------------------------------------------------ every frame

        public void OnFrame()
        {
            Ped p = GTA.Game.Player.Character;
            if (p == null || !p.Exists()) return;

            // Empty tank: engine off, no throttle.
            if (outOfFuel && curVeh != null && curVeh.Exists() && p.CurrentVehicle == curVeh)
            {
                NativeInput.Disable((GTA.Control)N.ControlVehicleAccelerate);
                NativeInput.Disable((GTA.Control)N.ControlVehicleBrake);
                int now = Environment.TickCount;
                if (unchecked(now - nextEngineKill) >= 0)
                {
                    nextEngineKill = now + 1000;
                    N.Call(N.SET_VEHICLE_ENGINE_ON, curVeh, false, true, true);
                }
                if (!outOfFuelNotified)
                {
                    outOfFuelNotified = true;
                    main.Notify(L.S("Out of fuel. Get a jerry can or push the car to a gas station.",
                                    "Топливо кончилось. Возьмите канистру или дотолкайте машину до АЗС."));
                }
            }

            if (session != null)
            {
                UpdateSession(p);
                return;
            }

            if (main.Menu.IsOpen || !main.Config.Settings.FuelEnabled || p.IsInVehicle() || p.IsDead) return;

            Vehicle last = GTA.Game.Player.LastVehicle;
            if (last == null || !last.Exists() || last.IsDead) return;
            FuelSpec spec = FuelModel.ForVehicle((int)last.ClassType, last.Model.Hash);
            if (!spec.Supported || spec.Electric) return;
            float dist = p.Position.DistanceTo(last.Position);

            if (nearPump && dist < 7f)
            {
                VehicleFuelRecord rec = GetRecordCached(last, spec);
                if (rec.Liters >= spec.TankLiters - 0.5) return;
                GTA.UI.Screen.ShowHelpTextThisFrame(L.F("Press ~INPUT_CONTEXT~ to refuel ({0:0}% in tank).",
                    "Нажмите ~INPUT_CONTEXT~, чтобы заправиться (в баке {0:0}%).", rec.Liters / spec.TankLiters * 100));
                if (NativeInput.JustPressed(GTA.Control.Context)) OpenPumpMenu(last, spec, rec);
                return;
            }

            if (dist < 3f && HasJerryCanFuel(p))
            {
                VehicleFuelRecord rec = GetRecordCached(last, spec);
                if (rec.Liters >= spec.TankLiters - 0.5) return;
                GTA.UI.Screen.ShowHelpTextThisFrame(L.S("Press ~INPUT_CONTEXT~ to pour fuel from the jerry can.",
                    "Нажмите ~INPUT_CONTEXT~, чтобы залить топливо из канистры."));
                if (NativeInput.JustPressed(GTA.Control.Context))
                {
                    int ammo = N.Call<int>(N.GET_AMMO_IN_PED_WEAPON, p, N.WeaponPetrolCan);
                    double canLiters = ammo / (double)FuelModel.JerryCanFullAmmo * FuelModel.JerryCanLiters;
                    StartSession(last, spec, rec, Math.Min(canLiters, spec.TankLiters - rec.Liters), true, 0f, null);
                }
            }
        }

        private Vehicle cachedVeh;
        private VehicleFuelRecord cachedRec;

        private VehicleFuelRecord GetRecordCached(Vehicle v, FuelSpec spec)
        {
            if (v == cachedVeh && cachedRec != null) return cachedRec;
            cachedVeh = v;
            cachedRec = GetRecord(v, spec);
            return cachedRec;
        }

        private static bool HasJerryCanFuel(Ped p)
        {
            if (!N.Call<bool>(N.HAS_PED_GOT_WEAPON, p, N.WeaponPetrolCan, false)) return false;
            return N.Call<int>(N.GET_AMMO_IN_PED_WEAPON, p, N.WeaponPetrolCan) > 50;
        }

        // ------------------------------------------------------------ pump menu

        private void OpenPumpMenu(Vehicle v, FuelSpec spec, VehicleFuelRecord rec)
        {
            float price = main.Config.Settings.FuelPricePerLiter;
            string station = nearStation != null ? nearStation.Name : L.S("Gas station", "АЗС");
            double missing = Math.Max(0, spec.TankLiters - rec.Liters);

            Menu m = new Menu(L.S("Gas station", "АЗС"), station);
            m.Add(new MenuItem(L.S("In tank", "В баке"), null).WithValue(() =>
                L.Liters(rec.Liters) + " / " + L.Liters(spec.TankLiters)));
            m.Add(new MenuItem(L.S("Price", "Цена"), null).WithValue(() => L.Money(price) + L.S(" / L", " / л")));
            m.Add(new MenuItem(L.S("Cash", "Наличные"), null).WithValue(() => L.Money(GTA.Game.Player.Money)));

            AddPumpOption(m, L.S("Fill the tank", "Полный бак"), missing, v, spec, rec, price, station);
            if (missing > 10.5) AddPumpOption(m, L.S("Add 10 L", "Залить 10 л"), 10, v, spec, rec, price, station);
            if (missing > 25.5) AddPumpOption(m, L.S("Add 25 L", "Залить 25 л"), 25, v, spec, rec, price, station);
            m.Add(new MenuItem(L.S("Cancel", "Отмена"), () => main.Menu.Close()));
            main.Menu.Open(m);
        }

        private void AddPumpOption(Menu m, string label, double liters, Vehicle v, FuelSpec spec,
                                   VehicleFuelRecord rec, float price, string station)
        {
            double cost = Math.Round(liters * price);
            m.Add(new MenuItem(label, () =>
            {
                main.Menu.Close();
                double affordable = GTA.Game.Player.Money / (double)price;
                double amount = Math.Min(liters, affordable);
                if (amount < 0.5)
                {
                    main.Notify(L.S("Not enough cash to buy fuel.", "Недостаточно наличных для покупки топлива."));
                    return;
                }
                StartSession(v, spec, rec, amount, false, price, station);
            }).WithValue(() => L.Liters(liters) + "   " + L.Money(cost))
              .WithDescription(L.S("You pay for the fuel actually pumped. Walk away or press E again to stop early.",
                                   "Оплата за фактически залитое топливо. Отойдите или снова нажмите E, чтобы остановить.")));
        }

        // ------------------------------------------------------------ refuel session

        private void StartSession(Vehicle v, FuelSpec spec, VehicleFuelRecord rec, double liters, bool jerry, float price, string station)
        {
            if (liters < 0.1) return;
            RefuelSession s = new RefuelSession();
            s.Vehicle = v;
            s.Spec = spec;
            s.Record = rec;
            s.Target = liters;
            s.JerryCan = jerry;
            s.Price = price;
            s.StationName = station;
            s.RequestedAt = Environment.TickCount;
            s.LastFrame = Environment.TickCount;
            session = s;

            Ped p = GTA.Game.Player.Character;
            Vector3 d = v.Position - p.Position;
            p.Heading = (float)(Math.Atan2(-d.X, d.Y) * 180.0 / Math.PI);
            N.Call(N.REQUEST_ANIM_DICT, AnimDict);
        }

        private void UpdateSession(Ped p)
        {
            RefuelSession s = session;
            int now = Environment.TickCount;
            double dt = Math.Min(0.25, Math.Max(0, unchecked(now - s.LastFrame) / 1000.0));
            s.LastFrame = now;

            bool vehicleOk = s.Vehicle != null && s.Vehicle.Exists() && !s.Vehicle.IsDead;
            bool stop = !vehicleOk || p.IsDead || p.IsInVehicle() || N.Call<bool>(N.IS_PED_RAGDOLL, p)
                        || p.Position.DistanceTo(s.Vehicle.Position) > 7.5f;
            if (!stop && unchecked(now - s.RequestedAt) > 400 && NativeInput.JustPressed(GTA.Control.Context)) stop = true;

            if (!s.AnimPlaying && N.Call<bool>(N.HAS_ANIM_DICT_LOADED, AnimDict))
            {
                N.Call(N.TASK_PLAY_ANIM, p, AnimDict, AnimName, 2.0f, 8.0f, -1, 49, 0f, false, false, false);
                s.AnimPlaying = true;
            }

            if (!stop && !GTA.Game.IsPaused)
            {
                double rate = s.JerryCan ? FuelModel.JerryCanRate : FuelModel.PumpRate;
                double add = Math.Min(rate * dt, s.Target - s.Added);
                add = Math.Min(add, s.Spec.TankLiters - s.Record.Liters);
                if (add > 0)
                {
                    s.Added += add;
                    s.Record.Liters += add;
                }
                if (s.Added >= s.Target - 0.001 || s.Record.Liters >= s.Spec.TankLiters - 0.001) stop = true;
            }

            DrawProgress(s);
            if (stop) FinishSession(true);
        }

        private void FinishSession(bool showReceipt)
        {
            RefuelSession s = session;
            session = null;
            Ped p = GTA.Game.Player.Character;
            if (p != null && p.Exists() && s.AnimPlaying) N.Call(N.STOP_ANIM_TASK, p, AnimDict, AnimName, 1.0f);
            N.Call(N.REMOVE_ANIM_DICT, AnimDict);
            if (s.Added < 0.05) return;

            if (s.Vehicle != null && s.Vehicle.Exists() && curVeh == s.Vehicle) outOfFuel = false;
            SaveData data = main.Saves.Data;

            if (s.JerryCan)
            {
                if (p != null && p.Exists())
                {
                    int ammo = N.Call<int>(N.GET_AMMO_IN_PED_WEAPON, p, N.WeaponPetrolCan);
                    int used = (int)Math.Ceiling(s.Added / FuelModel.JerryCanLiters * FuelModel.JerryCanFullAmmo);
                    N.Call(N.SET_PED_AMMO, p, N.WeaponPetrolCan, Math.Max(0, ammo - used), false);
                }
                if (showReceipt) main.Notify(L.F("Poured {0} from the jerry can.", "Залито {0} из канистры.", L.Liters(s.Added)));
                return;
            }

            int cost = (int)Math.Round(s.Added * s.Price);
            cost = Math.Min(cost, GTA.Game.Player.Money);
            GTA.Game.Player.Money -= cost;
            data.Driving.FuelLitersBought += s.Added;
            data.Driving.FuelMoneySpent += cost;
            data.AddTransaction("Fuel", string.Format(CultureInfo.InvariantCulture, "{0:0.0} L @ {1}", s.Added, s.StationName), -cost);
            if (showReceipt)
            {
                main.Notify(L.F("~b~Receipt~s~ {0}~n~{1} x {2} = {3}~n~Cash left: {4}",
                                "~b~Чек~s~ {0}~n~{1} x {2} = {3}~n~Осталось наличных: {4}",
                    s.StationName, L.Liters(s.Added), L.Money(s.Price), L.Money(cost), L.Money(GTA.Game.Player.Money)));
            }
            Log.Info(string.Format(CultureInfo.InvariantCulture, "Refuelled {0:0.0} L for ${1} at {2}", s.Added, cost, s.StationName));
        }

        private void DrawProgress(RefuelSession s)
        {
            float vw = GTA.UI.Screen.ScaledWidth;
            float x = vw / 2f - 150f, y = 600f;
            panel.Position = new PointF(x, y);
            panel.Draw();
            string label = s.JerryCan ? L.S("Pouring fuel", "Заливка из канистры") : L.S("Refuelling", "Заправка");
            barText.Caption = s.JerryCan
                ? string.Format("{0}   {1} / {2}", label, L.Liters(s.Added), L.Liters(s.Target))
                : string.Format("{0}   {1} / {2}   {3}", label, L.Liters(s.Added), L.Liters(s.Target), L.Money(Math.Round(s.Added * s.Price)));
            barText.Position = new PointF(x + 12f, y + 6f);
            barText.Draw();
            barBack.Position = new PointF(x + 12f, y + 31f);
            barBack.Draw();
            float frac = s.Target > 0 ? (float)Math.Min(1.0, s.Added / s.Target) : 0f;
            barFill.Position = new PointF(x + 12f, y + 31f);
            barFill.Size = new SizeF(276f * frac, 5f);
            barFill.Draw();
        }

        // ------------------------------------------------------------ stations file

        private void LoadStations()
        {
            stations.Clear();
            string file = main.Paths.GasStationsFile;
            if (!File.Exists(file)) WriteDefaultStations(file);
            try
            {
                IniFile ini = IniFile.Load(file);
                foreach (string section in ini.SectionNames)
                {
                    if (section.Length == 0) continue;
                    if (!string.Equals(ini.Get(section, "Enabled", "true"), "true", StringComparison.OrdinalIgnoreCase)) continue;
                    float x, y, z;
                    if (!F(ini.Get(section, "X", null), out x) || !F(ini.Get(section, "Y", null), out y) || !F(ini.Get(section, "Z", null), out z))
                    {
                        Log.Warn("GasStations.ini: [" + section + "] invalid coordinates; skipped.");
                        continue;
                    }
                    GasStation g = new GasStation();
                    g.Name = ini.Get(section, "Name", section);
                    g.Position = new Vector3(x, y, z);
                    stations.Add(g);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not read GasStations.ini", ex);
            }
            Log.Info("Gas stations loaded: " + stations.Count);
        }

        private static bool F(string s, out float f)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f);
        }

        private static void WriteDefaultStations(string file)
        {
            string[][] d =
            {
                new[] { "Route 68, Harmony", "49.42", "2778.79", "58.04" },
                new[] { "Route 68, Harmony East", "263.89", "2606.46", "44.98" },
                new[] { "Route 68, Grand Senora", "1039.96", "2671.13", "39.55" },
                new[] { "Route 68, Grand Senora East", "1207.26", "2660.18", "37.90" },
                new[] { "Senora Fwy, Rex's Diner", "2539.69", "2594.19", "37.94" },
                new[] { "Senora Fwy, Grand Senora", "2679.86", "3263.95", "55.24" },
                new[] { "Sandy Shores", "2005.06", "3773.89", "32.40" },
                new[] { "Sandy Shores Airfield", "1784.32", "3330.55", "41.25" },
                new[] { "Grapeseed", "1687.16", "4929.39", "42.08" },
                new[] { "Great Ocean Hwy, Mount Chiliad", "1701.31", "6416.03", "32.76" },
                new[] { "Paleto Bay", "179.86", "6602.84", "31.87" },
                new[] { "Paleto Bay West", "-94.46", "6419.59", "31.49" },
                new[] { "Lago Zancudo", "-2555.00", "2334.40", "33.08" },
                new[] { "Richman Glen", "-1800.38", "803.66", "138.65" },
                new[] { "Morningwood", "-1437.62", "-276.75", "46.21" },
                new[] { "Pacific Bluffs", "-2096.24", "-320.29", "13.17" },
                new[] { "Little Seoul", "-724.62", "-935.16", "19.21" },
                new[] { "Little Seoul, Calais Ave", "-526.02", "-1211.00", "18.18" },
                new[] { "Davis, Grove St", "-70.21", "-1761.79", "29.53" },
                new[] { "Davis, Macdonald St", "176.63", "-1562.03", "29.26" },
                new[] { "Strawberry", "265.65", "-1261.31", "29.29" },
                new[] { "Vespucci, Little Bighorn Ave", "-319.29", "-1471.72", "30.55" },
                new[] { "La Mesa", "819.65", "-1028.85", "26.40" },
                new[] { "El Burro Heights", "1208.95", "-1402.57", "35.22" },
                new[] { "Mirror Park", "1181.38", "-330.85", "69.32" },
                new[] { "Downtown Vinewood", "620.84", "269.10", "103.09" },
                new[] { "Palomino Fwy, Tataviam", "2581.32", "362.04", "108.47" }
            };
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; Realistic Life Simulator - gas stations / АЗС.");
            sb.AppendLine("; Approximate forecourt centres. Refuelling is offered next to any pump prop within 60 m.");
            sb.AppendLine("; Примерные координаты. Заправка доступна у любой колонки в радиусе 60 м.");
            sb.AppendLine();
            for (int i = 0; i < d.Length; i++)
            {
                sb.AppendLine("[Station" + (i + 1).ToString("00") + "]");
                sb.AppendLine("Name=" + d[i][0]);
                sb.AppendLine("X=" + d[i][1]);
                sb.AppendLine("Y=" + d[i][2]);
                sb.AppendLine("Z=" + d[i][3]);
                sb.AppendLine("Enabled=true");
                sb.AppendLine();
            }
            FileUtil.WriteAllTextAtomic(file, sb.ToString());
        }
    }
}
