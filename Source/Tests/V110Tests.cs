// Realistic Life Simulator - tests for the 1.1.0 changes (no game needed).
// These test the decision logic and data only. They do NOT prove anything works inside GTA V.

using System;
using System.Collections.Generic;
using System.IO;
using RealisticLifeSimulator.Core;

public static partial class CoreTests
{
    private static void RunV110()
    {
        Run("1.1.0 Auto speed limit: applied limit is always road limit + 10 km/h", AutoLimitMarginTest);
        Run("1.1.0 Auto speed limit: stable on the same road, confirms changes, handles unknown roads", AutoLimitTrackerTest);
        Run("1.1.0 Auto speed limit: cap steps down smoothly, rises at once", AutoLimitRampTest);
        Run("1.1.0 Auto speed limit: setting round trip, off by default, manual limiter untouched", AutoLimitSettingsTest);
        Run("1.1.0 Settings: a 1.0 file (with PedestrianAccidents) upgrades cleanly", PedestrianSettingRemovedTest);
        Run("1.1.0 Properties: apartments and houses are rental-only, garages unchanged", RentalOnlyRulesTest);
        Run("1.1.0 Properties: rental income is paid every bill, purchases and garages keep working", RentalBillingTest);
        Run("1.1.0 Properties: ownership of an old save still loads", RentalSaveCompatTest);
        Run("1.1.0 Accidents: pedestrian detection is gone from the game code", PedestrianDetectionRemovedTest);
    }

    private static void AutoLimitMarginTest()
    {
        int[,] table = { { 30, 40 }, { 40, 50 }, { 50, 60 }, { 60, 70 }, { 80, 90 }, { 90, 100 }, { 100, 110 }, { 110, 120 } };
        for (int i = 0; i < table.GetLength(0); i++)
            Check(AutoSpeedLimitRules.Applied(table[i, 0]) == table[i, 1],
                  "road " + table[i, 0] + " -> " + AutoSpeedLimitRules.Applied(table[i, 0]) + ", expected " + table[i, 1]);
        for (int road = 10; road <= 200; road += 5)
            Check(AutoSpeedLimitRules.Applied(road) == road + 10, "margin not consistent at " + road);
        Check(AutoSpeedLimitRules.Applied(0) == 0 && AutoSpeedLimitRules.Applied(-5) == 0, "unknown road must not produce a limit");
        Check(AutoSpeedLimitRules.Applied(295) == SpeedLimiterRules.MaxKmh, "clamped to the limiter maximum");
        // The default road table gives the values from the request.
        SpeedLimitTable t = SpeedLimitTable.CreateDefault();
        Check(AutoSpeedLimitRules.Applied(t.City) == 60 && AutoSpeedLimitRules.Applied(t.Freeway) == 120
              && AutoSpeedLimitRules.Applied(t.Residential) == 50 && AutoSpeedLimitRules.Applied(t.Rural) == 90, "default table");
        Check(AutoSpeedLimitRules.Supports(0) && AutoSpeedLimitRules.Supports(8) && AutoSpeedLimitRules.Supports(13), "cars, bikes, cycles");
        Check(!AutoSpeedLimitRules.Supports(14) && !AutoSpeedLimitRules.Supports(15) && !AutoSpeedLimitRules.Supports(16) && !AutoSpeedLimitRules.Supports(21),
              "boats / aircraft / trains must not use road limits");
    }

    private static void AutoLimitTrackerTest()
    {
        AutoSpeedLimitTracker k = new AutoSpeedLimitTracker();
        Check(!k.Known && k.AppliedLimit == 0, "starts unknown");
        // First reading applies at once.
        Check(k.Feed(50, 0) && k.RoadLimit == 50 && k.AppliedLimit == 60, "first reading");
        // Same road: no further changes (no repeated activation / notifications).
        int changes = 0;
        for (int t = 1000; t <= 60000; t += 1000) if (k.Feed(50, t)) changes++;
        Check(changes == 0 && k.AppliedLimit == 60, "same road re-activated " + changes + " times");
        // A single different reading (junction, overpass) does not switch.
        Check(!k.Feed(110, 61000) && k.RoadLimit == 50, "one stray reading switched the limit");
        Check(!k.Feed(50, 62000) && k.RoadLimit == 50, "back on the road");
        // A real change is confirmed on the second reading in a row.
        Check(!k.Feed(110, 63000), "changed after one reading");
        Check(k.Feed(110, 64000) && k.AppliedLimit == 120, "freeway not confirmed");
        // Alternating noise never confirms.
        AutoSpeedLimitTracker n = new AutoSpeedLimitTracker();
        n.Feed(80, 0);
        n.Feed(50, 1000); n.Feed(40, 2000); n.Feed(50, 3000); n.Feed(40, 4000);
        Check(n.RoadLimit == 80, "alternating readings switched the limit to " + n.RoadLimit);
        // Unknown road: keep the last limit for a while, then become unknown (caller falls back).
        AutoSpeedLimitTracker u = new AutoSpeedLimitTracker();
        u.Feed(90, 0);
        Check(!u.Feed(0, 1000) && u.RoadLimit == 90, "dropped at once on an unknown reading");
        Check(!u.Feed(0, AutoSpeedLimitRules.HoldUnknownMs) && u.Known, "dropped before the hold time");
        Check(u.Feed(0, AutoSpeedLimitRules.HoldUnknownMs + 1000) && !u.Known && u.AppliedLimit == 0, "never dropped");
        Check(!u.Feed(0, AutoSpeedLimitRules.HoldUnknownMs + 2000), "reported the drop twice");
        // Back on a road: applies at once.
        Check(u.Feed(50, 30000) && u.AppliedLimit == 60, "road after unknown");
        // Reset forgets everything.
        u.Reset();
        Check(!u.Known && u.Feed(30, 31000) && u.AppliedLimit == 40, "reset");
        // TickCount wrap-around.
        AutoSpeedLimitTracker w = new AutoSpeedLimitTracker();
        w.Feed(50, int.MaxValue - 500);
        Check(!w.Feed(0, unchecked(int.MaxValue + 500)) && w.Known, "wrap-around dropped the limit");
    }

    private static void AutoLimitRampTest()
    {
        // Raising, or first application: at once.
        Check(AutoSpeedLimitRules.StepCap(-1, 60, 100f) == 60, "first application");
        Check(AutoSpeedLimitRules.StepCap(60, 120, 60f) == 120, "raise");
        // Already slow enough: at once.
        Check(AutoSpeedLimitRules.StepCap(120, 60, 55f) == 60, "slow enough");
        // Freeway exit at 118 km/h into a 60 cap: lowered in steps of 5, never below the target.
        int cap = 120;
        float speed = 118f;
        int steps = 0;
        while (cap != 60 && steps < 100)
        {
            int next = AutoSpeedLimitRules.StepCap(cap, 60, speed);
            Check(next >= 60 && next < cap, "step " + steps + ": " + cap + " -> " + next);
            Check(cap - next <= Math.Max(AutoSpeedLimitRules.RampKmhPerStep, cap - (int)speed + AutoSpeedLimitRules.RampKmhPerStep), "step too big");
            cap = next;
            speed = Math.Min(speed, cap + 1.5f);   // the car follows the cap
            steps++;
        }
        Check(cap == 60, "never reached the target");
        Check(steps >= 8, "slowed down too abruptly (" + steps + " steps)");
        Check(AutoSpeedLimitRules.StepCap(80, 0, 100f) == 0, "unknown target");
    }

    private static void AutoLimitSettingsTest()
    {
        RlsPaths p = NewPaths();
        ConfigurationManager c = new ConfigurationManager(p);
        c.LoadAll();
        Check(!c.Settings.AutoSpeedLimit, "automatic limit must be off by default");
        Check(!c.Settings.SpeedLimiterEnabled && c.Settings.SpeedLimiterKmh == 60, "manual defaults changed");
        Check(File.ReadAllText(p.SettingsFile).Contains("AutoRoadLimit=false"), "setting not written");
        c.Settings.AutoSpeedLimit = true;
        c.Settings.SpeedLimiterEnabled = true;
        c.Settings.SpeedLimiterKmh = 75;
        c.SaveSettings();
        ConfigurationManager c2 = new ConfigurationManager(p);
        c2.LoadAll();
        Check(c2.Settings.AutoSpeedLimit, "auto setting lost");
        Check(c2.Settings.SpeedLimiterEnabled && c2.Settings.SpeedLimiterKmh == 75, "manual limiter changed by the auto setting");
        Check(Directory.GetFiles(p.ConfigBackups).Length == 0, "valid file treated as invalid");
        c2.Settings.AutoSpeedLimit = false;
        c2.SaveSettings();
        ConfigurationManager c3 = new ConfigurationManager(p);
        c3.LoadAll();
        Check(!c3.Settings.AutoSpeedLimit && c3.Settings.SpeedLimiterEnabled && c3.Settings.SpeedLimiterKmh == 75, "toggle off lost / manual changed");
        // Bad value: falls back to off and the file is repaired.
        File.WriteAllText(p.SettingsFile, File.ReadAllText(p.SettingsFile).Replace("AutoRoadLimit=false", "AutoRoadLimit=maybe"));
        ConfigurationManager c4 = new ConfigurationManager(p);
        c4.LoadAll();
        Check(!c4.Settings.AutoSpeedLimit && File.ReadAllText(p.SettingsFile).Contains("AutoRoadLimit=false"), "invalid value");
    }

    private static void PedestrianSettingRemovedTest()
    {
        RlsPaths p = NewPaths();
        ConfigurationManager c = new ConfigurationManager(p);
        c.LoadAll();
        string text = File.ReadAllText(p.SettingsFile);
        Check(!text.Contains("PedestrianAccidents"), "pedestrian setting still written");
        // Simulate a 1.0 file: has PedestrianAccidents, lacks AutoRoadLimit.
        string old = text.Replace("AutoRoadLimit=false", "").Replace("[Accidents]", "[Accidents]\r\nPedestrianAccidents=true");
        old = old.Replace("ShowSpeedLimit=true", "ShowSpeedLimit=false");
        File.WriteAllText(p.SettingsFile, old);
        ConfigurationManager c2 = new ConfigurationManager(p);
        c2.LoadAll();
        Check(!c2.Settings.ShowSpeedLimit, "other values lost on upgrade");
        Check(!c2.Settings.AutoSpeedLimit, "auto limit switched on by the upgrade");
        string now = File.ReadAllText(p.SettingsFile);
        Check(now.Contains("AutoRoadLimit=false") && !now.Contains("PedestrianAccidents"), "file not upgraded");
        Check(c2.Settings.AccidentsEnabled, "vehicle accident system switched off");
    }

    private static List<PropertyDef> DefaultProperties()
    {
        List<string> problems = new List<string>();
        return OwnershipRules.ParseProperties(IniFile.Parse(OwnershipRules.DefaultPropertiesIni()), problems);
    }

    private static void RentalOnlyRulesTest()
    {
        int rentals = 0, garages = 0;
        foreach (PropertyDef d in DefaultProperties())
        {
            if (d.Type == PropertyType.Garage)
            {
                garages++;
                Check(!OwnershipRules.IsRentalInvestment(d), d.Id + " garage treated as rental-only");
                Check(OwnershipRules.CanEnter(d, true), d.Id + " garage can no longer be entered");
                Check(d.GarageSlots > 0, d.Id + " garage lost its slots");
            }
            else
            {
                rentals++;
                Check(OwnershipRules.IsRentalInvestment(d), d.Id + " not a rental investment");
                Check(!OwnershipRules.CanEnter(d, true), d.Id + " can still be entered");
                Check(d.Price > 0 && d.Rent > 0, d.Id + " cannot be bought or earns nothing");
                Check(d.Rent > d.Maintenance, d.Id + " loses money every bill");
            }
        }
        Check(rentals >= 10 && garages >= 4, "properties missing: " + rentals + " / " + garages);
        Check(!OwnershipRules.CanEnter(null, true) && !OwnershipRules.IsRentalInvestment(null), "null");
        PropertyDef g = new PropertyDef(); g.Type = PropertyType.Garage;
        Check(!OwnershipRules.CanEnter(g, false), "garage without a known interior");
    }

    private static void RentalBillingTest()
    {
        Dictionary<string, PropertyDef> byId = new Dictionary<string, PropertyDef>();
        foreach (PropertyDef d in DefaultProperties()) byId[d.Id] = d;
        Func<string, PropertyDef> def = id => byId.ContainsKey(id) ? byId[id] : null;
        List<OwnedProperty> props = new List<OwnedProperty>();
        // Bought apartment: the old RentedOut flag (false by default) does not matter any more.
        props.Add(new OwnedProperty { Id = "AltaSt", Owner = "Franklin Clinton", RentedOut = false });
        props.Add(new OwnedProperty { Id = "MiltonRd", Owner = "Franklin Clinton", RentedOut = true });
        // Garages: rent only when rented out, exactly as before.
        props.Add(new OwnedProperty { Id = "GarageDavis", Owner = "Franklin Clinton", RentedOut = false });
        props.Add(new OwnedProperty { Id = "GarageLegion", Owner = "Franklin Clinton", RentedOut = true });
        // Another character's property is not billed to Franklin.
        props.Add(new OwnedProperty { Id = "WildOats", Owner = "Michael De Santa" });
        double maint, rent, prem;
        double bal = OwnershipRules.CycleBalance(props, def, new List<OwnedVehicle>(), "Franklin Clinton", out maint, out rent, out prem);
        double expRent = byId["AltaSt"].Rent + byId["MiltonRd"].Rent + byId["GarageLegion"].Rent;
        double expMaint = byId["AltaSt"].Maintenance + byId["MiltonRd"].Maintenance + byId["GarageDavis"].Maintenance + byId["GarageLegion"].Maintenance;
        Check(rent == expRent, "rent " + rent + ", expected " + expRent);
        Check(maint == expMaint, "maintenance " + maint + ", expected " + expMaint);
        Check(bal == expRent - expMaint && bal > 0, "balance " + bal);
        Check(OwnershipRules.EarnsRent(props[0], byId["AltaSt"]) && !OwnershipRules.EarnsRent(props[2], byId["GarageDavis"]), "EarnsRent");
        // Unknown property id (removed from Properties.ini) is skipped, not a crash.
        props.Add(new OwnedProperty { Id = "Gone", Owner = "Franklin Clinton" });
        OwnershipRules.CycleBalance(props, def, new List<OwnedVehicle>(), "Franklin Clinton", out maint, out rent, out prem);
        Check(rent == expRent, "unknown property paid rent");
        // Next payment timer.
        Check(OwnershipRules.SecondsToNextBill(1000, 1000, 60) == 3600, "full interval");
        Check(OwnershipRules.SecondsToNextBill(4000, 1000, 60) == 600, "remaining");
        Check(OwnershipRules.SecondsToNextBill(9000, 1000, 60) == 0, "never negative");
    }

    private static void RentalSaveCompatTest()
    {
        RlsPaths p = NewPaths();
        SaveManager s = new SaveManager(p, "1.0.0");
        s.Load();
        s.Data.Properties.Add(new OwnedProperty { Id = "IntegrityWay", Owner = "Franklin Clinton", RentedOut = false, PricePaid = 390000 });
        s.Data.Properties.Add(new OwnedProperty { Id = "GarageSandy", Owner = "Franklin Clinton", RentedOut = false, PricePaid = 25000 });
        s.Data.InsidePropertyId = "IntegrityWay"; // saved inside an apartment by 1.0
        Check(s.Save(), "save failed");
        SaveManager s2 = new SaveManager(p, "1.1.0");
        s2.Load();
        Check(s2.Data.Properties.Count == 2 && s2.Data.Properties[0].Id == "IntegrityWay" && s2.Data.Properties[0].PricePaid == 390000, "ownership lost");
        Check(s2.Data.InsidePropertyId == "IntegrityWay", "inside marker lost (needed to offer the exit)");
    }

    // Static guard: the game-side pedestrian detection must not come back by accident.
    private static void PedestrianDetectionRemovedTest()
    {
        string file = FindSource(Path.Combine("Game", "AccidentSystem.cs"));
        if (file == null) { Console.WriteLine("      (source not found next to the test exe - skipped)"); return; }
        string src = File.ReadAllText(file);
        Check(!src.Contains("TryDetectPedestrian") && !src.Contains("IsStruckPedestrian"), "pedestrian detection still present");
        Check(!src.Contains("GetNearbyPeds"), "accident system still scans pedestrians");
        Check(!src.Contains("PedestrianAccidents"), "pedestrian setting still read");
        Check(src.Contains("GetNearbyVehicles"), "vehicle accident detection removed");
        string prop = FindSource(Path.Combine("Game", "PropertySystem.cs"));
        if (prop != null)
        {
            string ps = File.ReadAllText(prop);
            Check(ps.Contains("if (!CanEnter(d)) return;"), "Enter() is not guarded by CanEnter");
        }
    }

    private static string FindSource(string relative)
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 4 && dir != null; i++)
        {
            string f = Path.Combine(dir, relative);
            if (File.Exists(f)) return f;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }
}
