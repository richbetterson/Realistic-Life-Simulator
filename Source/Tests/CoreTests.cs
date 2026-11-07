// Realistic Life Simulator - offline tests for the game-independent core.
// Built and run by build.ps1. Uses a throw-away folder under %TEMP%; never touches real data.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using RealisticLifeSimulator.Core;

public static partial class CoreTests
{
    private static int passed, failed;

    public static int Main()
    {
        Run("Fresh install: every key binding is empty", FreshBindingsEmpty);
        Run("Valid key binding is kept", ValidBindingKept);
        Run("Invalid binding becomes unassigned, original kept as backup", InvalidBindingRepaired);
        Run("Modifier / Escape keys are rejected", RejectsReservedKeys);
        Run("Numbers and booleans are not parsed as keys", KeyNameParsingStrict);
        Run("Damaged Settings.ini restored to defaults with backup", DamagedSettings);
        Run("Settings round trip", SettingsRoundTrip);
        Run("Save round trip and backups rotate", SaveRoundTrip);
        Run("Corrupt save recovered from backup, not deleted", CorruptSaveRecovered);
        Run("Save from newer version is never overwritten", NewerSaveProtected);
        Run("Conflict checker sees GTA, other mods and own keys", ConflictChecker);
        Run("Phase 2 settings round trip and old Settings.ini upgraded", Phase2Settings);
        Run("Schema 1 save migrates to schema 2 without losing data", SaveMigration);
        Run("Fuel: tank range, idle, electric and unsupported vehicles", FuelModelTest);
        Run("Joaat model hash matches GTA", JoaatTest);
        Run("Speed limits by road type, zone and overrides", SpeedLimitTest);
        Run("Fines by violation and outstanding total", FinesTest);
        Run("Russian text survives compilation (UTF-8)", RussianText);
        Run("Licence points, expiry and suspension", LicencePoints);
        Run("Warning chance follows reputation, attitude and severity", WarningChanceTest);
        Run("Accident severity thresholds", SeverityTest);
        Run("Fault from evidence and false statements", FaultTest);
        Run("Collision fines and reputation loss", CollisionFineTest);
        Run("Schema 2 save migrates to schema 3 (profiles, accidents)", SaveMigration3);
        Run("NPC personalities: distribution, traits, speeds", NpcPersonalities);
        Run("NPC mistakes follow personality rates", NpcMistakes);
        Run("Schema 3 save migrates to schema 4 (city incidents)", SaveMigration4);
        Run("Vehicle wear, service ceiling and engine penalties", WearTest);
        Run("Insurance split, premiums and payouts", InsuranceTest);
        Run("Vehicle value and billing cycle", ValueBillingTest);
        Run("Default Properties.ini and catalogue parse", DataFilesTest);
        Run("Schema 4 save migrates to schema 5 with owned vehicles", SaveMigration5);
        Run("Passenger: new key actions unassigned, existing key kept on upgrade", PassengerKeysUnassigned);
        Run("Passenger: settings round trip, old Settings.ini upgraded keeping values", PassengerSettings);
        Run("Passenger: schema 5 save migrates to schema 6 with ride stats", SaveMigration6);
        Run("Passenger: attitude is deterministic and follows driving personality", AttitudeTest);
        Run("Passenger: acceptance depends on situation, not chance", AcceptanceTest);
        Run("Passenger: stop requests, reactions and detours by attitude", RideReactionsTest);
        Run("Passenger: seats - driver seat is never a passenger seat", SeatTest);
        Run("Taxi: fare, rounding and cash/debt settlement", TaxiFareTest);
        RunV060();
        RunV110();

        Console.WriteLine();
        Console.WriteLine("Passed: " + passed + "  Failed: " + failed);
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        string dir = Path.Combine(Path.GetTempPath(), "RlsTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        currentRoot = dir;
        try
        {
            test();
            passed++;
            Console.WriteLine("PASS  " + name);
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine("FAIL  " + name + " :: " + ex.Message);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }

    private static string currentRoot;

    private static RlsPaths NewPaths()
    {
        RlsPaths p = new RlsPaths(currentRoot);
        p.EnsureDirectories();
        return p;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    // ------------------------------------------------------------ tests

    private static void FreshBindingsEmpty()
    {
        RlsPaths p = NewPaths();
        ConfigurationManager c = new ConfigurationManager(p);
        c.LoadAll();
        Check(c.Bindings.AssignedCount == 0, "expected 0 assigned");
        string[] lines = File.ReadAllLines(p.KeyBindingsFile);
        foreach (RlsAction a in ActionCatalog.All)
            Check(Array.IndexOf(lines, a + "=") >= 0, "missing empty line for " + a);
        foreach (string expected in new[] { "CallPolice=", "OpenBank=", "OpenPropertyMenu=", "OpenVehicleMenu=", "RequestRoadsideAssistance=", "StartJob=", "OpenSettings=" })
            Check(Array.IndexOf(lines, expected) >= 0, "spec entry missing: " + expected);
        // Loading again must not assign anything either.
        c.LoadAll();
        Check(c.Bindings.AssignedCount == 0, "second load assigned keys");
    }

    private static void ValidBindingKept()
    {
        RlsPaths p = NewPaths();
        ConfigurationManager c = new ConfigurationManager(p);
        c.LoadAll();
        c.Bindings.Set(RlsAction.OpenBank, Keys.F7);
        c.SaveBindings();
        ConfigurationManager c2 = new ConfigurationManager(p);
        c2.LoadAll();
        Check(c2.Bindings.Get(RlsAction.OpenBank) == Keys.F7, "F7 not kept");
        Check(c2.Bindings.AssignedCount == 1, "unexpected extra bindings");
    }

    private static void InvalidBindingRepaired()
    {
        RlsPaths p = NewPaths();
        File.WriteAllText(p.KeyBindingsFile, "[KeyBindings]\nOpenBank=Banana\nCallPolice=F9\n");
        ConfigurationManager c = new ConfigurationManager(p);
        c.LoadAll();
        Check(c.Bindings.Get(RlsAction.OpenBank) == Keys.None, "invalid value was assigned");
        Check(c.Bindings.Get(RlsAction.CallPolice) == Keys.F9, "valid value lost");
        Check(Directory.GetFiles(p.ConfigBackups, "KeyBindings.ini.invalid-*").Length == 1, "no backup of original");
        string text = File.ReadAllText(p.KeyBindingsFile);
        Check(text.Contains("OpenBank=\r\n") || text.Contains("OpenBank=\n"), "OpenBank not rewritten empty");
        Check(text.Contains("StartJob="), "missing entries not added");
    }

    private static void RejectsReservedKeys()
    {
        string reason;
        Check(!KeyRules.IsAssignable(Keys.ShiftKey, out reason), "Shift accepted");
        Check(!KeyRules.IsAssignable(Keys.Escape, out reason), "Escape accepted");
        Check(!KeyRules.IsAssignable(Keys.LButton, out reason), "mouse accepted");
        Check(KeyRules.IsAssignable(Keys.F7, out reason), "F7 rejected");

        RlsPaths p = NewPaths();
        File.WriteAllText(p.KeyBindingsFile, "[KeyBindings]\nOpenBank=Escape\n");
        ConfigurationManager c = new ConfigurationManager(p);
        c.LoadAll();
        Check(c.Bindings.Get(RlsAction.OpenBank) == Keys.None, "Escape loaded from file");
    }

    private static void KeyNameParsingStrict()
    {
        Keys k;
        Check(!KeyRules.TryParseKeyName("1", out k), "'1' parsed");
        Check(!KeyRules.TryParseKeyName("true", out k), "'true' parsed");
        Check(!KeyRules.TryParseKeyName("A, B", out k), "combination parsed");
        Check(KeyRules.TryParseKeyName("numpad4", out k) && k == Keys.NumPad4, "numpad4 not parsed");
        Check(KeyRules.TryParseKeyName("", out k) && k == Keys.None, "empty not unassigned");
    }

    private static void DamagedSettings()
    {
        RlsPaths p = NewPaths();
        File.WriteAllBytes(p.SettingsFile, new byte[] { 0, 1, 2, 3, 255, 254, 10, 0x5B, 0x5D });
        ConfigurationManager c = new ConfigurationManager(p);
        c.LoadAll();
        Check(c.Settings.Profile == PerformanceProfileKind.Low, "default profile not Low");
        Check(c.Settings.AutosaveMinutes == 5, "default autosave wrong");
        Check(Directory.GetFiles(p.ConfigBackups, "Settings.ini.*").Length == 1, "damaged file not backed up");
        Check(File.ReadAllText(p.SettingsFile).Contains("Profile=Low"), "settings not rewritten");
    }

    private static void SettingsRoundTrip()
    {
        RlsPaths p = NewPaths();
        ConfigurationManager c = new ConfigurationManager(p);
        c.LoadAll();
        c.Settings.Profile = PerformanceProfileKind.Balanced;
        c.Settings.AutosaveMinutes = 15;
        c.Settings.ShowHubBlips = false;
        c.SaveSettings();
        ConfigurationManager c2 = new ConfigurationManager(p);
        c2.LoadAll();
        Check(c2.Settings.Profile == PerformanceProfileKind.Balanced, "profile lost");
        Check(c2.Settings.AutosaveMinutes == 15, "autosave lost");
        Check(!c2.Settings.ShowHubBlips, "blips flag lost");
        Check(Directory.GetFiles(p.ConfigBackups).Length == 0, "valid file was treated as invalid");
    }

    private static void SaveRoundTrip()
    {
        RlsPaths p = NewPaths();
        SaveManager s = new SaveManager(p, "test");
        s.Load();
        Check(s.Status == "New profile", "status: " + s.Status);
        s.Data.Stats.TotalPlayTimeSeconds = 123.5;
        for (int i = 0; i < 8; i++) Check(s.Save(), "save failed: " + s.LastError);

        SaveManager s2 = new SaveManager(p, "test");
        s2.Load();
        Check(s2.Status == "Loaded", "status: " + s2.Status);
        Check(Math.Abs(s2.Data.Stats.TotalPlayTimeSeconds - 123.5) < 0.001, "play time lost");
        Check(s2.Data.SaveCount == 8, "save count " + s2.Data.SaveCount);
        for (int i = 1; i <= SaveManager.BackupCount; i++)
            Check(File.Exists(Path.Combine(p.SaveBackups, "profile.bak" + i + ".xml")), "missing bak" + i);
        Check(!File.Exists(Path.Combine(p.SaveBackups, "profile.bak6.xml")), "too many backups");
        Check(Directory.GetFiles(p.SaveBackups, "daily-*.xml").Length == 1, "daily snapshot missing");
        Check(!File.Exists(p.SaveFile + ".tmp"), "temp file left behind");
    }

    private static void CorruptSaveRecovered()
    {
        RlsPaths p = NewPaths();
        SaveManager s = new SaveManager(p, "test");
        s.Load();
        s.Data.Stats.SessionsStarted = 7;
        s.Save();
        s.Save(); // bak1 now holds a good copy
        File.WriteAllText(p.SaveFile, "<RealisticLifeSimulatorSave><Schema garbage");

        SaveManager s2 = new SaveManager(p, "test");
        s2.Load();
        Check(s2.Status.StartsWith("Restored from backup"), "status: " + s2.Status);
        Check(s2.Data.Stats.SessionsStarted == 7, "progress lost");
        Check(Directory.GetFiles(p.SaveBackups, "profile.xml.unreadable-*").Length == 1, "corrupt file not kept");
        Check(File.Exists(p.SaveFile), "profile.xml not re-created");
    }

    private static void NewerSaveProtected()
    {
        RlsPaths p = NewPaths();
        SaveData future = SaveData.CreateNew("future");
        future.SchemaVersion = SaveData.CurrentSchema + 1;
        File.WriteAllBytes(p.SaveFile, SaveManager.Serialize(future));
        byte[] before = File.ReadAllBytes(p.SaveFile);

        SaveManager s = new SaveManager(p, "test");
        s.Load();
        Check(s.ReadOnly, "not read-only");
        Check(!s.Save(), "save was allowed");
        byte[] after = File.ReadAllBytes(p.SaveFile);
        Check(Convert.ToBase64String(before) == Convert.ToBase64String(after), "newer save modified");
    }

    private static void ConflictChecker()
    {
        string scripts = Path.Combine(currentRoot, "scripts");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "SpeedoSignals.ini"),
            "[Keys]\nLeftSignal=NumPad4\nHazards=NumPad5\n[Options]\nShowSpeedometer=true\nBlinkPeriodMs=750\n");
        File.WriteAllText(Path.Combine(currentRoot, "ScriptHookVDotNet.ini"), "ReloadKeyBinding=None\nConsoleKeyBinding=F4\n");

        KeyConflictChecker k = new KeyConflictChecker();
        k.ScanExternal(currentRoot);
        KeyBindingSet own = new KeyBindingSet();
        own.Set(RlsAction.OpenBank, Keys.F8);

        Check(Contains(k.FindConflicts(Keys.NumPad4, RlsAction.CallPolice, own), "SpeedoSignals: LeftSignal"), "SpeedoSignals not detected");
        Check(Contains(k.FindConflicts(Keys.F4, RlsAction.CallPolice, own), "ScriptHookVDotNet: ConsoleKeyBinding"), "SHVDN console key not detected");
        Check(Contains(k.FindConflicts(Keys.W, RlsAction.CallPolice, own), "GTA V:"), "GTA default not detected");
        Check(Contains(k.FindConflicts(Keys.F8, RlsAction.CallPolice, own), "This mod: Open bank"), "own conflict not detected");
        Check(k.FindConflicts(Keys.F8, RlsAction.OpenBank, own).Count == 0, "action conflicts with itself");
        Check(k.FindConflicts(Keys.F10, RlsAction.CallPolice, own).Count == 0, "F10 should be free");
    }

    private static void Phase2Settings()
    {
        RlsPaths p = NewPaths();
        // A Phase 1 Settings.ini (no Fuel/Traffic/General sections) must keep its values.
        File.WriteAllText(p.SettingsFile, "[Performance]\nProfile=High\n[Saving]\nAutosaveMinutes=15\n[Interface]\nShowLifeHubMapIcons=false\n[Debug]\nDebugLogging=false\n");
        ConfigurationManager c = new ConfigurationManager(p);
        c.LoadAll();
        Check(c.Settings.Profile == PerformanceProfileKind.High, "old profile lost");
        Check(c.Settings.AutosaveMinutes == 15, "old autosave lost");
        Check(c.Settings.FuelEnabled && c.Settings.TrafficEnabled, "new defaults wrong");
        // 0.6.0: the default fine mode is Automatic (Witnessed almost never fined anything).
        Check(c.Settings.Fines == FineMode.Automatic, "default fine mode wrong: " + c.Settings.Fines);
        string text = File.ReadAllText(p.SettingsFile);
        Check(text.Contains("[Fuel]") && text.Contains("FineMode=Automatic") && text.Contains("Language=Auto"), "new sections not written");

        c.Settings.Language = LanguageSetting.Russian;
        c.Settings.FuelPricePerLiter = 2.5f;
        c.Settings.Fines = FineMode.Off;
        c.Settings.DetectRedLights = false;
        c.SaveSettings();
        ConfigurationManager c2 = new ConfigurationManager(p);
        c2.LoadAll();
        Check(c2.Settings.Language == LanguageSetting.Russian, "language lost");
        Check(Math.Abs(c2.Settings.FuelPricePerLiter - 2.5f) < 0.001f, "price lost");
        Check(c2.Settings.Fines == FineMode.Off && !c2.Settings.DetectRedLights, "traffic settings lost");

        // A comma decimal (Russian habit) is accepted.
        File.WriteAllText(p.SettingsFile, File.ReadAllText(p.SettingsFile).Replace("PricePerLiter=2.50", "PricePerLiter=1,75"));
        ConfigurationManager c3 = new ConfigurationManager(p);
        c3.LoadAll();
        Check(Math.Abs(c3.Settings.FuelPricePerLiter - 1.75f) < 0.001f, "comma decimal not accepted");
    }

    private static void SaveMigration()
    {
        RlsPaths p = NewPaths();
        File.WriteAllText(p.SaveFile,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<RealisticLifeSimulatorSave><SchemaVersion>1</SchemaVersion><ModVersion>0.1.0-phase1</ModVersion>" +
            "<CreatedUtc>2026-11-05T10:00:00Z</CreatedUtc><LastSavedUtc /><SaveCount>4</SaveCount><FirstRunNoticeShown>true</FirstRunNoticeShown>" +
            "<Stats><TotalPlayTimeSeconds>3600</TotalPlayTimeSeconds><SessionsStarted>3</SessionsStarted></Stats></RealisticLifeSimulatorSave>");
        SaveManager s = new SaveManager(p, "test");
        s.Load();
        Check(s.Status == "Loaded", "status " + s.Status);
        Check(Math.Abs(s.Data.Stats.TotalPlayTimeSeconds - 3600) < 0.01 && s.Data.Stats.SessionsStarted == 3, "phase 1 data lost");
        Check(s.Data.Vehicles != null && s.Data.Transactions != null && s.Data.Violations != null && s.Data.Driving != null, "new sections null");
        Check(s.Data.FirstRunNoticeShown && !s.Data.Phase2NoticeShown, "notice flags wrong");

        VehicleFuelRecord r = new VehicleFuelRecord();
        r.Plate = "12ABC345"; r.ModelHash = 123; r.Liters = 33.3; r.LastUsedUtc = DateTime.UtcNow.ToString("o");
        s.Data.Vehicles.Add(r);
        s.Data.AddTransaction("Fuel", "20.0 L @ Test", -33);
        ViolationRecord v = new ViolationRecord();
        v.Type = "Speeding"; v.Fine = 300; v.TimeUtc = DateTime.UtcNow.ToString("o");
        s.Data.Violations.Add(v);
        Check(s.Save(), "save failed: " + s.LastError);

        SaveManager s2 = new SaveManager(p, "test");
        s2.Load();
        Check(s2.Data.SchemaVersion == SaveData.CurrentSchema, "schema not upgraded");
        VehicleFuelRecord back = s2.Data.FindVehicle("12abc345", 123);
        Check(back != null && Math.Abs(back.Liters - 33.3) < 0.001, "vehicle fuel lost");
        Check(s2.Data.Transactions.Count == 1 && s2.Data.Violations.Count == 1, "lists lost");
        Check(Math.Abs(s2.Data.OutstandingFines() - 300) < 0.001, "outstanding wrong");
    }

    private static void FuelModelTest()
    {
        FuelSpec sedan = FuelModel.ForVehicle(1, Joaat.Hash("asea"));
        Check(sedan.Supported && !sedan.Electric && sedan.TankLiters == 60, "sedan spec");
        // Cruise at 25 m/s (90 km/h), half throttle, rpm 0.55, in 1 s steps until empty.
        double liters = sedan.TankLiters, meters = 0;
        int steps = 0;
        while (liters > 0 && steps < 100000)
        {
            liters -= FuelModel.Consumption(sedan, 25, 1, 0.5f, 0.55f, true, 1f);
            meters += 25;
            steps++;
        }
        double km = meters / 1000.0;
        Check(km > 90 && km < 160, "sedan range " + km.ToString("0") + " km");
        double minutes = steps / 60.0;
        Check(minutes > 50 && minutes < 110, "sedan time " + minutes.ToString("0") + " min");

        double idleHour = FuelModel.Consumption(sedan, 0, 3600, 0, 0.2f, true, 1f);
        Check(idleHour > 2 && idleHour < 8, "idle per hour " + idleHour);
        Check(FuelModel.Consumption(sedan, 25, 1, 1, 1, false, 1f) == 0, "engine off consumed fuel");
        Check(FuelModel.Consumption(sedan, 25, 1, 1, 1, true, 2f) > FuelModel.Consumption(sedan, 25, 1, 1, 1, true, 1f), "multiplier ignored");
        Check(FuelModel.Consumption(sedan, 25, 1, 1, 1, true, 1f) > FuelModel.Consumption(sedan, 25, 1, 0, 0.3f, true, 1f), "throttle ignored");

        FuelSpec truck = FuelModel.ForVehicle(20, 1), bike = FuelModel.ForVehicle(8, 1);
        Check(truck.TankLiters > sedan.TankLiters && bike.TankLiters < sedan.TankLiters, "class capacities");
        Check(FuelModel.Consumption(truck, 25, 1, 0.5f, 0.5f, true, 1) > FuelModel.Consumption(bike, 25, 1, 0.5f, 0.5f, true, 1), "truck vs bike");
        Check(FuelModel.ForVehicle(1, Joaat.Hash("voltic")).Electric, "voltic not electric");
        Check(!FuelModel.ForVehicle(13, 1).Supported && !FuelModel.ForVehicle(15, 1).Supported, "bicycle/heli supported");
        Random rng = new Random(1);
        for (int i = 0; i < 50; i++)
        {
            double l = FuelModel.InitialLiters(sedan, rng);
            Check(l >= 0.25 * 60 - 0.01 && l <= 0.85 * 60 + 0.01, "initial fuel out of range");
        }
    }

    private static void JoaatTest()
    {
        Check((uint)Joaat.Hash("adder") == 0xB779A091u, "adder hash " + ((uint)Joaat.Hash("adder")).ToString("X"));
        Check((uint)Joaat.Hash("ADDER") == 0xB779A091u, "hash not case-insensitive");
        Check((uint)Joaat.Hash("prop_gas_pump_1a") == (uint)Joaat.Hash("PROP_GAS_PUMP_1A"), "prop hash");
    }

    private static void SpeedLimitTest()
    {
        SpeedLimitTable t = SpeedLimitTable.CreateDefault();
        RoadCategory c;
        Check(t.Classify("Los Santos Fwy", "DOWNT", 0, out c) == 110 && c == RoadCategory.Freeway, "freeway by name");
        Check(t.Classify("Some Rd", "DOWNT", TrafficRules.NodeHighway, out c) == 110, "freeway by node flag");
        Check(t.Classify("Route 68", "DESRT", 0, out c) == 90 && c == RoadCategory.Highway, "route 68");
        Check(t.Classify("Great Ocean Hwy", "PALCOV", 0, out c) == 90, "great ocean hwy");
        Check(t.Classify("Panorama Dr", "DESRT", 0, out c) == 80 && c == RoadCategory.Rural, "rural");
        Check(t.Classify("Ginger St", "ROCKF", 0, out c) == 40 && c == RoadCategory.Residential, "residential");
        Check(t.Classify("Strawberry Ave", "STRAW", 0, out c) == 50 && c == RoadCategory.City, "city");
        Check(t.Classify("", "", 0, out c) == 50, "unknown road");

        RlsPaths p = NewPaths();
        string file = Path.Combine(p.Data, "SpeedLimits.ini");
        SpeedLimitTable.LoadOrCreate(file);
        Check(File.Exists(file), "default file not written");
        File.WriteAllText(file, File.ReadAllText(file).Replace("[Streets]", "[Streets]\nVinewood Blvd=30").Replace("[Zones]", "[Zones]\nAIRP=25").Replace("City=50", "City=60"));
        SpeedLimitTable t2 = SpeedLimitTable.LoadOrCreate(file);
        Check(t2.Classify("Vinewood Blvd", "VINE", 0, out c) == 30 && c == RoadCategory.Custom, "street override");
        Check(t2.Classify("Any St", "AIRP", 0, out c) == 25, "zone override");
        Check(t2.Classify("Any St", "DOWNT", 0, out c) == 60, "edited city limit");
        Check(t2.Classify("Del Perro Fwy", "AIRP", 0, out c) == 110, "freeway must beat zone override");
    }

    private static void FinesTest()
    {
        Check(TrafficRules.BaseFine(ViolationType.Speeding, 12) == 150, "speeding tier 1");
        Check(TrafficRules.BaseFine(ViolationType.Speeding, 25) == 300, "speeding tier 2");
        Check(TrafficRules.BaseFine(ViolationType.Speeding, 45) == 600, "speeding tier 3");
        Check(TrafficRules.BaseFine(ViolationType.Reckless, 0) > TrafficRules.BaseFine(ViolationType.RedLight, 0), "reckless vs red light");
        SaveData d = SaveData.CreateNew("t");
        ViolationRecord a = new ViolationRecord(); a.Fine = 150; d.Violations.Add(a);
        ViolationRecord b = new ViolationRecord(); b.Fine = 300; b.Paid = true; d.Violations.Add(b);
        ViolationRecord c = new ViolationRecord(); c.Fine = 0; d.Violations.Add(c);
        Check(Math.Abs(d.OutstandingFines() - 150) < 0.001, "outstanding " + d.OutstandingFines());
        for (int i = 0; i < SaveData.MaxTransactions + 20; i++) d.AddTransaction("Fuel", "x", -1);
        Check(d.Transactions.Count == SaveData.MaxTransactions, "transactions not capped");
    }

    private static void RussianText()
    {
        L.Set(UiLanguage.Russian);
        try
        {
            string s = L.S("Yes", "Да");
            Check(s.Length == 2 && s[0] == 'Д' && s[1] == 'а', "Cyrillic mangled: " + s);
            Check(TrafficRules.Name(ViolationType.Speeding) == "Превышение скорости", "violation name mangled");
            Check(L.Money(1234.4) == "$1 234", "money format '" + L.Money(1234.4) + "'");
            Check(L.Money(-16.5) == "-$16,50", "money format '" + L.Money(-16.5) + "'");
            Check(L.Liters(12.34) == "12,3 л", "liters format '" + L.Liters(12.34) + "'");
            Check(L.Resolve(LanguageSetting.English, true) == UiLanguage.English, "explicit English ignored");
            Check(L.Resolve(LanguageSetting.Auto, true) == UiLanguage.Russian, "auto with Russian game");
        }
        finally
        {
            L.Set(UiLanguage.English);
        }
    }

    private static void LicencePoints()
    {
        DriverProfile p = DriverProfile.Create("Franklin Clinton", new Random(3));
        Check(p.LicenceNumber.StartsWith("D") && p.PolicyNumber.StartsWith("SA-"), "documents not generated");
        Check(p.Reputation == PoliceRules.StartingReputation, "starting reputation");
        p.AddPoints(6, "a", 100);
        p.AddPoints(4, "b", 200);
        Check(p.ActivePoints(300) == 10 && !p.IsSuspended(300), "10 points");
        p.AddPoints(2, "c", 300);
        Check(p.IsSuspended(400), "12 points should suspend");
        double later = 100 + PoliceRules.PointsLifetimeSeconds + 1;
        Check(p.ActivePoints(later) == 6 && !p.IsSuspended(later), "points did not expire: " + p.ActivePoints(later));
        p.AddPoints(0, "none", 500);
        Check(p.Points.Count == 3, "zero points stored");
        Check(PoliceRules.PointsFor(ViolationType.Speeding, 45) > PoliceRules.PointsFor(ViolationType.Speeding, 12), "speeding tiers");
    }

    private static void WarningChanceTest()
    {
        double good = PoliceRules.WarningChance(90, 2, ViolationType.StopSign, 0, false, 0);
        double bad = PoliceRules.WarningChance(20, -2, ViolationType.StopSign, 0, true, 2);
        Check(good > 0.5 && bad < 0.1, "good " + good + " bad " + bad);
        Check(PoliceRules.WarningChance(100, 2, ViolationType.Reckless, 0, false, 0) == 0, "reckless warned");
        Check(PoliceRules.WarningChance(100, 2, ViolationType.Speeding, 45, false, 0) == 0, "40+ over warned");
        Check(PoliceRules.WarningChance(70, 0, ViolationType.Speeding, 12, false, 0) > PoliceRules.WarningChance(70, 0, ViolationType.Speeding, 25, false, 0), "severity ignored");
        for (int rep = 0; rep <= 100; rep += 10)
            for (int att = -2; att <= 2; att++)
            {
                double c = PoliceRules.WarningChance(rep, att, ViolationType.RedLight, 0, false, 0);
                Check(c >= 0 && c <= 0.85, "chance out of range " + c);
            }
    }

    private static void SeverityTest()
    {
        Check(PoliceRules.Severity(20, 40, false, false) == AccidentSeverity.Minor, "minor");
        Check(PoliceRules.Severity(45, 40, false, false) == AccidentSeverity.Moderate, "moderate by speed");
        Check(PoliceRules.Severity(20, 150, false, false) == AccidentSeverity.Moderate, "moderate by damage");
        Check(PoliceRules.Severity(15, 10, true, false) == AccidentSeverity.Moderate, "injury -> moderate");
        Check(PoliceRules.Severity(80, 10, false, false) == AccidentSeverity.Severe, "severe by speed");
        Check(PoliceRules.Severity(10, 10, false, true) == AccidentSeverity.Severe, "death -> severe");
    }

    private static void FaultTest()
    {
        CollisionEvidence rearEnd = new CollisionEvidence();
        rearEnd.PlayerSpeedKmh = 50; rearEnd.OtherSpeedKmh = 20; rearEnd.OtherAhead = 0.9;
        Check(PoliceRules.DetermineFault(rearEnd) == Fault.Player, "rear-ending is player's fault");
        Check(PoliceRules.IsFalseStatement(Statement.OtherAtFault, Fault.Player, rearEnd), "lie not detected");
        Check(!PoliceRules.IsFalseStatement(Statement.MyFault, Fault.Player, rearEnd), "honest flagged");

        CollisionEvidence hitWhileStopped = new CollisionEvidence();
        hitWhileStopped.PlayerSpeedKmh = 0; hitWhileStopped.OtherSpeedKmh = 40; hitWhileStopped.OtherAhead = -0.9;
        Check(PoliceRules.DetermineFault(hitWhileStopped) == Fault.Other, "stationary player blamed");

        CollisionEvidence parked = new CollisionEvidence();
        parked.PlayerSpeedKmh = 30; parked.OtherWasStopped = true;
        Check(PoliceRules.DetermineFault(parked) == Fault.Player, "hitting a stopped car");

        CollisionEvidence side = new CollisionEvidence();
        side.PlayerSpeedKmh = 40; side.OtherSpeedKmh = 40; side.OtherAhead = 0.1;
        Check(PoliceRules.DetermineFault(side) == Fault.Shared, "side impact should be shared");
        side.PlayerViolationJustBefore = true;
        Check(PoliceRules.DetermineFault(side) == Fault.Player, "red light before crash");

        CollisionEvidence ped = new CollisionEvidence();
        ped.PedestrianStruck = true; ped.PlayerSpeedKmh = 40;
        Check(PoliceRules.DetermineFault(ped) == Fault.Player, "pedestrian");
    }

    private static void CollisionFineTest()
    {
        Check(PoliceRules.CollisionFine(AccidentSeverity.Severe, Fault.Player) > PoliceRules.CollisionFine(AccidentSeverity.Minor, Fault.Player), "severity fine");
        Check(PoliceRules.CollisionFine(AccidentSeverity.Moderate, Fault.Other) == 0, "innocent fined");
        Check(PoliceRules.CollisionFine(AccidentSeverity.Moderate, Fault.Shared) == PoliceRules.CollisionFine(AccidentSeverity.Moderate, Fault.Player) / 2, "shared half");
        Check(PoliceRules.ReputationForAccident(70, AccidentSeverity.Severe, Fault.Other) == 70, "innocent lost reputation");
        Check(PoliceRules.ReputationForAccident(70, AccidentSeverity.Severe, Fault.Player) == 55, "severe loss");
        Check(PoliceRules.ReputationAfter(5, 6, false) == 0, "reputation below 0");
        Check(PoliceRules.DamageEstimate(1000, 1000, 1) == 0, "no damage estimate");
        Check(PoliceRules.DamageEstimate(500, 800, 1) > 0, "damage estimate");
    }

    private static void SaveMigration3()
    {
        RlsPaths p = NewPaths();
        SaveData d2 = SaveData.CreateNew("phase2");
        d2.SchemaVersion = 2;
        d2.Profiles = null;
        d2.Accidents = null;
        ViolationRecord old = new ViolationRecord(); old.Type = "Speeding"; old.Fine = 150; old.TimeUtc = "x";
        d2.Violations.Add(old);
        File.WriteAllBytes(p.SaveFile, SaveManager.Serialize(d2));

        SaveManager s = new SaveManager(p, "test");
        s.Load();
        Check(s.Status == "Loaded", "status " + s.Status);
        Check(s.Data.Profiles != null && s.Data.Accidents != null, "new lists null");
        Check(s.Data.Violations.Count == 1 && s.Data.Violations[0].Character == null, "old violation changed");

        DriverProfile prof = s.Data.Profile("Michael De Santa", new Random(1));
        prof.AddPoints(4, "test", 10);
        Check(s.Data.Profile("Michael De Santa", new Random(2)) == prof, "profile duplicated");
        AccidentReport r = new AccidentReport(); r.Id = "LSPD-1"; r.Severity = "Moderate"; r.Fault = "Player"; r.Fine = 600;
        s.Data.AddAccident(r);
        Check(s.Save(), "save failed " + s.LastError);

        SaveManager s2 = new SaveManager(p, "test");
        s2.Load();
        Check(s2.Data.SchemaVersion == SaveData.CurrentSchema, "schema " + s2.Data.SchemaVersion);
        Check(s2.Data.Profiles.Count == 1 && s2.Data.Profiles[0].ActivePoints(20) == 4, "profile lost");
        Check(s2.Data.Accidents.Count == 1 && s2.Data.Accidents[0].Fine == 600, "accident lost");
    }

    private static void NpcPersonalities()
    {
        Random r = new Random(42);
        int[] cars = new int[6], trucks = new int[6];
        for (int i = 0; i < 20000; i++)
        {
            cars[(int)NpcRules.Pick(r, 1)]++;
            trucks[(int)NpcRules.Pick(r, 20)]++;
        }
        for (int i = 0; i < 6; i++)
            Check(cars[i] > 600, "sedan personality " + (DriverPersonality)i + " too rare: " + cars[i]);
        Check(cars[(int)DriverPersonality.Average] > cars[(int)DriverPersonality.Aggressive], "average should be most common");
        Check(trucks[(int)DriverPersonality.Professional] > 9000, "truck drivers mostly professional");
        Check(trucks[(int)DriverPersonality.Aggressive] == 0, "aggressive truck drivers");

        foreach (DriverPersonality p in Enum.GetValues(typeof(DriverPersonality)))
        {
            PersonalityTraits t = NpcRules.Traits(p);
            Check(t.Ability >= 0 && t.Ability <= 1 && t.Aggressiveness >= 0 && t.Aggressiveness <= 1, "trait range " + p);
            Check((t.DrivingStyle & NpcRules.StopAtLights) != 0, p + " does not stop at lights by default");
        }
        Check(NpcRules.CruiseSpeed(50, DriverPersonality.Aggressive, 1) > NpcRules.CruiseSpeed(50, DriverPersonality.Careful, 1), "aggressive slower than careful");
        Check(NpcRules.CruiseSpeed(50, DriverPersonality.Slow, 1) < 50 / 3.6f, "slow driver at/above limit");
        Check(Math.Abs(NpcRules.CruiseSpeed(110, DriverPersonality.Average, 1) - 110 / 3.6f) < 0.01f, "average = limit");
        Check(NpcRules.CruiseSpeed(50, DriverPersonality.Average, 0.8f) < NpcRules.CruiseSpeed(50, DriverPersonality.Average, 1), "weather ignored");
        Check(NpcRules.CruiseSpeed(0, DriverPersonality.Average, 1) > 4, "unknown limit");
    }

    private static void NpcMistakes()
    {
        Random r = new Random(7);
        int careful = 0, aggressive = 0, redLights = 0;
        for (int i = 0; i < 20000; i++)
        {
            if (NpcRules.RollMistake(r, DriverPersonality.Careful, 60) != NpcMistake.None) careful++;
            NpcMistake m = NpcRules.RollMistake(r, DriverPersonality.Aggressive, 60);
            if (m != NpcMistake.None) aggressive++;
            if (m == NpcMistake.RunRedLight) redLights++;
        }
        Check(aggressive > careful * 5, "aggressive " + aggressive + " vs careful " + careful);
        Check(aggressive > 1800 && aggressive < 3000, "aggressive per-minute rate " + aggressive);
        Check(redLights > 0, "aggressive never runs lights");
        Check(NpcRules.RollMistake(r, DriverPersonality.Professional, 0) == NpcMistake.None, "mistake with zero time");
    }

    private static void SaveMigration4()
    {
        RlsPaths p = NewPaths();
        SaveData d3 = SaveData.CreateNew("phase3");
        d3.SchemaVersion = 3;
        d3.Incidents = null;
        AccidentReport keep = new AccidentReport(); keep.Id = "LSPD-KEEP"; keep.Severity = "Minor"; keep.Fault = "Other";
        d3.Accidents.Add(keep);
        File.WriteAllBytes(p.SaveFile, SaveManager.Serialize(d3));
        SaveManager s = new SaveManager(p, "test");
        s.Load();
        Check(s.Status == "Loaded", "status " + s.Status);
        Check(s.Data.Incidents != null && s.Data.Accidents.Count == 1, "migration lost data");
        for (int i = 0; i < SaveData.MaxIncidents + 10; i++) s.Data.AddIncident("Accident", "Loc", "d", "o");
        Check(s.Data.Incidents.Count == SaveData.MaxIncidents, "incidents not capped");
        Check(s.Save(), "save failed");
        SaveManager s2 = new SaveManager(p, "test");
        s2.Load();
        Check(s2.Data.SchemaVersion == SaveData.CurrentSchema && s2.Data.Incidents.Count == SaveData.MaxIncidents, "incidents not saved");
        Check(TrafficRules.BaseFine(ViolationType.FailureToYieldEmergency, 0) > 0, "yield fine");
    }

    private static void WearTest()
    {
        Check(OwnershipRules.WearPerKm(100, 400, 1) * 3 == OwnershipRules.WearPerKm(500, 400, 1), "overdue not 3x");
        Check(OwnershipRules.WearPerKm(100, 400, 2) > OwnershipRules.WearPerKm(100, 400, 1), "rate ignored");
        // 400 km on schedule costs about 8 % of condition.
        double lost = OwnershipRules.WearPerKm(0, 400, 1) * 400;
        Check(lost > 5 && lost < 12, "wear over a service interval " + lost);
        Check(OwnershipRules.ServiceCeiling(0) == 100 && OwnershipRules.ServiceCeiling(100000) == 70, "service ceiling");
        Check(OwnershipRules.TopSpeedPenaltyPercent(80) == 0 && OwnershipRules.TopSpeedPenaltyPercent(0) == 20, "speed penalty range");
        Check(OwnershipRules.TopSpeedPenaltyPercent(30) > 0 && OwnershipRules.TopSpeedPenaltyPercent(30) < 20, "speed penalty mid");
        Check(OwnershipRules.FuelPenaltyFactor(20) > 1 && OwnershipRules.FuelPenaltyFactor(60) == 1, "fuel penalty");
        Check(OwnershipRules.ServiceCost(7) > OwnershipRules.ServiceCost(1), "super service not dearer");
    }

    private static void InsuranceTest()
    {
        double own, ins;
        OwnershipRules.SplitRepairCost(2000, InsuranceLevel.None, out own, out ins);
        Check(own == 2000 && ins == 0, "no insurance");
        OwnershipRules.SplitRepairCost(2000, InsuranceLevel.Basic, out own, out ins);
        Check(ins == 750 && own == 1250, "basic split " + own + "/" + ins);
        OwnershipRules.SplitRepairCost(2000, InsuranceLevel.Full, out own, out ins);
        Check(ins == 1400 && own == 600, "full split " + own + "/" + ins);
        OwnershipRules.SplitRepairCost(200, InsuranceLevel.Full, out own, out ins);
        Check(ins == 0 && own == 200, "below deductible");
        Check(OwnershipRules.Premium(100000, InsuranceLevel.Full) > OwnershipRules.Premium(100000, InsuranceLevel.Basic), "premium order");
        Check(OwnershipRules.Premium(100000, InsuranceLevel.None) == 0, "no premium without insurance");
        Check(OwnershipRules.Premium(1000, InsuranceLevel.Basic) == 25, "minimum premium");
        Check(OwnershipRules.TotalLossPayout(100000, InsuranceLevel.Full, 100) > OwnershipRules.TotalLossPayout(100000, InsuranceLevel.Basic, 100), "payout order");
        Check(OwnershipRules.TotalLossPayout(100000, InsuranceLevel.None, 100) == 0, "uninsured payout");
    }

    private static void ValueBillingTest()
    {
        Check(OwnershipRules.CurrentValue(100000, 0, 100) == 75000, "new car value " + OwnershipRules.CurrentValue(100000, 0, 100));
        Check(OwnershipRules.CurrentValue(100000, 10000, 100) < 75000, "mileage depreciation");
        Check(OwnershipRules.CurrentValue(100000, 0, 20) < OwnershipRules.CurrentValue(100000, 0, 90), "condition ignored");
        Check(OwnershipRules.CurrentValue(100000, 1000000, 0) > 0, "value never zero");

        PropertyDef a = new PropertyDef(); a.Id = "A"; a.Maintenance = 500; a.Rent = 1200;
        // 1.1.0: apartments/houses always earn rent; B is a garage, which earns rent only when rented out.
        PropertyDef b = new PropertyDef(); b.Id = "B"; b.Type = PropertyType.Garage; b.Maintenance = 100; b.Rent = 300;
        List<OwnedProperty> props = new List<OwnedProperty>();
        OwnedProperty pa = new OwnedProperty(); pa.Id = "A"; pa.Owner = "M"; pa.RentedOut = true; props.Add(pa);
        OwnedProperty pb = new OwnedProperty(); pb.Id = "B"; pb.Owner = "M"; props.Add(pb);
        OwnedProperty pc = new OwnedProperty(); pc.Id = "B"; pc.Owner = "F"; props.Add(pc);
        List<OwnedVehicle> cars = new List<OwnedVehicle>();
        OwnedVehicle c1 = new OwnedVehicle(); c1.Owner = "M"; c1.Value = 100000; c1.Insurance = InsuranceLevel.Full; cars.Add(c1);
        OwnedVehicle c2 = new OwnedVehicle(); c2.Owner = "M"; c2.Value = 100000; c2.Insurance = InsuranceLevel.Full; c2.State = VehicleState.Sold; cars.Add(c2);
        double maint, rent, prem;
        double bal = OwnershipRules.CycleBalance(props, id => id == "A" ? a : b, cars, "M", out maint, out rent, out prem);
        Check(maint == 600 && rent == 1200 && prem == 900, "cycle parts " + maint + "/" + rent + "/" + prem);
        Check(bal == 1200 - 600 - 900, "balance " + bal);
    }

    private static void DataFilesTest()
    {
        List<string> problems = new List<string>();
        List<PropertyDef> defs = OwnershipRules.ParseProperties(IniFile.Parse(OwnershipRules.DefaultPropertiesIni()), problems);
        Check(problems.Count == 0, "default properties problems: " + string.Join(";", problems.ToArray()));
        Check(defs.Count >= 15, "too few properties " + defs.Count);
        bool apt = false, house = false, garage = false;
        foreach (PropertyDef d in defs)
        {
            if (d.Type == PropertyType.Apartment) apt = true;
            if (d.Type == PropertyType.House) house = true;
            if (d.Type == PropertyType.Garage) { garage = true; Check(d.GarageSlots > 0, "garage without slots"); }
            Check(d.Price > 0 && d.Maintenance > 0 && d.Rent > d.Maintenance, "economics of " + d.Id);
        }
        Check(apt && house && garage, "missing property types");

        List<PropertyDef> bad = OwnershipRules.ParseProperties(IniFile.Parse("[X]\nType=Castle\nPrice=5\nX=1\nY=1\nZ=1\n[Y]\nType=House\nPrice=abc\n"), problems);
        Check(bad.Count == 0 && problems.Count == 2, "bad entries accepted");

        List<CatalogEntry> cat = OwnershipRules.ParseCatalog(IniFile.Parse(OwnershipRules.DefaultCatalogIni()));
        Check(cat.Count >= 30, "catalogue size " + cat.Count);
        for (int i = 1; i < cat.Count; i++) Check(cat[i].Price >= cat[i - 1].Price, "catalogue not sorted");
        Check(FuelModel.ForVehicle(cat[0].Class, Joaat.Hash(cat[0].Model)).Supported, "cheapest vehicle class unsupported");
    }

    private static void SaveMigration5()
    {
        RlsPaths p = NewPaths();
        SaveData d4 = SaveData.CreateNew("phase4");
        d4.SchemaVersion = 4;
        d4.Properties = null;
        d4.OwnedVehicles = null;
        d4.Incidents.Add(new CityIncident());
        File.WriteAllBytes(p.SaveFile, SaveManager.Serialize(d4));
        SaveManager s = new SaveManager(p, "test");
        s.Load();
        Check(s.Status == "Loaded" && s.Data.Properties != null && s.Data.OwnedVehicles != null, "migration");
        Check(s.Data.Incidents.Count == 1, "old data lost");

        OwnedVehicle v = new OwnedVehicle();
        v.Id = "V1"; v.Owner = "Franklin Clinton"; v.DisplayName = "Buffalo"; v.Plate = "12ABC345"; v.ModelHash = 123;
        v.Condition = 77.5; v.MileageKm = 1234.5; v.Insurance = InsuranceLevel.Full; v.State = VehicleState.Stored; v.GarageId = "GarageLegion";
        v.Look.Primary = 12; v.Look.Mods = new int[50]; v.Look.Mods[11] = 3;
        ServiceEntry e = new ServiceEntry(); e.Kind = "Service"; e.Cost = 180; v.Services.Add(e);
        s.Data.OwnedVehicles.Add(v);
        OwnedProperty op = new OwnedProperty(); op.Id = "GarageLegion"; op.Owner = "Franklin Clinton"; op.RentedOut = true;
        s.Data.Properties.Add(op);
        s.Data.Debt = 321;
        Check(s.Save(), "save failed " + s.LastError);

        SaveManager s2 = new SaveManager(p, "test");
        s2.Load();
        OwnedVehicle back = s2.Data.OwnedVehicles[0];
        Check(s2.Data.SchemaVersion == SaveData.CurrentSchema, "schema");
        Check(back.Insurance == InsuranceLevel.Full && back.State == VehicleState.Stored && back.GarageId == "GarageLegion", "vehicle state lost");
        Check(Math.Abs(back.Condition - 77.5) < 0.001 && Math.Abs(back.MileageKm - 1234.5) < 0.001, "numbers lost");
        Check(back.Look.Primary == 12 && back.Look.Mods[11] == 3 && back.Services.Count == 1, "look or history lost");
        Check(s2.Data.Properties[0].RentedOut && Math.Abs(s2.Data.Debt - 321) < 0.001, "property or debt lost");
    }

    // ------------------------------------------------------------ passengers and taxi

    private static void PassengerKeysUnassigned()
    {
        RlsPaths p = NewPaths();
        ConfigurationManager c = new ConfigurationManager(p);
        c.LoadAll();
        Check(c.Bindings.Get(RlsAction.PassengerMenu) == Keys.None && c.Bindings.Get(RlsAction.CallTaxi) == Keys.None, "new actions assigned on fresh install");

        // A Phase 5 file (no new entries) where the player assigned the Life Hub key.
        RlsPaths p2 = NewPathsIn("upgrade");
        File.WriteAllText(p2.KeyBindingsFile, "[KeyBindings]\nOpenLifeHub=Oem5\nOpenSettings=\nOpenBank=\nOpenPropertyMenu=\nOpenVehicleMenu=\nCallPolice=\nRequestRoadsideAssistance=\nStartJob=\n");
        ConfigurationManager u = new ConfigurationManager(p2);
        u.LoadAll();
        Check(u.Bindings.Get(RlsAction.OpenLifeHub) == Keys.Oem5, "existing Life Hub key lost");
        Check(u.Bindings.AssignedCount == 1, "upgrade assigned extra keys: " + u.Bindings.AssignedCount);
        string text = File.ReadAllText(p2.KeyBindingsFile);
        Check(text.Contains("PassengerMenu=\r\n") || text.Contains("PassengerMenu=\n"), "PassengerMenu not written empty");
        Check(text.Contains("CallTaxi=\r\n") || text.Contains("CallTaxi=\n"), "CallTaxi not written empty");
        Check(text.Contains("OpenLifeHub=Oem5"), "Oem5 not written back");
    }

    private static RlsPaths NewPathsIn(string sub)
    {
        RlsPaths p = new RlsPaths(Path.Combine(currentRoot, sub));
        p.EnsureDirectories();
        return p;
    }

    private static void PassengerSettings()
    {
        RlsPaths p = NewPaths();
        ConfigurationManager c = new ConfigurationManager(p);
        c.LoadAll();
        Check(c.Settings.PassengerEnabled && c.Settings.TaxiEnabled, "defaults off");
        Check(Math.Abs(c.Settings.TaxiPricePerKm - 2.2f) < 0.001f, "default per km");
        c.Settings.TaxiEnabled = false;
        c.Settings.TaxiBaseFare = 5f;
        c.Settings.TaxiPricePerKm = 3f;
        c.Settings.TaxiPricePerMinute = 0.5f;
        c.SaveSettings();
        ConfigurationManager c2 = new ConfigurationManager(p);
        c2.LoadAll();
        Check(!c2.Settings.TaxiEnabled && Math.Abs(c2.Settings.TaxiBaseFare - 5f) < 0.001f && Math.Abs(c2.Settings.TaxiPricePerKm - 3f) < 0.001f
              && Math.Abs(c2.Settings.TaxiPricePerMinute - 0.5f) < 0.001f, "taxi settings lost");
        Check(Directory.GetFiles(p.ConfigBackups).Length == 0, "valid file treated as invalid");

        // Phase 5 Settings.ini without the new sections: user values must survive the upgrade.
        RlsPaths p2 = NewPathsIn("old");
        File.WriteAllText(p2.SettingsFile, "[General]\nLanguage=Russian\n[Performance]\nProfile=Low\n[Fuel]\nPricePerLiter=2.50\n[Ownership]\nBillingIntervalMinutes=90\n");
        ConfigurationManager o = new ConfigurationManager(p2);
        o.LoadAll();
        Check(o.Settings.Language == LanguageSetting.Russian && Math.Abs(o.Settings.FuelPricePerLiter - 2.5f) < 0.001f && o.Settings.BillingIntervalMinutes == 90, "old values lost");
        string text = File.ReadAllText(p2.SettingsFile);
        Check(text.Contains("[Passenger]") && text.Contains("[Taxi]") && text.Contains("PricePerKm=2.20"), "new sections not added");
        Check(text.Contains("PricePerLiter=2.50") && text.Contains("Language=Russian"), "old values not written back");
    }

    private static void SaveMigration6()
    {
        RlsPaths p = NewPaths();
        SaveData d5 = SaveData.CreateNew("phase5");
        d5.SchemaVersion = 5;
        d5.Passenger = null;
        d5.Debt = 55;
        d5.Incidents.Add(new CityIncident());
        File.WriteAllBytes(p.SaveFile, SaveManager.Serialize(d5));
        SaveManager s = new SaveManager(p, "test");
        s.Load();
        Check(s.Status == "Loaded" && s.Data.Passenger != null, "migration");
        Check(Math.Abs(s.Data.Debt - 55) < 0.001 && s.Data.Incidents.Count == 1, "old data lost");
        s.Data.Passenger.Rides = 3;
        s.Data.Passenger.TaxiRides = 2;
        s.Data.Passenger.TaxiSpent = 41;
        s.Data.Passenger.DistanceKm = 12.5;
        s.Data.AddTransaction("Taxi", "test", -20);
        Check(s.Save(), "save failed " + s.LastError);
        SaveManager s2 = new SaveManager(p, "test");
        s2.Load();
        Check(s2.Data.SchemaVersion == SaveData.CurrentSchema && s2.Data.Passenger.Rides == 3 && s2.Data.Passenger.TaxiRides == 2
              && Math.Abs(s2.Data.Passenger.TaxiSpent - 41) < 0.001 && Math.Abs(s2.Data.Passenger.DistanceKm - 12.5) < 0.001, "stats lost");
        Check(s2.Data.Transactions.Count == 1 && s2.Data.Transactions[0].Category == "Taxi", "taxi receipt lost");
    }

    private static void AttitudeTest()
    {
        // Same driver, same answer every time.
        for (int seed = 0; seed < 200; seed++)
            Check(PassengerRules.AttitudeFor(DriverPersonality.Average, seed) == PassengerRules.AttitudeFor(DriverPersonality.Average, seed), "not deterministic");

        int[] careful = new int[5], aggressive = new int[5];
        for (int seed = 0; seed < 4000; seed++)
        {
            careful[(int)PassengerRules.AttitudeFor(DriverPersonality.Careful, seed * 7919)]++;
            aggressive[(int)PassengerRules.AttitudeFor(DriverPersonality.Aggressive, seed * 7919)]++;
        }
        Check(careful[(int)PassengerAttitude.Cautious] > careful[(int)PassengerAttitude.Unfriendly] * 4, "careful drivers not mostly cautious");
        Check(aggressive[(int)PassengerAttitude.Unfriendly] > aggressive[(int)PassengerAttitude.Friendly] * 4, "aggressive drivers not mostly unfriendly");
        for (int i = 0; i < 5; i++) Check(careful[i] > 0 && aggressive[i] > 0, "attitude " + (PassengerAttitude)i + " never occurs");
        double u = PassengerRules.Unit(12345, 1);
        Check(u >= 0 && u < 1 && Math.Abs(u - PassengerRules.Unit(12345, 1)) < 1e-12, "unit value");
    }

    private static int AcceptCount(PassengerAttitude a, bool night)
    {
        int n = 0;
        for (int seed = 0; seed < 2000; seed++)
        {
            RideContext c = new RideContext();
            c.Attitude = a;
            c.Night = night;
            c.VehicleClass = 1;
            RideRefusal r;
            if (PassengerRules.Accepts(c, seed * 31337, out r)) n++;
        }
        return n;
    }

    private static void AcceptanceTest()
    {
        RideContext c = new RideContext();
        c.Attitude = PassengerAttitude.Friendly;
        c.Reputation = 100;
        RideRefusal r;
        c.PlayerWanted = true;
        Check(!PassengerRules.Accepts(c, 1, out r) && r == RideRefusal.Wanted, "wanted accepted");
        c.PlayerWanted = false; c.PlayerArmed = true;
        Check(!PassengerRules.Accepts(c, 1, out r) && r == RideRefusal.Armed, "armed accepted");
        c.PlayerArmed = false; c.AskedRecently = true;
        Check(!PassengerRules.Accepts(c, 1, out r) && r == RideRefusal.AskedBefore, "repeat accepted");

        int friendly = AcceptCount(PassengerAttitude.Friendly, false);
        int neutral = AcceptCount(PassengerAttitude.Neutral, false);
        int cautious = AcceptCount(PassengerAttitude.Cautious, false);
        int unfriendly = AcceptCount(PassengerAttitude.Unfriendly, false);
        int suspicious = AcceptCount(PassengerAttitude.Suspicious, false);
        Check(friendly > neutral && neutral > cautious && cautious > unfriendly && unfriendly >= suspicious, "ordering " + friendly + "/" + neutral + "/" + cautious + "/" + unfriendly + "/" + suspicious);
        Check(friendly > 1500, "friendly drivers rarely accept: " + friendly);
        Check(suspicious < 100, "suspicious drivers accept too often: " + suspicious);
        Check(AcceptCount(PassengerAttitude.Cautious, true) < cautious / 2 + 1, "night does not matter");

        // Same driver, same situation: same answer (no dice roll).
        RideContext n = new RideContext();
        n.Attitude = PassengerAttitude.Neutral;
        RideRefusal r1, r2;
        for (int seed = 0; seed < 100; seed++)
            Check(PassengerRules.Accepts(n, seed, out r1) == PassengerRules.Accepts(n, seed, out r2) && r1 == r2, "random answer");

        // Work vehicles and moving cars are harder to get into.
        RideContext w = new RideContext(); w.Attitude = PassengerAttitude.Friendly; w.VehicleClass = 20;
        RideContext car = new RideContext(); car.Attitude = PassengerAttitude.Friendly; car.VehicleClass = 1;
        RideRefusal h;
        Check(PassengerRules.Score(w, out h) < PassengerRules.Score(car, out h), "work vehicle not harder");
        RideContext hurt = new RideContext(); hurt.Attitude = PassengerAttitude.Neutral; hurt.PlayerInjured = true;
        Check(PassengerRules.Score(hurt, out h) > PassengerRules.Score(n, out h), "injured passenger gets no sympathy");
    }

    private static void RideReactionsTest()
    {
        Check(PassengerRules.OnStopRequest(PassengerAttitude.Friendly, true, 40, 50) == StopResponse.StopNow, "friendly urgent stop");
        Check(PassengerRules.OnStopRequest(PassengerAttitude.Friendly, true, 100, 120) == StopResponse.PullOverNow, "stopped on a fast road");
        Check(PassengerRules.OnStopRequest(PassengerAttitude.Cautious, true, 30, 50) == StopResponse.PullOverNow, "cautious stops in lane");
        Check(PassengerRules.OnStopRequest(PassengerAttitude.Unfriendly, false, 30, 50) == StopResponse.PullOverLater, "unfriendly not delayed");
        int g = PassengerRules.GrumbleDelayMs(42);
        Check(g >= 6000 && g <= 12000 && g == PassengerRules.GrumbleDelayMs(42), "grumble delay");

        Check(PassengerRules.React(PassengerAttitude.Friendly, RideSituation.Wanted, false) == DriverReaction.Warn, "friendly warns first");
        Check(PassengerRules.React(PassengerAttitude.Friendly, RideSituation.Wanted, true) == DriverReaction.PullOverAskOut, "friendly second time");
        Check(PassengerRules.React(PassengerAttitude.Suspicious, RideSituation.Wanted, false) == DriverReaction.StopNowAskOut, "suspicious wanted");
        Check(PassengerRules.React(PassengerAttitude.Cautious, RideSituation.WeaponOut, false) == DriverReaction.StopNowAskOut, "cautious weapon");
        foreach (PassengerAttitude a in (PassengerAttitude[])Enum.GetValues(typeof(PassengerAttitude)))
        {
            Check(PassengerRules.React(a, RideSituation.VehicleOnFire, false) == DriverReaction.StopNowAskOut, "fire");
            Check(PassengerRules.React(a, RideSituation.Shooting, false) == DriverReaction.StopNowAskOut, "shooting");
        }

        Check(PassengerRules.AgreesToDetour(PassengerAttitude.Friendly, 3.5) && !PassengerRules.AgreesToDetour(PassengerAttitude.Friendly, 6), "friendly detour");
        Check(!PassengerRules.AgreesToDetour(PassengerAttitude.Unfriendly, 0.5) && !PassengerRules.AgreesToDetour(PassengerAttitude.Suspicious, 0.5), "unfriendly detour");
    }

    private static void SeatTest()
    {
        Check(!PassengerRules.IsPassengerSeat(-1), "driver seat allowed");
        for (int s = 0; s < 8; s++) Check(PassengerRules.IsPassengerSeat(s), "seat " + s);
        L.Set(UiLanguage.English);
        Check(PassengerRules.SeatName(0) == "Front passenger" && PassengerRules.SeatName(1) == "Rear left" && PassengerRules.SeatName(2) == "Rear right", "seat names");
        Check(PassengerRules.SeatName(4) == "Extra seat 2", "extra seat name " + PassengerRules.SeatName(4));
        L.Set(UiLanguage.Russian);
        Check(PassengerRules.SeatName(1) == "Заднее левое", "russian seat name");
        foreach (PassengerAttitude a in (PassengerAttitude[])Enum.GetValues(typeof(PassengerAttitude)))
        {
            L.Set(UiLanguage.English);
            string en = PassengerRules.AcceptLine(a) + PassengerRules.RefuseLine(a, RideRefusal.Distrust);
            L.Set(UiLanguage.Russian);
            string ru = PassengerRules.AcceptLine(a) + PassengerRules.RefuseLine(a, RideRefusal.Distrust);
            Check(en != ru && en.Length > 0 && ru.Length > 0, "missing translation for " + a);
        }
        L.Set(UiLanguage.English);
    }

    private static void TaxiFareTest()
    {
        TaxiPricing p = new TaxiPricing(); // 3.00 + 2.20/km + 0.35/min
        Check(TaxiRules.Fare(p, 0, 0) == 3, "base fare " + TaxiRules.Fare(p, 0, 0));
        Check(TaxiRules.Fare(p, 5, 0) == 14, "5 km " + TaxiRules.Fare(p, 5, 0));
        Check(TaxiRules.Fare(p, 5, 2) == 15, "waiting " + TaxiRules.Fare(p, 5, 2)); // 14.70 -> 15
        Check(TaxiRules.Fare(p, 1.01, 0) == 6, "round up " + TaxiRules.Fare(p, 1.01, 0)); // 5.222 -> 6
        Check(TaxiRules.Fare(p, -3, -1) == 3, "negative inputs");
        Check(TaxiRules.Fare(p, double.NaN, 0) == 0, "NaN fare");
        Check(TaxiRules.Estimate(p, 4) == TaxiRules.Fare(p, 4 * TaxiRules.RoadFactor, 0), "estimate");
        TaxiPricing pricey = new TaxiPricing(); pricey.BaseFare = 5; pricey.PerKm = 4; pricey.PerMinuteWaiting = 1;
        Check(TaxiRules.Fare(pricey, 2, 3) == 16, "configured pricing");
        int paid, debt;
        TaxiRules.Settle(40, 100, out paid, out debt);
        Check(paid == 40 && debt == 0, "enough cash");
        TaxiRules.Settle(40, 15, out paid, out debt);
        Check(paid == 15 && debt == 25, "short of cash");
        TaxiRules.Settle(40, -5, out paid, out debt);
        Check(paid == 0 && debt == 40, "negative cash");
    }

    private static bool Contains(List<string> list, string fragment)
    {
        foreach (string s in list) if (s.Contains(fragment)) return true;
        return false;
    }
}
