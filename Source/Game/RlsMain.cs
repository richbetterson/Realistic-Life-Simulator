// Realistic Life Simulator - entry point (CoreManager).
// Loaded by ScriptHookVDotNet v3 from scripts\RealisticLifeSimulator.dll.
// Owns configuration, saving, logging, the module scheduler and the menu.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GTA;
using RealisticLifeSimulator.Core;
using Keys = System.Windows.Forms.Keys;

namespace RealisticLifeSimulator.Game
{
    public class RlsMain : Script
    {
        public const string Version = "1.1.0";
        private const int MaxModuleFailures = 5;

        private sealed class ModuleSlot
        {
            public IModule Module;
            public int NextRun;
            public int Failures;
            public bool Disabled;
        }

        public RlsPaths Paths;
        public ConfigurationManager Config;
        public SaveManager Saves;
        public KeyConflictChecker Conflicts;
        public MenuController Menu;
        public PerformanceManager Performance;
        public LifeHubModule Hubs;
        public FuelSystem Fuel;
        public NpcFuelModule NpcFuel;
        public TrafficLawSystem Traffic;
        public Dispatch Dispatch;
        public PoliceStopSystem Police;
        public AccidentSystem Accidents;
        public NpcTrafficModule NpcTraffic;
        public NpcAccidentSystem NpcAccidents;
        public VehicleOwnership Ownership;
        public RepairShops Repairs;
        public PropertySystem Properties;
        public PassengerSystem Passenger;
        public TaxiService Taxi;
        public RideMenus RideMenus;
        public SpeedLimiter Limiter;
        public PolicePatrolSystem Patrols;
        public LifeHubMenus Menus { get { return menus; } }
        private readonly Random rng = new Random();

        private readonly List<ModuleSlot> modules = new List<ModuleSlot>();
        private readonly List<IFrameModule> frameModules = new List<IFrameModule>();
        private readonly Dictionary<RlsAction, Action> actionHandlers = new Dictionary<RlsAction, Action>();
        private readonly Stopwatch frameTimer = new Stopwatch();
        private LifeHubMenus menus;
        private bool startupFailed;
        private bool gameStarted;
        private bool shutDown;
        private int nextLogFlush;

        public RlsMain()
        {
            try
            {
                string gameRoot = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                Paths = new RlsPaths(gameRoot);
                Paths.EnsureDirectories();
                Log.Init(Paths.LogFile);
                Log.Info("=== Realistic Life Simulator " + Version + " starting (game root: " + gameRoot + ") ===");

                Config = new ConfigurationManager(Paths);
                Config.LoadAll();
                Log.DebugEnabled = Config.Settings.DebugLogging;
                L.Set(L.Resolve(Config.Settings.Language, false)); // refined with the game language once in game
                Log.Info("Key bindings assigned: " + Config.Bindings.AssignedCount + " of " + ActionCatalog.All.Length);

                Saves = new SaveManager(Paths, Version);
                Saves.Load();

                Conflicts = new KeyConflictChecker();
                Conflicts.ScanExternal(gameRoot);
                Log.Info("Scanned " + Conflicts.ScannedFiles.Count + " other config file(s) for key conflicts.");

                Performance = new PerformanceManager(Config.Settings.Profile);
                Menu = new MenuController();
                menus = new LifeHubMenus(this);
            }
            catch (Exception ex)
            {
                startupFailed = true;
                Log.Error("Startup failed; the mod is inactive this session", ex);
                Log.Flush();
            }

            Interval = 0; // needed for drawing; all real work is throttled per module
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;
        }

        // ------------------------------------------------------------ startup (in game)

        private void StartGameSide()
        {
            gameStarted = true;

            ApplyLanguage();

            Hubs = new LifeHubModule(Paths, Config, OpenLifeHub, () => Menu.IsOpen);
            Fuel = new FuelSystem(this);
            NpcFuel = new NpcFuelModule(this, Fuel);
            Traffic = new TrafficLawSystem(this);
            Dispatch = new Dispatch(this);
            Police = new PoliceStopSystem(this);
            Accidents = new AccidentSystem(this);
            NpcTraffic = new NpcTrafficModule(this);
            NpcAccidents = new NpcAccidentSystem(this);
            Ownership = new VehicleOwnership(this);
            Repairs = new RepairShops(this);
            Properties = new PropertySystem(this);
            Passenger = new PassengerSystem(this);
            Taxi = new TaxiService(this);
            RideMenus = new RideMenus(this);
            Limiter = new SpeedLimiter(this);
            Patrols = new PolicePatrolSystem(this);
            AddModule(new PlaytimeModule(Saves));
            AddModule(new AutosaveModule(Saves, Config));
            AddModule(Hubs);
            AddModule(Fuel);
            AddModule(NpcFuel);
            AddModule(Traffic);
            AddModule(Police);
            AddModule(Accidents);
            AddModule(NpcTraffic);
            AddModule(NpcAccidents);
            AddModule(Ownership);
            AddModule(Properties);
            AddModule(Passenger);
            AddModule(Taxi);
            AddModule(Limiter);
            AddModule(Patrols);
            AddModule(new LicenceWatchModule(this));
            frameModules.Add(Hubs);
            frameModules.Add(Limiter);
            frameModules.Add(Fuel);
            frameModules.Add(Police);
            frameModules.Add(Accidents);
            frameModules.Add(NpcAccidents);
            frameModules.Add(Ownership);
            frameModules.Add(Properties);
            frameModules.Add(new DriveHud(this, Fuel, Traffic));

            actionHandlers[RlsAction.OpenLifeHub] = OpenLifeHub;
            actionHandlers[RlsAction.OpenSettings] = () => menus.OpenSettingsDirect();
            actionHandlers[RlsAction.CallPolice] = () => Police.CallForAssistance();
            actionHandlers[RlsAction.OpenPropertyMenu] = () => menus.OpenDirect(menus.BuildRealEstate());
            actionHandlers[RlsAction.OpenVehicleMenu] = () => menus.OpenDirect(menus.BuildMyVehicles());
            actionHandlers[RlsAction.PassengerMenu] = OpenRideMenu;
            actionHandlers[RlsAction.CallTaxi] = () => Taxi.Request();
            actionHandlers[RlsAction.ToggleSpeedLimiter] = () => Limiter.Toggle();

            foreach (ModuleSlot s in modules) Run(s, s.Module.Start, "Start");

            bool firstRun = !Saves.Data.FirstRunNoticeShown;
            if (firstRun) Saves.Data.PassengerNoticeShown = true; // covered by the install notice
            Saves.Data.Stats.SessionsStarted++;
            if (!Saves.Data.FirstRunNoticeShown)
            {
                Notify(L.S("Realistic Life Simulator installed. No keys are assigned. Walk into a blue Life Hub marker (map icon) to open the menu.",
                           "Realistic Life Simulator установлен. Клавиши не назначены. Встаньте в синий маркер центра управления (значок на карте), чтобы открыть меню."));
                Saves.Data.FirstRunNoticeShown = true;
                Saves.Data.Phase2NoticeShown = true;
                Saves.Data.Phase3NoticeShown = true;
                Saves.Data.Phase4NoticeShown = true;
                Saves.Data.Phase5NoticeShown = true;
            }
            else if (!Saves.Data.Phase5NoticeShown)
            {
                Notify(L.S("Realistic Life Simulator updated: buy homes and cars, store cars in garages, service and repair them. See the Life Hub.",
                           "Realistic Life Simulator обновлён: покупайте жильё и машины, храните машины в гаражах, обслуживайте и ремонтируйте их. Подробнее в центре управления."));
                Saves.Data.Phase2NoticeShown = true;
                Saves.Data.Phase3NoticeShown = true;
                Saves.Data.Phase4NoticeShown = true;
                Saves.Data.Phase5NoticeShown = true;
            }
            else if (!Saves.Data.Phase4NoticeShown)
            {
                Notify(L.S("Realistic Life Simulator updated: NPC drivers now have personalities, follow speed limits, make mistakes and handle their own accidents.",
                           "Realistic Life Simulator обновлён: у водителей NPC появились характеры, они соблюдают ограничения скорости, ошибаются и сами разбираются в своих ДТП."));
                Saves.Data.Phase2NoticeShown = true;
                Saves.Data.Phase3NoticeShown = true;
                Saves.Data.Phase4NoticeShown = true;
                Saves.Data.Phase5NoticeShown = true;
            }
            else if (!Saves.Data.Phase3NoticeShown)
            {
                Notify(L.S("Realistic Life Simulator updated: police traffic stops, accidents and accident reports are now active.",
                           "Realistic Life Simulator обновлён: теперь работают остановки полицией, ДТП и протоколы."));
                Saves.Data.Phase2NoticeShown = true;
                Saves.Data.Phase3NoticeShown = true;
            }
            else if (!Saves.Data.Phase2NoticeShown)
            {
                Notify(L.S("Realistic Life Simulator updated: fuel, gas stations and traffic laws are now active.",
                           "Realistic Life Simulator обновлён: теперь работают топливо, АЗС и правила движения."));
                Saves.Data.Phase2NoticeShown = true;
            }
            if (!Saves.Data.PassengerNoticeShown)
            {
                Notify(L.S("New: ask NPC drivers for a ride or call a taxi. Life Hub > Rides and taxi. No keys assigned.",
                           "Новое: просите водителей NPC подвезти или вызывайте такси. Центр управления > Поездки и такси. Клавиши не назначены."));
                Saves.Data.PassengerNoticeShown = true;
            }
            if (firstRun) Saves.Data.V060NoticeShown = true;
            if (!Saves.Data.V060NoticeShown)
            {
                Notify(L.S("Updated: automatic traffic fines, more police patrols, speed limiter with automatic road limits, rental income from properties, fixed licence checks. See the Life Hub.",
                           "Обновление: автоматические штрафы, больше патрулей, ограничитель скорости с автоматическими лимитами дорог, доход от сдачи недвижимости, исправлена проверка прав. Подробнее в центре управления."));
                Saves.Data.V060NoticeShown = true;
            }
            // Licence status (e.g. 24+ points -> revoked) is applied by LicenceWatchModule on its first update.
            Saves.Save();
            Log.Info("Game-side start complete. Modules: " + modules.Count);
        }

        private void AddModule(IModule m)
        {
            ModuleSlot s = new ModuleSlot();
            s.Module = m;
            s.NextRun = Environment.TickCount;
            modules.Add(s);
        }

        // ------------------------------------------------------------ main loop

        private void OnTick(object sender, EventArgs e)
        {
            if (startupFailed || shutDown) return;
            frameTimer.Restart();

            if (!gameStarted)
            {
                Ped p = GTA.Game.Player.Character;
                if (p == null || !p.Exists()) return;
                StartGameSide();
            }

            int now = Environment.TickCount;
            for (int i = 0; i < modules.Count; i++)
            {
                ModuleSlot s = modules[i];
                if (s.Disabled || unchecked(now - s.NextRun) < 0) continue;
                s.NextRun = now + s.Module.IntervalMs;
                Run(s, s.Module.Update, "Update");
            }

            for (int i = 0; i < frameModules.Count; i++)
            {
                try { frameModules[i].OnFrame(); }
                catch (Exception ex) { Log.Error("Frame update failed", ex); }
            }

            try { Menu.OnFrame(); }
            catch (Exception ex)
            {
                Log.Error("Menu failed; closing it", ex);
                Menu.Close();
            }

            if (unchecked(now - nextLogFlush) >= 0)
            {
                nextLogFlush = now + 5000;
                Log.Flush();
            }

            frameTimer.Stop();
            Performance.RecordFrame(frameTimer.Elapsed.TotalMilliseconds);
        }

        // A module that keeps throwing is switched off instead of taking the whole mod down.
        private void Run(ModuleSlot s, Action call, string phase)
        {
            try
            {
                call();
                s.Failures = 0;
            }
            catch (Exception ex)
            {
                s.Failures++;
                Log.Error(s.Module.Name + "." + phase + " failed (" + s.Failures + ")", ex);
                if (s.Failures >= MaxModuleFailures || phase == "Start")
                {
                    s.Disabled = true;
                    Log.Error(s.Module.Name + " disabled for this session.");
                    Notify(L.F("{0} stopped after errors. See the Logs folder.", "{0}: остановлено из-за ошибок. Смотрите папку Logs.", s.Module.Name));
                }
            }
        }

        private void OnKeyDown(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (startupFailed || !gameStarted || shutDown) return;
            if (SpeedLimiter.TextInputActive) return; // typing into GTA's on-screen keyboard

            Keys key = e.KeyCode;
            if (Menu.IsOpen)
            {
                if (!Menu.IsCapturingKey && key != Keys.None && Config.Bindings.Get(RlsAction.OpenLifeHub) == key)
                {
                    Menu.Close(); // the Life Hub key also closes the menu
                    return;
                }
                Menu.HandleKey(key);
                return;
            }

            if (e.Alt || e.Control) return; // leave Alt/Ctrl combinations to Windows and other tools
            if (GTA.Game.IsPaused) return;

            RlsAction action;
            if (!Config.Bindings.TryGetAction(key, out action)) return;

            Action handler;
            if (actionHandlers.TryGetValue(action, out handler))
            {
                try { handler(); }
                catch (Exception ex) { Log.Error("Action " + action + " failed", ex); }
            }
            else
            {
                Notify(L.F("{0} arrives in a later phase.", "«{0}» появится на следующих этапах.", ActionCatalog.DisplayName(action)));
            }
        }

        public bool IsActionAvailable(RlsAction a)
        {
            return actionHandlers.ContainsKey(a);
        }

        private void OpenLifeHub()
        {
            if (Menu.IsOpen) return;
            Ped p = GTA.Game.Player.Character;
            if (p == null || !p.Exists() || p.IsDead) return;
            if (GTA.Native.Function.Call<bool>(GTA.Native.Hash.IS_CUTSCENE_PLAYING)) return;
            menus.OpenMain();
        }

        // Rides and taxi page (works on foot, as a passenger and in a taxi).
        public void OpenRideMenu()
        {
            if (Menu.IsOpen || RideMenus == null) return;
            Ped p = GTA.Game.Player.Character;
            if (p == null || !p.Exists() || p.IsDead) return;
            menus.OpenDirect(RideMenus.BuildPage());
        }

        public void ApplyProfile()
        {
            Performance.SetProfile(Config.Settings.Profile);
        }

        public void ApplyLanguage()
        {
            bool gameRussian = false;
            try { gameRussian = GTA.Game.Language == GTA.Language.Russian; }
            catch (Exception) { }
            L.Set(L.Resolve(Config.Settings.Language, gameRussian));
            Log.Info("Language: " + L.Current + " (setting " + Config.Settings.Language + ", game Russian: " + gameRussian + ")");
            if (Hubs != null) Hubs.RecreateBlips();
        }

        // Licence, insurance and reputation of the character currently played.
        public DriverProfile CurrentProfile()
        {
            Ped p = GTA.Game.Player.Character;
            string name = p != null && p.Exists() ? N.CharacterName(p) : "Unknown";
            return Saves.Data.Profile(name, rng);
        }

        // One compact feed post. Used sparingly for things the player must know.
        // The same text within 4 seconds is posted only once (no notification spam).
        private string lastNotice = "";
        private int lastNoticeAt;

        public void Notify(string message)
        {
            int now = Environment.TickCount;
            if (message == lastNotice && unchecked(now - lastNoticeAt) < 4000) return;
            lastNotice = message;
            lastNoticeAt = now;
            GTA.UI.Notification.Show("~b~Life Sim:~s~ " + message);
        }

        // Tells the player when their licence status changed (one place for every system).
        public void AnnounceLicenceChange(LicenceStatus before, DriverProfile p)
        {
            double play = Saves.Data.Stats.TotalPlayTimeSeconds;
            LicenceStatus now = LicenceRules.Status(p, play);
            if (now == before) return;
            Log.Info("Licence status " + before + " -> " + now + " (" + (p.StatusReason ?? "") + ")");
            if (now == LicenceStatus.Revoked)
                Notify(L.F("~r~Driving licence REVOKED.~s~ {0}~n~Reinstatement: Life Hub > Police & legal status.",
                           "~r~Права ОТОЗВАНЫ.~s~ {0}~n~Восстановление: центр управления > Полиция и правовой статус.", p.StatusReason ?? ""));
            else if (now == LicenceStatus.Suspended)
                Notify(L.S("~o~Driving licence SUSPENDED.~s~ Do not drive until it is valid again.",
                           "~o~Права ПРИОСТАНОВЛЕНЫ.~s~ Не садитесь за руль, пока они снова не станут действительными."));
            else
                Notify(L.S("~g~Your driving licence is valid again.~s~", "~g~Ваши права снова действительны.~s~"));
        }

        // ------------------------------------------------------------ shutdown

        private void OnAborted(object sender, EventArgs e)
        {
            if (shutDown) return;
            shutDown = true;
            try
            {
                foreach (ModuleSlot s in modules)
                {
                    try { s.Module.Shutdown(); }
                    catch (Exception ex) { Log.Error(s.Module.Name + ".Shutdown failed", ex); }
                }
                if (Dispatch != null) Dispatch.ReleaseAll();
                if (Saves != null && gameStarted) Saves.Save();
                Log.Info("Shut down cleanly.");
            }
            catch (Exception ex)
            {
                Log.Error("Shutdown error", ex);
            }
            Log.Close();
        }
    }
}
