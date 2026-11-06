// Realistic Life Simulator - persistent save data.
//   Saves\profile.xml                 current save
//   Saves\Backups\profile.bak1..5     rolling backups (bak1 = newest)
//   Saves\Backups\daily-yyyyMMdd.xml  one snapshot per day, last 7 kept
// A save file is never deleted when it cannot be read: it is moved aside instead.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace RealisticLifeSimulator.Core
{
    [XmlRoot("RealisticLifeSimulatorSave")]
    public class SaveData
    {
        // 1 = Phase 1. 2 = Phase 2 (fuel, transactions, violations). 3 = Phase 3 (licences, accidents).
        // 4 = Phase 4 (city incidents). 5 = Phase 5 (properties, owned vehicles, bills).
        // 6 = passenger rides and taxi statistics.
        public const int CurrentSchema = 6;
        public const int MaxIncidents = 50;
        public const int MaxAccidents = 100;
        public const int MaxTransactions = 500;
        public const int MaxViolations = 300;
        public const int MaxVehicleRecords = 60;

        public int SchemaVersion;
        public string ModVersion;
        public string CreatedUtc;
        public string LastSavedUtc;
        public int SaveCount;
        public bool FirstRunNoticeShown;
        public bool Phase2NoticeShown;
        public PlayerStats Stats;
        public DrivingStats Driving;
        public List<VehicleFuelRecord> Vehicles;
        public List<Transaction> Transactions;
        public List<ViolationRecord> Violations;
        public List<DriverProfile> Profiles;
        public List<AccidentReport> Accidents;
        public bool Phase3NoticeShown;
        public bool Phase4NoticeShown;
        public bool Phase5NoticeShown;
        public List<OwnedProperty> Properties;
        public List<OwnedVehicle> OwnedVehicles;
        public double Debt;                    // unpaid bills (settled when cash allows; bank in Phase 6)
        public double LastBillingPlaySeconds;
        public List<CityIncident> Incidents;
        public PassengerStats Passenger;
        public bool PassengerNoticeShown;

        public SaveData()
        {
            Passenger = new PassengerStats();
            Profiles = new List<DriverProfile>();
            Incidents = new List<CityIncident>();
            Properties = new List<OwnedProperty>();
            OwnedVehicles = new List<OwnedVehicle>();
            Accidents = new List<AccidentReport>();
            Stats = new PlayerStats();
            Driving = new DrivingStats();
            Vehicles = new List<VehicleFuelRecord>();
            Transactions = new List<Transaction>();
            Violations = new List<ViolationRecord>();
        }

        public void AddTransaction(string category, string description, double amount)
        {
            Transaction t = new Transaction();
            t.TimeUtc = DateTime.UtcNow.ToString("o");
            t.Category = category;
            t.Description = description;
            t.Amount = Math.Round(amount, 2);
            Transactions.Add(t);
            if (Transactions.Count > MaxTransactions) Transactions.RemoveRange(0, Transactions.Count - MaxTransactions);
        }

        public double OutstandingFines()
        {
            double sum = 0;
            foreach (ViolationRecord v in Violations) if (!v.Paid && v.Fine > 0) sum += v.Fine;
            return sum;
        }

        public DriverProfile Profile(string character, Random rng)
        {
            foreach (DriverProfile p in Profiles) if (p.Character == character) return p;
            DriverProfile n = DriverProfile.Create(character, rng);
            Profiles.Add(n);
            return n;
        }

        public void AddIncident(string kind, string location, string description, string outcome)
        {
            CityIncident i = new CityIncident();
            i.TimeUtc = DateTime.UtcNow.ToString("o");
            i.Kind = kind;
            i.Location = location ?? "";
            i.Description = description ?? "";
            i.Outcome = outcome ?? "";
            Incidents.Add(i);
            if (Incidents.Count > MaxIncidents) Incidents.RemoveAt(0);
        }

        public void AddAccident(AccidentReport r)
        {
            Accidents.Add(r);
            if (Accidents.Count > MaxAccidents) Accidents.RemoveAt(0);
        }

        public VehicleFuelRecord FindVehicle(string plate, int modelHash)
        {
            foreach (VehicleFuelRecord r in Vehicles)
                if (r.ModelHash == modelHash && string.Equals(r.Plate, plate, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        public void TrimVehicles()
        {
            if (Vehicles.Count <= MaxVehicleRecords) return;
            Vehicles.Sort((a, b) => string.CompareOrdinal(b.LastUsedUtc ?? "", a.LastUsedUtc ?? ""));
            Vehicles.RemoveRange(MaxVehicleRecords, Vehicles.Count - MaxVehicleRecords);
        }

        public static SaveData CreateNew(string modVersion)
        {
            SaveData d = new SaveData();
            d.SchemaVersion = CurrentSchema;
            d.ModVersion = modVersion;
            d.CreatedUtc = DateTime.UtcNow.ToString("o");
            d.LastSavedUtc = "";
            return d;
        }

        // Repairs small problems in place. Returns false if the data cannot be trusted.
        public bool Validate(List<string> issues)
        {
            if (SchemaVersion < 1) { issues.Add("missing schema version"); return false; }
            if (SchemaVersion > CurrentSchema) { issues.Add("made by a newer version (schema " + SchemaVersion + ")"); return false; }
            if (Stats == null) { Stats = new PlayerStats(); issues.Add("stats missing (reset)"); }
            if (double.IsNaN(Stats.TotalPlayTimeSeconds) || Stats.TotalPlayTimeSeconds < 0) { Stats.TotalPlayTimeSeconds = 0; issues.Add("invalid play time"); }
            if (Stats.SessionsStarted < 0) { Stats.SessionsStarted = 0; issues.Add("invalid session count"); }
            if (SaveCount < 0) SaveCount = 0;
            if (CreatedUtc == null) CreatedUtc = "";
            if (LastSavedUtc == null) LastSavedUtc = "";
            if (ModVersion == null) ModVersion = "";

            // Schema 1 saves have no Phase 2 data: start those sections empty (migration).
            if (Driving == null) Driving = new DrivingStats();
            if (Vehicles == null) Vehicles = new List<VehicleFuelRecord>();
            if (Transactions == null) Transactions = new List<Transaction>();
            if (Violations == null) Violations = new List<ViolationRecord>();
            if (Profiles == null) Profiles = new List<DriverProfile>();
            if (Accidents == null) Accidents = new List<AccidentReport>();
            if (Incidents == null) Incidents = new List<CityIncident>();
            if (Properties == null) Properties = new List<OwnedProperty>();
            if (OwnedVehicles == null) OwnedVehicles = new List<OwnedVehicle>();
            Properties.RemoveAll(p => p == null || string.IsNullOrEmpty(p.Id));
            OwnedVehicles.RemoveAll(v => v == null || string.IsNullOrEmpty(v.Id));
            foreach (OwnedVehicle v in OwnedVehicles)
            {
                if (v.Services == null) v.Services = new List<ServiceEntry>();
                if (v.Damage == null) v.Damage = new List<DamageEntry>();
                if (v.Look == null) v.Look = new VehicleLook();
                if (double.IsNaN(v.Condition) || v.Condition < 0 || v.Condition > 100) v.Condition = Math.Max(0, Math.Min(100, double.IsNaN(v.Condition) ? 80 : v.Condition));
                if (v.MileageKm < 0 || double.IsNaN(v.MileageKm)) v.MileageKm = 0;
            }
            if (Passenger == null) Passenger = new PassengerStats(); // schema 5 -> 6
            if (Passenger.Rides < 0) Passenger.Rides = 0;
            if (Passenger.Refusals < 0) Passenger.Refusals = 0;
            if (Passenger.TaxiRides < 0) Passenger.TaxiRides = 0;
            if (double.IsNaN(Passenger.DistanceKm) || Passenger.DistanceKm < 0) Passenger.DistanceKm = 0;
            if (double.IsNaN(Passenger.TaxiSpent) || Passenger.TaxiSpent < 0) Passenger.TaxiSpent = 0;
            if (double.IsNaN(Debt) || Debt < 0) Debt = 0;
            if (double.IsNaN(LastBillingPlaySeconds) || LastBillingPlaySeconds < 0) LastBillingPlaySeconds = 0;
            Incidents.RemoveAll(i => i == null);
            if (Incidents.Count > MaxIncidents) Incidents.RemoveRange(0, Incidents.Count - MaxIncidents);
            Profiles.RemoveAll(p => p == null || string.IsNullOrEmpty(p.Character));
            foreach (DriverProfile p in Profiles)
            {
                if (p.Points == null) p.Points = new List<PointEntry>();
                if (double.IsNaN(p.Reputation)) p.Reputation = PoliceRules.StartingReputation;
                p.Reputation = Math.Max(0, Math.Min(100, p.Reputation));
            }
            Accidents.RemoveAll(a => a == null);
            if (Accidents.Count > MaxAccidents) Accidents.RemoveRange(0, Accidents.Count - MaxAccidents);

            Vehicles.RemoveAll(v => v == null || v.Plate == null || double.IsNaN(v.Liters));
            foreach (VehicleFuelRecord v in Vehicles) if (v.Liters < 0) v.Liters = 0;
            Transactions.RemoveAll(t => t == null || double.IsNaN(t.Amount));
            Violations.RemoveAll(v => v == null || double.IsNaN(v.Fine));
            if (Driving.DistanceKm < 0 || double.IsNaN(Driving.DistanceKm)) Driving.DistanceKm = 0;
            if (Transactions.Count > MaxTransactions) Transactions.RemoveRange(0, Transactions.Count - MaxTransactions);
            if (Violations.Count > MaxViolations) Violations.RemoveRange(0, Violations.Count - MaxViolations);
            return true;
        }
    }

    public class PlayerStats
    {
        public double TotalPlayTimeSeconds;
        public int SessionsStarted;
    }

    public class DrivingStats
    {
        public double DistanceKm;
        public double FuelLitersBought;
        public double FuelMoneySpent;
        public int Violations;
        public int FinesIssued;
        public double FinesPaid;
        public int NpcRefuelsSeen;
        public int TrafficStops;
        public int Warnings;
        public int Accidents;
        public int NpcStopsSeen;
        public int NpcAccidentsSeen;
    }

    public class VehicleFuelRecord
    {
        public string Plate;
        public int ModelHash;
        public double Liters;
        public string LastUsedUtc;
    }

    public class Transaction
    {
        public string TimeUtc;
        public string Category;     // "Fuel", "Fine", ...
        public string Description;
        public double Amount;       // negative = expense
    }

    public class ViolationRecord
    {
        public string TimeUtc;
        public string Type;         // ViolationType name
        public string Details;
        public string Location;
        public double Fine;         // 0 = recorded only
        public bool Witnessed;
        public bool Paid;
        public string Character;    // Phase 3+
        public int Points;
        public bool Warning;        // let off with a warning at a traffic stop
    }

    public sealed class SaveManager
    {
        public const int BackupCount = 5;
        public const int DailyKeep = 7;

        private readonly RlsPaths paths;
        private readonly string modVersion;
        private SaveData data;
        private bool readOnly;
        private string status = "Not loaded";
        private DateTime lastSaveLocal = DateTime.MinValue;
        private string lastError = "";

        public SaveManager(RlsPaths paths, string modVersion)
        {
            this.paths = paths;
            this.modVersion = modVersion;
            data = SaveData.CreateNew(modVersion);
        }

        public SaveData Data { get { return data; } }
        public bool ReadOnly { get { return readOnly; } }
        public string Status { get { return status; } } // English, for logs

        public string StatusText
        {
            get
            {
                if (status == "Loaded") return L.S("Loaded", "Загружено");
                if (status == "New profile") return L.S("New profile", "Новый профиль");
                if (status.StartsWith("Restored from backup"))
                    return L.S("Restored from backup ", "Восстановлено из копии ") + status.Substring("Restored from backup ".Length);
                if (status.StartsWith("Save is from a newer"))
                    return L.S("Newer version save - read only", "Сохранение новой версии - только чтение");
                if (status.StartsWith("Save damaged"))
                    return L.S("Damaged - started fresh (copy kept)", "Повреждено - начато заново (копия сохранена)");
                return status;
            }
        }
        public DateTime LastSaveLocal { get { return lastSaveLocal; } }
        public string LastError { get { return lastError; } }

        private string BackupPath(int n)
        {
            return Path.Combine(paths.SaveBackups, "profile.bak" + n + ".xml");
        }

        public void Load()
        {
            string main = paths.SaveFile;
            List<string> candidates = new List<string>();
            candidates.Add(main);
            for (int i = 1; i <= BackupCount; i++) candidates.Add(BackupPath(i));

            bool anyExisted = false;
            foreach (string file in candidates)
            {
                if (!File.Exists(file)) continue;
                anyExisted = true;

                SaveData loaded;
                List<string> issues = new List<string>();
                try
                {
                    loaded = Deserialize(File.ReadAllBytes(file));
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not read " + file + ": " + ex.Message);
                    continue;
                }

                if (loaded.SchemaVersion > SaveData.CurrentSchema)
                {
                    // Never overwrite progress made by a newer version of the mod.
                    readOnly = true;
                    data = SaveData.CreateNew(modVersion);
                    status = "Save is from a newer version - saving disabled to protect it";
                    Log.Warn(status + " (" + file + ")");
                    return;
                }

                if (!loaded.Validate(issues))
                {
                    Log.Warn("Rejected " + file + ": " + string.Join(", ", issues.ToArray()));
                    continue;
                }
                if (issues.Count > 0) Log.Warn("Repaired " + file + ": " + string.Join(", ", issues.ToArray()));

                data = loaded;
                if (file == main)
                {
                    status = "Loaded";
                }
                else
                {
                    if (File.Exists(main))
                    {
                        string aside = FileUtil.MoveAside(main, "unreadable", paths.SaveBackups);
                        Log.Warn("Main save unreadable; moved to " + aside);
                    }
                    status = "Restored from backup " + Path.GetFileName(file);
                    Log.Warn(status);
                    Save(); // re-create profile.xml from the recovered data
                }
                Log.Info("Save loaded: " + status);
                return;
            }

            data = SaveData.CreateNew(modVersion);
            if (anyExisted)
            {
                if (File.Exists(main))
                {
                    string aside = FileUtil.MoveAside(main, "unreadable", paths.SaveBackups);
                    Log.Error("No readable save or backup; damaged save kept at " + aside);
                }
                status = "Save damaged - started fresh (old file kept in Saves\\Backups)";
            }
            else
            {
                status = "New profile";
            }
            Log.Info("Save: " + status);
        }

        public bool Save()
        {
            if (readOnly)
            {
                lastError = "Saving disabled (save belongs to a newer version)";
                return false;
            }
            try
            {
                data.SchemaVersion = SaveData.CurrentSchema;
                data.ModVersion = modVersion;
                data.SaveCount++;
                data.LastSavedUtc = DateTime.UtcNow.ToString("o");

                byte[] bytes = Serialize(data);

                // Verify the bytes before touching anything on disk.
                SaveData check = Deserialize(bytes);
                List<string> issues = new List<string>();
                if (!check.Validate(issues)) throw new InvalidDataException("verification failed: " + string.Join(", ", issues.ToArray()));

                Directory.CreateDirectory(paths.SaveBackups);
                string main = paths.SaveFile;
                if (File.Exists(main))
                {
                    RotateBackups(main);
                    WriteDailySnapshot(main);
                }
                FileUtil.WriteAllBytesAtomic(main, bytes);

                lastSaveLocal = DateTime.Now;
                lastError = "";
                Log.Debug("Saved (#" + data.SaveCount + ")");
                return true;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                Log.Error("Save failed", ex);
                return false;
            }
        }

        private void RotateBackups(string main)
        {
            string oldest = BackupPath(BackupCount);
            if (File.Exists(oldest)) File.Delete(oldest);
            for (int i = BackupCount - 1; i >= 1; i--)
            {
                string src = BackupPath(i);
                if (File.Exists(src)) File.Move(src, BackupPath(i + 1));
            }
            File.Copy(main, BackupPath(1), true);
        }

        private void WriteDailySnapshot(string main)
        {
            string today = Path.Combine(paths.SaveBackups, "daily-" + DateTime.Now.ToString("yyyyMMdd") + ".xml");
            if (!File.Exists(today)) File.Copy(main, today);

            string[] dailies = Directory.GetFiles(paths.SaveBackups, "daily-*.xml");
            if (dailies.Length <= DailyKeep) return;
            Array.Sort(dailies, StringComparer.Ordinal);
            for (int i = 0; i < dailies.Length - DailyKeep; i++) File.Delete(dailies[i]);
        }

        public static byte[] Serialize(SaveData d)
        {
            XmlSerializer ser = new XmlSerializer(typeof(SaveData));
            XmlWriterSettings xs = new XmlWriterSettings();
            xs.Encoding = new UTF8Encoding(false);
            xs.Indent = true;
            using (MemoryStream ms = new MemoryStream())
            {
                using (XmlWriter w = XmlWriter.Create(ms, xs)) ser.Serialize(w, d);
                return ms.ToArray();
            }
        }

        public static SaveData Deserialize(byte[] bytes)
        {
            XmlSerializer ser = new XmlSerializer(typeof(SaveData));
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                SaveData d = ser.Deserialize(ms) as SaveData;
                if (d == null) throw new InvalidDataException("empty save");
                return d;
            }
        }
    }
}
