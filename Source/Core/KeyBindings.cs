// Realistic Life Simulator - key binding model.
// Every action is UNASSIGNED by default. A key is only stored after the player
// explicitly picks one in the Key bindings menu.

using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace RealisticLifeSimulator.Core
{
    // Enum member names are the entry names written to Config\KeyBindings.ini.
    public enum RlsAction
    {
        OpenLifeHub,
        OpenSettings,
        OpenBank,
        OpenPropertyMenu,
        OpenVehicleMenu,
        CallPolice,
        RequestRoadsideAssistance,
        StartJob,
        PassengerMenu,
        CallTaxi,
        ToggleSpeedLimiter   // 0.6.0, unassigned by default
    }

    public static class ActionCatalog
    {
        public static readonly RlsAction[] All = (RlsAction[])Enum.GetValues(typeof(RlsAction));

        public static string DisplayName(RlsAction a)
        {
            switch (a)
            {
                case RlsAction.OpenLifeHub: return L.S("Open Life Hub", "Открыть центр управления");
                case RlsAction.OpenSettings: return L.S("Open settings", "Открыть настройки");
                case RlsAction.OpenBank: return L.S("Open bank", "Открыть банк");
                case RlsAction.OpenPropertyMenu: return L.S("Open properties", "Недвижимость");
                case RlsAction.OpenVehicleMenu: return L.S("Open vehicles", "Мои автомобили");
                case RlsAction.CallPolice: return L.S("Call police", "Вызвать полицию");
                case RlsAction.RequestRoadsideAssistance: return L.S("Roadside assistance", "Помощь на дороге");
                case RlsAction.StartJob: return L.S("Start job", "Начать работу");
                case RlsAction.PassengerMenu: return L.S("Passenger / rides menu", "Меню поездок пассажиром");
                case RlsAction.CallTaxi: return L.S("Call a taxi", "Вызвать такси");
                case RlsAction.ToggleSpeedLimiter: return L.S("Speed limiter on / off", "Ограничитель скорости вкл / выкл");
            }
            return a.ToString();
        }
    }

    public static class KeyRules
    {
        private static readonly HashSet<Keys> NotAssignable = new HashSet<Keys>
        {
            Keys.None, Keys.LButton, Keys.RButton, Keys.MButton, Keys.XButton1, Keys.XButton2,
            Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey, Keys.ControlKey, Keys.LControlKey,
            Keys.RControlKey, Keys.Menu, Keys.LMenu, Keys.RMenu, Keys.LWin, Keys.RWin,
            Keys.Escape, Keys.Shift, Keys.Control, Keys.Alt, Keys.Modifiers, Keys.KeyCode
        };

        public static bool IsAssignable(Keys key, out string reason)
        {
            reason = null;
            if (NotAssignable.Contains(key))
            {
                reason = key == Keys.Escape
                    ? L.S("Escape is reserved for cancelling.", "Esc зарезервирована для отмены.")
                    : L.S("Mouse buttons and modifier keys cannot be assigned.", "Кнопки мыши и клавиши-модификаторы назначить нельзя.");
                return false;
            }
            return true;
        }

        // Accepts only exact Keys enum names (case-insensitive). Empty means "unassigned".
        // Rejects numbers and combinations so that values such as "1" or "true" in other
        // mods' INI files are never mistaken for keys.
        public static bool TryParseKeyName(string value, out Keys key)
        {
            key = Keys.None;
            if (value == null) return false;
            string v = value.Trim();
            if (v.Length == 0) return true;
            foreach (string name in Enum.GetNames(typeof(Keys)))
            {
                if (string.Equals(name, v, StringComparison.OrdinalIgnoreCase))
                {
                    key = (Keys)Enum.Parse(typeof(Keys), name);
                    return true;
                }
            }
            return false;
        }
    }

    public sealed class KeyBindingSet
    {
        private readonly Dictionary<RlsAction, Keys> map = new Dictionary<RlsAction, Keys>();

        public KeyBindingSet()
        {
            foreach (RlsAction a in ActionCatalog.All) map[a] = Keys.None;
        }

        public Keys Get(RlsAction action)
        {
            Keys k;
            return map.TryGetValue(action, out k) ? k : Keys.None;
        }

        public void Set(RlsAction action, Keys key) { map[action] = key; }

        public void Clear(RlsAction action) { map[action] = Keys.None; }

        public bool IsAssigned(RlsAction action) { return Get(action) != Keys.None; }

        public bool TryGetAction(Keys key, out RlsAction action)
        {
            action = RlsAction.OpenLifeHub;
            if (key == Keys.None) return false;
            foreach (RlsAction a in ActionCatalog.All)
            {
                if (map[a] == key)
                {
                    action = a;
                    return true;
                }
            }
            return false;
        }

        public List<RlsAction> ActionsUsing(Keys key)
        {
            List<RlsAction> list = new List<RlsAction>();
            if (key == Keys.None) return list;
            foreach (RlsAction a in ActionCatalog.All)
                if (map[a] == key) list.Add(a);
            return list;
        }

        public int AssignedCount
        {
            get
            {
                int n = 0;
                foreach (Keys k in map.Values) if (k != Keys.None) n++;
                return n;
            }
        }
    }
}
