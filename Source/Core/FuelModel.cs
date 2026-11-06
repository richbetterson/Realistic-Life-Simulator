// Realistic Life Simulator - fuel consumption model (no game dependencies).

using System;
using System.Collections.Generic;

namespace RealisticLifeSimulator.Core
{
    public sealed class FuelSpec
    {
        public bool Supported;     // false: bicycles, boats, aircraft, trains
        public bool Electric;
        public float TankLiters;
        public float Per100Km;     // real-world style figure at normal load
        public float IdleLitersPerHour;
    }

    public static class FuelModel
    {
        // Los Santos is far smaller than a real city, so consumption is scaled up so that
        // refuelling actually happens: with multiplier 1.0 a sedan's full tank lasts about
        // 110-120 km of in-game driving (roughly 1-1.5 hours of normal play).
        public const double WorldScale = 6.0;

        // Liters per second at a pump / from a jerry can.
        public const double PumpRate = 2.5;
        public const double JerryCanRate = 1.0;
        public const double JerryCanLiters = 20.0;
        public const int JerryCanFullAmmo = 4500;

        private static readonly HashSet<int> ElectricModels = BuildElectric();

        private static HashSet<int> BuildElectric()
        {
            string[] names = { "voltic", "voltic2", "raiden", "neon", "tezeract", "cyclone", "cyclone2", "khamelion",
                               "surge", "imorgon", "iwagen", "omnisegt", "virtue", "powersurge", "buffalo5",
                               "caddy", "caddy2", "caddy3", "airtug", "rcbandito", "minitank" };
            HashSet<int> set = new HashSet<int>();
            foreach (string n in names) set.Add(Joaat.Hash(n));
            return set;
        }

        // vehicleClass = GTA VehicleClass value (0 Compacts ... 22 Open wheel).
        public static FuelSpec ForVehicle(int vehicleClass, int modelHash)
        {
            FuelSpec s = new FuelSpec();
            s.Supported = true;
            switch (vehicleClass)
            {
                case 0: Set(s, 45, 7.0f, 0.7f); break;     // Compacts
                case 1: Set(s, 60, 8.5f, 0.8f); break;     // Sedans
                case 2: Set(s, 75, 12f, 1.1f); break;      // SUVs
                case 3: Set(s, 55, 9.5f, 0.9f); break;     // Coupes
                case 4: Set(s, 70, 14f, 1.3f); break;      // Muscle
                case 5: Set(s, 65, 13f, 1.2f); break;      // Sports classics
                case 6: Set(s, 65, 12f, 1.1f); break;      // Sports
                case 7: Set(s, 75, 16f, 1.4f); break;      // Super
                case 8: Set(s, 16, 5f, 0.4f); break;       // Motorcycles
                case 9: Set(s, 80, 14f, 1.2f); break;      // Off-road
                case 10: Set(s, 200, 35f, 2.5f); break;    // Industrial
                case 11: Set(s, 120, 20f, 1.8f); break;    // Utility
                case 12: Set(s, 80, 12f, 1.1f); break;     // Vans
                case 17: Set(s, 150, 25f, 2.2f); break;    // Service (buses, taxis)
                case 18: Set(s, 80, 14f, 1.3f); break;     // Emergency
                case 19: Set(s, 300, 40f, 3.0f); break;    // Military
                case 20: Set(s, 300, 32f, 2.8f); break;    // Commercial (trucks)
                case 22: Set(s, 110, 30f, 1.5f); break;    // Open wheel
                default: s.Supported = false; break;       // 13 cycles, 14 boats, 15/16 aircraft, 21 trains
            }
            if (s.Supported && ElectricModels.Contains(modelHash)) s.Electric = true;
            return s;
        }

        private static void Set(FuelSpec s, float tank, float per100, float idle)
        {
            s.TankLiters = tank;
            s.Per100Km = per100;
            s.IdleLitersPerHour = idle;
        }

        // Liters used over one update step.
        public static double Consumption(FuelSpec spec, double meters, double seconds, float throttle, float rpm,
                                         bool engineRunning, float multiplier)
        {
            if (spec == null || !spec.Supported || spec.Electric || !engineRunning) return 0;
            if (meters < 0) meters = 0;
            if (seconds < 0) seconds = 0;
            double t = Math.Max(0, Math.Min(1, throttle));
            double r = Math.Max(0, Math.Min(1, rpm));
            double load = 0.7 + 0.6 * t + 0.7 * Math.Max(0, r - 0.6) / 0.4; // 0.7 coasting .. 2.0 flat out
            double driving = meters / 100000.0 * spec.Per100Km * load;
            double idle = spec.IdleLitersPerHour * seconds / 3600.0;
            return (driving + idle) * WorldScale * multiplier;
        }

        // Fuel found in a car the player has never driven before: 25-85 %.
        public static double InitialLiters(FuelSpec spec, Random rng)
        {
            return spec.TankLiters * (0.25 + rng.NextDouble() * 0.6);
        }
    }

    // GTA's model-name hash (Jenkins one-at-a-time, lower-case).
    public static class Joaat
    {
        public static int Hash(string text)
        {
            unchecked
            {
                uint h = 0;
                foreach (char c in text.ToLowerInvariant())
                {
                    h += c;
                    h += h << 10;
                    h ^= h >> 6;
                }
                h += h << 3;
                h ^= h >> 11;
                h += h << 15;
                return (int)h;
            }
        }
    }
}
