// Realistic Life Simulator - custom vehicle speed limiter (0.6.0).
//
// Life Hub > Vehicle controls > Speed limiter: enter a number (km/h) and switch it on.
// While the player drives a supported vehicle:
//   * a hard cap slightly above the limit is applied to that vehicle (SET_ENTITY_MAX_SPEED,
//     metres per second), which also holds the speed downhill without braking jolts;
//   * from 1 km/h below the limit the accelerator input is ignored for that frame, so the
//     car settles smoothly at the limit. Steering, braking and reversing are never touched.
// The cap is removed from a vehicle as soon as the player stops driving it (switching cars,
// getting out, disabling the limiter, unloading the mod). Aircraft and trains are ignored.

using System;
using GTA;
using RealisticLifeSimulator.Core;

namespace RealisticLifeSimulator.Game
{
    public sealed class SpeedLimiter : IModule, IFrameModule
    {
        private const ulong SET_ENTITY_MAX_SPEED = 0x0E46A3FCBDE2A1B1;
        private const float NoCap = 1000f;    // m/s: effectively no limit (GTA's default is unlimited)
        private const int ControlAccelerate = 71, ControlPushbikePedal = 136;

        // True while GTA's on-screen keyboard is open: the mod's own key handling pauses.
        public static bool TextInputActive;

        private readonly RlsMain main;
        private Vehicle capped;
        private int appliedKmh = -1;
        private int announcedHandle;

        public SpeedLimiter(RlsMain main) { this.main = main; }

        public string Name { get { return "SpeedLimiter"; } }
        public int IntervalMs { get { return 250; } }

        public bool Enabled { get { return main.Config.Settings.SpeedLimiterEnabled; } }
        public int LimitKmh { get { return main.Config.Settings.SpeedLimiterKmh; } }

        // True while the cap is applied to the vehicle the player is driving.
        public bool ActiveNow { get { return capped != null && capped.Exists() && appliedKmh > 0; } }

        public void Start() { }

        public void Shutdown() { Release(); }

        public void Update()
        {
            Vehicle v = DrivenVehicle();
            if (!Enabled || v == null || !SpeedLimiterRules.Supports((int)v.ClassType))
            {
                Release();
                return;
            }
            if (capped == null || !capped.Exists() || capped.Handle != v.Handle)
            {
                Release();
                capped = v;
                appliedKmh = -1;
                if (announcedHandle != v.Handle)
                {
                    announcedHandle = v.Handle;
                    main.Notify(L.F("Speed limiter: {0} km/h.", "Ограничитель скорости: {0} км/ч.", LimitKmh));
                }
            }
            if (appliedKmh != LimitKmh)
            {
                N.Call(SET_ENTITY_MAX_SPEED, v, SpeedLimiterRules.CapMetersPerSecond(LimitKmh));
                appliedKmh = LimitKmh;
            }
        }

        public void OnFrame()
        {
            if (capped == null || appliedKmh <= 0) return;
            Ped p = GTA.Game.Player.Character;
            if (p == null || !p.Exists() || !capped.Exists() || p.CurrentVehicle != capped) return;
            float kmh = SpeedLimiterRules.ToKmh(capped.Speed);
            if (!SpeedLimiterRules.CutThrottle(kmh, appliedKmh)) return;
            // Only while moving forward: reversing is never limited by the throttle cut.
            if (capped.ForwardVector.X * capped.Velocity.X + capped.ForwardVector.Y * capped.Velocity.Y < 0) return;
            NativeInput.Disable((GTA.Control)ControlAccelerate);
            if ((int)capped.ClassType == 13) NativeInput.Disable((GTA.Control)ControlPushbikePedal);
        }

        private static Vehicle DrivenVehicle()
        {
            Ped p = GTA.Game.Player.Character;
            if (p == null || !p.Exists() || p.IsDead) return null;
            Vehicle v = p.CurrentVehicle;
            if (v == null || !v.Exists() || v.Driver != p) return null;
            return v;
        }

        private void Release()
        {
            if (capped != null)
            {
                try { if (capped.Exists()) N.Call(SET_ENTITY_MAX_SPEED, capped, NoCap); }
                catch (Exception ex) { Log.Warn("Speed limiter release failed: " + ex.Message); }
            }
            capped = null;
            appliedKmh = -1;
        }

        public void SetEnabled(bool on)
        {
            main.Config.Settings.SpeedLimiterEnabled = on;
            SaveSettings();
            if (!on) Release();
            announcedHandle = 0;
            main.Notify(on ? L.F("Speed limiter on: {0} km/h.", "Ограничитель скорости включён: {0} км/ч.", LimitKmh)
                           : L.S("Speed limiter off.", "Ограничитель скорости выключен."));
        }

        public void Toggle() { SetEnabled(!Enabled); }

        public void SetLimit(int kmh)
        {
            main.Config.Settings.SpeedLimiterKmh = SpeedLimiterRules.Clamp(kmh);
            SaveSettings();
            appliedKmh = -1; // re-applied on the next update, also while driving
        }

        // Shows GTA's on-screen keyboard and returns the typed text (null if cancelled).
        // Blocks this script only while the keyboard is open.
        public static string AskNumber(string current)
        {
            const ulong DISPLAY_ONSCREEN_KEYBOARD = 0x00DC833F2568DBF6;
            const ulong UPDATE_ONSCREEN_KEYBOARD = 0x0CF2B696BBF945AE;
            const ulong GET_ONSCREEN_KEYBOARD_RESULT = 0x8362B09B91893647;
            N.Call(DISPLAY_ONSCREEN_KEYBOARD, 0, "FMMC_KEY_TIP8", "", current ?? "", "", "", "", 4);
            int start = Environment.TickCount;
            while (true)
            {
                Script.Wait(0);
                int state = N.Call<int>(UPDATE_ONSCREEN_KEYBOARD);
                if (state == 1) return N.Call<string>(GET_ONSCREEN_KEYBOARD_RESULT);
                if (state == 2 || state == 3) return null;       // cancelled / failed to display
                if (state == -1 && unchecked(Environment.TickCount - start) > 1000) return null; // never became active
                if (unchecked(Environment.TickCount - start) > 120000) return null;
            }
        }

        public string StatusText()
        {
            if (!Enabled) return L.S("Off", "Выкл");
            return ActiveNow ? L.F("On, {0} km/h (active)", "Вкл, {0} км/ч (работает)", LimitKmh)
                             : L.F("On, {0} km/h", "Вкл, {0} км/ч", LimitKmh);
        }

        private void SaveSettings()
        {
            try { main.Config.SaveSettings(); }
            catch (Exception ex) { Log.Error("Could not save settings", ex); }
        }
    }
}
