// Realistic Life Simulator - tests for the 0.6.0 core rules (no game needed).
// These test the decision logic only. They do NOT prove anything works inside GTA V.

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using RealisticLifeSimulator.Core;

public static partial class CoreTests
{
    private static void RunV060()
    {
        Run("0.6.0 Fines: enforcement decision for every fine mode", EnforcementDecisionTest);
        Run("0.6.0 Fines: speeding is one violation per episode, with grace after a limit drop", SpeedingMonitorTest);
        Run("0.6.0 Fines: duplicate protection by type, place and time", DeduperTest);
        Run("0.6.0 Fines: red-light and stop-sign false-positive guards", DetectionGuardsTest);
        Run("0.6.0 Fines: amounts are configurable and survive a settings round trip", FineAmountsTest);
        Run("0.6.0 Settings: old default Witnessed upgraded once, explicit choices kept", FineModeUpgradeTest);
        Run("0.6.0 Settings: all new sections round trip, valid file is not rewritten", V060SettingsRoundTrip);
        Run("0.6.0 Licence: status is never Valid when suspended or revoked", LicenceStatusTest);
        Run("0.6.0 Licence: driving while suspended escalates, reinstatement rules", LicenceEscalationTest);
        Run("0.6.0 Licence: officer line matches the real status (EN/RU)", OfficerLineTest);
        Run("0.6.0 Save: schema 6 migrates to 7, licence status persists across save/load", SaveMigration7);
        Run("0.6.0 Save: ViolationType names from older saves still parse", ViolationNamesStable);
        Run("0.6.0 Speed limiter: input validation and km/h <-> m/s units", SpeedLimiterTest);
        Run("0.6.0 Police patrols: budget never exceeds the cap, Off creates none", PatrolBudgetTest);
        Run("0.6.0 Traffic lights: axis classification and assist conditions", TrafficLightTest);
        Run("0.6.0 Pedestrian accidents: fault and fines", PedestrianTest);
        Run("0.6.0 Keys: new limiter action is unassigned, existing keys kept", LimiterKeyTest);
    }

    private static void EnforcementDecisionTest()
    {
        AutoEnforcement a = new AutoEnforcement();
        // Automatic: witness -> roadside stop (or ticket if stops are off)
        Check(EnforcementRules.Decide(FineMode.Automatic, ViolationType.Speeding, true, true, a, false) == FineDecision.PoliceStop, "auto witnessed");
        Check(EnforcementRules.Decide(FineMode.Automatic, ViolationType.Speeding, true, false, a, false) == FineDecision.Ticket, "auto witnessed, no stops");
        // Automatic, no witness: only confident and auto-enforceable violations are fined
        Check(EnforcementRules.Decide(FineMode.Automatic, ViolationType.Speeding, false, true, a, true) == FineDecision.Ticket, "camera speeding");
        Check(EnforcementRules.Decide(FineMode.Automatic, ViolationType.RedLight, false, true, a, false) == FineDecision.RecordOnly, "uncertain red light fined");
        Check(EnforcementRules.Decide(FineMode.Automatic, ViolationType.RedLight, false, true, a, true) == FineDecision.Ticket, "camera red light");
        Check(EnforcementRules.Decide(FineMode.Automatic, ViolationType.StopSign, false, true, a, true) == FineDecision.RecordOnly, "stop sign needs a witness by default");
        Check(EnforcementRules.Decide(FineMode.Automatic, ViolationType.Reckless, false, true, a, true) == FineDecision.RecordOnly, "reckless needs a witness");
        a.Speeding = false;
        Check(EnforcementRules.Decide(FineMode.Automatic, ViolationType.Speeding, false, true, a, true) == FineDecision.RecordOnly, "disabled camera still fines");
        // Other modes keep their 0.5 behaviour
        Check(EnforcementRules.Decide(FineMode.Witnessed, ViolationType.Speeding, false, true, a, true) == FineDecision.RecordOnly, "witnessed mode fined without witness");
        Check(EnforcementRules.Decide(FineMode.Witnessed, ViolationType.Speeding, true, true, a, true) == FineDecision.PoliceStop, "witnessed mode");
        Check(EnforcementRules.Decide(FineMode.Always, ViolationType.StopSign, false, true, a, false) == FineDecision.Ticket, "always mode");
        Check(EnforcementRules.Decide(FineMode.Off, ViolationType.Speeding, true, true, a, true) == FineDecision.RecordOnly, "off mode fined");
        Check(new RlsSettings().Fines == FineMode.Automatic, "default mode");
    }

    private static void SpeedingMonitorTest()
    {
        SpeedingMonitor m = new SpeedingMonitor();
        int over;
        int reports = 0;
        // 65 km/h in a 50 zone, tolerance 10: 3 s needed, then exactly one report.
        for (int t = 0; t <= 20000; t += 250)
            if (m.Feed(65f, 50, 10, t, out over)) { reports++; Check(over == 15, "over " + over); Check(t >= 3000, "reported too early at " + t); }
        Check(reports == 1, "expected one report per episode, got " + reports);
        // Within tolerance: never.
        SpeedingMonitor m2 = new SpeedingMonitor();
        for (int t = 0; t <= 20000; t += 250) Check(!m2.Feed(59f, 50, 10, t, out over), "fined within tolerance");
        // Short burst (2 s) is not speeding.
        SpeedingMonitor m3 = new SpeedingMonitor();
        bool any = false;
        for (int t = 0; t < 2000; t += 250) any |= m3.Feed(90f, 50, 10, t, out over);
        for (int t = 2000; t < 4000; t += 250) any |= m3.Feed(40f, 50, 10, t, out over);
        Check(!any, "a 2 s burst was fined");
        // New episode after slowing down for more than 5 s.
        SpeedingMonitor m4 = new SpeedingMonitor();
        reports = 0;
        int now = 0;
        for (; now < 5000; now += 250) if (m4.Feed(80f, 50, 10, now, out over)) reports++;
        for (int i = 0; i < 30; i++, now += 250) m4.Feed(45f, 50, 10, now, out over);
        for (int i = 0; i < 20; i++, now += 250) if (m4.Feed(80f, 50, 10, now, out over)) reports++;
        Check(reports == 2, "second episode not detected: " + reports);
        // Grace: leaving a 110 freeway into a 50 city road at 100 km/h is not fined at once.
        SpeedingMonitor g = new SpeedingMonitor();
        for (int t = 0; t < 4000; t += 250) g.Feed(100f, 110, 10, t, out over);
        bool fined = false;
        for (int t = 4000; t < 8500; t += 250) fined |= g.Feed(100f - (t - 4000) / 100f, 50, 10, t, out over);
        Check(!fined, "fined during the grace period after a limit drop");
        Check(g.EffectiveLimit(4000 + 6000) == 50, "grace never ends");
        // Unknown road (0) clears the episode.
        Check(!g.Feed(200f, 0, 10, 20000, out over) && g.CurrentLimit == 0, "unknown limit");
    }

    private static void DeduperTest()
    {
        ViolationDeduper d = new ViolationDeduper();
        Check(d.Allow(ViolationType.RedLight, 100, 100, 0, 60000), "first");
        Check(!d.Allow(ViolationType.RedLight, 120, 110, 5000, 60000), "same junction fined twice");
        Check(d.Allow(ViolationType.Speeding, 120, 110, 5000, 60000), "other type blocked");
        Check(d.Allow(ViolationType.RedLight, 900, 900, 6000, 60000), "other place blocked");
        Check(d.Allow(ViolationType.RedLight, 100, 100, 70000, 60000), "window never ends");
        for (int i = 0; i < 200; i++) d.Allow(ViolationType.WrongWay, i * 500f, 0, 80000 + i, 1000);
        Check(d.Count <= 65, "deduper grows without limit: " + d.Count);
    }

    private static void DetectionGuardsTest()
    {
        Check(EnforcementRules.IsRedLight(1, 30f, false) && !EnforcementRules.IsRedLight(0, 30f, false), "red light basic");
        Check(!EnforcementRules.IsRedLight(3, 30f, true), "red light during the assisted green");
        Check(!EnforcementRules.IsRedLight(3, 10f, false), "slow roll counted");
        Check(!EnforcementRules.RedLightConfident(1, 40f) && EnforcementRules.RedLightConfident(2, 40f), "camera confidence");
        Check(EnforcementRules.IsStopSign(30f, 40f), "stop sign at 40 km/h");
        Check(!EnforcementRules.IsStopSign(8f, 40f), "stopped properly");
        Check(!EnforcementRules.IsStopSign(100f, 100f), "through road at 100 km/h treated as a stop sign (0.5 false positive)");
    }

    private static void FineAmountsTest()
    {
        RlsPaths p = NewPaths();
        ConfigurationManager c = new ConfigurationManager(p);
        c.LoadAll();
        Check(TrafficRules.BaseFine(ViolationType.RedLight, 0) == 250, "default red light");
        c.Settings.FineAmounts.RedLight = 400;
        c.Settings.FineAmounts.SpeedingExtreme = 2000;
        c.Settings.FineAmounts.HitPedestrian = 1500;
        c.SaveSettings();
        ConfigurationManager c2 = new ConfigurationManager(p);
        c2.LoadAll();
        Check(c2.Settings.FineAmounts.RedLight == 400 && c2.Settings.FineAmounts.SpeedingExtreme == 2000, "amounts lost");
        Check(TrafficRules.BaseFine(ViolationType.RedLight, 0) == 400, "BaseFine does not use the table");
        Check(TrafficRules.BaseFine(ViolationType.Speeding, 45) == 2000 && TrafficRules.BaseFine(ViolationType.Speeding, 10) == 150, "speeding tiers");
        Check(TrafficRules.BaseFine(ViolationType.HitPedestrian, 0) == 1500, "pedestrian amount");
        // Out-of-range values fall back to the default and the file is repaired.
        File.WriteAllText(p.SettingsFile, File.ReadAllText(p.SettingsFile).Replace("RedLight=400", "RedLight=-5"));
        ConfigurationManager c3 = new ConfigurationManager(p);
        c3.LoadAll();
        Check(c3.Settings.FineAmounts.RedLight == 250, "negative fine accepted");
        TrafficRules.Fines = new FineTable(); // do not leak into other tests
    }

    private static void FineModeUpgradeTest()
    {
        // 0.5.1 file: Witnessed was the default and is moved to Automatic once.
        RlsPaths p = NewPathsIn("old");
        File.WriteAllText(p.SettingsFile, "[General]\nLanguage=Russian\n[Traffic]\nFineMode=Witnessed\nSpeedToleranceKmh=15\n");
        ConfigurationManager c = new ConfigurationManager(p);
        c.LoadAll();
        Check(c.Settings.Fines == FineMode.Automatic, "old default not upgraded");
        Check(c.Settings.Language == LanguageSetting.Russian && c.Settings.SpeedToleranceKmh == 15, "other values lost");
        string text = File.ReadAllText(p.SettingsFile);
        Check(text.Contains("SettingsVersion=2") && text.Contains("FineMode=Automatic"), "upgrade not written");
        // Choosing Witnessed again after the upgrade is respected.
        c.Settings.Fines = FineMode.Witnessed;
        c.SaveSettings();
        ConfigurationManager again = new ConfigurationManager(p);
        again.LoadAll();
        Check(again.Settings.Fines == FineMode.Witnessed, "explicit Witnessed overwritten");
        // Any other old choice is kept.
        RlsPaths p2 = NewPathsIn("always");
        File.WriteAllText(p2.SettingsFile, "[Traffic]\nFineMode=Always\n");
        ConfigurationManager c2 = new ConfigurationManager(p2);
        c2.LoadAll();
        Check(c2.Settings.Fines == FineMode.Always, "Always changed");
    }

    private static void V060SettingsRoundTrip()
    {
        RlsPaths p = NewPaths();
        ConfigurationManager c = new ConfigurationManager(p);
        c.LoadAll();
        RlsSettings s = c.Settings;
        Check(!s.SpeedLimiterEnabled && s.SpeedLimiterKmh == 60 && s.PolicePatrols == PoliceDensity.Normal && s.ShorterRedLights, "defaults");
        Check(s.Auto.Speeding && s.Auto.RedLight && !s.Auto.StopSign && !s.Auto.AutoPayFromCash, "enforcement defaults");
        s.SpeedLimiterEnabled = true; s.SpeedLimiterKmh = 85; s.PolicePatrols = PoliceDensity.High; s.PatrolLicenceChecks = false;
        s.ShorterRedLights = false; s.MaxRedWaitSeconds = 45; s.AssistGreenSeconds = 12; s.PedestrianAccidents = false;
        s.Auto.WrongWay = false; s.Auto.AutoPayFromCash = true;
        c.SaveSettings();
        ConfigurationManager c2 = new ConfigurationManager(p);
        c2.LoadAll();
        RlsSettings r = c2.Settings;
        Check(r.SpeedLimiterEnabled && r.SpeedLimiterKmh == 85, "limiter lost");
        Check(r.PolicePatrols == PoliceDensity.High && !r.PatrolLicenceChecks, "patrols lost");
        Check(!r.ShorterRedLights && r.MaxRedWaitSeconds == 45 && r.AssistGreenSeconds == 12, "traffic lights lost");
        Check(!r.PedestrianAccidents && !r.Auto.WrongWay && r.Auto.AutoPayFromCash, "accident / enforcement lost");
        Check(Directory.GetFiles(p.ConfigBackups).Length == 0, "valid file treated as invalid");
        // Limiter value outside 10-300 is rejected on load.
        File.WriteAllText(p.SettingsFile, File.ReadAllText(p.SettingsFile).Replace("LimitKmh=85", "LimitKmh=5000"));
        ConfigurationManager c3 = new ConfigurationManager(p);
        c3.LoadAll();
        Check(c3.Settings.SpeedLimiterKmh == 60, "invalid limiter value accepted");
    }

    private static DriverProfile NewProfile()
    {
        return DriverProfile.Create("Franklin Clinton", new Random(1));
    }

    private static void LicenceStatusTest()
    {
        DriverProfile p = NewProfile();
        Check(LicenceRules.Status(p, 0) == LicenceStatus.Valid, "new licence");
        p.AddPoints(12, "x", 100);
        Check(LicenceRules.Status(p, 200) == LicenceStatus.Suspended, "12 points not suspended");
        Check(!LicenceRules.IsValid(p, 200), "suspended reported valid");
        Check(LicenceRules.SecondsRemaining(p, 200) > 0, "no time left shown");
        Check(LicenceRules.Status(p, 100 + PoliceRules.PointsLifetimeSeconds + 1) == LicenceStatus.Valid, "suspension did not end with the points");
        // 24+ points: revoked, and revocation does NOT end by itself.
        DriverProfile q = NewProfile();
        q.AddPoints(12, "a", 0); q.AddPoints(12, "b", 0);
        Check(LicenceRules.Update(q, 10), "24 points did not revoke");
        Check(LicenceRules.Status(q, 10) == LicenceStatus.Revoked, "not revoked");
        Check(LicenceRules.Status(q, 10 + 100000) == LicenceStatus.Revoked, "revocation ended on its own");
        Check(!LicenceRules.Update(q, 20), "Update changed an already revoked licence");
        // Timed police suspension without points.
        DriverProfile t = NewProfile();
        LicenceRules.Suspend(t, 50, 600, "test");
        Check(LicenceRules.Status(t, 100) == LicenceStatus.Suspended && LicenceRules.Status(t, 700) == LicenceStatus.Valid, "timed suspension");
        // Revoked always wins over suspended.
        LicenceRules.Suspend(q, 10, 600, "x");
        Check(LicenceRules.Status(q, 20) == LicenceStatus.Revoked, "suspension replaced revocation");
    }

    private static void LicenceEscalationTest()
    {
        DriverProfile p = NewProfile();
        p.AddPoints(12, "x", 0);
        LicenceRules.CaughtDriving(p, 100);
        Check(LicenceRules.Status(p, 100) == LicenceStatus.Suspended, "first offence");
        Check(Math.Abs(p.SuspendedUntilPlay - (100 + LicenceRules.PoliceSuspensionSeconds)) < 0.001, "timed suspension not added: " + p.SuspendedUntilPlay);
        LicenceRules.CaughtDriving(p, 200);
        Check(LicenceRules.Status(p, 200) == LicenceStatus.Revoked, "second offence not revoked");
        double ends = p.RevocationEndsPlay;
        LicenceRules.CaughtDriving(p, 300);
        Check(p.RevocationEndsPlay > ends, "driving while revoked did not extend");

        string reason;
        Check(!LicenceRules.CanReinstate(p, 400, 0, 5000, out reason), "reinstated during the waiting period");
        double after = p.RevocationEndsPlay + 1;
        Check(!LicenceRules.CanReinstate(p, after, 250, 5000, out reason), "reinstated with unpaid fines");
        Check(!LicenceRules.CanReinstate(p, after, 0, 100, out reason), "reinstated without the fee");
        Check(LicenceRules.CanReinstate(p, after, 0, 5000, out reason), "cannot reinstate: " + reason);
        LicenceRules.Reinstate(p, after);
        Check(LicenceRules.Status(p, after) == LicenceStatus.Valid && p.ActivePoints(after) == 0 && p.Reinstatements == 1, "reinstatement");
        DriverProfile valid = NewProfile();
        Check(!LicenceRules.CanReinstate(valid, 0, 0, 5000, out reason), "valid licence reinstated");
    }

    private static void OfficerLineTest()
    {
        DriverProfile p = NewProfile();
        L.Set(UiLanguage.English);
        Check(LicenceRules.OfficerLine(p, 0).Contains("Valid"), "valid line");
        p.AddPoints(12, "x", 0);
        string sus = LicenceRules.OfficerLine(p, 1);
        Check(sus.Contains("SUSPENDED") && !sus.Contains("Valid"), "suspended licence reported as valid: " + sus);
        LicenceRules.Revoke(p, 2, "test");
        string rev = LicenceRules.OfficerLine(p, 3);
        Check(rev.Contains("REVOKED") && !rev.Contains("Valid"), "revoked licence reported as valid: " + rev);
        L.Set(UiLanguage.Russian);
        string ru = LicenceRules.OfficerLine(p, 3);
        Check(ru.Contains("ОТОЗВАНЫ") && !ru.Contains("Действительны"), "russian revoked line: " + ru);
        Check(LicenceRules.StatusName(LicenceStatus.Revoked) != LicenceRules.StatusName(LicenceStatus.Suspended), "statuses look the same");
        L.Set(UiLanguage.English);
    }

    private static void SaveMigration7()
    {
        RlsPaths p = NewPaths();
        SaveData d6 = SaveData.CreateNew("0.5.1-passenger");
        d6.SchemaVersion = 6;
        DriverProfile f = d6.Profile("Franklin Clinton", new Random(3));
        f.AddPoints(6, "Collision", 10);
        f.StatusReason = null;
        d6.InsidePropertyId = null;
        d6.Properties.Add(new OwnedProperty { Id = "IntegrityWay", Owner = "Franklin Clinton", RentedOut = true });
        File.WriteAllBytes(p.SaveFile, SaveManager.Serialize(d6));

        SaveManager s = new SaveManager(p, "test");
        s.Load();
        Check(s.Status == "Loaded", "schema 6 not loaded: " + s.Status);
        DriverProfile lp = s.Data.Profile("Franklin Clinton", new Random(3));
        Check(lp.Points.Count == 1 && lp.StatusReason == "" && s.Data.InsidePropertyId == "", "migration defaults");
        Check(s.Data.Properties.Count == 1 && s.Data.Properties[0].RentedOut, "properties lost");

        LicenceRules.Revoke(lp, 50, "test revoke");
        s.Data.InsidePropertyId = "IntegrityWay";
        Check(s.Save(), "save failed " + s.LastError);
        SaveManager s2 = new SaveManager(p, "test");
        s2.Load();
        DriverProfile rp = s2.Data.Profile("Franklin Clinton", new Random(3));
        Check(s2.Data.SchemaVersion == 7, "schema " + s2.Data.SchemaVersion);
        Check(LicenceRules.Status(rp, 60) == LicenceStatus.Revoked && rp.StatusReason == "test revoke" && rp.Revocations == 1, "revocation lost on save/load");
        Check(s2.Data.InsidePropertyId == "IntegrityWay", "inside property lost");
    }

    private static void ViolationNamesStable()
    {
        // Names written by 0.2-0.5 must keep parsing (they are stored as text in saves).
        string[] old = { "Speeding", "RedLight", "StopSign", "WrongWay", "NoHeadlights", "Reckless", "LeavingScene",
                         "CausingCollision", "NoDocuments", "SuspendedLicence", "EvadingPolice", "FalseStatement", "FailureToYieldEmergency" };
        for (int i = 0; i < old.Length; i++)
            Check((int)(ViolationType)Enum.Parse(typeof(ViolationType), old[i]) == i, "enum order changed for " + old[i]);
        L.Set(UiLanguage.English);
        Check(TrafficRules.NameFromString("HitPedestrian") == "Striking a pedestrian", "new name");
        Check(TrafficRules.NameFromString("SomethingFromTheFuture") == "SomethingFromTheFuture", "unknown name");
        Check(Enum.IsDefined(typeof(FineMode), "Witnessed") && (int)FineMode.Off == 2, "FineMode order changed");
    }

    private static void SpeedLimiterTest()
    {
        int k;
        string e;
        Check(SpeedLimiterRules.TryParse("60", out k, out e) && k == 60, "60");
        Check(SpeedLimiterRules.TryParse(" 90 km/h ", out k, out e) && k == 90, "unit en");
        Check(SpeedLimiterRules.TryParse("110 км/ч", out k, out e) && k == 110, "unit ru");
        foreach (string bad in new[] { "", "   ", "abc", "-20", "5", "301", "60.5", "6O", "99999", null })
            Check(!SpeedLimiterRules.TryParse(bad, out k, out e) && e.Length > 0, "accepted '" + bad + "'");
        Check(SpeedLimiterRules.TryParse("10", out k, out e) && SpeedLimiterRules.TryParse("300", out k, out e), "bounds");
        // Units: 60 km/h = 16.67 m/s; the cap adds 1.5 km/h of headroom.
        Check(Math.Abs(SpeedLimiterRules.CapMetersPerSecond(60) - 61.5f / 3.6f) < 0.001f, "cap units");
        Check(Math.Abs(SpeedLimiterRules.ToKmh(16.6667f) - 60f) < 0.01f, "m/s to km/h");
        Check(!SpeedLimiterRules.CutThrottle(55f, 60) && SpeedLimiterRules.CutThrottle(59.2f, 60), "throttle cut point");
        Check(SpeedLimiterRules.Clamp(2) == 10 && SpeedLimiterRules.Clamp(999) == 300 && SpeedLimiterRules.Clamp(75) == 75, "clamp");
        Check(SpeedLimiterRules.Supports(0) && SpeedLimiterRules.Supports(8) && SpeedLimiterRules.Supports(14) && SpeedLimiterRules.Supports(13), "cars, bikes, boats, cycles");
        Check(!SpeedLimiterRules.Supports(15) && !SpeedLimiterRules.Supports(16) && !SpeedLimiterRules.Supports(21), "aircraft / trains");
    }

    private static void PatrolBudgetTest()
    {
        foreach (PerformanceProfileKind perf in (PerformanceProfileKind[])Enum.GetValues(typeof(PerformanceProfileKind)))
        {
            Check(PatrolRules.ExtraUnitsCap(PoliceDensity.Off, perf) == 0, "off has units");
            Check(!PatrolRules.ShouldSpawn(PoliceDensity.Off, perf, true, 0, 0), "off spawns");
            foreach (PoliceDensity d in (PoliceDensity[])Enum.GetValues(typeof(PoliceDensity)))
            {
                int cap = PatrolRules.ExtraUnitsCap(d, perf);
                Check(cap <= 4, "cap too high");
                if (perf == PerformanceProfileKind.Low) Check(cap <= 2, "Low profile allows " + cap);
                Check(!PatrolRules.ShouldSpawn(d, perf, true, 0, cap), "spawns beyond the cap");
            }
        }
        // Enough ambient police already: no extra unit (no duplication).
        Check(!PatrolRules.ShouldSpawn(PoliceDensity.Normal, PerformanceProfileKind.Balanced, true, 2, 0), "duplicates ambient police");
        Check(PatrolRules.ShouldSpawn(PoliceDensity.Normal, PerformanceProfileKind.Balanced, true, 0, 0), "never spawns");
        Check(PatrolRules.DesiredNearby(PoliceDensity.High, false) < PatrolRules.DesiredNearby(PoliceDensity.High, true), "countryside as busy as city");
        // Behaviours: deterministic and varied; no foot patrols at night or in the countryside.
        Dictionary<PatrolBehavior, int> seen = new Dictionary<PatrolBehavior, int>();
        for (int i = 0; i < 400; i++)
        {
            PatrolBehavior b = PatrolRules.PickBehavior(i, true, false, false, PoliceDensity.High);
            Check(b == PatrolRules.PickBehavior(i, true, false, false, PoliceDensity.High), "not deterministic");
            int n; seen.TryGetValue(b, out n); seen[b] = n + 1;
            Check(PatrolRules.PickBehavior(i, true, false, true, PoliceDensity.High) != PatrolBehavior.FootPatrol, "foot patrol at night");
            Check(PatrolRules.PickBehavior(i, false, false, false, PoliceDensity.High) != PatrolBehavior.FootPatrol, "foot patrol in the countryside");
        }
        Check(seen.Count == 4, "not all behaviours occur: " + seen.Count);
    }

    private static void TrafficLightTest()
    {
        Check(TrafficLightRules.Axis(1f) == LightAxis.Same && TrafficLightRules.Axis(-0.95f) == LightAxis.Same, "same axis (both faces)");
        Check(TrafficLightRules.Axis(0.05f) == LightAxis.Cross && TrafficLightRules.Axis(-0.3f) == LightAxis.Cross, "cross axis");
        Check(TrafficLightRules.Axis(0.55f) == LightAxis.Unknown, "diagonal signal must be left alone");
        Check(!TrafficLightRules.ShouldAssist(true, 10000, 20, true, false), "assisted too early");
        Check(TrafficLightRules.ShouldAssist(true, 20000, 20, true, false), "no assist after the wait");
        Check(!TrafficLightRules.ShouldAssist(true, 60000, 20, false, false), "assisted while not first in line");
        Check(!TrafficLightRules.ShouldAssist(true, 60000, 20, true, true), "assisted with a siren nearby");
        Check(!TrafficLightRules.ShouldAssist(false, 60000, 20, true, false), "assisted while disabled");
        Check(!TrafficLightRules.ShouldAssist(true, 4000, 1, true, false), "minimum wait not enforced");
    }

    private static void PedestrianTest()
    {
        TrafficRules.Fines = new FineTable();
        CollisionEvidence e = new CollisionEvidence();
        e.PedestrianStruck = true;
        e.PlayerSpeedKmh = 40;
        Check(PoliceRules.DetermineFault(e) == Fault.Player, "pedestrian at 40 km/h");
        e.PlayerSpeedKmh = 10;
        Check(PoliceRules.DetermineFault(e) == Fault.Shared, "pedestrian walking into a slow car");
        Check(PoliceRules.PedestrianFine(AccidentSeverity.Moderate, Fault.Player, false) == 800, "moderate");
        Check(PoliceRules.PedestrianFine(AccidentSeverity.Severe, Fault.Player, false) == 1600, "severe");
        Check(PoliceRules.PedestrianFine(AccidentSeverity.Minor, Fault.Shared, false) == 200, "minor shared");
        Check(PoliceRules.PedestrianFine(AccidentSeverity.Severe, Fault.Player, true) == 2400, "fatal");
        Check(PoliceRules.PedestrianFine(AccidentSeverity.Severe, Fault.Other, true) == 0, "not at fault");
        Check(PoliceRules.PointsFor(ViolationType.HitPedestrian, 0) == 6, "points");
        Check(PoliceRules.Severity(30, 0, true, false) == AccidentSeverity.Moderate, "an injured pedestrian is at least moderate");
    }

    private static void LimiterKeyTest()
    {
        RlsPaths p = NewPaths();
        File.WriteAllText(p.KeyBindingsFile, "[KeyBindings]\nOpenLifeHub=Oem5\nOpenSettings=\nOpenBank=\nOpenPropertyMenu=\nOpenVehicleMenu=\nCallPolice=\nRequestRoadsideAssistance=\nStartJob=\nPassengerMenu=\nCallTaxi=\n");
        ConfigurationManager c = new ConfigurationManager(p);
        c.LoadAll();
        Check(c.Bindings.Get(RlsAction.ToggleSpeedLimiter) == Keys.None, "limiter key assigned by default");
        Check(c.Bindings.Get(RlsAction.OpenLifeHub) == Keys.Oem5 && c.Bindings.AssignedCount == 1, "existing key lost");
        string text = File.ReadAllText(p.KeyBindingsFile);
        Check(text.Contains("ToggleSpeedLimiter=\r\n") || text.Contains("ToggleSpeedLimiter=\n"), "limiter entry not written empty");
    }
}
