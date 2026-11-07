// Realistic Life Simulator - vehicle appearance, fade teleports, roadside spots.

using System;
using GTA;
using GTA.Math;
using GTA.Native;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public static class VehicleLooks
    {
        private const ulong GET_VEHICLE_COLOURS = 0xA19435F193E081AC;
        private const ulong SET_VEHICLE_COLOURS = 0x4F1D4BE3A7F24601;
        private const ulong GET_VEHICLE_EXTRA_COLOURS = 0x3BC4245933A166F7;
        private const ulong SET_VEHICLE_EXTRA_COLOURS = 0x2036F561ADD12E33;
        private const ulong SET_VEHICLE_MOD_KIT = 0x1F2AA07F00B3217A;
        private const ulong GET_VEHICLE_MOD = 0x772960298DA26FDB;
        private const ulong SET_VEHICLE_MOD = 0x6AF0636DDEDCB6DD;
        private const ulong IS_TOGGLE_MOD_ON = 0x84B233A8C8FC8AE7;
        private const ulong TOGGLE_VEHICLE_MOD = 0x2A1F4F37F95BAD08;
        private const ulong GET_VEHICLE_WINDOW_TINT = 0x0EE21293DAD47C95;
        private const ulong SET_VEHICLE_WINDOW_TINT = 0x57C51E6BAD752696;
        private const ulong GET_VEHICLE_WHEEL_TYPE = 0xB3ED1BFB4BE636DC;
        private const ulong SET_VEHICLE_WHEEL_TYPE = 0x487EB21CC7295BA1;
        private const ulong GET_VEHICLE_LIVERY = 0x2BB9230590DA5E8A;
        private const ulong SET_VEHICLE_LIVERY = 0x60BF608F1B8CD1B6;
        public const ulong SET_VEHICLE_NUMBER_PLATE_TEXT = 0x95A88F0B409CDA47;
        public const ulong SET_VEHICLE_FIXED = 0x115722B1B9C14C1C;
        public const ulong SET_VEHICLE_DEFORMATION_FIXED = 0x953DA1E1B12C0491;
        public const ulong MODIFY_VEHICLE_TOP_SPEED = 0x93A3996368C94158;
        public const ulong SET_VEHICLE_DIRT_LEVEL = 0x79D3B596FE44EE8B;
        public const ulong SET_VEHICLE_ON_GROUND_PROPERLY = 0x49733E92263139D1;

        public static VehicleLook Read(Vehicle v)
        {
            VehicleLook l = new VehicleLook();
            try
            {
                OutputArgument a = new OutputArgument(), b = new OutputArgument();
                N.Call(GET_VEHICLE_COLOURS, v, a, b);
                l.Primary = a.GetResult<int>();
                l.Secondary = b.GetResult<int>();
                OutputArgument c = new OutputArgument(), d = new OutputArgument();
                N.Call(GET_VEHICLE_EXTRA_COLOURS, v, c, d);
                l.Pearl = c.GetResult<int>();
                l.WheelColor = d.GetResult<int>();
                l.WheelType = N.Call<int>(GET_VEHICLE_WHEEL_TYPE, v);
                l.WindowTint = N.Call<int>(GET_VEHICLE_WINDOW_TINT, v);
                l.Livery = N.Call<int>(GET_VEHICLE_LIVERY, v);
                l.Mods = new int[50];
                for (int i = 0; i < 50; i++) l.Mods[i] = N.Call<int>(GET_VEHICLE_MOD, v, i);
                l.Turbo = N.Call<bool>(IS_TOGGLE_MOD_ON, v, 18);
                l.Xenon = N.Call<bool>(IS_TOGGLE_MOD_ON, v, 22);
            }
            catch (Exception ex) { Log.Warn("Reading vehicle appearance failed: " + ex.Message); }
            return l;
        }

        public static void Apply(Vehicle v, VehicleLook l, string plate)
        {
            if (l == null) return;
            try
            {
                N.Call(SET_VEHICLE_MOD_KIT, v, 0);
                if (l.WheelType >= 0) N.Call(SET_VEHICLE_WHEEL_TYPE, v, l.WheelType);
                if (l.Mods != null)
                    for (int i = 0; i < l.Mods.Length && i < 50; i++)
                        if (l.Mods[i] >= 0) N.Call(SET_VEHICLE_MOD, v, i, l.Mods[i], false);
                N.Call(TOGGLE_VEHICLE_MOD, v, 18, l.Turbo);
                N.Call(TOGGLE_VEHICLE_MOD, v, 22, l.Xenon);
                if (l.Primary >= 0 && l.Secondary >= 0) N.Call(SET_VEHICLE_COLOURS, v, l.Primary, l.Secondary);
                if (l.Pearl >= 0 && l.WheelColor >= 0) N.Call(SET_VEHICLE_EXTRA_COLOURS, v, l.Pearl, l.WheelColor);
                if (l.WindowTint >= 0) N.Call(SET_VEHICLE_WINDOW_TINT, v, l.WindowTint);
                if (l.Livery >= 0) N.Call(SET_VEHICLE_LIVERY, v, l.Livery);
                if (!string.IsNullOrEmpty(plate)) N.Call(SET_VEHICLE_NUMBER_PLATE_TEXT, v, plate);
            }
            catch (Exception ex) { Log.Warn("Applying vehicle appearance failed: " + ex.Message); }
        }

        public static void Repair(Vehicle v)
        {
            N.Call(SET_VEHICLE_FIXED, v);
            N.Call(SET_VEHICLE_DEFORMATION_FIXED, v);
            N.Call(SET_VEHICLE_DIRT_LEVEL, v, 0f);
            v.EngineHealth = 1000f;
            v.BodyHealth = 1000f;
        }
    }

    public static class Transition
    {
        private const ulong DO_SCREEN_FADE_OUT = 0x891B5B39AC6302AF;
        private const ulong DO_SCREEN_FADE_IN = 0xD4E8E24955024033;
        private const ulong IS_SCREEN_FADED_OUT = 0xB16FCE9DDC7BA182;
        private const ulong REQUEST_COLLISION_AT_COORD = 0x07503F7948F491A7;
        private const ulong GET_INTERIOR_FROM_ENTITY = 0x2107BA504071A6BB;
        private const ulong ADD_TO_CLOCK_TIME = 0xD716F30D8C8980E2;

        public static void FadeOut()
        {
            N.Call(DO_SCREEN_FADE_OUT, 500);
            int start = Environment.TickCount;
            while (!N.Call<bool>(IS_SCREEN_FADED_OUT) && unchecked(Environment.TickCount - start) < 1500) Script.Wait(0);
        }

        public static void FadeIn()
        {
            N.Call(DO_SCREEN_FADE_IN, 500);
        }

        // Moves the player (fading the screen). When requireInterior is set and no interior
        // loads at the destination, the player is put back where they were: never stuck.
        public static bool MovePlayer(Vector3 to, float heading, bool requireInterior)
        {
            Ped p = GTA.Game.Player.Character;
            Vector3 from = p.Position;
            float fromHeading = p.Heading;
            FadeOut();
            N.Call(REQUEST_COLLISION_AT_COORD, to.X, to.Y, to.Z);
            p.Position = to;
            p.Heading = heading;
            int start = Environment.TickCount;
            while (unchecked(Environment.TickCount - start) < 1200) Script.Wait(0);
            bool ok = true;
            if (requireInterior && N.Call<int>(GET_INTERIOR_FROM_ENTITY, p) == 0)
            {
                ok = false;
                p.Position = from;
                p.Heading = fromHeading;
                Script.Wait(300);
                Log.Warn(string.Format("Interior not available at {0:0.0},{1:0.0},{2:0.0}; returned the player", to.X, to.Y, to.Z));
            }
            FadeIn();
            return ok;
        }

        public static void AdvanceClock(int hours)
        {
            N.Call(ADD_TO_CLOCK_TIME, hours, 0, 0);
        }

        // ------------------------------------------------------------ interiors (0.6.0)

        private const ulong GET_INTERIOR_AT_COORDS = 0xB0F7F8663821D9C3;
        private const ulong PIN_INTERIOR_IN_MEMORY = 0x2CA429C029CCF247;
        private const ulong IS_INTERIOR_READY = 0x6726BDCCC1932F0E;
        private const ulong UNPIN_INTERIOR = 0x261CCE7EED010641;
        private const ulong HAS_COLLISION_LOADED_AROUND_ENTITY = 0xE9676F61BC0B3321;
        private const ulong NEW_LOAD_SCENE_START_SPHERE = 0xACCFB4ACF53551B0;
        private const ulong NEW_LOAD_SCENE_STOP = 0xC197616D221FF4A4;
        private const ulong IS_NEW_LOAD_SCENE_LOADED = 0x01B8247A7A8B9AD1;
        private const ulong FREEZE_ENTITY_POSITION = 0x428CA6DBD1094446;
        private const ulong GET_GROUND_Z_FOR_3D_COORD = 0xC906A7DAB05C8D2B;

        public static int InteriorAt(Vector3 at)
        {
            return N.Call<int>(GET_INTERIOR_AT_COORDS, at.X, at.Y, at.Z);
        }

        public static bool IsInInterior(Ped p)
        {
            return p != null && p.Exists() && N.Call<int>(GET_INTERIOR_FROM_ENTITY, p) != 0;
        }

        // The 0.5 version moved the player, waited 1.2 s and gave up if GTA had not attached the
        // player to the interior yet - which is why entering often "failed". Now the interior is
        // pinned and must report ready, the scene and collision are streamed while the player is
        // frozen (no falling through the floor), and only then is the result checked. The screen
        // stays faded for at most ~6 s. On failure the player is put back where they were.
        public static bool MoveIntoInterior(Vector3 to, float heading, bool fadeIn, out int pinnedInterior)
        {
            pinnedInterior = 0;
            Ped p = GTA.Game.Player.Character;
            Vector3 from = p.Position;
            float fromHeading = p.Heading;
            FadeOut();
            int id = InteriorAt(to);
            if (id != 0)
            {
                N.Call(PIN_INTERIOR_IN_MEMORY, id);
                Wait(() => N.Call<bool>(IS_INTERIOR_READY, id), 4000);
            }
            N.Call(NEW_LOAD_SCENE_START_SPHERE, to.X, to.Y, to.Z, 25f, 0);
            N.Call(FREEZE_ENTITY_POSITION, p, true);
            N.Call(REQUEST_COLLISION_AT_COORD, to.X, to.Y, to.Z);
            p.Position = to;
            p.Heading = heading;
            Wait(() => N.Call<bool>(HAS_COLLISION_LOADED_AROUND_ENTITY, p) && N.Call<bool>(IS_NEW_LOAD_SCENE_LOADED), 5000);
            N.Call(NEW_LOAD_SCENE_STOP);
            bool ok = Wait(() => N.Call<int>(GET_INTERIOR_FROM_ENTITY, p) != 0, 1500)
                      || (id != 0 && N.Call<bool>(IS_INTERIOR_READY, id) && N.Call<bool>(HAS_COLLISION_LOADED_AROUND_ENTITY, p));
            if (!ok)
            {
                if (id != 0) N.Call(UNPIN_INTERIOR, id);
                N.Call(REQUEST_COLLISION_AT_COORD, from.X, from.Y, from.Z);
                p.Position = from;
                p.Heading = fromHeading;
                Script.Wait(300);
                N.Call(FREEZE_ENTITY_POSITION, p, false);
                Log.Warn(string.Format("Interior not available at {0:0.0},{1:0.0},{2:0.0} (id {3}); returned the player", to.X, to.Y, to.Z, id));
                FadeIn();
                return false;
            }
            N.Call(FREEZE_ENTITY_POSITION, p, false);
            pinnedInterior = id;
            if (fadeIn) FadeIn();
            return true;
        }

        public static void Unpin(int interior)
        {
            if (interior != 0) N.Call(UNPIN_INTERIOR, interior);
        }

        // Back outside: wait for collision, then put the player on the ground (never under it).
        public static void MoveOutside(Vector3 to, float heading)
        {
            Ped p = GTA.Game.Player.Character;
            FadeOut();
            N.Call(FREEZE_ENTITY_POSITION, p, true);
            N.Call(REQUEST_COLLISION_AT_COORD, to.X, to.Y, to.Z);
            p.Position = to;
            p.Heading = heading;
            Wait(() => N.Call<bool>(HAS_COLLISION_LOADED_AROUND_ENTITY, p), 4000);
            OutputArgument gz = new OutputArgument();
            if (N.Call<bool>(GET_GROUND_Z_FOR_3D_COORD, to.X, to.Y, to.Z + 3f, gz, false, false))
            {
                float z = gz.GetResult<float>();
                if (z > to.Z - 6f && z < to.Z + 4f) p.Position = new Vector3(to.X, to.Y, z + 0.05f);
            }
            N.Call(FREEZE_ENTITY_POSITION, p, false);
            FadeIn();
        }

        private static bool Wait(Func<bool> done, int timeoutMs)
        {
            int start = Environment.TickCount;
            while (unchecked(Environment.TickCount - start) < timeoutMs)
            {
                if (done()) return true;
                Script.Wait(50);
            }
            return done();
        }
    }

    public static class Spots
    {
        // A road node near 'around' (between min and max metres) to place a vehicle on.
        public static bool RoadSpot(Vector3 around, float min, float max, out Vector3 pos, out float heading)
        {
            for (int n = 1; n <= 40; n++)
            {
                OutputArgument p = new OutputArgument(), h = new OutputArgument(), lanes = new OutputArgument();
                if (!N.Call<bool>(N.GET_NTH_CLOSEST_VEHICLE_NODE_WITH_HEADING, around.X, around.Y, around.Z, n, p, h, lanes, 0, 3f, 0f)) continue;
                Vector3 c = p.GetResult<Vector3>();
                float d = N.Dist2D(c, around);
                if (d < min) continue;
                if (d > max) break;
                if (World.GetNearbyVehicles(c, 4f).Length > 0) continue; // occupied
                pos = c;
                heading = h.GetResult<float>();
                return true;
            }
            pos = around;
            heading = 0f;
            return false;
        }

        public static Vehicle SpawnOwned(OwnedVehicle o, Vector3 pos, float heading)
        {
            Model m = new Model(o.ModelHash);
            if (!m.IsInCdImage || !m.Request(2000))
            {
                Log.Warn("Model not available for owned vehicle " + o.DisplayName);
                return null;
            }
            Vehicle v = World.CreateVehicle(m, pos, heading);
            m.MarkAsNoLongerNeeded();
            if (v == null || !v.Exists()) return null;
            VehicleLooks.Apply(v, o.Look, o.Plate);
            N.Call(VehicleLooks.SET_VEHICLE_ON_GROUND_PROPERLY, v);
            if (o.BodyHealth > 0) v.BodyHealth = o.BodyHealth;
            if (o.EngineHealth > 0) v.EngineHealth = Math.Max(150f, o.EngineHealth);
            v.IsPersistent = true;
            return v;
        }
    }
}
