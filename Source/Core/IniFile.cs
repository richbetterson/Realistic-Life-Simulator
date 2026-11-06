// Realistic Life Simulator - tolerant INI reader.
// Keys before the first [section] belong to the section "" (used by ScriptHookVDotNet.ini).

using System;
using System.Collections.Generic;
using System.IO;

namespace RealisticLifeSimulator.Core
{
    public sealed class IniFile
    {
        private readonly Dictionary<string, Dictionary<string, string>> sections =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private int malformedLines;

        public int MalformedLines { get { return malformedLines; } }

        public IEnumerable<string> SectionNames { get { return sections.Keys; } }

        public static IniFile Load(string path)
        {
            return Parse(File.ReadAllText(path));
        }

        public static IniFile Parse(string text)
        {
            IniFile ini = new IniFile();
            string current = "";
            ini.EnsureSection(current);

            string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line[0] == ';' || line[0] == '#' || line.StartsWith("//")) continue;

                if (ContainsControlChars(line))
                {
                    ini.malformedLines++;
                    continue;
                }

                if (line[0] == '[')
                {
                    int end = line.IndexOf(']');
                    if (end <= 1)
                    {
                        ini.malformedLines++;
                        continue;
                    }
                    current = line.Substring(1, end - 1).Trim();
                    ini.EnsureSection(current);
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    ini.malformedLines++;
                    continue;
                }

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                    value = value.Substring(1, value.Length - 2);

                ini.sections[current][key] = value;
            }
            return ini;
        }

        private static bool ContainsControlChars(string s)
        {
            foreach (char c in s)
            {
                if (c < 0x20 && c != '\t') return true;
                if (c == '�') return true; // invalid UTF-8 sequence
            }
            return false;
        }

        private Dictionary<string, string> EnsureSection(string name)
        {
            Dictionary<string, string> s;
            if (!sections.TryGetValue(name, out s))
            {
                s = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                sections[name] = s;
            }
            return s;
        }

        public bool TryGet(string section, string key, out string value)
        {
            value = null;
            Dictionary<string, string> s;
            if (!sections.TryGetValue(section ?? "", out s)) return false;
            return s.TryGetValue(key, out value);
        }

        public string Get(string section, string key, string fallback)
        {
            string v;
            return TryGet(section, key, out v) ? v : fallback;
        }

        public void Set(string section, string key, string value)
        {
            EnsureSection(section ?? "")[key] = value ?? "";
        }

        public IEnumerable<KeyValuePair<string, string>> GetSection(string section)
        {
            Dictionary<string, string> s;
            if (sections.TryGetValue(section ?? "", out s)) return s;
            return new KeyValuePair<string, string>[0];
        }
    }
}
