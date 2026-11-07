// Realistic Life Simulator - automatic traffic enforcement (no game dependencies).
//
// 0.6.0: until now a violation was only fined when a police officer saw it (FineMode
// Witnessed), so almost every violation ended as "recorded only". The new default mode
// Automatic keeps roadside stops for witnessed violations and adds automatic enforcement
// (traffic cameras and patrol radar) for the violations that can be detected reliably.
// Anything detected with low confidence is recorded but never fined automatically.

using System;
using System.Collections.Generic;

namespace RealisticLifeSimulator.Core
{
    public enum FineDecision { RecordOnly, PoliceStop, Ticket }

    // Which violations may be fined without a witnessing officer (Settings.ini [Enforcement]).
    public sealed class AutoEnforcement
    {
        public bool Speeding = true;
        public bool RedLight = true;
        public bool WrongWay = true;
        public bool FailureToYield = true;
        public bool StopSign = false;     // GTA's give-way data is too unreliable to fine without a witness
        public bool Headlights = false;
        public bool AutoPayFromCash = false;

        public AutoEnforcement Clone() { return (AutoEnforcement)MemberwiseClone(); }
    }

    public static class EnforcementRules
    {
        public static bool AutoEnforced(ViolationType t, AutoEnforcement a)
        {
            if (a == null) return false;
            switch (t)
            {
                case ViolationType.Speeding: return a.Speeding;
                case ViolationType.RedLight: return a.RedLight;
                case ViolationType.WrongWay: return a.WrongWay;
                case ViolationType.FailureToYieldEmergency: return a.FailureToYield;
                case ViolationType.StopSign: return a.StopSign;
                case ViolationType.NoHeadlights: return a.Headlights;
            }
            return false; // reckless, accidents, documents etc. always need an officer
        }

        // confident: the detector is sure (e.g. two or more cars were waiting at the red light).
        public static FineDecision Decide(FineMode mode, ViolationType t, bool witnessed, bool roadsideStops,
                                          AutoEnforcement auto, bool confident)
        {
            switch (mode)
            {
                case FineMode.Off:
                    return FineDecision.RecordOnly;
                case FineMode.Always:
                    return witnessed && roadsideStops ? FineDecision.PoliceStop : FineDecision.Ticket;
                case FineMode.Witnessed:
                    if (!witnessed) return FineDecision.RecordOnly;
                    return roadsideStops ? FineDecision.PoliceStop : FineDecision.Ticket;
            }
            // Automatic
            if (witnessed) return roadsideStops ? FineDecision.PoliceStop : FineDecision.Ticket;
            return confident && AutoEnforced(t, auto) ? FineDecision.Ticket : FineDecision.RecordOnly;
        }

        public static string SourceName(bool witnessed)
        {
            return witnessed ? L.S("police officer", "сотрудник полиции") : L.S("traffic camera", "камера фиксации");
        }

        // Red light: cars beside or behind the player in the same direction were stopped.
        public static bool IsRedLight(int waitingCars, float playerKmh, bool assistGreen)
        {
            return !assistGreen && waitingCars >= 1 && playerKmh > 15f;
        }

        // One waiting car can be parked or turning; a camera fine needs two.
        public static bool RedLightConfident(int waitingCars, float playerKmh)
        {
            return waitingCars >= 2 && playerKmh >= 20f;
        }

        // Stop / give way: never slowed below 12 km/h. Above 65 km/h the player is on a
        // through road whose node merely touches a give-way approach (false positives seen in 0.5).
        public static bool IsStopSign(float minSpeedBefore, float passSpeed)
        {
            return minSpeedBefore > 12f && passSpeed <= 65f;
        }

        // Speeding +50 km/h is one "excessive speeding" violation, not speeding plus reckless driving.
        public const int ExcessiveSpeedKmh = 50;
    }

    // One speeding episode = one violation. The limit must have been stable for a few
    // seconds (after leaving a freeway the old, higher limit applies for a short grace time).
    public sealed class SpeedingMonitor
    {
        public int SustainMs = 3000;
        public int ResetMs = 5000;
        public int GraceMs = 5000;

        private int overSince = -1, underSince = -1, maxOver;
        private bool reported;
        private int limit, previousLimit, limitSince;
        private bool hasLimit;

        public bool IsOver { get; private set; }
        public int MaxOver { get { return maxOver; } }

        public void Reset()
        {
            overSince = -1;
            underSince = -1;
            maxOver = 0;
            reported = false;
            IsOver = false;
        }

        public void ClearLimit()
        {
            hasLimit = false;
            limit = 0;
            previousLimit = 0;
            Reset();
        }

        public int EffectiveLimit(int now)
        {
            if (!hasLimit) return 0;
            if (previousLimit > limit && unchecked(now - limitSince) < GraceMs) return previousLimit;
            return limit;
        }

        // Returns true once per episode, when the violation should be reported.
        public bool Feed(float kmh, int roadLimit, int tolerance, int now, out int overKmh)
        {
            overKmh = 0;
            if (roadLimit <= 0) { ClearLimit(); return false; }
            if (!hasLimit) { hasLimit = true; limit = roadLimit; previousLimit = 0; limitSince = now; }
            else if (roadLimit != limit) { previousLimit = limit; limit = roadLimit; limitSince = now; }

            int eff = EffectiveLimit(now);
            IsOver = kmh > eff + 2;
            if (kmh > eff + Math.Max(0, tolerance))
            {
                underSince = -1;
                if (overSince < 0) { overSince = now; maxOver = 0; }
                maxOver = Math.Max(maxOver, (int)(kmh - eff));
                if (!reported && unchecked(now - overSince) >= SustainMs)
                {
                    reported = true;
                    overKmh = maxOver;
                    return true;
                }
            }
            else if (overSince >= 0)
            {
                if (underSince < 0) underSince = now;
                if (unchecked(now - underSince) > ResetMs) Reset();
            }
            return false;
        }

        public int CurrentLimit { get { return hasLimit ? limit : 0; } }
    }

    // Stops the same violation being fined twice: same type within the same area and time.
    public sealed class ViolationDeduper
    {
        public float CellMeters = 80f;
        private readonly Dictionary<string, int> last = new Dictionary<string, int>();

        public bool Allow(ViolationType t, float x, float y, int now, int windowMs)
        {
            string key = t + ":" + (int)Math.Floor(x / CellMeters) + ":" + (int)Math.Floor(y / CellMeters);
            int when;
            if (last.TryGetValue(key, out when) && unchecked(now - when) < windowMs) return false;
            if (last.Count > 64)
            {
                List<string> old = new List<string>();
                foreach (KeyValuePair<string, int> kv in last) if (unchecked(now - kv.Value) > 600000) old.Add(kv.Key);
                foreach (string k in old) last.Remove(k);
                if (last.Count > 64) last.Clear();
            }
            last[key] = now;
            return true;
        }

        public int Count { get { return last.Count; } }
    }
}
