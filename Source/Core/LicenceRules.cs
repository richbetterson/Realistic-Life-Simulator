// Realistic Life Simulator - driving licence status: the one source of truth (0.6.0).
//
// Every system (Life Hub, police stops, accident investigations, patrol checks) asks
// LicenceRules.Status(...). Nothing else decides whether a licence is valid.
//
//   Valid      no restriction.
//   Suspended  12+ active points (ends by itself when points expire), or a timed
//              suspension imposed by the police (e.g. caught driving while suspended).
//   Revoked    24+ active points, driving while suspended a second time, or a serious
//              pedestrian collision. Ends only by reinstatement in the Life Hub after the
//              waiting period, with a fee and no unpaid fines.

using System;

namespace RealisticLifeSimulator.Core
{
    public enum LicenceStatus { Valid, Suspended, Revoked }

    public static class LicenceRules
    {
        public const int RevokePoints = 24;
        public const double RevocationSeconds = 3 * 3600;      // waiting period (play time)
        public const double PoliceSuspensionSeconds = 3600;    // timed suspension
        public const double ReinstatementFee = 1500;

        public static LicenceStatus Status(DriverProfile p, double play)
        {
            if (p == null) return LicenceStatus.Valid;
            if (p.RevokedAtPlay > 0) return LicenceStatus.Revoked;
            if (p.SuspendedUntilPlay > play || p.IsSuspended(play)) return LicenceStatus.Suspended;
            return LicenceStatus.Valid;
        }

        public static bool IsValid(DriverProfile p, double play)
        {
            return Status(p, play) == LicenceStatus.Valid;
        }

        // Applies automatic transitions (24+ points -> revoked). Safe to call any time;
        // returns true when the status changed so the caller can tell the player.
        public static bool Update(DriverProfile p, double play)
        {
            if (p == null || p.RevokedAtPlay > 0) return false;
            if (p.ActivePoints(play) >= RevokePoints)
            {
                Revoke(p, play, L.S("24 or more licence points", "24 и более штрафных баллов"));
                return true;
            }
            return false;
        }

        public static void Revoke(DriverProfile p, double play, string reason)
        {
            if (p == null) return;
            if (p.RevokedAtPlay <= 0)
            {
                p.RevokedAtPlay = Math.Max(1, play);
                p.Revocations++;
            }
            p.RevocationEndsPlay = Math.Max(p.RevocationEndsPlay, play + RevocationSeconds);
            p.StatusReason = reason ?? "";
        }

        public static void Suspend(DriverProfile p, double play, double seconds, string reason)
        {
            if (p == null || p.RevokedAtPlay > 0) return;
            p.SuspendedUntilPlay = Math.Max(p.SuspendedUntilPlay, play) + Math.Max(0, seconds);
            p.Suspensions++;
            p.StatusReason = reason ?? "";
        }

        // Caught driving without a valid licence: the first time extends the suspension,
        // a second time (or while revoked) leads to / extends revocation.
        public static void CaughtDriving(DriverProfile p, double play)
        {
            if (p == null) return;
            LicenceStatus s = Status(p, play);
            if (s == LicenceStatus.Revoked)
            {
                p.RevocationEndsPlay = Math.Max(p.RevocationEndsPlay, play) + PoliceSuspensionSeconds;
                return;
            }
            if (s == LicenceStatus.Suspended)
            {
                if (p.CaughtWhileSuspended >= 1)
                    Revoke(p, play, L.S("Driving while suspended (repeat)", "Повторная езда с приостановленными правами"));
                else
                    Suspend(p, play, PoliceSuspensionSeconds, L.S("Driving while suspended", "Езда с приостановленными правами"));
                p.CaughtWhileSuspended++;
            }
        }

        public static bool CanReinstate(DriverProfile p, double play, double unpaidFines, double cash, out string reason)
        {
            reason = "";
            if (p == null || p.RevokedAtPlay <= 0) { reason = L.S("Your licence is not revoked.", "Права не отозваны."); return false; }
            if (play < p.RevocationEndsPlay)
            {
                reason = L.F("Waiting period: {0} more minute(s) of play.", "Срок лишения: ещё {0} мин игры.",
                    (int)Math.Ceiling((p.RevocationEndsPlay - play) / 60.0));
                return false;
            }
            if (unpaidFines > 0.5) { reason = L.S("Pay all outstanding fines first.", "Сначала оплатите все штрафы."); return false; }
            if (cash < ReinstatementFee) { reason = L.S("Not enough cash for the reinstatement fee.", "Недостаточно наличных для оплаты пошлины."); return false; }
            return true;
        }

        // Licence restored: points cleared, a fresh start (reputation is kept).
        public static void Reinstate(DriverProfile p, double play)
        {
            if (p == null) return;
            p.RevokedAtPlay = 0;
            p.RevocationEndsPlay = 0;
            p.SuspendedUntilPlay = 0;
            p.CaughtWhileSuspended = 0;
            p.Points.Clear();
            p.Reinstatements++;
            p.StatusReason = "";
        }

        // Seconds until the licence becomes valid on its own (suspension), or until
        // reinstatement can be requested (revocation). 0 = valid now / can apply now.
        public static double SecondsRemaining(DriverProfile p, double play)
        {
            if (p == null) return 0;
            LicenceStatus s = Status(p, play);
            if (s == LicenceStatus.Revoked) return Math.Max(0, p.RevocationEndsPlay - play);
            if (s != LicenceStatus.Suspended) return 0;
            double byTimer = Math.Max(0, p.SuspendedUntilPlay - play);
            double byPoints = 0;
            if (p.IsSuspended(play))
            {
                // Points expire one entry at a time: find when the total drops below the threshold.
                System.Collections.Generic.List<PointEntry> active = new System.Collections.Generic.List<PointEntry>();
                foreach (PointEntry e in p.Points) if (e.ExpiresAtPlaySeconds > play) active.Add(e);
                active.Sort((a, b) => a.ExpiresAtPlaySeconds.CompareTo(b.ExpiresAtPlaySeconds));
                int sum = 0;
                foreach (PointEntry e in active) sum += e.Points;
                foreach (PointEntry e in active)
                {
                    if (sum < PoliceRules.SuspensionPoints) break;
                    sum -= e.Points;
                    byPoints = e.ExpiresAtPlaySeconds - play;
                }
            }
            return Math.Max(byTimer, byPoints);
        }

        public static string StatusName(LicenceStatus s)
        {
            switch (s)
            {
                case LicenceStatus.Suspended: return L.S("Suspended", "Приостановлены");
                case LicenceStatus.Revoked: return L.S("Revoked", "Отозваны (лишение)");
            }
            return L.S("Valid", "Действительны");
        }

        // What the officer says when handed the licence. Never "valid" unless it is.
        public static string OfficerLine(DriverProfile p, double play)
        {
            int pts = p.ActivePoints(play);
            switch (Status(p, play))
            {
                case LicenceStatus.Revoked:
                    return L.F("Licence {0}... this licence has been REVOKED. You are not allowed to drive.",
                               "Права {0}... эти права ОТОЗВАНЫ. Вы лишены права управления.", p.LicenceNumber);
                case LicenceStatus.Suspended:
                    return L.F("Licence {0}... it's SUSPENDED ({1} points). You may not drive right now.",
                               "Права {0}... они ПРИОСТАНОВЛЕНЫ ({1} баллов). Сейчас вам нельзя управлять машиной.", p.LicenceNumber, pts);
            }
            return L.F("Licence {0}, {1}. Valid, {2} active point(s).", "Права {0}, {1}. Действительны, активных баллов: {2}.",
                p.LicenceNumber, p.Character, pts);
        }
    }
}
