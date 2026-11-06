// Realistic Life Simulator - raw native hashes.
// Using hash values instead of SHVDN's Hash enum names keeps the mod working across
// SHVDN builds where native names were changed.

using GTA;
using GTA.Math;
using GTA.Native;

namespace RealisticLifeSimulator.Game
{
    public static class N
    {
        public const ulong GET_VEHICLE_NODE_PROPERTIES = 0x0568566ACBB5DEDC;
        public const ulong GET_CLOSEST_VEHICLE_NODE = 0x240A18690AE96513;
        public const ulong GET_CLOSEST_ROAD = 0x132F52BBA570FE92;
        public const ulong GET_STREET_NAME_AT_COORD = 0x2EB41072B4C1E4C0;
        public const ulong GET_STREET_NAME_FROM_HASH_KEY = 0xD0EF8A959B8A4CB9;
        public const ulong GET_NAME_OF_ZONE = 0xCD90657D4C30E1CA;
        public const ulong GET_VEHICLE_LIGHTS_STATE = 0xB91B4C20085BD12F;
        public const ulong GET_PED_TYPE = 0xFF059E1E4C01E63C;
        public const ulong HAS_ENTITY_CLEAR_LOS_TO_ENTITY = 0xFCDFF7B72D23A1AC;
        public const ulong GET_CLOCK_HOURS = 0x25223CA6B4D20B7F;
        public const ulong IS_VEHICLE_SIREN_ON = 0x4C9BF537BE2634B2;
        public const ulong SET_VEHICLE_ENGINE_ON = 0x2497C4717C8B881E;
        public const ulong GET_VEHICLE_NUMBER_PLATE_TEXT = 0x7CE1CCB9B293020E;
        public const ulong TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE = 0x158BB33F920D360C;
        public const ulong TASK_LEAVE_VEHICLE = 0xD3DBCE61A490BE02;
        public const ulong TASK_START_SCENARIO_IN_PLACE = 0x142A02425FF02BD9;
        public const ulong TASK_ENTER_VEHICLE = 0xC20E50AA46D09CA8;
        public const ulong TASK_VEHICLE_DRIVE_WANDER = 0x480142959D337D00;
        public const ulong REQUEST_ANIM_DICT = 0xD3BD40951412FEF6;
        public const ulong HAS_ANIM_DICT_LOADED = 0xD031A9162D01088C;
        public const ulong REMOVE_ANIM_DICT = 0xF66A602F829E2A06;
        public const ulong TASK_PLAY_ANIM = 0xEA47FE3719165B94;
        public const ulong STOP_ANIM_TASK = 0x97FF36A1D40EA00A;
        public const ulong HAS_PED_GOT_WEAPON = 0x8DECB02F88F428BC;
        public const ulong GET_AMMO_IN_PED_WEAPON = 0x015A522136D7F951;
        public const ulong SET_PED_AMMO = 0x14E56BC5B5DB6A19;
        public const ulong IS_ENTITY_A_MISSION_ENTITY = 0x0A7B270912999B3C;
        public const ulong GET_CONTROL_NORMAL = 0xEC3C9B8D5327B563;
        public const ulong IS_PED_RAGDOLL = 0x47E4E977581C5B55;
        public const ulong IS_HUD_HIDDEN = 0xA86478C6958735C5;
        public const ulong IS_PAUSE_MENU_ACTIVE = 0xB0034A223497FFCB;
        public const ulong IS_CUTSCENE_PLAYING = 0xD3C2E180A40F031E;
        public const ulong PLAY_SOUND_FRONTEND = 0x67C540AA08E4A6F5;

        // Phase 3
        public const ulong SET_VEHICLE_SIREN = 0xF4924635A19EB37D;
        public const ulong TASK_VEHICLE_FOLLOW = 0xFC545A9F0626E3B6;
        public const ulong TASK_VEHICLE_ESCORT = 0x0FA6E4B75F302400;
        public const ulong TASK_VEHICLE_DRIVE_TO_COORD = 0xE2A2AA2F659D77A7;
        public const ulong TASK_VEHICLE_TEMP_ACTION = 0xC429DCEEB339E129;
        public const ulong TASK_GO_TO_ENTITY = 0x6A071245EB0D1882;
        public const ulong TASK_FOLLOW_NAV_MESH_TO_COORD = 0x15D3A79D4E44B913;
        public const ulong TASK_TURN_PED_TO_FACE_ENTITY = 0x5AD23D40115353AC;
        public const ulong CLEAR_PED_TASKS = 0xE1EF3C1216AFF2CD;
        public const ulong SET_BLOCKING_OF_NON_TEMPORARY_EVENTS = 0x90D2156198831D69;
        public const ulong SET_PED_KEEP_TASK = 0x971D38760FBC02EF;
        public const ulong IS_VEHICLE_STOLEN = 0x4AF9BD80EEBEB453;
        public const ulong GET_NTH_CLOSEST_VEHICLE_NODE_WITH_HEADING = 0x80CA6A8B6C094CC4;
        public const ulong IS_SPHERE_VISIBLE = 0xE33D59DA70B58FDF;
        public const ulong SET_NEW_WAYPOINT = 0xFE43368D2AA4F2FC;
        public const ulong IS_ENTITY_TOUCHING_ENTITY = 0x17FFC1B2BA35A494;
        public const ulong HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY = 0xC86D67D52A707CF8;
        public const ulong CLEAR_ENTITY_LAST_DAMAGE_ENTITY = 0xA72CD9CA74A5ECBA;

        public const ulong SET_VEHICLE_HAS_MUTED_SIRENS = 0xD8050E0EB60CF274;
        public const ulong GET_MISSION_FLAG = 0xA33CDCCDA663159E;

        // Phase 4
        public const ulong SET_DRIVE_TASK_DRIVING_STYLE = 0xDACE1BE37D88AF67;
        public const ulong SET_DRIVE_TASK_CRUISE_SPEED = 0x5C9B84BD7D31D908;
        public const ulong SET_DRIVER_ABILITY = 0xB195FFA8042FC5C3;
        public const ulong SET_DRIVER_AGGRESSIVENESS = 0xA731F608CA104E3C;
        public const ulong ADD_ROAD_NODE_SPEED_ZONE = 0x2CE544C68FB812A0;
        public const ulong REMOVE_ROAD_NODE_SPEED_ZONE = 0x1033371FC8E842A7;

        // An ordinary NPC driver this mod may influence (not the player, police, a mission
        // or an entity another system of this mod has taken over).
        public static bool IsAmbientDriver(Ped d)
        {
            return d != null && d.Exists() && !d.IsDead && !d.IsPlayer && !IsCop(d) && !IsMissionEntity(d);
        }

        public const ulong GET_PLAYER_WANTED_LEVEL = 0xE28E54788CE8F12D;
        public const ulong SET_PLAYER_WANTED_LEVEL = 0x39FF19C64EF7DA5B;
        public const ulong SET_PLAYER_WANTED_LEVEL_NOW = 0xE0A7D1E497FFCD6F;

        public static int Wanted()
        {
            return Call<int>(GET_PLAYER_WANTED_LEVEL, GTA.Game.Player.Handle);
        }

        // Raises (never lowers) the player's wanted level through GTA's own police system.
        public static void RaiseWanted(int level)
        {
            if (Wanted() >= level) return;
            Call(SET_PLAYER_WANTED_LEVEL, GTA.Game.Player.Handle, level, false);
            Call(SET_PLAYER_WANTED_LEVEL_NOW, GTA.Game.Player.Handle, false);
        }

        // True while a story mission runs: stops and accidents stay out of missions.
        public static bool InMission()
        {
            return Call<bool>(GET_MISSION_FLAG);
        }

        public const int StyleNormal = 786603;
        public const int StyleUrgent = 1074528293;

        public static void Block(Ped p)
        {
            Call(SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, p, true);
            Call(SET_PED_KEEP_TASK, p, true);
        }

        public static void WalkTo(Ped p, Vector3 pos)
        {
            Call(TASK_FOLLOW_NAV_MESH_TO_COORD, p, pos.X, pos.Y, pos.Z, 1.0f, -1, 0.6f, false, 0f);
        }

        public static void Face(Ped p, Entity target)
        {
            Call(TASK_TURN_PED_TO_FACE_ENTITY, p, target, -1);
        }

        public static void Scenario(Ped p, string name)
        {
            Call(TASK_START_SCENARIO_IN_PLACE, p, name, 0, true);
        }

        public static void Siren(Vehicle v, bool on)
        {
            Call(SET_VEHICLE_SIREN, v, on);
        }

        public static void Release(Entity e)
        {
            if (e == null || !e.Exists()) return;
            try
            {
                Ped p = e as Ped;
                if (p != null && !p.IsPlayer)
                {
                    Call(SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, p, false);
                    Call(SET_PED_KEEP_TASK, p, false);
                }
                e.IsPersistent = false;
                e.MarkAsNoLongerNeeded();
            }
            catch (System.Exception) { }
        }

        public static string CharacterName(Ped p)
        {
            int h = p.Model.Hash;
            if (h == RealisticLifeSimulator.Core.Joaat.Hash("player_zero")) return "Michael De Santa";
            if (h == RealisticLifeSimulator.Core.Joaat.Hash("player_one")) return "Franklin Clinton";
            if (h == RealisticLifeSimulator.Core.Joaat.Hash("player_two")) return "Trevor Philips";
            return "Unknown";
        }

        public static string SafeName(Vehicle v)
        {
            return v != null && v.Exists() ? VehicleName(v) : "?";
        }

        public static string VehicleName(Vehicle v)
        {
            try
            {
                string label = GTA.Game.GetLocalizedString(v.DisplayName);
                if (!string.IsNullOrEmpty(label) && label != "NULL") return label;
                return v.DisplayName;
            }
            catch (System.Exception) { return "?"; }
        }

        public const int ControlVehicleAccelerate = 71;
        public const int ControlVehicleBrake = 72;
        public const uint WeaponPetrolCan = 0x34A67B97;

        public static void Call(ulong hash, params InputArgument[] args) { Function.Call((Hash)hash, args); }
        public static T Call<T>(ulong hash, params InputArgument[] args) { return Function.Call<T>((Hash)hash, args); }

        public static bool HudBlocked()
        {
            return GTA.Game.IsPaused || Call<bool>(IS_HUD_HIDDEN) || Call<bool>(IS_PAUSE_MENU_ACTIVE);
        }

        public static void Beep()
        {
            Call(PLAY_SOUND_FRONTEND, -1, "5_SEC_WARNING", "HUD_MINI_GAME_SOUNDSET", true);
        }

        public static string Plate(Vehicle v)
        {
            string p = Call<string>(GET_VEHICLE_NUMBER_PLATE_TEXT, v);
            return p == null ? "" : p.Trim();
        }

        public static bool IsMissionEntity(Entity e)
        {
            return Call<bool>(IS_ENTITY_A_MISSION_ENTITY, e);
        }

        public static int PedType(Ped p)
        {
            return Call<int>(GET_PED_TYPE, p);
        }

        public static bool IsCop(Ped p)
        {
            int t = PedType(p);
            return t == 6 || t == 27; // PED_TYPE_COP, PED_TYPE_SWAT
        }

        public static float Dist2D(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)System.Math.Sqrt(dx * dx + dy * dy);
        }

        public static float DistSq(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }
    }
}
