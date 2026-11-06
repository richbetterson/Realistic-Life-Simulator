// Realistic Life Simulator - detects key conflicts with GTA V, other mods and our own bindings.
// This is a warning system only: the player may still choose a conflicting key.

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace RealisticLifeSimulator.Core
{
    public sealed class KeyConflictChecker
    {
        // Default GTA V keyboard controls (story mode, default layout). Approximate list:
        // the player may have remapped controls in the game's own settings.
        private static readonly Dictionary<Keys, string[]> GtaDefaults = BuildGtaDefaults();

        private readonly Dictionary<Keys, List<string>> external = new Dictionary<Keys, List<string>>();
        private readonly List<string> scannedFiles = new List<string>();

        public IList<string> ScannedFiles { get { return scannedFiles; } }

        private static string[] P(string en, string ru) { return new[] { en, ru }; }

        private static Dictionary<Keys, string[]> BuildGtaDefaults()
        {
            Dictionary<Keys, string[]> d = new Dictionary<Keys, string[]>();
            d[Keys.W] = P("Move forward / accelerate", "Вперёд / газ");
            d[Keys.A] = P("Move / steer left", "Влево / руль влево");
            d[Keys.S] = P("Move back / brake", "Назад / тормоз");
            d[Keys.D] = P("Move / steer right", "Вправо / руль вправо");
            d[Keys.Space] = P("Jump / handbrake", "Прыжок / ручной тормоз");
            d[Keys.E] = P("Context action / horn", "Действие / сигнал");
            d[Keys.F] = P("Enter or exit vehicle", "Сесть / выйти из машины");
            d[Keys.G] = P("Throw grenade", "Бросок гранаты");
            d[Keys.R] = P("Reload / melee", "Перезарядка / ближний бой");
            d[Keys.Q] = P("Cover / radio wheel", "Укрытие / радио");
            d[Keys.C] = P("Look behind", "Взгляд назад");
            d[Keys.V] = P("Change camera view", "Смена камеры");
            d[Keys.X] = P("Cancel / duck in vehicle", "Отмена / пригнуться в машине");
            d[Keys.Z] = P("Expand radar", "Увеличить радар");
            d[Keys.H] = P("Headlights", "Фары");
            d[Keys.T] = P("Chat / phone text", "Чат / сообщения");
            d[Keys.M] = P("Interaction menu", "Меню взаимодействия");
            d[Keys.P] = P("Pause menu", "Меню паузы");
            d[Keys.B] = P("Point", "Указать");
            d[Keys.L] = P("Vehicle lock / roof (some vehicles)", "Замок / крыша (некоторые машины)");
            d[Keys.K] = P("Vehicle special", "Особая функция машины");
            d[Keys.Tab] = P("Weapon wheel", "Колесо оружия");
            d[Keys.Capital] = P("Special ability", "Особая способность");
            d[Keys.Return] = P("Accept", "Подтвердить");
            d[Keys.Back] = P("Phone cancel", "Телефон: назад");
            d[Keys.Up] = P("Phone / menu up", "Телефон / меню вверх");
            d[Keys.Down] = P("Phone / menu down", "Телефон / меню вниз");
            d[Keys.Left] = P("Phone / menu left", "Телефон / меню влево");
            d[Keys.Right] = P("Phone / menu right", "Телефон / меню вправо");
            d[Keys.Delete] = P("Phone camera / misc", "Камера телефона / прочее");
            d[Keys.Oemcomma] = P("Previous radio station", "Предыдущая радиостанция");
            d[Keys.OemPeriod] = P("Next radio station", "Следующая радиостанция");
            d[Keys.F1] = P("Rockstar Editor", "Rockstar Editor");
            d[Keys.F2] = P("Rockstar Editor", "Rockstar Editor");
            d[Keys.F3] = P("Rockstar Editor", "Rockstar Editor");
            d[Keys.PageUp] = P("Weapon / camera", "Оружие / камера");
            d[Keys.PageDown] = P("Weapon / camera", "Оружие / камера");
            Keys[] digits = { Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9 };
            foreach (Keys k in digits) d[k] = P("Weapon slot", "Слот оружия");
            Keys[] numpad = { Keys.NumPad0, Keys.NumPad1, Keys.NumPad2, Keys.NumPad3, Keys.NumPad4,
                              Keys.NumPad5, Keys.NumPad6, Keys.NumPad7, Keys.NumPad8, Keys.NumPad9,
                              Keys.Add, Keys.Subtract };
            foreach (Keys k in numpad) d[k] = P("Aircraft / vehicle controls", "Управление авиатехникой / машиной");
            return d;
        }

        // Scans ScriptHookVDotNet.ini and every *.ini in the game's scripts folder for key
        // settings belonging to other mods (e.g. SpeedoSignals). Read-only: nothing is changed.
        public void ScanExternal(string gameRoot)
        {
            external.Clear();
            scannedFiles.Clear();

            string shvdn = Path.Combine(gameRoot, "ScriptHookVDotNet.ini");
            if (File.Exists(shvdn)) ScanIni(shvdn, "ScriptHookVDotNet");

            string scripts = Path.Combine(gameRoot, "scripts");
            if (Directory.Exists(scripts))
            {
                foreach (string file in Directory.GetFiles(scripts, "*.ini", SearchOption.AllDirectories))
                {
                    if (Path.GetFileName(file).StartsWith("RealisticLifeSimulator", StringComparison.OrdinalIgnoreCase))
                        continue;
                    ScanIni(file, Path.GetFileNameWithoutExtension(file));
                }
            }
        }

        private void ScanIni(string file, string source)
        {
            try
            {
                if (new FileInfo(file).Length > 512 * 1024) return;
                IniFile ini = IniFile.Load(file);
                scannedFiles.Add(file);
                foreach (string section in ini.SectionNames)
                {
                    foreach (KeyValuePair<string, string> kv in ini.GetSection(section))
                    {
                        if (!LooksLikeKeySetting(section, kv.Key)) continue;
                        Keys k;
                        if (!KeyRules.TryParseKeyName(kv.Value, out k) || k == Keys.None) continue;
                        AddExternal(k, source + ": " + kv.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not scan " + file + " for key settings: " + ex.Message);
            }
        }

        private static bool LooksLikeKeySetting(string section, string key)
        {
            string s = (section ?? "").ToLowerInvariant();
            string k = (key ?? "").ToLowerInvariant();
            return s.Contains("key") || s.Contains("bind") || s.Contains("control")
                || k.Contains("key") || k.Contains("bind") || k.Contains("hotkey");
        }

        private void AddExternal(Keys k, string description)
        {
            List<string> list;
            if (!external.TryGetValue(k, out list))
            {
                list = new List<string>();
                external[k] = list;
            }
            if (!list.Contains(description)) list.Add(description);
        }

        public List<string> FindConflicts(Keys key, RlsAction forAction, KeyBindingSet own)
        {
            List<string> result = new List<string>();
            if (key == Keys.None) return result;

            string[] gta;
            if (GtaDefaults.TryGetValue(key, out gta)) result.Add("GTA V: " + L.S(gta[0], gta[1]));

            List<string> ext;
            if (external.TryGetValue(key, out ext)) result.AddRange(ext);

            if (own != null)
            {
                foreach (RlsAction a in own.ActionsUsing(key))
                    if (a != forAction) result.Add(L.S("This mod: ", "Этот мод: ") + ActionCatalog.DisplayName(a));
            }
            return result;
        }

        public bool HasOwnConflict(Keys key, RlsAction forAction, KeyBindingSet own)
        {
            foreach (RlsAction a in own.ActionsUsing(key))
                if (a != forAction) return true;
            return false;
        }
    }
}
