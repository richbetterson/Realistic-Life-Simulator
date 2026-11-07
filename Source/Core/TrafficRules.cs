// Realistic Life Simulator - speed limits and fines (no game dependencies).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RealisticLifeSimulator.Core
{
    // Append new values at the end only: names are stored in save files.
    public enum ViolationType { Speeding, RedLight, StopSign, WrongWay, NoHeadlights, Reckless,
        LeavingScene, CausingCollision, NoDocuments, SuspendedLicence, EvadingPolice, FalseStatement,
        FailureToYieldEmergency,
        HitPedestrian, DrivingWhileRevoked }   // 0.6.0

    public enum RoadCategory { Freeway, Highway, Rural, City, Residential, Custom }

    // Base fine amounts in dollars (Settings.ini [FineAmounts]). Multiplied by FineMultiplier.
    public sealed class FineTable
    {
        public int SpeedingMinor = 150;     // up to 20 km/h over
        public int SpeedingMajor = 300;     // 21-40 km/h over
        public int SpeedingExtreme = 600;   // more than 40 km/h over
        public int RedLight = 250;
        public int StopSign = 150;
        public int WrongWay = 400;
        public int NoHeadlights = 80;
        public int Reckless = 1000;
        public int FailureToYield = 350;
        public int LeavingScene = 750;
        public int HitPedestrian = 800;
        public int NoDocuments = 200;
        public int UnlicensedDriving = 1000; // suspended or revoked licence
        public int EvadingPolice = 1500;
        public int FalseStatement = 250;

        public static readonly int[] Choices = { 0, 50, 80, 100, 150, 200, 250, 300, 400, 500, 600, 750, 800, 1000, 1500, 2000, 3000, 5000 };

        public FineTable Clone() { return (FineTable)MemberwiseClone(); }
    }

    public static class TrafficRules
    {
        private static FineTable fines = new FineTable();

        // The table used by BaseFine. Set from the settings when they load or change.
        public static FineTable Fines
        {
            get { return fines; }
            set { fines = value ?? new FineTable(); }
        }

        // Vehicle node property flags returned by GET_VEHICLE_NODE_PROPERTIES.
        public const int NodeOffRoad = 1;
        public const int NodeSwitchedOff = 8;
        public const int NodeTunnel = 16;
        public const int NodeHighway = 64;
        public const int NodeJunction = 128;
        public const int NodeTrafficLight = 256;
        public const int NodeGiveWay = 512;

        public static double BaseFine(ViolationType t, int overKmh)
        {
            FineTable f = fines;
            switch (t)
            {
                case ViolationType.Speeding:
                    if (overKmh > 40) return f.SpeedingExtreme;
                    if (overKmh > 20) return f.SpeedingMajor;
                    return f.SpeedingMinor;
                case ViolationType.RedLight: return f.RedLight;
                case ViolationType.StopSign: return f.StopSign;
                case ViolationType.WrongWay: return f.WrongWay;
                case ViolationType.NoHeadlights: return f.NoHeadlights;
                case ViolationType.Reckless: return f.Reckless;
                case ViolationType.LeavingScene: return f.LeavingScene;
                case ViolationType.CausingCollision: return 600;   // accidents use PoliceRules.CollisionFine
                case ViolationType.NoDocuments: return f.NoDocuments;
                case ViolationType.SuspendedLicence: return f.UnlicensedDriving;
                case ViolationType.DrivingWhileRevoked: return f.UnlicensedDriving;
                case ViolationType.EvadingPolice: return f.EvadingPolice;
                case ViolationType.FalseStatement: return f.FalseStatement;
                case ViolationType.FailureToYieldEmergency: return f.FailureToYield;
                case ViolationType.HitPedestrian: return f.HitPedestrian;
            }
            return 100;
        }

        public static string Name(ViolationType t)
        {
            switch (t)
            {
                case ViolationType.Speeding: return L.S("Speeding", "Превышение скорости");
                case ViolationType.RedLight: return L.S("Running a red light", "Проезд на красный");
                case ViolationType.StopSign: return L.S("Failure to stop / yield", "Проезд без остановки / не уступил");
                case ViolationType.WrongWay: return L.S("Driving the wrong way", "Езда по встречной");
                case ViolationType.NoHeadlights: return L.S("No headlights at night", "Без фар ночью");
                case ViolationType.Reckless: return L.S("Reckless driving", "Опасное вождение");
                case ViolationType.LeavingScene: return L.S("Leaving the scene of an accident", "Оставление места ДТП");
                case ViolationType.CausingCollision: return L.S("Causing a collision", "Виновник ДТП");
                case ViolationType.NoDocuments: return L.S("Failure to present documents", "Отказ предъявить документы");
                case ViolationType.SuspendedLicence: return L.S("Driving with a suspended licence", "Езда с приостановленными правами");
                case ViolationType.EvadingPolice: return L.S("Evading the police", "Уклонение от остановки полицией");
                case ViolationType.FalseStatement: return L.S("False statement to police", "Ложные показания полиции");
                case ViolationType.FailureToYieldEmergency: return L.S("Failure to yield to an emergency vehicle", "Не уступил дорогу спецтранспорту");
                case ViolationType.HitPedestrian: return L.S("Striking a pedestrian", "Наезд на пешехода");
                case ViolationType.DrivingWhileRevoked: return L.S("Driving with a revoked licence", "Езда без права управления (права отозваны)");
            }
            return t.ToString();
        }

        public static string NameFromString(string type)
        {
            ViolationType t;
            try { t = (ViolationType)Enum.Parse(typeof(ViolationType), type); }
            catch (Exception) { return type; }
            return Name(t);
        }

        public static string CategoryName(RoadCategory c)
        {
            switch (c)
            {
                case RoadCategory.Freeway: return L.S("freeway", "автомагистраль");
                case RoadCategory.Highway: return L.S("highway", "трасса");
                case RoadCategory.Rural: return L.S("rural road", "загородная дорога");
                case RoadCategory.Residential: return L.S("residential", "жилая зона");
                case RoadCategory.Custom: return L.S("signed limit", "знак ограничения");
            }
            return L.S("city", "город");
        }
    }

    // Speed limits by road type. Editable in Data\SpeedLimits.ini.
    public sealed class SpeedLimitTable
    {
        public int Freeway = 110, Highway = 90, Rural = 80, City = 50, Residential = 40;

        private readonly HashSet<string> residentialZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> ruralZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> streetOverrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> zoneOverrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private const string DefaultResidential =
            "ROCKF,RICHM,CHIL,MIRR,WVINE,VCANA,CHAMH,DAVIS,RANCHO,RGLEN,PBLUFF,BANHAMC,BHAMCA,TONGVAH";
        private const string DefaultRural =
            "ALAMO,ARMYB,BRADP,BRADT,CALAFB,CANNY,CCREAK,CHU,CMSW,DESRT,ELGORL,GALFISH,GREATC,HUMLAB,JAIL,LACT,LAGO," +
            "LDAM,MTCHIL,MTGORDO,MTJOSE,NCHU,PALCOV,PALFOR,PALHIGH,PROCOB,RTRAK,SANAND,SANCHIA,SLAB,TATAMO,TONGVAV," +
            "WINDF,ZANCUDO,ZQ_UAR,OCEANA";

        public static SpeedLimitTable CreateDefault()
        {
            SpeedLimitTable t = new SpeedLimitTable();
            AddList(t.residentialZones, DefaultResidential);
            AddList(t.ruralZones, DefaultRural);
            return t;
        }

        private static void AddList(HashSet<string> set, string csv)
        {
            foreach (string part in (csv ?? "").Split(','))
            {
                string p = part.Trim();
                if (p.Length > 0) set.Add(p);
            }
        }

        public int Classify(string street, string zone, int nodeFlags, out RoadCategory category)
        {
            street = street ?? "";
            zone = zone ?? "";
            int v;
            if (street.Length > 0 && streetOverrides.TryGetValue(street, out v)) { category = RoadCategory.Custom; return v; }

            string s = " " + street.ToLowerInvariant() + " ";
            if ((nodeFlags & TrafficRules.NodeHighway) != 0 || s.Contains(" fwy ") || s.Contains("freeway"))
            {
                category = RoadCategory.Freeway;
                return Freeway;
            }
            if (zone.Length > 0 && zoneOverrides.TryGetValue(zone, out v)) { category = RoadCategory.Custom; return v; }
            if (s.Contains(" hwy ") || s.Contains("highway") || s.Contains(" route ") || s.Contains(" rte "))
            {
                category = RoadCategory.Highway;
                return Highway;
            }
            if (residentialZones.Contains(zone)) { category = RoadCategory.Residential; return Residential; }
            if (ruralZones.Contains(zone)) { category = RoadCategory.Rural; return Rural; }
            category = RoadCategory.City;
            return City;
        }

        public static SpeedLimitTable LoadOrCreate(string file)
        {
            if (!File.Exists(file))
            {
                SpeedLimitTable d = CreateDefault();
                try { FileUtil.WriteAllTextAtomic(file, d.ToIni()); }
                catch (Exception ex) { Log.Error("Could not write " + file, ex); }
                return d;
            }

            SpeedLimitTable t = new SpeedLimitTable();
            try
            {
                IniFile ini = IniFile.Load(file);
                t.Freeway = Int(ini, "Limits", "Freeway", 110);
                t.Highway = Int(ini, "Limits", "Highway", 90);
                t.Rural = Int(ini, "Limits", "Rural", 80);
                t.City = Int(ini, "Limits", "City", 50);
                t.Residential = Int(ini, "Limits", "Residential", 40);
                AddList(t.residentialZones, ini.Get("ResidentialZones", "Codes", DefaultResidential));
                AddList(t.ruralZones, ini.Get("RuralZones", "Codes", DefaultRural));
                foreach (KeyValuePair<string, string> kv in ini.GetSection("Streets"))
                {
                    int n;
                    if (int.TryParse(kv.Value, out n) && n >= 10 && n <= 200) t.streetOverrides[kv.Key] = n;
                }
                foreach (KeyValuePair<string, string> kv in ini.GetSection("Zones"))
                {
                    int n;
                    if (int.TryParse(kv.Value, out n) && n >= 10 && n <= 200) t.zoneOverrides[kv.Key] = n;
                }
            }
            catch (Exception ex)
            {
                Log.Error("SpeedLimits.ini unreadable, using defaults", ex);
                return CreateDefault();
            }
            return t;
        }

        private static int Int(IniFile ini, string section, string key, int fallback)
        {
            int n;
            return int.TryParse(ini.Get(section, key, ""), out n) && n >= 10 && n <= 200 ? n : fallback;
        }

        public string ToIni()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; Realistic Life Simulator - speed limits in km/h / ограничения скорости в км/ч.");
            sb.AppendLine("; Freeways are detected from the road network and street names (\"Fwy\").");
            sb.AppendLine();
            sb.AppendLine("[Limits]");
            sb.AppendLine("Freeway=" + Freeway);
            sb.AppendLine("Highway=" + Highway);
            sb.AppendLine("Rural=" + Rural);
            sb.AppendLine("City=" + City);
            sb.AppendLine("Residential=" + Residential);
            sb.AppendLine();
            sb.AppendLine("; GTA zone codes (GET_NAME_OF_ZONE). / Коды зон GTA.");
            sb.AppendLine("[ResidentialZones]");
            sb.AppendLine("Codes=" + DefaultResidential);
            sb.AppendLine();
            sb.AppendLine("[RuralZones]");
            sb.AppendLine("Codes=" + DefaultRural);
            sb.AppendLine();
            sb.AppendLine("; Exact street name = limit, e.g. Vinewood Blvd=40 / точное название улицы = лимит");
            sb.AppendLine("[Streets]");
            sb.AppendLine();
            sb.AppendLine("; Zone code = limit, e.g. AIRP=30 / код зоны = лимит");
            sb.AppendLine("[Zones]");
            return sb.ToString();
        }
    }
}
