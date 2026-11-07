// Realistic Life Simulator - speed limiter, traffic-light assist and police patrol budgets
// (no game dependencies, unit tested). 0.6.0.

using System;
using System.Globalization;

namespace RealisticLifeSimulator.Core
{
    // ------------------------------------------------------------ speed limiter

    public static class SpeedLimiterRules
    {
        public const int MinKmh = 10;
        public const int MaxKmh = 300;
        public const int StepKmh = 5;

        // Accepts whole numbers, optionally followed by a unit ("60", "60 km/h", "60 км/ч").
        public static bool TryParse(string input, out int kmh, out string error)
        {
            kmh = 0;
            error = "";
            string s = (input ?? "").Trim().ToLowerInvariant();
            foreach (string unit in new[] { "km/h", "kmh", "kph", "км/ч", "кмч" })
                if (s.EndsWith(unit)) { s = s.Substring(0, s.Length - unit.Length).Trim(); break; }
            if (s.Length == 0) { error = L.S("Please enter a number.", "Введите число."); return false; }
            foreach (char c in s)
                if (c < '0' || c > '9') { error = L.S("Use whole numbers only, for example 60.", "Только целое число, например 60."); return false; }
            if (s.Length > 4 || !int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out kmh))
            {
                error = L.F("Enter a value from {0} to {1} km/h.", "Введите значение от {0} до {1} км/ч.", MinKmh, MaxKmh);
                return false;
            }
            if (kmh < MinKmh || kmh > MaxKmh)
            {
                error = L.F("Enter a value from {0} to {1} km/h.", "Введите значение от {0} до {1} км/ч.", MinKmh, MaxKmh);
                return false;
            }
            return true;
        }

        public static int Clamp(int kmh) { return Math.Max(MinKmh, Math.Min(MaxKmh, kmh)); }

        // Helicopters, planes and trains are not limited (their throttle works differently).
        public static bool Supports(int vehicleClass)
        {
            return vehicleClass != 15 && vehicleClass != 16 && vehicleClass != 21;
        }

        // GTA works in metres per second. A little headroom avoids fighting the throttle cut.
        public static float CapMetersPerSecond(int kmh) { return (kmh + 1.5f) / 3.6f; }

        public static float ToKmh(float metersPerSecond) { return metersPerSecond * 3.6f; }

        // The accelerator is ignored from just below the limit: smooth, no braking jolt.
        public static bool CutThrottle(float currentKmh, int limitKmh) { return currentKmh >= limitKmh - 1f; }
    }

    // ------------------------------------------------------------ traffic lights

    public enum LightAxis { Same, Cross, Unknown }

    public static class TrafficLightRules
    {
        public static readonly int[] MaxWaitChoices = { 10, 15, 20, 30, 45, 60 };
        public static readonly int[] GreenChoices = { 6, 8, 10, 12, 15 };
        public const int AmberMs = 3000;

        // |dot| of a signal's forward vector with the player's forward vector.
        public static LightAxis Axis(float dot)
        {
            float a = Math.Abs(dot);
            if (a >= 0.75f) return LightAxis.Same;
            if (a <= 0.35f) return LightAxis.Cross;
            return LightAxis.Unknown;
        }

        // Only when the player has been first in line at a red light for too long.
        public static bool ShouldAssist(bool enabled, int waitedMs, int maxWaitSeconds, bool firstInLine, bool emergencyNearby)
        {
            return enabled && firstInLine && !emergencyNearby && waitedMs >= Math.Max(5, maxWaitSeconds) * 1000;
        }
    }

    // ------------------------------------------------------------ police patrols

    public enum PoliceDensity { Off, Low, Normal, High }

    public enum PatrolBehavior { Cruise, SlowPatrol, SpeedTrap, FootPatrol }

    public static class PatrolRules
    {
        // Extra mod-managed patrol units at most (on top of GTA's own police).
        public static int ExtraUnitsCap(PoliceDensity d, PerformanceProfileKind perf)
        {
            int cap;
            switch (d)
            {
                case PoliceDensity.Low: cap = 1; break;
                case PoliceDensity.Normal: cap = 2; break;
                case PoliceDensity.High: cap = 3; break;
                default: return 0;
            }
            if (perf == PerformanceProfileKind.Low) cap = Math.Min(cap, 2);
            if (perf == PerformanceProfileKind.High && d == PoliceDensity.High) cap = 4;
            return cap;
        }

        // Police vehicles we want within ~300 m of the player (GTA's ambient units count too).
        public static int DesiredNearby(PoliceDensity d, bool city)
        {
            int n;
            switch (d)
            {
                case PoliceDensity.Low: n = 1; break;
                case PoliceDensity.Normal: n = 2; break;
                case PoliceDensity.High: n = 3; break;
                default: return 0;
            }
            return city ? n : Math.Max(1, n - 1); // fewer units in the countryside
        }

        public static bool ShouldSpawn(PoliceDensity d, PerformanceProfileKind perf, bool city, int ambientNearby, int ours)
        {
            if (d == PoliceDensity.Off) return false;
            if (ours >= ExtraUnitsCap(d, perf)) return false;
            return ambientNearby + ours < DesiredNearby(d, city);
        }

        // Deterministic for a seed so behaviour is varied but testable.
        public static PatrolBehavior PickBehavior(int seed, bool city, bool residential, bool night, PoliceDensity d)
        {
            int r = (int)((uint)(seed * 2654435761u) % 100u);
            if (city && !night && d >= PoliceDensity.Normal && r < 12) return PatrolBehavior.FootPatrol;
            if (r < 32) return PatrolBehavior.SpeedTrap;
            if (residential || r < 55) return PatrolBehavior.SlowPatrol;
            return PatrolBehavior.Cruise;
        }

        public static float CruiseSpeed(PatrolBehavior b)
        {
            switch (b)
            {
                case PatrolBehavior.SlowPatrol: return 9f;   // ~32 km/h
                case PatrolBehavior.Cruise: return 14f;      // ~50 km/h
            }
            return 0f;
        }

        public static string BehaviorName(PatrolBehavior b)
        {
            switch (b)
            {
                case PatrolBehavior.SlowPatrol: return L.S("neighbourhood patrol", "патруль по району");
                case PatrolBehavior.SpeedTrap: return L.S("speed check", "контроль скорости");
                case PatrolBehavior.FootPatrol: return L.S("foot patrol", "пеший патруль");
            }
            return L.S("patrol", "патрулирование");
        }

        public static string DensityName(PoliceDensity d)
        {
            switch (d)
            {
                case PoliceDensity.Off: return L.S("Off (GTA only)", "Выкл (только GTA)");
                case PoliceDensity.Low: return L.S("Low", "Низкая");
                case PoliceDensity.High: return L.S("High", "Высокая");
            }
            return L.S("Normal", "Средняя");
        }
    }
}
