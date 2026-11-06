// Realistic Life Simulator - all file locations in one place.

using System.IO;

namespace RealisticLifeSimulator.Core
{
    public sealed class RlsPaths
    {
        public readonly string GameRoot;
        public readonly string Root;
        public readonly string Config;
        public readonly string Data;
        public readonly string Saves;
        public readonly string SaveBackups;
        public readonly string Logs;
        public readonly string Backups;
        public readonly string ConfigBackups;

        public RlsPaths(string gameRoot)
        {
            GameRoot = gameRoot;
            Root = Path.Combine(gameRoot, "RealisticLifeSimulator");
            Config = Path.Combine(Root, "Config");
            Data = Path.Combine(Root, "Data");
            Saves = Path.Combine(Root, "Saves");
            SaveBackups = Path.Combine(Saves, "Backups");
            Logs = Path.Combine(Root, "Logs");
            Backups = Path.Combine(Root, "Backups");
            ConfigBackups = Path.Combine(Backups, "Config");
        }

        public string SettingsFile { get { return Path.Combine(Config, "Settings.ini"); } }
        public string KeyBindingsFile { get { return Path.Combine(Config, "KeyBindings.ini"); } }
        public string LifeHubsFile { get { return Path.Combine(Data, "LifeHubs.ini"); } }
        public string GasStationsFile { get { return Path.Combine(Data, "GasStations.ini"); } }
        public string SpeedLimitsFile { get { return Path.Combine(Data, "SpeedLimits.ini"); } }
        public string PropertiesFile { get { return Path.Combine(Data, "Properties.ini"); } }
        public string CatalogFile { get { return Path.Combine(Data, "VehicleCatalog.ini"); } }
        public string ShopsFile { get { return Path.Combine(Data, "Shops.ini"); } }
        public string SaveFile { get { return Path.Combine(Saves, "profile.xml"); } }
        public string LogFile { get { return Path.Combine(Logs, "RealisticLifeSimulator.log"); } }

        public void EnsureDirectories()
        {
            string[] dirs = { Root, Config, Data, Saves, SaveBackups, Logs, Backups, ConfigBackups };
            foreach (string d in dirs) Directory.CreateDirectory(d);
        }
    }
}
