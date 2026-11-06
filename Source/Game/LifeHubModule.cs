// Realistic Life Simulator - Life Hub points.
// Walk into the marker and press the game's own Context control (shown as a prompt,
// E by default) to open the menu. This is the keyless way into every menu of the mod.
// Locations come from Data\LifeHubs.ini and can be edited.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using GTA;
using GTA.Math;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public sealed class LifeHubModule : IModule, IFrameModule
    {
        private sealed class HubPoint
        {
            public string Name;
            public Vector3 Position;
            public bool GroundChecked;
            public Blip Blip;
        }

        private const float NearDistance = 40f;
        private const float UseRadius = 1.3f;

        private readonly RlsPaths paths;
        private readonly ConfigurationManager config;
        private readonly Action openHub;
        private readonly Func<bool> menuOpen;
        private readonly List<HubPoint> hubs = new List<HubPoint>();
        private HubPoint near;

        public LifeHubModule(RlsPaths paths, ConfigurationManager config, Action openHub, Func<bool> menuOpen)
        {
            this.paths = paths;
            this.config = config;
            this.openHub = openHub;
            this.menuOpen = menuOpen;
        }

        public string Name { get { return "LifeHub"; } }
        public int IntervalMs { get { return 300; } }
        public int HubCount { get { return hubs.Count; } }

        public void Start()
        {
            LoadHubs();
            RefreshBlips();
        }

        public void Update()
        {
            near = null;
            Ped p = GTA.Game.Player.Character;
            if (p == null || !p.Exists()) return;
            Vector3 pos = p.Position;
            float best = NearDistance * NearDistance;
            foreach (HubPoint h in hubs)
            {
                float d = DistSq(pos, h.Position);
                if (d < best)
                {
                    best = d;
                    near = h;
                }
            }

            if (near != null && !near.GroundChecked && best < 30f * 30f)
            {
                // Snap the marker to the ground once the area has loaded.
                float ground = World.GetGroundHeight(new Vector3(near.Position.X, near.Position.Y, near.Position.Z + 2f));
                if (ground > 0f && Math.Abs(ground - near.Position.Z) < 4f)
                    near.Position = new Vector3(near.Position.X, near.Position.Y, ground);
                near.GroundChecked = true;
            }
        }

        public void OnFrame()
        {
            if (near == null || menuOpen()) return;
            Ped p = GTA.Game.Player.Character;
            if (p == null || !p.Exists() || p.IsDead) return;

            World.DrawMarker(MarkerType.Cylinder, near.Position, Vector3.Zero, Vector3.Zero,
                new Vector3(1.4f, 1.4f, 0.5f), Color.FromArgb(110, 92, 148, 214));

            if (p.IsInVehicle()) return;
            Vector3 pos = p.Position;
            float dx = pos.X - near.Position.X, dy = pos.Y - near.Position.Y;
            if (dx * dx + dy * dy > UseRadius * UseRadius || Math.Abs(pos.Z - near.Position.Z) > 2.5f) return;

            GTA.UI.Screen.ShowHelpTextThisFrame(L.S("Press ~INPUT_CONTEXT~ to open the Life Hub.", "Нажмите ~INPUT_CONTEXT~, чтобы открыть центр управления."));
            if (NativeInput.JustPressed(GTA.Control.Context)) openHub();
        }

        public void Shutdown()
        {
            foreach (HubPoint h in hubs) DeleteBlip(h);
        }

        public void RefreshBlips()
        {
            foreach (HubPoint h in hubs)
            {
                if (config.Settings.ShowHubBlips)
                {
                    if (h.Blip != null && h.Blip.Exists()) continue;
                    h.Blip = World.CreateBlip(h.Position);
                    h.Blip.Color = BlipColor.Blue;
                    h.Blip.Scale = 0.75f;
                    h.Blip.IsShortRange = true;
                    h.Blip.Name = L.S("Life Hub", "Центр управления");
                }
                else
                {
                    DeleteBlip(h);
                }
            }
        }

        // Used after a language change so map labels update.
        public void RecreateBlips()
        {
            foreach (HubPoint h in hubs) DeleteBlip(h);
            RefreshBlips();
        }

        private static void DeleteBlip(HubPoint h)        {
            if (h.Blip != null && h.Blip.Exists()) h.Blip.Delete();
            h.Blip = null;
        }

        private static float DistSq(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        // ------------------------------------------------------------ data file

        private void LoadHubs()
        {
            hubs.Clear();
            string file = paths.LifeHubsFile;
            if (!File.Exists(file)) WriteDefaultHubs(file);

            try
            {
                IniFile ini = IniFile.Load(file);
                foreach (string section in ini.SectionNames)
                {
                    if (section.Length == 0) continue;
                    float x, y, z;
                    if (!TryFloat(ini.Get(section, "X", null), out x) || !TryFloat(ini.Get(section, "Y", null), out y)
                        || !TryFloat(ini.Get(section, "Z", null), out z))
                    {
                        Log.Warn("LifeHubs.ini: [" + section + "] has invalid coordinates; skipped.");
                        continue;
                    }
                    if (!string.Equals(ini.Get(section, "Enabled", "true"), "true", StringComparison.OrdinalIgnoreCase)) continue;
                    HubPoint h = new HubPoint();
                    h.Name = ini.Get(section, "Name", section);
                    h.Position = new Vector3(x, y, z);
                    hubs.Add(h);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not read LifeHubs.ini", ex);
            }
            Log.Info("Life Hubs loaded: " + hubs.Count);
        }

        private static bool TryFloat(string s, out float f)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f);
        }

        private static void WriteDefaultHubs(string file)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; Realistic Life Simulator - Life Hub locations (walk in, press the Context control).");
            sb.AppendLine("; Coordinates use a dot as decimal separator. Set Enabled=false to hide a hub.");
            sb.AppendLine("; Markers snap to the ground automatically when you get close.");
            sb.AppendLine();
            AppendHub(sb, "LegionSquare", "Legion Square", "195.0", "-934.0", "30.7");
            AppendHub(sb, "MichaelHouse", "Rockford Hills (Michael)", "-818.0", "177.5", "72.2");
            AppendHub(sb, "FranklinHills", "Vinewood Hills (Franklin)", "7.6", "538.2", "176.0");
            AppendHub(sb, "FranklinForum", "Forum Drive (Franklin)", "-14.0", "-1441.5", "31.1");
            AppendHub(sb, "TrevorTrailer", "Sandy Shores (Trevor)", "1973.0", "3815.5", "33.4");
            FileUtil.WriteAllTextAtomic(file, sb.ToString());
        }

        private static void AppendHub(StringBuilder sb, string id, string name, string x, string y, string z)
        {
            sb.AppendLine("[" + id + "]");
            sb.AppendLine("Name=" + name);
            sb.AppendLine("X=" + x);
            sb.AppendLine("Y=" + y);
            sb.AppendLine("Z=" + z);
            sb.AppendLine("Enabled=true");
            sb.AppendLine();
        }
    }
}
