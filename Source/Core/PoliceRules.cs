// Realistic Life Simulator - police and accident rules (no game dependencies).
// Licence points, warning-or-fine decisions, accident severity and fault.

using System;
using System.Collections.Generic;

namespace RealisticLifeSimulator.Core
{
    public enum AccidentSeverity { Minor, Moderate, Severe }

    public enum Fault { Undetermined, Player, Other, Shared }

    public enum Statement { None, OtherAtFault, MyFault, DontKnow }

    // Facts the investigating officer works from. Collected at the moment of impact.
    public sealed class CollisionEvidence
    {
        public double PlayerSpeedKmh;   // just before impact
        public double OtherSpeedKmh;
        public double OtherAhead;       // -1..1: +1 = other vehicle was straight ahead of the player
        public bool PlayerViolationJustBefore;  // speeding / red light / wrong way in the last 10 s
        public bool OtherWasStopped;
        public bool PedestrianStruck;
    }

    public static class PoliceRules
    {
        public const int SuspensionPoints = 12;
        public const double PointsLifetimeSeconds = 2 * 3600; // points expire after 2 h of play
        public const double StartingReputation = 70;

        public static int PointsFor(ViolationType t, int overKmh)
        {
            switch (t)
            {
                case ViolationType.Speeding: return overKmh > 40 ? 6 : overKmh > 20 ? 4 : 2;
                case ViolationType.RedLight: return 3;
                case ViolationType.StopSign: return 2;
                case ViolationType.WrongWay: return 4;
                case ViolationType.NoHeadlights: return 1;
                case ViolationType.Reckless: return 8;
                case ViolationType.LeavingScene: return 6;
                case ViolationType.CausingCollision: return 3;
                case ViolationType.NoDocuments: return 0;
                case ViolationType.SuspendedLicence: return 0;
                case ViolationType.EvadingPolice: return 6;
                case ViolationType.FalseStatement: return 2;
                case ViolationType.FailureToYieldEmergency: return 3;
                case ViolationType.HitPedestrian: return 6;
                case ViolationType.DrivingWhileRevoked: return 0;
            }
            return 0;
        }

        // Probability (0..1) that an officer lets the driver go with a warning.
        // attitude: -2 (hostile) .. +2 (honest and polite).
        public static double WarningChance(double reputation, int attitude, ViolationType t, int overKmh,
                                           bool unpaidFines, int stopsLastHour)
        {
            if (t == ViolationType.Reckless || t == ViolationType.EvadingPolice || t == ViolationType.LeavingScene) return 0;
            if (t == ViolationType.Speeding && overKmh > 40) return 0;

            double p = 0.30;
            p += (reputation - 50) / 200.0;          // +/- 0.25
            p += attitude * 0.10;
            if (t == ViolationType.RedLight || t == ViolationType.WrongWay) p -= 0.15;
            if (t == ViolationType.Speeding && overKmh > 20) p -= 0.15;
            if (t == ViolationType.NoHeadlights || t == ViolationType.StopSign) p += 0.10;
            if (unpaidFines) p -= 0.20;
            p -= 0.15 * Math.Max(0, stopsLastHour);
            return Math.Max(0, Math.Min(0.85, p));
        }

        public static AccidentSeverity Severity(double impactKmh, double bodyDamage, bool injury, bool death)
        {
            if (death || impactKmh >= 70 || bodyDamage >= 350) return AccidentSeverity.Severe;
            if (injury || impactKmh >= 35 || bodyDamage >= 120) return AccidentSeverity.Moderate;
            return AccidentSeverity.Minor;
        }

        // The officer's finding, based on physical evidence first.
        public static Fault DetermineFault(CollisionEvidence e)
        {
            if (e.PedestrianStruck) return e.PlayerSpeedKmh > 15 || e.PlayerViolationJustBefore ? Fault.Player : Fault.Shared;
            bool playerMoving = e.PlayerSpeedKmh > 5;
            if (!playerMoving && e.OtherSpeedKmh > 5) return Fault.Other;           // hit while stationary
            if (e.OtherWasStopped && playerMoving) return Fault.Player;             // ran into a stopped car
            if (e.OtherAhead > 0.6 && e.PlayerSpeedKmh > e.OtherSpeedKmh) return Fault.Player; // rear-ended them
            if (e.OtherAhead < -0.6 && e.OtherSpeedKmh > e.PlayerSpeedKmh) return Fault.Other;  // they rear-ended us
            if (e.PlayerViolationJustBefore) return Fault.Player;
            return Fault.Shared;
        }

        // Did the driver's statement contradict clear evidence?
        public static bool IsFalseStatement(Statement s, Fault finding, CollisionEvidence e)
        {
            if (s != Statement.OtherAtFault || finding != Fault.Player) return false;
            return e.OtherWasStopped || e.OtherAhead > 0.6 || e.PedestrianStruck;
        }

        public static double CollisionFine(AccidentSeverity s, Fault f)
        {
            if (f != Fault.Player && f != Fault.Shared) return 0;
            double basis = s == AccidentSeverity.Severe ? 1200 : s == AccidentSeverity.Moderate ? 600 : 250;
            return f == Fault.Shared ? Math.Round(basis / 2) : basis;
        }

        // Fine for striking a pedestrian (0.6.0): base amount by severity, halved for shared fault.
        public static double PedestrianFine(AccidentSeverity sev, Fault f, bool dead)
        {
            if (f != Fault.Player && f != Fault.Shared) return 0;
            double factor = dead ? 3.0 : sev == AccidentSeverity.Severe ? 2.0 : sev == AccidentSeverity.Moderate ? 1.0 : 0.5;
            double fine = TrafficRules.BaseFine(ViolationType.HitPedestrian, 0) * factor;
            return f == Fault.Shared ? Math.Round(fine / 2) : fine;
        }

        // Rough repair estimate from GTA body / engine health (0-1000 each).
        public static double DamageEstimate(double bodyHealth, double engineHealth, double vehicleValueFactor)
        {
            double body = Math.Max(0, 1000 - bodyHealth);
            double engine = Math.Max(0, 1000 - Math.Max(0, engineHealth));
            return Math.Round((body * 4.0 + engine * 6.0) * Math.Max(0.5, vehicleValueFactor) / 10.0) * 10.0;
        }

        public static double ReputationAfter(double rep, int points, bool warningOnly)
        {
            rep -= warningOnly ? 1 : points * 2;
            return Math.Max(0, Math.Min(100, rep));
        }

        public static double ReputationForAccident(double rep, AccidentSeverity s, Fault f)
        {
            if (f == Fault.Other) return rep;
            double loss = s == AccidentSeverity.Severe ? 15 : s == AccidentSeverity.Moderate ? 10 : 5;
            if (f == Fault.Shared) loss /= 2;
            return Math.Max(0, rep - loss);
        }

        // Character-specific documents. Deterministic per character so they look stable.
        private static readonly string[] Insurers =
            { "Mors Mutual Insurance", "San Andreas General", "Coastline Auto Insurance", "Vespucci Mutual", "Pacific Shield Insurance" };
        private static readonly string[] FirstNames =
            { "James", "Maria", "David", "Linda", "Carlos", "Susan", "Kevin", "Angela", "Marcus", "Rosa",
              "Brian", "Tanya", "Luis", "Karen", "Andre", "Emily", "Victor", "Grace", "Hector", "Nina" };
        private static readonly string[] LastNames =
            { "Ramirez", "Johnson", "Nguyen", "Walker", "Morales", "Bennett", "Kim", "Lopez", "Carter", "Price",
              "Hughes", "Ortiz", "Fischer", "Reed", "Alvarez", "Shaw", "Patel", "Coleman", "Diaz", "Turner" };

        public static string RandomName(Random r)
        {
            return FirstNames[r.Next(FirstNames.Length)] + " " + LastNames[r.Next(LastNames.Length)];
        }

        public static string RandomInsurer(Random r) { return Insurers[r.Next(Insurers.Length)]; }

        public static string RandomPolicy(Random r)
        {
            return "SA-" + r.Next(100, 999) + "-" + r.Next(100000, 999999);
        }

        public static string LicenceNumber(Random r)
        {
            return "D" + r.Next(1000000, 9999999);
        }

        public static string ReportId(Random r)
        {
            return "LSPD-" + DateTime.Now.ToString("yyMMdd") + "-" + r.Next(1000, 9999);
        }

        public static int Badge(Random r) { return r.Next(1000, 9999); }

        public static string SeverityName(AccidentSeverity s)
        {
            switch (s)
            {
                case AccidentSeverity.Severe: return L.S("severe", "тяжёлое");
                case AccidentSeverity.Moderate: return L.S("moderate", "среднее");
            }
            return L.S("minor", "лёгкое");
        }

        public static string FaultName(Fault f)
        {
            switch (f)
            {
                case Fault.Player: return L.S("You", "Вы");
                case Fault.Other: return L.S("Other driver", "Другой водитель");
                case Fault.Shared: return L.S("Shared", "Обоюдная");
            }
            return L.S("Not determined", "Не установлена");
        }
    }

    // Per-character licence, insurance and reputation.
    public class DriverProfile
    {
        public string Character;
        public string LicenceNumber;
        public string IssuedUtc;
        public string Insurer;
        public string PolicyNumber;
        public double Reputation;
        public int Stops;
        public int Warnings;
        public double CleanKm;            // distance since the last violation (reputation recovery)
        public List<PointEntry> Points;

        // 0.6.0 licence status (see LicenceRules). Play-time seconds; 0 = not set.
        public double SuspendedUntilPlay;
        public double RevokedAtPlay;
        public double RevocationEndsPlay;
        public int Suspensions;
        public int Revocations;
        public int Reinstatements;
        public int CaughtWhileSuspended;
        public string StatusReason;

        public DriverProfile()
        {
            Points = new List<PointEntry>();
        }

        public int ActivePoints(double playSeconds)
        {
            int sum = 0;
            foreach (PointEntry p in Points) if (p.ExpiresAtPlaySeconds > playSeconds) sum += p.Points;
            return sum;
        }

        public bool IsSuspended(double playSeconds)
        {
            return ActivePoints(playSeconds) >= PoliceRules.SuspensionPoints;
        }

        public void AddPoints(int points, string reason, double playSeconds)
        {
            if (points <= 0) return;
            PointEntry e = new PointEntry();
            e.Points = points;
            e.Reason = reason;
            e.ExpiresAtPlaySeconds = playSeconds + PoliceRules.PointsLifetimeSeconds;
            Points.Add(e);
            Points.RemoveAll(p => p.ExpiresAtPlaySeconds <= playSeconds);
        }

        public static DriverProfile Create(string character, Random r)
        {
            DriverProfile p = new DriverProfile();
            p.Character = character;
            p.LicenceNumber = PoliceRules.LicenceNumber(r);
            p.IssuedUtc = DateTime.UtcNow.AddYears(-r.Next(3, 20)).ToString("o");
            p.Insurer = PoliceRules.RandomInsurer(r);
            p.PolicyNumber = PoliceRules.RandomPolicy(r);
            p.Reputation = PoliceRules.StartingReputation;
            return p;
        }
    }

    public class PointEntry
    {
        public int Points;
        public string Reason;
        public double ExpiresAtPlaySeconds;
    }

    public class AccidentReport
    {
        public string Id;
        public string TimeUtc;
        public string Character;
        public string Location;
        public string Severity;          // AccidentSeverity name
        public string OtherVehicle;
        public string OtherPlate;
        public string OtherDriver;       // name, or empty
        public string OtherInsurer;
        public string OtherPolicy;
        public string OtherDriverState;  // Unhurt, Injured, Dead, Fled, None
        public bool PedestrianStruck;
        public double PlayerSpeedKmh;
        public double OtherSpeedKmh;
        public double PlayerDamageEstimate;
        public bool InfoExchanged;
        public bool PoliceAttended;
        public bool AmbulanceAttended;
        public bool EmergencyCalledByOther;
        public string Statement;         // Statement name
        public string Fault;             // Fault name
        public string Officer;
        public string Findings;
        public double Fine;
        public bool FinePaid;
        public bool LeftScene;
        public bool Closed;
    }
}
