// Realistic Life Simulator - module contract and the small Phase 1 modules.
// Each system is a module with its own update interval, so nothing runs every frame
// unless it has to draw something.

using System;
using GTA;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public interface IModule
    {
        string Name { get; }
        int IntervalMs { get; }   // how often Update runs (real time)
        void Start();
        void Update();
        void Shutdown();
    }

    // Optional: modules that must draw or read input every frame (kept as cheap as possible).
    public interface IFrameModule
    {
        void OnFrame();
    }

    // Tracks real play time while the game is running and not paused.
    public sealed class PlaytimeModule : IModule
    {
        private readonly SaveManager saves;
        private int last;

        public PlaytimeModule(SaveManager saves) { this.saves = saves; }

        public string Name { get { return "Playtime"; } }
        public int IntervalMs { get { return 1000; } }

        public void Start() { last = Environment.TickCount; }

        public void Update()
        {
            int now = Environment.TickCount;
            int delta = unchecked(now - last);
            last = now;
            if (GTA.Game.IsPaused) return;
            if (delta > 0 && delta < 5000) saves.Data.Stats.TotalPlayTimeSeconds += delta / 1000.0;
        }

        public void Shutdown() { }
    }

    // Licence status over time (0.6.0): points expire and timed suspensions end while playing.
    // Checks every 10 s and announces a change once. Status itself comes from LicenceRules.
    public sealed class LicenceWatchModule : IModule
    {
        private readonly RlsMain main;
        private string character = "";
        private LicenceStatus last;

        public LicenceWatchModule(RlsMain main) { this.main = main; }

        public string Name { get { return "Licence"; } }
        public int IntervalMs { get { return 10000; } }

        public void Start()
        {
            DriverProfile p = main.CurrentProfile();
            character = p.Character;
            last = LicenceRules.Status(p, main.Saves.Data.Stats.TotalPlayTimeSeconds);
        }

        public void Update()
        {
            DriverProfile p = main.CurrentProfile();
            double play = main.Saves.Data.Stats.TotalPlayTimeSeconds;
            LicenceRules.Update(p, play);
            LicenceStatus now = LicenceRules.Status(p, play);
            if (p.Character != character) { character = p.Character; last = now; return; } // switched character
            if (now != last) main.AnnounceLicenceChange(last, p);
            last = now;
        }

        public void Shutdown() { }
    }

    // Saves on the configured interval. Skips while the game is loading.
    public sealed class AutosaveModule : IModule
    {
        private readonly SaveManager saves;
        private readonly ConfigurationManager config;
        private int lastSave;

        public AutosaveModule(SaveManager saves, ConfigurationManager config)
        {
            this.saves = saves;
            this.config = config;
        }

        public string Name { get { return "Autosave"; } }
        public int IntervalMs { get { return 10000; } }

        public void Start() { lastSave = Environment.TickCount; }

        public void Update()
        {
            int due = config.Settings.AutosaveMinutes * 60000;
            if (unchecked(Environment.TickCount - lastSave) < due) return;
            lastSave = Environment.TickCount;
            saves.Save();
        }

        public void Shutdown() { }
    }

    // Measures how much frame time this mod itself uses, so its cost is visible in game.
    public sealed class PerformanceManager
    {
        private double sumMs, maxMs, windowMax;
        private int frames;
        private int windowStart = Environment.TickCount;
        private double avgMs, peakMs;
        private PerformanceProfile profile;

        public PerformanceManager(PerformanceProfileKind kind) { profile = PerformanceProfile.For(kind); }

        public PerformanceProfile Profile { get { return profile; } }
        public double AverageMs { get { return avgMs; } }
        public double PeakMs { get { return peakMs; } }

        public void SetProfile(PerformanceProfileKind kind)
        {
            profile = PerformanceProfile.For(kind);
            Log.Info("Performance profile: " + kind + " (" + profile.Summary + ")");
        }

        public void RecordFrame(double ms)
        {
            sumMs += ms;
            frames++;
            if (ms > windowMax) windowMax = ms;

            int now = Environment.TickCount;
            if (unchecked(now - windowStart) < 2000) return;
            avgMs = frames > 0 ? sumMs / frames : 0;
            peakMs = windowMax;
            if (windowMax > maxMs) maxMs = windowMax;
            if (Log.DebugEnabled) Log.Debug(string.Format("Mod frame cost avg {0:0.000} ms, peak {1:0.000} ms over {2} frames", avgMs, peakMs, frames));
            sumMs = 0;
            frames = 0;
            windowMax = 0;
            windowStart = now;
        }
    }
}
