// Realistic Life Simulator - compact driving panel: speed limit + fuel
// (+ 1.1.0: a small second line "AUTO 60" / "LIMITER 60" while the speed limiter is active).
// It sits directly ABOVE the existing SpeedoSignals speedometer (position read, read-only,
// from scripts\SpeedoSignals.ini) and has the same width, so it never overlaps it.
// This mod does not draw a speedometer of its own.

using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using GTA;
using GTA.UI;
using RealisticLifeSimulator.Core;
using Font = GTA.UI.Font;

namespace RealisticLifeSimulator.Game
{
    public sealed class DriveHud : IFrameModule
    {
        private const float W = 170f, H = 40f, Gap = 4f;

        private readonly RlsMain main;
        private readonly FuelSystem fuel;
        private readonly TrafficLawSystem traffic;
        private float x = 1095f, y = 620f - H - Gap;

        private readonly ContainerElement bg, barBack, barFill;
        private readonly TextElement limitText, fuelText, limiterText;

        private static readonly Color Normal = Color.FromArgb(255, 236, 238, 241);
        private static readonly Color Dim = Color.FromArgb(200, 170, 176, 186);
        private static readonly Color Warn = Color.FromArgb(255, 232, 170, 70);
        private static readonly Color Bad = Color.FromArgb(255, 226, 88, 76);
        private static readonly Color Good = Color.FromArgb(255, 92, 148, 214);

        public DriveHud(RlsMain main, FuelSystem fuel, TrafficLawSystem traffic)
        {
            this.main = main;
            this.fuel = fuel;
            this.traffic = traffic;
            ReadSpeedoPosition();

            bg = new ContainerElement(new PointF(x, y), new SizeF(W, H), Color.FromArgb(150, 0, 0, 0));
            barBack = new ContainerElement(new PointF(x + 10f, y + 29f), new SizeF(W - 20f, 5f), Color.FromArgb(90, 255, 255, 255));
            barFill = new ContainerElement(new PointF(x + 10f, y + 29f), new SizeF(0f, 5f), Good);
            limitText = new TextElement("", new PointF(x + 10f, y + 3f), 0.3f, Dim, Font.ChaletLondon, Alignment.Left, true, false);
            fuelText = new TextElement("", new PointF(x + W - 10f, y + 3f), 0.3f, Normal, Font.ChaletLondon, Alignment.Right, true, false);
            // 1.1.0: second line, shown only while the speed limiter caps the vehicle.
            limiterText = new TextElement("", new PointF(x + 10f, y + 15f), 0.24f, Good, Font.ChaletLondon, Alignment.Left, true, false);
        }

        private void ReadSpeedoPosition()
        {
            try
            {
                string ini = Path.Combine(main.Paths.GameRoot, "scripts", "SpeedoSignals.ini");
                if (!File.Exists(ini)) return;
                IniFile f = IniFile.Load(ini);
                float sx, sy;
                if (float.TryParse(f.Get("Position", "X", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out sx)
                    && float.TryParse(f.Get("Position", "Y", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out sy))
                {
                    x = sx;
                    y = Math.Max(0f, sy - H - Gap);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read speedometer position: " + ex.Message);
            }
        }

        public void OnFrame()
        {
            if (main.Menu.IsOpen) return;
            Ped p = GTA.Game.Player.Character;
            if (p == null || !p.Exists() || p.IsDead) return;
            Vehicle v = p.CurrentVehicle;
            if (v == null || !v.Exists() || v.Driver != p) return;
            if (N.HudBlocked()) return;

            RlsSettings set = main.Config.Settings;
            double frac = set.FuelEnabled ? fuel.CurrentFraction : -1;
            bool lowFuel = frac >= 0 && frac * 100 <= Math.Max(set.LowFuelWarningPercent, 1);
            bool showFuel = frac >= 0 && (set.FuelGauge == FuelGaugeMode.Always || (set.FuelGauge == FuelGaugeMode.LowOnly && lowFuel));
            int limit = set.TrafficEnabled && set.ShowSpeedLimit ? traffic.CurrentLimit : 0;
            SpeedLimiter lim = main.Limiter;
            int capKmh = lim != null ? lim.AppliedKmh : 0;
            if (!showFuel && limit <= 0 && capKmh <= 0) return;

            bg.Draw();

            if (capKmh > 0)
            {
                // AUTO 60 = automatic road limit (+10) in use; LIMITER 60 = manual limiter.
                // While slowing down for a lower limit the cap steps down; show the target limit.
                limiterText.Caption = lim.AutoActiveNow
                    ? L.F("AUTO {0}", "АВТО {0}", lim.AutoTargetKmh)
                    : L.F("LIMITER {0}", "ОГРАНИЧ. {0}", capKmh);
                limiterText.Draw();
            }

            if (limit > 0)
            {
                limitText.Caption = L.F("LIMIT {0}", "ОГРАН. {0}", limit);
                limitText.Color = traffic.IsSpeeding ? Bad : Dim;
                limitText.Draw();
            }

            if (showFuel)
            {
                float f = (float)Math.Max(0, Math.Min(1, frac));
                fuelText.Caption = L.F("FUEL {0:0}%", "ТОПЛИВО {0:0}%", f * 100f);
                fuelText.Color = f <= 0.05f ? Bad : (lowFuel ? Warn : Normal);
                fuelText.Draw();
                barBack.Draw();
                barFill.Size = new SizeF((W - 20f) * f, 5f);
                barFill.Color = f <= 0.05f ? Bad : (lowFuel ? Warn : Good);
                barFill.Draw();
            }
        }
    }
}
