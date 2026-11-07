// Realistic Life Simulator - settings and key binding files.
//   Config\Settings.ini     general options (validated, repaired if broken)
//   Config\KeyBindings.ini  every entry EMPTY on first install

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace RealisticLifeSimulator.Core
{
    public enum PerformanceProfileKind { Low, Balanced, High }

    public enum FuelGaugeMode { Always, LowOnly, Off }

    // Append only (names are written to Settings.ini). Automatic = 0.6.0 default.
    public enum FineMode { Witnessed, Always, Off, Automatic }

    public sealed class RlsSettings
    {
        // General / interface
        public LanguageSetting Language = LanguageSetting.Auto;
        public PerformanceProfileKind Profile = PerformanceProfileKind.Low;
        public int AutosaveMinutes = 5;
        public bool ShowHubBlips = true;
        public FuelGaugeMode FuelGauge = FuelGaugeMode.Always;
        public bool ShowSpeedLimit = true;
        public bool DebugLogging = false;

        // Fuel
        public bool FuelEnabled = true;
        public float FuelPricePerLiter = 1.65f;
        public float FuelConsumption = 1.0f;     // 1.0 = full tank lasts roughly 1-1.5 h of driving
        public bool NpcRefuelling = true;
        public int LowFuelWarningPercent = 15;

        // Traffic
        public bool TrafficEnabled = true;
        public FineMode Fines = FineMode.Automatic;
        public int SpeedToleranceKmh = 10;
        public float FineMultiplier = 1.0f;
        public bool DetectSpeeding = true;
        public bool DetectRedLights = true;
        public bool DetectStopSigns = true;
        public bool DetectWrongWay = true;
        public bool DetectHeadlights = true;

        // Police and accidents (Phase 3)
        public bool PoliceStops = true;      // police pull the player over instead of mailing a ticket
        public bool NpcPoliceStops = true;   // police also stop speeding NPC drivers
        public bool AccidentsEnabled = true;
        public int AccidentMinSpeedKmh = 12; // impacts below this are ignored (parking bumps)
        public bool NpcCallEmergency = true; // other drivers call 911 after serious crashes

        // NPC traffic (Phase 4)
        public bool NpcTraffic = true;        // personalities, speed limits
        public bool NpcMistakes = true;       // occasional believable mistakes
        public bool NpcYieldEmergency = true; // NPCs slow down for sirens
        public bool NpcAccidents = true;      // NPC-vs-NPC accident scenes
        public bool DetectFailureToYield = true;

        // Ownership (Phase 5)
        public int BillingIntervalMinutes = 60;  // play time between bills (maintenance, premiums, rent)
        public bool VehicleWear = true;
        public float WearRate = 1.0f;
        public int ServiceIntervalKm = 400;
        public bool ShowPropertiesOnMap = true;

        // Passengers and taxi
        public bool PassengerEnabled = true;
        public bool TaxiEnabled = true;
        public float TaxiBaseFare = 3.0f;
        public float TaxiPricePerKm = 2.2f;
        public float TaxiPricePerMinute = 0.35f;

        // 0.6.0
        public const int CurrentSettingsVersion = 2;
        public FineTable FineAmounts = new FineTable();
        public AutoEnforcement Auto = new AutoEnforcement();
        public PoliceDensity PolicePatrols = PoliceDensity.Normal;
        public bool PatrolLicenceChecks = true;       // patrols run plate / licence checks
        // 1.1.0: pedestrian accidents were removed; an old "PedestrianAccidents" entry is ignored.
        public bool SpeedLimiterEnabled = false;
        public int SpeedLimiterKmh = 60;
        public bool AutoSpeedLimit = false;           // 1.1.0: limiter follows the road limit + 10 km/h
        public bool ShorterRedLights = true;
        public int MaxRedWaitSeconds = 20;
        public int AssistGreenSeconds = 10;

        public static readonly FineMode[] FineModeOrder = { FineMode.Automatic, FineMode.Witnessed, FineMode.Always, FineMode.Off };

        public static readonly float[] TaxiBaseChoices = { 0f, 2.0f, 3.0f, 5.0f, 8.0f };
        public static readonly float[] TaxiPerKmChoices = { 1.0f, 1.5f, 2.2f, 3.0f, 4.0f };
        public static readonly float[] TaxiPerMinuteChoices = { 0f, 0.2f, 0.35f, 0.5f, 1.0f };

        public static readonly int[] BillingChoices = { 30, 60, 90, 120 };
        public static readonly float[] WearChoices = { 0.5f, 1.0f, 1.5f, 2.0f };

        public static readonly int[] AutosaveChoices = { 2, 5, 10, 15, 30 };
        public static readonly float[] FuelPriceChoices = { 1.0f, 1.35f, 1.65f, 2.0f, 2.5f, 3.0f };
        public static readonly float[] ConsumptionChoices = { 0.5f, 0.75f, 1.0f, 1.5f, 2.0f };
        public static readonly int[] ToleranceChoices = { 0, 5, 10, 15, 20 };
        public static readonly float[] FineMultiplierChoices = { 0.5f, 1.0f, 1.5f, 2.0f };
    }

    public sealed class ConfigurationManager
    {
        private const string BindingsSection = "KeyBindings";

        private readonly RlsPaths paths;
        private RlsSettings settings = new RlsSettings();
        private KeyBindingSet bindings = new KeyBindingSet();

        public ConfigurationManager(RlsPaths paths)
        {
            this.paths = paths;
        }

        public RlsSettings Settings { get { return settings; } }
        public KeyBindingSet Bindings { get { return bindings; } }

        public void LoadAll()
        {
            LoadSettings();
            LoadBindings();
        }

        // ------------------------------------------------------------ settings

        public void LoadSettings()
        {
            string file = paths.SettingsFile;
            RlsSettings s = new RlsSettings();

            if (!File.Exists(file))
            {
                settings = s;
                TrafficRules.Fines = s.FineAmounts;
                SaveSettings();
                Log.Info("Created default " + file);
                return;
            }

            IniFile ini;
            try
            {
                ini = IniFile.Load(file);
            }
            catch (Exception ex)
            {
                Log.Error("Settings.ini unreadable, restoring defaults", ex);
                TryBackup(file, "unreadable");
                settings = s;
                TrafficRules.Fines = s.FineAmounts;
                SaveSettings();
                return;
            }

            SettingsReader r = new SettingsReader(ini);
            s.Language = r.Enum("General", "Language", s.Language);
            s.Profile = r.Enum("Performance", "Profile", s.Profile);
            s.AutosaveMinutes = r.Int("Saving", "AutosaveMinutes", s.AutosaveMinutes, 1, 60);
            s.ShowHubBlips = r.Bool("Interface", "ShowLifeHubMapIcons", s.ShowHubBlips);
            s.FuelGauge = r.Enum("Interface", "FuelGauge", s.FuelGauge);
            s.ShowSpeedLimit = r.Bool("Interface", "ShowSpeedLimit", s.ShowSpeedLimit);

            s.FuelEnabled = r.Bool("Fuel", "Enabled", s.FuelEnabled);
            s.FuelPricePerLiter = r.Float("Fuel", "PricePerLiter", s.FuelPricePerLiter, 0.1f, 20f);
            s.FuelConsumption = r.Float("Fuel", "ConsumptionMultiplier", s.FuelConsumption, 0.1f, 5f);
            s.NpcRefuelling = r.Bool("Fuel", "NpcRefuelling", s.NpcRefuelling);
            s.LowFuelWarningPercent = r.Int("Fuel", "LowFuelWarningPercent", s.LowFuelWarningPercent, 0, 50);

            s.TrafficEnabled = r.Bool("Traffic", "Enabled", s.TrafficEnabled);
            s.Fines = r.Enum("Traffic", "FineMode", s.Fines);
            s.SpeedToleranceKmh = r.Int("Traffic", "SpeedToleranceKmh", s.SpeedToleranceKmh, 0, 40);
            s.FineMultiplier = r.Float("Traffic", "FineMultiplier", s.FineMultiplier, 0.1f, 10f);
            s.DetectSpeeding = r.Bool("Traffic", "DetectSpeeding", s.DetectSpeeding);
            s.DetectRedLights = r.Bool("Traffic", "DetectRedLights", s.DetectRedLights);
            s.DetectStopSigns = r.Bool("Traffic", "DetectStopSigns", s.DetectStopSigns);
            s.DetectWrongWay = r.Bool("Traffic", "DetectWrongWay", s.DetectWrongWay);
            s.DetectHeadlights = r.Bool("Traffic", "DetectHeadlights", s.DetectHeadlights);

            s.PoliceStops = r.Bool("Police", "TrafficStops", s.PoliceStops);
            s.NpcPoliceStops = r.Bool("Police", "NpcTrafficStops", s.NpcPoliceStops);
            s.AccidentsEnabled = r.Bool("Accidents", "Enabled", s.AccidentsEnabled);
            s.AccidentMinSpeedKmh = r.Int("Accidents", "MinImpactSpeedKmh", s.AccidentMinSpeedKmh, 3, 60);
            s.NpcCallEmergency = r.Bool("Accidents", "OtherDriversCallEmergency", s.NpcCallEmergency);

            s.NpcTraffic = r.Bool("NpcTraffic", "Enabled", s.NpcTraffic);
            s.NpcMistakes = r.Bool("NpcTraffic", "Mistakes", s.NpcMistakes);
            s.NpcYieldEmergency = r.Bool("NpcTraffic", "YieldToEmergencyVehicles", s.NpcYieldEmergency);
            s.NpcAccidents = r.Bool("NpcTraffic", "Accidents", s.NpcAccidents);
            s.DetectFailureToYield = r.Bool("Traffic", "DetectFailureToYield", s.DetectFailureToYield);

            s.BillingIntervalMinutes = r.Int("Ownership", "BillingIntervalMinutes", s.BillingIntervalMinutes, 10, 600);
            s.VehicleWear = r.Bool("Ownership", "VehicleWear", s.VehicleWear);
            s.WearRate = r.Float("Ownership", "WearRate", s.WearRate, 0.1f, 5f);
            s.ServiceIntervalKm = r.Int("Ownership", "ServiceIntervalKm", s.ServiceIntervalKm, 50, 5000);
            s.ShowPropertiesOnMap = r.Bool("Ownership", "ShowPropertiesOnMap", s.ShowPropertiesOnMap);

            s.PassengerEnabled = r.Bool("Passenger", "Enabled", s.PassengerEnabled);
            s.TaxiEnabled = r.Bool("Taxi", "Enabled", s.TaxiEnabled);
            s.TaxiBaseFare = r.Float("Taxi", "BaseFare", s.TaxiBaseFare, 0f, 100f);
            s.TaxiPricePerKm = r.Float("Taxi", "PricePerKm", s.TaxiPricePerKm, 0f, 100f);
            s.TaxiPricePerMinute = r.Float("Taxi", "PricePerMinuteWaiting", s.TaxiPricePerMinute, 0f, 20f);

            s.DebugLogging = r.Bool("Debug", "DebugLogging", s.DebugLogging);

            // 0.6.0 sections
            AutoEnforcement a = s.Auto;
            a.Speeding = r.Bool("Enforcement", "AutoSpeeding", a.Speeding);
            a.RedLight = r.Bool("Enforcement", "AutoRedLight", a.RedLight);
            a.WrongWay = r.Bool("Enforcement", "AutoWrongWay", a.WrongWay);
            a.FailureToYield = r.Bool("Enforcement", "AutoFailureToYield", a.FailureToYield);
            a.StopSign = r.Bool("Enforcement", "AutoStopSign", a.StopSign);
            a.Headlights = r.Bool("Enforcement", "AutoHeadlights", a.Headlights);
            a.AutoPayFromCash = r.Bool("Enforcement", "AutoPayFromCash", a.AutoPayFromCash);

            FineTable f = s.FineAmounts;
            f.SpeedingMinor = r.Int("FineAmounts", "SpeedingUpTo20", f.SpeedingMinor, 0, 100000);
            f.SpeedingMajor = r.Int("FineAmounts", "Speeding21To40", f.SpeedingMajor, 0, 100000);
            f.SpeedingExtreme = r.Int("FineAmounts", "SpeedingOver40", f.SpeedingExtreme, 0, 100000);
            f.RedLight = r.Int("FineAmounts", "RedLight", f.RedLight, 0, 100000);
            f.StopSign = r.Int("FineAmounts", "StopSign", f.StopSign, 0, 100000);
            f.WrongWay = r.Int("FineAmounts", "WrongWay", f.WrongWay, 0, 100000);
            f.NoHeadlights = r.Int("FineAmounts", "NoHeadlights", f.NoHeadlights, 0, 100000);
            f.Reckless = r.Int("FineAmounts", "RecklessDriving", f.Reckless, 0, 100000);
            f.FailureToYield = r.Int("FineAmounts", "FailureToYield", f.FailureToYield, 0, 100000);
            f.LeavingScene = r.Int("FineAmounts", "LeavingScene", f.LeavingScene, 0, 100000);
            f.HitPedestrian = r.Int("FineAmounts", "HitPedestrian", f.HitPedestrian, 0, 100000);
            f.NoDocuments = r.Int("FineAmounts", "NoDocuments", f.NoDocuments, 0, 100000);
            f.UnlicensedDriving = r.Int("FineAmounts", "UnlicensedDriving", f.UnlicensedDriving, 0, 100000);
            f.EvadingPolice = r.Int("FineAmounts", "EvadingPolice", f.EvadingPolice, 0, 100000);
            f.FalseStatement = r.Int("FineAmounts", "FalseStatement", f.FalseStatement, 0, 100000);

            s.PolicePatrols = r.Enum("Police", "PatrolDensity", s.PolicePatrols);
            s.PatrolLicenceChecks = r.Bool("Police", "LicenceChecks", s.PatrolLicenceChecks);
            s.SpeedLimiterEnabled = r.Bool("SpeedLimiter", "Enabled", s.SpeedLimiterEnabled);
            s.SpeedLimiterKmh = r.Int("SpeedLimiter", "LimitKmh", s.SpeedLimiterKmh, SpeedLimiterRules.MinKmh, SpeedLimiterRules.MaxKmh);
            s.AutoSpeedLimit = r.Bool("SpeedLimiter", "AutoRoadLimit", s.AutoSpeedLimit);
            s.ShorterRedLights = r.Bool("TrafficLights", "ShorterRedLights", s.ShorterRedLights);
            s.MaxRedWaitSeconds = r.Int("TrafficLights", "MaxRedWaitSeconds", s.MaxRedWaitSeconds, 5, 120);
            s.AssistGreenSeconds = r.Int("TrafficLights", "AssistGreenSeconds", s.AssistGreenSeconds, 4, 30);

            // Settings written before 0.6.0 used "Witnessed" as the default, which almost never
            // fined anything. Move that old default to Automatic once; any other choice is kept.
            string ver;
            int version = 1;
            if (ini.TryGet("General", "SettingsVersion", out ver))
            {
                int parsed;
                if (int.TryParse(ver, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) version = parsed;
            }
            if (version < 2 && s.Fines == FineMode.Witnessed)
            {
                s.Fines = FineMode.Automatic;
                r.Problems = true; // rewrite the file with the new mode and version
                Log.Info("Settings upgraded: FineMode Witnessed (old default) -> Automatic");
            }

            settings = s;
            TrafficRules.Fines = s.FineAmounts;
            if (r.Problems || ini.MalformedLines > 0)
            {
                Log.Warn("Settings.ini had missing or invalid entries; kept a copy and rewrote it.");
                TryBackup(file, "invalid");
                SaveSettings();
            }
        }

        public void SaveSettings()
        {
            RlsSettings s = settings;
            CultureInfo inv = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; Realistic Life Simulator - general settings / общие настройки.");
            sb.AppendLine("; Change these in game (Life Hub > Settings) or edit here while the game is closed.");
            sb.AppendLine("; Меняйте в игре (Центр управления > Настройки) или здесь, когда игра закрыта.");
            sb.AppendLine();
            sb.AppendLine("[General]");
            sb.AppendLine("; Auto | English | Russian");
            sb.AppendLine("Language=" + s.Language);
            sb.AppendLine("; Format version of this file (do not edit) / версия формата файла");
            sb.AppendLine("SettingsVersion=" + RlsSettings.CurrentSettingsVersion);
            sb.AppendLine();
            sb.AppendLine("[Performance]");
            sb.AppendLine("; Low | Balanced | High");
            sb.AppendLine("Profile=" + s.Profile);
            sb.AppendLine();
            sb.AppendLine("[Saving]");
            sb.AppendLine("; 1-60 minutes / минут");
            sb.AppendLine("AutosaveMinutes=" + s.AutosaveMinutes);
            sb.AppendLine();
            sb.AppendLine("[Interface]");
            sb.AppendLine("ShowLifeHubMapIcons=" + B(s.ShowHubBlips));
            sb.AppendLine("; Always | LowOnly | Off");
            sb.AppendLine("FuelGauge=" + s.FuelGauge);
            sb.AppendLine("ShowSpeedLimit=" + B(s.ShowSpeedLimit));
            sb.AppendLine();
            sb.AppendLine("[Fuel]");
            sb.AppendLine("Enabled=" + B(s.FuelEnabled));
            sb.AppendLine("; Dollars per liter / долларов за литр");
            sb.AppendLine("PricePerLiter=" + s.FuelPricePerLiter.ToString("0.00", inv));
            sb.AppendLine("; 1.0 = a full tank lasts about 1-1.5 hours of driving / полный бак примерно на 1-1,5 часа езды");
            sb.AppendLine("ConsumptionMultiplier=" + s.FuelConsumption.ToString("0.00", inv));
            sb.AppendLine("NpcRefuelling=" + B(s.NpcRefuelling));
            sb.AppendLine("LowFuelWarningPercent=" + s.LowFuelWarningPercent);
            sb.AppendLine();
            sb.AppendLine("[Traffic]");
            sb.AppendLine("Enabled=" + B(s.TrafficEnabled));
            sb.AppendLine("; Automatic = police stops when an officer sees it, otherwise cameras fine reliable detections (see [Enforcement])");
            sb.AppendLine(";             полиция останавливает при свидетелях, иначе штраф с камеры за надёжно обнаруженные нарушения");
            sb.AppendLine("; Witnessed = fine only when police see it / штраф только при свидетелях-полицейских");
            sb.AppendLine("; Always = every detected violation is fined / штраф за каждое нарушение");
            sb.AppendLine("; Off = record only / только запись в историю");
            sb.AppendLine("FineMode=" + s.Fines);
            sb.AppendLine("SpeedToleranceKmh=" + s.SpeedToleranceKmh);
            sb.AppendLine("FineMultiplier=" + s.FineMultiplier.ToString("0.00", inv));
            sb.AppendLine("DetectSpeeding=" + B(s.DetectSpeeding));
            sb.AppendLine("DetectRedLights=" + B(s.DetectRedLights));
            sb.AppendLine("DetectStopSigns=" + B(s.DetectStopSigns));
            sb.AppendLine("DetectWrongWay=" + B(s.DetectWrongWay));
            sb.AppendLine("DetectHeadlights=" + B(s.DetectHeadlights));
            sb.AppendLine("DetectFailureToYield=" + B(s.DetectFailureToYield));
            sb.AppendLine();
            sb.AppendLine("[Enforcement]");
            sb.AppendLine("; FineMode=Automatic: violations fined without a police witness (traffic cameras / patrol radar)");
            sb.AppendLine("; FineMode=Automatic: какие нарушения штрафуются без полицейского (камеры / радар патруля)");
            sb.AppendLine("AutoSpeeding=" + B(s.Auto.Speeding));
            sb.AppendLine("AutoRedLight=" + B(s.Auto.RedLight));
            sb.AppendLine("AutoWrongWay=" + B(s.Auto.WrongWay));
            sb.AppendLine("AutoFailureToYield=" + B(s.Auto.FailureToYield));
            sb.AppendLine("AutoStopSign=" + B(s.Auto.StopSign));
            sb.AppendLine("AutoHeadlights=" + B(s.Auto.Headlights));
            sb.AppendLine("; Pay camera fines from cash at once (otherwise pay in the Life Hub) / сразу оплачивать штрафы с камер наличными");
            sb.AppendLine("AutoPayFromCash=" + B(s.Auto.AutoPayFromCash));
            sb.AppendLine();
            sb.AppendLine("[FineAmounts]");
            sb.AppendLine("; Dollars before FineMultiplier / долларов до умножения на FineMultiplier");
            FineTable f = s.FineAmounts;
            sb.AppendLine("SpeedingUpTo20=" + f.SpeedingMinor);
            sb.AppendLine("Speeding21To40=" + f.SpeedingMajor);
            sb.AppendLine("SpeedingOver40=" + f.SpeedingExtreme);
            sb.AppendLine("RedLight=" + f.RedLight);
            sb.AppendLine("StopSign=" + f.StopSign);
            sb.AppendLine("WrongWay=" + f.WrongWay);
            sb.AppendLine("NoHeadlights=" + f.NoHeadlights);
            sb.AppendLine("RecklessDriving=" + f.Reckless);
            sb.AppendLine("FailureToYield=" + f.FailureToYield);
            sb.AppendLine("LeavingScene=" + f.LeavingScene);
            sb.AppendLine("HitPedestrian=" + f.HitPedestrian);
            sb.AppendLine("NoDocuments=" + f.NoDocuments);
            sb.AppendLine("UnlicensedDriving=" + f.UnlicensedDriving);
            sb.AppendLine("EvadingPolice=" + f.EvadingPolice);
            sb.AppendLine("FalseStatement=" + f.FalseStatement);
            sb.AppendLine();
            sb.AppendLine("[TrafficLights]");
            sb.AppendLine("; When you are first in line at a red light for MaxRedWaitSeconds, your direction gets a short green");
            sb.AppendLine("; and cross traffic is held. GTA's own signal cycle is not changed. / укороченное ожидание на красный");
            sb.AppendLine("ShorterRedLights=" + B(s.ShorterRedLights));
            sb.AppendLine("MaxRedWaitSeconds=" + s.MaxRedWaitSeconds);
            sb.AppendLine("AssistGreenSeconds=" + s.AssistGreenSeconds);
            sb.AppendLine();
            sb.AppendLine("[SpeedLimiter]");
            sb.AppendLine("; Life Hub > Vehicle controls > Speed limiter. km/h, 10-300 / ограничитель скорости");
            sb.AppendLine("Enabled=" + B(s.SpeedLimiterEnabled));
            sb.AppendLine("LimitKmh=" + s.SpeedLimiterKmh);
            sb.AppendLine("; Automatic: the limiter follows the detected road limit + 10 km/h (50 -> 60, 110 -> 120).");
            sb.AppendLine("; Off = manual limiter above. / Авто: лимит дороги + 10 км/ч; Выкл = ручной ограничитель");
            sb.AppendLine("AutoRoadLimit=" + B(s.AutoSpeedLimit));
            sb.AppendLine();
            sb.AppendLine("[Police]");
            sb.AppendLine("; Police pull you over for witnessed violations / полиция останавливает за замеченные нарушения");
            sb.AppendLine("TrafficStops=" + B(s.PoliceStops));
            sb.AppendLine("NpcTrafficStops=" + B(s.NpcPoliceStops));
            sb.AppendLine("; Extra police patrols: Off | Low | Normal | High / дополнительные патрули");
            sb.AppendLine("PatrolDensity=" + s.PolicePatrols);
            sb.AppendLine("; Patrols check the licence of drivers they follow / патрули проверяют права");
            sb.AppendLine("LicenceChecks=" + B(s.PatrolLicenceChecks));
            sb.AppendLine();
            sb.AppendLine("[Accidents]");
            sb.AppendLine("Enabled=" + B(s.AccidentsEnabled));
            sb.AppendLine("MinImpactSpeedKmh=" + s.AccidentMinSpeedKmh);
            sb.AppendLine("OtherDriversCallEmergency=" + B(s.NpcCallEmergency));
            sb.AppendLine();
            sb.AppendLine("[NpcTraffic]");
            sb.AppendLine("; Driver personalities and speed limits for NPCs / характеры водителей NPC и ограничения скорости");
            sb.AppendLine("Enabled=" + B(s.NpcTraffic));
            sb.AppendLine("Mistakes=" + B(s.NpcMistakes));
            sb.AppendLine("YieldToEmergencyVehicles=" + B(s.NpcYieldEmergency));
            sb.AppendLine("Accidents=" + B(s.NpcAccidents));
            sb.AppendLine();
            sb.AppendLine("[Ownership]");
            sb.AppendLine("; Minutes of play between bills / минут игры между счетами");
            sb.AppendLine("BillingIntervalMinutes=" + s.BillingIntervalMinutes);
            sb.AppendLine("VehicleWear=" + B(s.VehicleWear));
            sb.AppendLine("WearRate=" + s.WearRate.ToString("0.00", inv));
            sb.AppendLine("; In-game km between services / км между ТО");
            sb.AppendLine("ServiceIntervalKm=" + s.ServiceIntervalKm);
            sb.AppendLine("ShowPropertiesOnMap=" + B(s.ShowPropertiesOnMap));
            sb.AppendLine();
            sb.AppendLine("[Passenger]");
            sb.AppendLine("; Ask NPC drivers for a ride (Life Hub > Rides and taxi) / просить водителей NPC подвезти");
            sb.AppendLine("Enabled=" + B(s.PassengerEnabled));
            sb.AppendLine();
            sb.AppendLine("[Taxi]");
            sb.AppendLine("Enabled=" + B(s.TaxiEnabled));
            sb.AppendLine("; Fare = BaseFare + PricePerKm x km driven + PricePerMinuteWaiting x minutes stopped in traffic (dollars)");
            sb.AppendLine("; Тариф = посадка + цена за км x км + цена за минуту x минуты в пробке (доллары)");
            sb.AppendLine("BaseFare=" + s.TaxiBaseFare.ToString("0.00", inv));
            sb.AppendLine("PricePerKm=" + s.TaxiPricePerKm.ToString("0.00", inv));
            sb.AppendLine("PricePerMinuteWaiting=" + s.TaxiPricePerMinute.ToString("0.00", inv));
            sb.AppendLine();
            sb.AppendLine("[Debug]");
            sb.AppendLine("DebugLogging=" + B(s.DebugLogging));
            FileUtil.WriteAllTextAtomic(paths.SettingsFile, sb.ToString());
        }

        private static string B(bool b) { return b ? "true" : "false"; }

        private sealed class SettingsReader
        {
            private readonly IniFile ini;
            public bool Problems;

            public SettingsReader(IniFile ini) { this.ini = ini; }

            private bool Raw(string section, string key, out string v)
            {
                if (ini.TryGet(section, key, out v)) return true;
                Problems = true; // missing entry: added with its default on rewrite
                return false;
            }

            public T Enum<T>(string section, string key, T fallback) where T : struct
            {
                string v;
                if (!Raw(section, key, out v)) return fallback;
                T result;
                if (TryParseEnumName(v, out result)) return result;
                Problems = true;
                return fallback;
            }

            public int Int(string section, string key, int fallback, int min, int max)
            {
                string v;
                if (!Raw(section, key, out v)) return fallback;
                int n;
                if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= min && n <= max) return n;
                Problems = true;
                return fallback;
            }

            public float Float(string section, string key, float fallback, float min, float max)
            {
                string v;
                if (!Raw(section, key, out v)) return fallback;
                float f;
                if (float.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out f)
                    && !float.IsNaN(f) && f >= min && f <= max) return f;
                Problems = true;
                return fallback;
            }

            public bool Bool(string section, string key, bool fallback)
            {
                string v;
                if (!Raw(section, key, out v)) return fallback;
                bool b;
                if (bool.TryParse(v, out b)) return b;
                Problems = true;
                return fallback;
            }
        }

        // ------------------------------------------------------------ key bindings

        public void LoadBindings()
        {
            string file = paths.KeyBindingsFile;
            KeyBindingSet set = new KeyBindingSet();

            if (!File.Exists(file))
            {
                bindings = set;
                SaveBindings();
                Log.Info("Created " + file + " with all actions unassigned.");
                return;
            }

            IniFile ini;
            try
            {
                ini = IniFile.Load(file);
            }
            catch (Exception ex)
            {
                Log.Error("KeyBindings.ini unreadable; all actions left unassigned", ex);
                TryBackup(file, "unreadable");
                bindings = set;
                SaveBindings();
                return;
            }

            bool problems = ini.MalformedLines > 0;
            foreach (RlsAction a in ActionCatalog.All)
            {
                string v;
                if (!ini.TryGet(BindingsSection, a.ToString(), out v))
                {
                    problems = true; // entry missing (e.g. added in a newer version): write it empty
                    continue;
                }
                Keys k;
                string reason;
                if (!KeyRules.TryParseKeyName(v, out k))
                {
                    Log.Warn("KeyBindings.ini: '" + v + "' is not a valid key for " + a + "; left unassigned.");
                    problems = true;
                    continue;
                }
                if (k != Keys.None && !KeyRules.IsAssignable(k, out reason))
                {
                    Log.Warn("KeyBindings.ini: " + k + " cannot be used for " + a + " (" + reason + ")");
                    problems = true;
                    continue;
                }
                set.Set(a, k);
            }

            bindings = set;
            if (problems)
            {
                TryBackup(file, "invalid");
                SaveBindings();
            }
        }

        public void SaveBindings()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; Realistic Life Simulator - keyboard shortcuts.");
            sb.AppendLine("; All actions are unassigned by default. Assign keys in game: Life Hub > Key bindings.");
            sb.AppendLine("; По умолчанию ни одна клавиша не назначена. Назначить: Центр управления > Клавиши.");
            sb.AppendLine("; An empty value means the action has no key. Key names follow System.Windows.Forms.Keys.");
            sb.AppendLine();
            sb.AppendLine("[" + BindingsSection + "]");
            foreach (RlsAction a in ActionCatalog.All)
            {
                Keys k = bindings.Get(a);
                sb.AppendLine(a + "=" + (k == Keys.None ? "" : k.ToString()));
            }
            FileUtil.WriteAllTextAtomic(paths.KeyBindingsFile, sb.ToString());
        }

        // ------------------------------------------------------------ helpers

        private void TryBackup(string file, string tag)
        {
            try
            {
                if (File.Exists(file))
                {
                    string dest = FileUtil.CopyAside(file, tag, paths.ConfigBackups);
                    Log.Info("Kept a copy of the previous file at " + dest);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not back up " + file, ex);
            }
        }

        private static bool TryParseEnumName<T>(string value, out T result) where T : struct
        {
            result = default(T);
            if (string.IsNullOrEmpty(value)) return false;
            foreach (string name in Enum.GetNames(typeof(T)))
            {
                if (string.Equals(name, value.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    result = (T)Enum.Parse(typeof(T), name);
                    return true;
                }
            }
            return false;
        }
    }
}
