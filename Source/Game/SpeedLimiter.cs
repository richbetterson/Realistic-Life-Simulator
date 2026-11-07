// Realistic Life Simulator - custom vehicle speed limiter (0.6.0, automatic road limits 1.1.0).
//
// Life Hub > Vehicle controls > Speed limiter: enter a number (km/h) and switch it on.
// While the player drives a supported vehicle:
//   * a hard cap slightly above the limit is applied to that vehicle (SET_ENTITY_MAX_SPEED,
//     metres per second), which also holds the speed downhill without braking jolts;
//   * from 1 km/h below the limit the accelerator input is ignored for that frame, so the
//     car settles smoothly at the limit. Steering, braking and reversing are never touched.
// The cap is removed from a vehicle as soon as the player stops driving it (switching cars,
// getting out, disabling the limiter, unloading the mod). Aircraft and trains are ignored.
//
// 1.1.0 automatic speed limit (setting "Automatic road limit", off by default):
//   * once per second the road limit is read (the same value the LIMIT panel shows) and the
//     limiter is set to that limit + 10 km/h (50 -> 60, 110 -> 120). It works without
//     switching the manual limiter on;
//   * a new road limit must be read twice in a row before it applies (no flicker at junctions);
//     no notification is shown when the limit changes - the panel shows it;
//   * when the limit drops below the current speed the cap is lowered in steps (smooth);
//   * if the road cannot be read reliably (off-road), the last limit is kept for 15 s, then
//     the manual limiter value is used as the fallback;
//   * boats are not limited by the automatic mode (no roads); the manual limiter still applies.
// With the setting off nothing changes: the manual limiter works exactly as in 0.6.0.

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
        private const int RoadReadMs = 1000;

        // True while GTA's on-screen keyboard is open: the mod's own key handling pauses.
        public static bool TextInputActive;

        private readonly RlsMain main;
        private readonly AutoSpeedLimitTracker tracker = new AutoSpeedLimitTracker();
        private Vehicle capped;
        private int appliedKmh = -1;
        private int announcedHandle;
        private int nextRoadRead;
        private bool autoActive;              // the current cap comes from the automatic mode

        public SpeedLimiter(RlsMain main) { this.main = main; }

        public string Name { get { return "SpeedLimiter"; } }
        public int IntervalMs { get { return 250; } }

        public bool Enabled { get { return main.Config.Settings.SpeedLimiterEnabled; } }
        public int LimitKmh { get { return main.Config.Settings.SpeedLimiterKmh; } }
        public bool AutoMode { get { return main.Config.Settings.AutoSpeedLimit; } }

        // True while the cap is applied to the vehicle the player is driving.
        public bool ActiveNow { get { return capped != null && capped.Exists() && appliedKmh > 0; } }

        // Cap currently applied (km/h), 0 when inactive. May be between two limits while ramping down.
        public int AppliedKmh { get { return ActiveNow ? appliedKmh : 0; } }

        // True while the active cap comes from the automatic road limit.
        public bool AutoActiveNow { get { return ActiveNow && autoActive; } }

        // Detected road limit used by the automatic mode (0 = unknown, the fallback is in use).
        public int AutoRoadLimit { get { return tracker.RoadLimit; } }

        // Limit the automatic mode is aiming for right now (road + 10, or the fallback).
        public int AutoTargetKmh { get { return tracker.Known ? tracker.AppliedLimit : LimitKmh; } }

        public void Start() { }

        public void Shutdown() { Release(); }

        public void Update()
        {
            Vehicle v = DrivenVehicle();
            int cls = v != null ? (int)v.ClassType : -1;
            bool useAuto = AutoMode && v != null && AutoSpeedLimitRules.Supports(cls);
            bool useManual = !useAuto && Enabled && v != null && SpeedLimiterRules.Supports(cls);
            if (!useAuto) tracker.Reset();
            if (!useAuto && !useManual)
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
                    if (useAuto) main.Notify(L.S("Automatic speed limit on: road limit + 10 km/h.", "Автоматическое ограничение: лимит дороги + 10 км/ч."));
                    else main.Notify(L.F("Speed limiter: {0} km/h.", "Ограничитель скорости: {0} км/ч.", LimitKmh));
                }
            }

            int target;
            if (useAuto)
            {
                int now = Environment.TickCount;
                if (unchecked(now - nextRoadRead) >= 0)
                {
                    nextRoadRead = now + RoadReadMs;
                    int road = 0;
                    try { road = main.Traffic != null ? main.Traffic.RoadLimitForAutoLimiter(v.Position) : 0; }
                    catch (Exception ex) { Log.Warn("Automatic speed limit: road read failed: " + ex.Message); }
                    if (tracker.Feed(road, now))
                        Log.Debug("Automatic speed limit: road " + tracker.RoadLimit + " -> cap " + AutoTargetKmh);
                }
                target = AutoSpeedLimitRules.StepCap(appliedKmh, AutoTargetKmh, SpeedLimiterRules.ToKmh(v.Speed));
            }
            else target = LimitKmh;

            autoActive = useAuto;
            if (appliedKmh != target)
            {
                N.Call(SET_ENTITY_MAX_SPEED, v, SpeedLimiterRules.CapMetersPerSecond(target));
                appliedKmh = target;
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
            autoActive = false;
        }

        public void SetEnabled(bool on)
        {
            main.Config.Settings.SpeedLimiterEnabled = on;
            SaveSettings();
            if (!on && !AutoMode) Release();
            announcedHandle = 0;
            main.Notify(on ? L.F("Speed limiter on: {0} km/h.", "Ограничитель скорости включён: {0} км/ч.", LimitKmh)
                           : L.S("Speed limiter off.", "Ограничитель скорости выключен."));
        }

        // The toggle key: while the automatic mode is on it switches everything off (automatic
        // mode and manual limiter); otherwise it switches the manual limiter as before.
        public void Toggle()
        {
            if (AutoMode)
            {
                main.Config.Settings.AutoSpeedLimit = false;
                main.Config.Settings.SpeedLimiterEnabled = false;
                SaveSettings();
                Release();
                tracker.Reset();
                announcedHandle = 0;
                main.Notify(L.S("Speed limiter off (automatic road limit off).", "Ограничитель скорости выключен (автоматический лимит выключен)."));
                return;
            }
            SetEnabled(!Enabled);
        }

        public void SetAuto(bool on)
        {
            main.Config.Settings.AutoSpeedLimit = on;
            SaveSettings();
            Release();          // re-applied on the next update with the right source
            tracker.Reset();
            nextRoadRead = 0;
            announcedHandle = 0;
            main.Notify(on ? L.S("Automatic speed limit on: road limit + 10 km/h.", "Автоматическое ограничение включено: лимит дороги + 10 км/ч.")
                           : (Enabled ? L.F("Automatic speed limit off. Manual limiter: {0} km/h.", "Автоматическое ограничение выключено. Ручной ограничитель: {0} км/ч.", LimitKmh)
                                      : L.S("Automatic speed limit off.", "Автоматическое ограничение выключено.")));
        }

        public void SetLimit(int kmh)
        {
            main.Config.Settings.SpeedLimiterKmh = SpeedLimiterRules.Clamp(kmh);
            SaveSettings();
            if (!autoActive) appliedKmh = -1; // re-applied on the next update, also while driving
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
            if (AutoMode)
            {
                if (!AutoActiveNow) return L.S("Auto (road + 10)", "Авто (дорога + 10)");
                return tracker.Known
                    ? L.F("Auto, {0} km/h (road {1})", "Авто, {0} км/ч (дорога {1})", AutoTargetKmh, tracker.RoadLimit)
                    : L.F("Auto, {0} km/h (fallback)", "Авто, {0} км/ч (резервный)", AutoTargetKmh);
            }
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
