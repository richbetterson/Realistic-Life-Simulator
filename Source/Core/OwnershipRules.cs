// Realistic Life Simulator - properties, owned vehicles, wear, insurance (no game dependencies).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RealisticLifeSimulator.Core
{
    public enum PropertyType { Apartment, House, Garage }

    public enum InsuranceLevel { None, Basic, Full }

    public enum VehicleState { Out, Stored, Destroyed, Sold }

    // ------------------------------------------------------------ data kept in the save

    public class OwnedProperty
    {
        public string Id;
        public string Owner;          // character
        public string PurchasedUtc;
        public double PricePaid;
        public bool RentedOut;
    }

    public class OwnedVehicle
    {
        public string Id;
        public string Owner;
        public int ModelHash;
        public string ModelName;      // catalog name, or "" for registered vehicles
        public string DisplayName;
        public string Plate;
        public double Value;          // reference value (purchase price or estimate)
        public double PricePaid;
        public string AcquiredUtc;
        public bool Managed;          // GTA story personal vehicle: GTA spawns it, we only track it
        public VehicleState State;
        public string GarageId;
        public InsuranceLevel Insurance;
        public double MileageKm;
        public double KmAtService;
        public double Condition;      // engine condition 0..100
        public float BodyHealth;
        public float EngineHealth;
        public double FuelLiters;
        public float LastX, LastY, LastZ, LastHeading;
        public VehicleLook Look;
        public List<ServiceEntry> Services;
        public List<DamageEntry> Damage;

        public OwnedVehicle()
        {
            Services = new List<ServiceEntry>();
            Damage = new List<DamageEntry>();
            Look = new VehicleLook();
        }
    }

    public class VehicleLook
    {
        public int Primary = -1, Secondary = -1, Pearl = -1, WheelColor = -1, WheelType = -1, WindowTint = -1, Livery = -1;
        public bool Turbo, Xenon;
        public int[] Mods;            // index per mod type 0..49, -1 = stock
    }

    public class ServiceEntry
    {
        public string TimeUtc;
        public double Km;
        public string Kind;           // "Service", "Repair", "Inspection"
        public double Cost;
        public double InsurancePaid;
    }

    public class DamageEntry
    {
        public string TimeUtc;
        public double Km;
        public string Location;
        public double Estimate;
    }

    // ------------------------------------------------------------ data files

    public sealed class PropertyDef
    {
        public string Id, Name, Interior;   // Interior: LowApt, MidApt, HighApt, Garage2, Garage6, Garage10, None
        public PropertyType Type;
        public double Price, Maintenance, Rent;
        public int Luxury, GarageSlots;
        public float X, Y, Z;
    }

    public sealed class CatalogEntry
    {
        public string Model, Name;
        public int Class;
        public double Price;
    }

    public static class OwnershipRules
    {
        public const double SellPropertyFactor = 0.85;     // agent and transfer fees
        public const double RecoveryFee = 250;
        public const double RegistrationFee = 150;
        public const double InspectionFee = 50;

        // Engine condition lost per km. Overdue service wears three times faster.
        public static double WearPerKm(double kmSinceService, double serviceIntervalKm, double rate)
        {
            double w = 0.02 * rate;
            if (kmSinceService > serviceIntervalKm) w *= 3;
            return w;
        }

        // Condition that a service can restore: very high mileage leaves some permanent wear.
        public static double ServiceCeiling(double mileageKm)
        {
            return 100 - Math.Min(30, mileageKm / 2000.0);
        }

        // Effects of a worn engine.
        public static double TopSpeedPenaltyPercent(double condition)
        {
            if (condition >= 50) return 0;
            return Math.Min(20, (50 - condition) * 0.4);
        }

        public static double FuelPenaltyFactor(double condition)
        {
            return condition < 25 ? 1.2 : 1.0;
        }

        public static double ClassFactor(int vehicleClass)
        {
            switch (vehicleClass)
            {
                case 7: return 4.0;                    // super
                case 6: case 5: return 2.5;            // sports, sports classics
                case 3: case 4: case 2: return 1.5;    // coupes, muscle, SUVs
                case 8: return 0.6;                    // motorcycles
                case 10: case 20: case 19: return 2.0; // industrial, commercial, military
            }
            return 1.0;
        }

        public static double ServiceCost(int vehicleClass)
        {
            return Math.Round(180 * ClassFactor(vehicleClass));
        }

        // What the owner pays for a repair and what the insurer pays.
        public static void SplitRepairCost(double cost, InsuranceLevel ins, out double owner, out double insurer)
        {
            double deductible = ins == InsuranceLevel.Full ? 250 : 500;
            double share = ins == InsuranceLevel.Full ? 0.8 : ins == InsuranceLevel.Basic ? 0.5 : 0;
            if (share <= 0 || cost <= deductible)
            {
                owner = cost;
                insurer = 0;
                return;
            }
            insurer = Math.Round((cost - deductible) * share);
            owner = cost - insurer;
        }

        public static double Premium(double value, InsuranceLevel ins)
        {
            if (ins == InsuranceLevel.None) return 0;
            return Math.Max(25, Math.Round(value * (ins == InsuranceLevel.Full ? 0.009 : 0.004)));
        }

        public static double TotalLossPayout(double value, InsuranceLevel ins, double condition)
        {
            double share = ins == InsuranceLevel.Full ? 0.8 : ins == InsuranceLevel.Basic ? 0.4 : 0;
            return Math.Round(CurrentValue(value, 0, condition) * share);
        }

        // Market value: depreciation by mileage and engine condition.
        public static double CurrentValue(double value, double mileageKm, double condition)
        {
            double age = Math.Max(0.35, 1.0 - mileageKm / 20000.0);
            double cond = 0.6 + 0.4 * Math.Max(0, Math.Min(100, condition)) / 100.0;
            return Math.Round(value * 0.75 * age * cond);
        }

        public static double EstimateValue(int vehicleClass)
        {
            switch (vehicleClass)
            {
                case 0: return 15000;
                case 1: return 25000;
                case 2: return 45000;
                case 3: return 60000;
                case 4: return 40000;
                case 5: return 120000;
                case 6: return 150000;
                case 7: return 600000;
                case 8: return 12000;
                case 9: return 40000;
                case 12: return 28000;
            }
            return 30000;
        }

        // Billing for one cycle: maintenance and premiums out, rent in.
        public static double CycleBalance(IEnumerable<OwnedProperty> props, Func<string, PropertyDef> def,
                                          IEnumerable<OwnedVehicle> vehicles, string owner,
                                          out double maintenance, out double rent, out double premiums)
        {
            maintenance = 0; rent = 0; premiums = 0;
            foreach (OwnedProperty p in props)
            {
                if (p.Owner != owner) continue;
                PropertyDef d = def(p.Id);
                if (d == null) continue;
                maintenance += d.Maintenance;
                if (p.RentedOut) rent += d.Rent;
            }
            foreach (OwnedVehicle v in vehicles)
            {
                if (v.Owner != owner || v.State == VehicleState.Destroyed || v.State == VehicleState.Sold) continue;
                premiums += Premium(v.Value, v.Insurance);
            }
            return rent - maintenance - premiums;
        }

        public static string NewId(Random r)
        {
            return DateTime.UtcNow.ToString("yyMMddHHmmss") + r.Next(100, 999);
        }

        // ------------------------------------------------------------ data files

        private static bool F(string s, out float f)
        {
            return float.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out f);
        }

        private static bool D(string s, out double d)
        {
            return double.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        }

        public static List<PropertyDef> ParseProperties(IniFile ini, List<string> problems)
        {
            List<PropertyDef> list = new List<PropertyDef>();
            foreach (string id in ini.SectionNames)
            {
                if (id.Length == 0) continue;
                PropertyDef p = new PropertyDef();
                p.Id = id;
                p.Name = ini.Get(id, "Name", id);
                p.Interior = ini.Get(id, "Interior", "None");
                PropertyType t;
                try { t = (PropertyType)Enum.Parse(typeof(PropertyType), ini.Get(id, "Type", "House"), true); }
                catch (Exception) { problems.Add(id + ": bad Type"); continue; }
                p.Type = t;
                int lux, slots;
                if (!D(ini.Get(id, "Price", ""), out p.Price) || p.Price <= 0 || !F(ini.Get(id, "X", ""), out p.X)
                    || !F(ini.Get(id, "Y", ""), out p.Y) || !F(ini.Get(id, "Z", ""), out p.Z))
                {
                    problems.Add(id + ": missing price or coordinates");
                    continue;
                }
                D(ini.Get(id, "Maintenance", "0"), out p.Maintenance);
                D(ini.Get(id, "Rent", "0"), out p.Rent);
                p.Luxury = int.TryParse(ini.Get(id, "Luxury", "1"), out lux) ? Math.Max(1, Math.Min(5, lux)) : 1;
                p.GarageSlots = int.TryParse(ini.Get(id, "GarageSlots", "0"), out slots) ? Math.Max(0, Math.Min(10, slots)) : 0;
                if (!string.Equals(ini.Get(id, "Enabled", "true"), "true", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(p);
            }
            return list;
        }

        public static List<CatalogEntry> ParseCatalog(IniFile ini)
        {
            List<CatalogEntry> list = new List<CatalogEntry>();
            foreach (KeyValuePair<string, string> kv in ini.GetSection("Vehicles"))
            {
                // model=Display name;class;price
                string[] parts = kv.Value.Split(';');
                double price;
                int cls;
                if (parts.Length < 3 || !int.TryParse(parts[1].Trim(), out cls) || !D(parts[2].Trim(), out price) || price <= 0) continue;
                CatalogEntry c = new CatalogEntry();
                c.Model = kv.Key.Trim().ToLowerInvariant();
                c.Name = parts[0].Trim();
                c.Class = cls;
                c.Price = price;
                list.Add(c);
            }
            list.Sort((a, b) => a.Price.CompareTo(b.Price));
            return list;
        }

        public static string DefaultPropertiesIni()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; Realistic Life Simulator - properties for sale / недвижимость.");
            sb.AppendLine("; Type: Apartment | House | Garage. Interior: LowApt, MidApt, HighApt, Garage2, Garage6, Garage10, None.");
            sb.AppendLine("; X/Y/Z = front door or garage entrance (approximate - adjust if a marker is misplaced).");
            sb.AppendLine("; Maintenance and Rent are per billing cycle (Settings: BillingIntervalMinutes).");
            sb.AppendLine();
            P(sb, "AltaSt", "3 Alta Street, Apt 10", "Apartment", 160000, 3, 0, 350, 800, "MidApt", -268.0f, -957.4f, 31.2f);
            P(sb, "WeazelPlaza", "Weazel Plaza, Apt 26", "Apartment", 290000, 3, 0, 500, 1300, "MidApt", -913.9f, -455.3f, 39.6f);
            P(sb, "IntegrityWay", "4 Integrity Way, Apt 28", "Apartment", 390000, 4, 2, 800, 1900, "HighApt", -47.2f, -585.9f, 37.9f);
            P(sb, "RichardsMajestic", "Richards Majestic, Apt 2", "Apartment", 410000, 4, 2, 850, 2000, "HighApt", -936.3f, -378.7f, 38.9f);
            P(sb, "DelPerroHeights", "Del Perro Heights, Apt 7", "Apartment", 420000, 4, 2, 850, 2100, "HighApt", -1443.6f, -545.0f, 34.7f);
            P(sb, "TinselTowers", "Tinsel Towers, Apt 42", "Apartment", 450000, 5, 2, 950, 2300, "HighApt", -618.6f, 37.9f, 43.6f);
            P(sb, "EclipseTowers", "Eclipse Towers, Apt 31", "Apartment", 480000, 5, 3, 1000, 2500, "HighApt", -773.5f, 309.8f, 85.7f);
            P(sb, "MiltonRd", "2117 Milton Road", "House", 780000, 4, 3, 1500, 3600, "HighApt", -559.4f, 664.3f, 145.4f);
            P(sb, "NConker2044", "2044 North Conker Avenue", "House", 850000, 5, 3, 1600, 3900, "HighApt", 346.4f, 440.6f, 147.7f);
            P(sb, "NConker2045", "2045 North Conker Avenue", "House", 900000, 5, 3, 1700, 4100, "HighApt", 373.0f, 427.9f, 145.7f);
            P(sb, "WildOats", "3655 Wild Oats Drive", "House", 950000, 5, 4, 1800, 4300, "HighApt", -174.4f, 502.6f, 137.4f);
            P(sb, "Whispymound", "3677 Whispymound Drive", "House", 1050000, 5, 4, 1900, 4700, "HighApt", 119.3f, 564.2f, 183.9f);
            P(sb, "Hillcrest2874", "2874 Hillcrest Avenue", "House", 1150000, 5, 4, 2000, 5000, "HighApt", -853.2f, 695.4f, 148.8f);
            P(sb, "Hillcrest2866", "2866 Hillcrest Avenue", "House", 1200000, 5, 4, 2100, 5200, "HighApt", -743.0f, 617.4f, 142.9f);
            P(sb, "GarageLegion", "Legion Square Parking, Unit 6", "Garage", 80000, 2, 6, 150, 450, "Garage6", 215.0f, -810.0f, 30.7f);
            P(sb, "GarageDavis", "Davis Lock-up, Grove Street", "Garage", 35000, 1, 2, 60, 200, "Garage2", -60.0f, -1740.0f, 29.3f);
            P(sb, "GarageSandy", "Sandy Shores Lock-up", "Garage", 25000, 1, 2, 40, 120, "Garage2", 1990.0f, 3780.0f, 32.2f);
            P(sb, "GaragePaleto", "Paleto Bay Workshop", "Garage", 45000, 2, 6, 80, 250, "Garage6", 165.0f, 6590.0f, 31.9f);
            return sb.ToString();
        }

        private static void P(StringBuilder sb, string id, string name, string type, int price, int lux, int slots,
                              int maint, int rent, string interior, float x, float y, float z)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            sb.AppendLine("[" + id + "]");
            sb.AppendLine("Name=" + name);
            sb.AppendLine("Type=" + type);
            sb.AppendLine("Price=" + price);
            sb.AppendLine("Luxury=" + lux);
            sb.AppendLine("GarageSlots=" + slots);
            sb.AppendLine("Maintenance=" + maint);
            sb.AppendLine("Rent=" + rent);
            sb.AppendLine("Interior=" + interior);
            sb.AppendLine("X=" + x.ToString("0.0", inv));
            sb.AppendLine("Y=" + y.ToString("0.0", inv));
            sb.AppendLine("Z=" + z.ToString("0.0", inv));
            sb.AppendLine("Enabled=true");
            sb.AppendLine();
        }

        public static string DefaultCatalogIni()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; Realistic Life Simulator - dealership catalogue / каталог автосалона.");
            sb.AppendLine("; model=Display name;vehicle class;price   (class: 0 compact,1 sedan,2 SUV,3 coupe,4 muscle,6 sports,7 super,8 motorcycle,9 off-road,12 van)");
            sb.AppendLine();
            sb.AppendLine("[Vehicles]");
            string[] rows =
            {
                "faggio=Pegassi Faggio;8;5000", "pcj=Shitzu PCJ-600;8;9000", "akuma=Dinka Akuma;8;9000", "bati=Pegassi Bati 801;8;15000",
                "asea=Declasse Asea;1;12000", "prairie=Bollokan Prairie;0;12000", "premier=Declasse Premier;1;14000", "blista=Dinka Blista;0;15000",
                "issi2=Weeny Issi;0;18000", "burrito3=Declasse Burrito;12;23000", "fugitive=Cheval Fugitive;1;24000", "speedo=Vapid Speedo;12;25000",
                "seminole=Canis Seminole;2;30000", "gauntlet=Bravado Gauntlet;4;32000", "dominator=Vapid Dominator;4;35000", "buffalo=Bravado Buffalo;6;35000",
                "granger=Declasse Granger;2;35000", "surge=Cheval Surge;1;38000", "sandking=Vapid Sandking XL;9;45000", "tailgater=Obey Tailgater;1;55000",
                "sentinel=Ubermacht Sentinel;3;60000", "cavalcade=Albany Cavalcade;2;60000", "schafter2=Benefactor Schafter;1;65000", "oracle=Ubermacht Oracle;3;80000",
                "baller=Gallivanter Baller;2;90000", "felon=Lampadati Felon;3;90000", "elegy2=Annis Elegy RH8;6;95000", "comet2=Pfister Comet;6;100000",
                "feltzer2=Benefactor Feltzer;6;145000", "voltic=Coil Voltic;6;150000", "carbonizzare=Grotti Carbonizzare;6;195000", "jester=Dinka Jester;6;240000",
                "infernus=Pegassi Infernus;7;440000", "zentorno=Pegassi Zentorno;7;725000", "entityxf=Overflod Entity XF;7;795000", "adder=Truffade Adder;7;1000000"
            };
            foreach (string r in rows) sb.AppendLine(r);
            return sb.ToString();
        }
    }
}
