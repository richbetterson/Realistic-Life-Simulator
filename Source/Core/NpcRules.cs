// Realistic Life Simulator - NPC driver personalities (no game dependencies).

using System;

namespace RealisticLifeSimulator.Core
{
    public enum DriverPersonality { Careful, Average, Aggressive, Slow, Professional, Distracted }

    public enum NpcMistake { None, RunRedLight, SpeedingBurst, LateBraking }

    public sealed class PersonalityTraits
    {
        public float SpeedFactor;      // multiplier on the road's speed limit
        public float Ability;          // SET_DRIVER_ABILITY 0..1 (reaction / control)
        public float Aggressiveness;   // SET_DRIVER_AGGRESSIVENESS 0..1
        public int DrivingStyle;       // driving-style flags
        public double MistakesPerMinute;
        public double FleeAfterCrash;  // chance to drive off after a minor crash
    }

    public static class NpcRules
    {
        // GTA driving-style flags.
        public const int StopForCars = 1, StopForPeds = 2, SwerveCars = 4, SteerAroundEmpty = 8, SteerAroundPeds = 16,
                         SteerAroundObjects = 32, StopAtLights = 128, UseBlinkers = 256;
        public const int Normal = 786603; // GTA's standard "obey traffic" style (includes StopAtLights)

        public static PersonalityTraits Traits(DriverPersonality p)
        {
            PersonalityTraits t = new PersonalityTraits();
            switch (p)
            {
                case DriverPersonality.Careful:
                    Set(t, 0.92f, 1.0f, 0.10f, Normal | SwerveCars | SteerAroundPeds | UseBlinkers, 0.01, 0.0); break;
                case DriverPersonality.Aggressive:
                    Set(t, 1.20f, 0.75f, 0.90f, Normal | SwerveCars, 0.12, 0.15); break;
                case DriverPersonality.Slow:
                    Set(t, 0.80f, 0.90f, 0.00f, Normal | SwerveCars | SteerAroundPeds | UseBlinkers, 0.01, 0.0); break;
                case DriverPersonality.Professional:
                    Set(t, 0.97f, 1.0f, 0.30f, Normal | SwerveCars | UseBlinkers, 0.005, 0.0); break;
                case DriverPersonality.Distracted:
                    Set(t, 0.97f, 0.45f, 0.50f, Normal, 0.10, 0.05); break;
                default:
                    Set(t, 1.00f, 0.80f, 0.40f, Normal | UseBlinkers, 0.03, 0.05); break;
            }
            return t;
        }

        private static void Set(PersonalityTraits t, float speed, float ability, float aggr, int style, double mistakes, double flee)
        {
            t.SpeedFactor = speed;
            t.Ability = ability;
            t.Aggressiveness = aggr;
            t.DrivingStyle = style;
            t.MistakesPerMinute = mistakes;
            t.FleeAfterCrash = flee;
        }

        // Share of each personality on the road. Commercial / service drivers are mostly professionals.
        public static DriverPersonality Pick(Random r, int vehicleClass)
        {
            bool work = vehicleClass == 10 || vehicleClass == 11 || vehicleClass == 12 || vehicleClass == 17 || vehicleClass == 20;
            double x = r.NextDouble();
            if (work)
            {
                if (x < 0.55) return DriverPersonality.Professional;
                if (x < 0.85) return DriverPersonality.Average;
                return DriverPersonality.Careful;
            }
            bool sporty = vehicleClass == 4 || vehicleClass == 6 || vehicleClass == 7 || vehicleClass == 8;
            double aggr = sporty ? 0.24 : 0.12;
            if (x < 0.20) return DriverPersonality.Careful;
            if (x < 0.20 + aggr) return DriverPersonality.Aggressive;
            if (x < 0.30 + aggr) return DriverPersonality.Slow;
            if (x < 0.36 + aggr) return DriverPersonality.Professional;
            if (x < 0.46 + aggr) return DriverPersonality.Distracted;
            return DriverPersonality.Average;
        }

        // Target speed in m/s for a road limit in km/h. weatherFactor < 1 in bad weather.
        public static float CruiseSpeed(int limitKmh, DriverPersonality p, float weatherFactor)
        {
            if (limitKmh <= 0) limitKmh = 50;
            float kmh = limitKmh * Traits(p).SpeedFactor * Math.Max(0.5f, Math.Min(1f, weatherFactor));
            return Math.Max(4f, kmh / 3.6f);
        }

        // Whether a mistake happens in this step (probability scaled by elapsed time).
        public static NpcMistake RollMistake(Random r, DriverPersonality p, double seconds)
        {
            double chance = Traits(p).MistakesPerMinute * seconds / 60.0;
            if (r.NextDouble() >= chance) return NpcMistake.None;
            switch (p)
            {
                case DriverPersonality.Aggressive: return r.NextDouble() < 0.5 ? NpcMistake.RunRedLight : NpcMistake.SpeedingBurst;
                case DriverPersonality.Distracted: return r.NextDouble() < 0.7 ? NpcMistake.LateBraking : NpcMistake.RunRedLight;
            }
            return NpcMistake.SpeedingBurst;
        }

        public static string Name(DriverPersonality p)
        {
            switch (p)
            {
                case DriverPersonality.Careful: return L.S("Careful", "Аккуратный");
                case DriverPersonality.Aggressive: return L.S("Aggressive", "Агрессивный");
                case DriverPersonality.Slow: return L.S("Slow", "Медленный");
                case DriverPersonality.Professional: return L.S("Professional", "Профессионал");
                case DriverPersonality.Distracted: return L.S("Distracted", "Рассеянный");
            }
            return L.S("Average", "Обычный");
        }
    }

    // Something that happened in the city without the player: NPC accidents and stops.
    public class CityIncident
    {
        public string TimeUtc;
        public string Kind;          // "Accident", "Stop"
        public string Location;
        public string Description;
        public string Outcome;
    }
}
