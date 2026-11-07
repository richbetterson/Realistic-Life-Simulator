// Realistic Life Simulator - passenger and taxi rules (no game dependencies, unit tested).
//
// A driver's attitude towards passengers is separate from the driving personality of
// Phase 4 (careful, aggressive, ...) but derived from it, plus a stable per-driver seed:
// asking the same driver twice under the same conditions gives the same answer.
// Acceptance is a score built from the situation (time of day, wanted level, weapon in
// hand, reputation, vehicle type, weather) compared with that driver's fixed "patience".

using System;

namespace RealisticLifeSimulator.Core
{
    public enum PassengerAttitude { Friendly, Neutral, Cautious, Unfriendly, Suspicious }

    public enum RideRefusal { None, Wanted, Armed, Night, Distrust, Busy, Rude, AskedBefore, Moving }

    // What a driver does when asked to stop.
    public enum StopResponse { StopNow, PullOverNow, PullOverLater }

    public enum RideSituation { Wanted, WeaponOut, Shooting, VehicleOnFire }

    public enum DriverReaction { Ignore, Warn, PullOverAskOut, StopNowAskOut }

    public sealed class RideContext
    {
        public PassengerAttitude Attitude;
        public bool Night;            // 22:00 - 05:59
        public bool PlayerWanted;
        public bool PlayerArmed;      // weapon in hand
        public bool PlayerInjured;
        public double Reputation = 60; // 0..100 (driver reputation of the character)
        public int VehicleClass;
        public bool BadWeather;
        public bool AskedRecently;    // the same driver already said no
        public bool VehicleMoving;    // faster than walking pace when asked
    }

    public sealed class TaxiPricing
    {
        public double BaseFare = 3.0;
        public double PerKm = 2.2;
        public double PerMinuteWaiting = 0.35;
    }

    public static class PassengerRules
    {
        // ------------------------------------------------------------ attitude

        // Weighted by driving personality; the seed picks within the weights (deterministic).
        public static PassengerAttitude AttitudeFor(DriverPersonality p, int seed)
        {
            // Friendly, Neutral, Cautious, Unfriendly, Suspicious
            int[] w;
            switch (p)
            {
                case DriverPersonality.Careful:      w = new[] { 20, 25, 40, 5, 10 }; break;
                case DriverPersonality.Aggressive:   w = new[] { 5, 20, 5, 50, 20 }; break;
                case DriverPersonality.Slow:         w = new[] { 35, 25, 30, 5, 5 }; break;
                case DriverPersonality.Professional: w = new[] { 20, 55, 15, 5, 5 }; break;
                case DriverPersonality.Distracted:   w = new[] { 35, 40, 5, 15, 5 }; break;
                default:                             w = new[] { 25, 40, 15, 12, 8 }; break;
            }
            int total = 0;
            foreach (int x in w) total += x;
            int roll = (int)(Unit(seed, 17) * total);
            for (int i = 0; i < w.Length; i++)
            {
                if (roll < w[i]) return (PassengerAttitude)i;
                roll -= w[i];
            }
            return PassengerAttitude.Neutral;
        }

        // Stable 0..1 value for a seed (no Random: same seed, same value, every session).
        public static double Unit(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u ^ (uint)salt * 40503u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }

        // How much convincing this particular driver needs (0.2 .. 0.8).
        public static double Patience(int seed)
        {
            return 0.2 + 0.6 * Unit(seed, 91);
        }

        public static double BaseWillingness(PassengerAttitude a)
        {
            switch (a)
            {
                case PassengerAttitude.Friendly: return 0.85;
                case PassengerAttitude.Neutral: return 0.60;
                case PassengerAttitude.Cautious: return 0.42;
                case PassengerAttitude.Unfriendly: return 0.22;
            }
            return 0.12; // Suspicious
        }

        public static bool IsWorkClass(int cls)
        {
            return cls == 10 || cls == 11 || cls == 12 || cls == 17 || cls == 20;
        }

        // 0..1. Hard refusals (wanted, weapon) give 0 with a reason.
        public static double Score(RideContext c, out RideRefusal hard)
        {
            hard = RideRefusal.None;
            if (c.PlayerWanted) { hard = RideRefusal.Wanted; return 0; }
            if (c.PlayerArmed) { hard = RideRefusal.Armed; return 0; }
            if (c.AskedRecently) { hard = RideRefusal.AskedBefore; return 0; }

            double s = BaseWillingness(c.Attitude);
            if (c.Night)
                s *= c.Attitude == PassengerAttitude.Cautious || c.Attitude == PassengerAttitude.Suspicious ? 0.45 : 0.75;
            if (c.PlayerInjured) s += c.Attitude == PassengerAttitude.Friendly || c.Attitude == PassengerAttitude.Neutral ? 0.2 : 0.05;
            if (c.BadWeather && c.Attitude != PassengerAttitude.Suspicious) s += 0.08;
            s += ReputationEffect(c.Reputation);
            if (c.VehicleClass == 6 || c.VehicleClass == 7) s *= 0.6;    // sports / super: protective owners
            if (c.VehicleClass == 8) s *= 0.7;                          // motorcycles: pillion is personal
            if (IsWorkClass(c.VehicleClass)) s *= 0.55;                 // on the job
            if (c.VehicleMoving) s *= 0.7;                              // flagging down a moving car
            return Math.Max(0, Math.Min(1, s));
        }

        // A good record helps (up to +0.2 at 100); a bad one hurts, but at most -0.15 at 0,
        // so a ruined reputation makes rides harder without locking them out entirely
        // (reputation recovers only 1 point per 15 clean km).
        public static double ReputationEffect(double reputation)
        {
            double r = Math.Max(0, Math.Min(100, reputation)) - 60;
            return r >= 0 ? r / 200.0 : r / 400.0;
        }

        public static bool Accepts(RideContext c, int seed, out RideRefusal reason)
        {
            RideRefusal hard;
            double s = Score(c, out hard);
            if (hard != RideRefusal.None) { reason = hard; return false; }
            if (s >= Patience(seed)) { reason = RideRefusal.None; return true; }
            // Pick the most plausible soft reason for the "no".
            if (c.Night && (c.Attitude == PassengerAttitude.Cautious || c.Attitude == PassengerAttitude.Suspicious)) reason = RideRefusal.Night;
            else if (IsWorkClass(c.VehicleClass)) reason = RideRefusal.Busy;
            else if (c.Attitude == PassengerAttitude.Unfriendly) reason = RideRefusal.Rude;
            else if (c.VehicleMoving) reason = RideRefusal.Moving;
            else reason = RideRefusal.Distrust;
            return false;
        }

        // ------------------------------------------------------------ during the ride

        // urgent = "stop right now"; otherwise "stop somewhere safe".
        public static StopResponse OnStopRequest(PassengerAttitude a, bool urgent, double speedKmh, int limitKmh)
        {
            bool fastRoad = limitKmh >= 80 || speedKmh > 55;
            if (urgent && !fastRoad)
            {
                if (a == PassengerAttitude.Friendly || a == PassengerAttitude.Neutral || a == PassengerAttitude.Suspicious) return StopResponse.StopNow;
            }
            if (a == PassengerAttitude.Unfriendly) return StopResponse.PullOverLater;
            return StopResponse.PullOverNow;
        }

        // Delay before an unfriendly driver gets round to pulling over (deterministic per driver).
        public static int GrumbleDelayMs(int seed)
        {
            return 6000 + (int)(Unit(seed, 7) * 6000);
        }

        public static DriverReaction React(PassengerAttitude a, RideSituation s, bool warnedBefore)
        {
            switch (s)
            {
                case RideSituation.VehicleOnFire:
                case RideSituation.Shooting:
                    return DriverReaction.StopNowAskOut;
                case RideSituation.Wanted:
                    if (a == PassengerAttitude.Suspicious) return DriverReaction.StopNowAskOut;
                    if (a == PassengerAttitude.Friendly && !warnedBefore) return DriverReaction.Warn;
                    return DriverReaction.PullOverAskOut;
                case RideSituation.WeaponOut:
                    if (a == PassengerAttitude.Cautious || a == PassengerAttitude.Suspicious) return DriverReaction.StopNowAskOut;
                    if (!warnedBefore && (a == PassengerAttitude.Friendly || a == PassengerAttitude.Neutral)) return DriverReaction.Warn;
                    return DriverReaction.PullOverAskOut;
            }
            return DriverReaction.Ignore;
        }

        // Will this driver change their route to drop the passenger at a waypoint?
        public static bool AgreesToDetour(PassengerAttitude a, double km)
        {
            switch (a)
            {
                case PassengerAttitude.Friendly: return km <= 4.0;
                case PassengerAttitude.Neutral: return km <= 2.0;
                case PassengerAttitude.Cautious: return km <= 1.5;
            }
            return false;
        }

        // Seat index as used by GTA: -1 driver, 0 front passenger, 1 rear left, 2 rear right, 3+ extra.
        public static bool IsPassengerSeat(int seat) { return seat >= 0; }

        public static string SeatName(int seat)
        {
            switch (seat)
            {
                case -1: return L.S("Driver", "Водитель");
                case 0: return L.S("Front passenger", "Переднее пассажирское");
                case 1: return L.S("Rear left", "Заднее левое");
                case 2: return L.S("Rear right", "Заднее правое");
            }
            return L.F("Extra seat {0}", "Дополнительное место {0}", seat - 2);
        }

        public static string AttitudeName(PassengerAttitude a)
        {
            switch (a)
            {
                case PassengerAttitude.Friendly: return L.S("Friendly", "Дружелюбный");
                case PassengerAttitude.Cautious: return L.S("Cautious", "Осторожный");
                case PassengerAttitude.Unfriendly: return L.S("Unfriendly", "Недружелюбный");
                case PassengerAttitude.Suspicious: return L.S("Suspicious", "Подозрительный");
            }
            return L.S("Neutral", "Нейтральный");
        }

        // ------------------------------------------------------------ driver lines

        public static string AcceptLine(PassengerAttitude a)
        {
            switch (a)
            {
                case PassengerAttitude.Friendly: return L.S("Sure, hop in! Where are you headed?", "Конечно, садитесь! Куда направляетесь?");
                case PassengerAttitude.Cautious: return L.S("Alright... but no funny business.", "Ладно... только без глупостей.");
                case PassengerAttitude.Unfriendly: return L.S("Fine. Get in and keep quiet.", "Ладно. Садись и помалкивай.");
                case PassengerAttitude.Suspicious: return L.S("Hm. Okay, get in. I'm watching you.", "Хм. Ладно, садись. Я за тобой слежу.");
            }
            return L.S("Okay, get in. I'm going my way, though.", "Хорошо, садитесь. Но я еду по своим делам.");
        }

        public static string RefuseLine(PassengerAttitude a, RideRefusal r)
        {
            switch (r)
            {
                case RideRefusal.Wanted: return L.S("No way! The cops are after you!", "Ни за что! Тебя полиция ищет!");
                case RideRefusal.Armed: return L.S("Put the gun away! I'm not taking you anywhere!", "Убери оружие! Никуда я тебя не повезу!");
                case RideRefusal.AskedBefore: return L.S("I already said no.", "Я уже сказал - нет.");
                case RideRefusal.Night: return L.S("Sorry, I don't pick up strangers at night.", "Извините, ночью я незнакомцев не подвожу.");
                case RideRefusal.Busy: return L.S("Sorry, I'm working. Can't take passengers.", "Извините, я на работе. Пассажиров не беру.");
                case RideRefusal.Moving: return L.S("I'm not stopping here, sorry.", "Я тут не остановлюсь, извините.");
                case RideRefusal.Rude: return L.S("Get lost.", "Отвали.");
            }
            switch (a)
            {
                case PassengerAttitude.Friendly: return L.S("Sorry, not today - I'm in a hurry.", "Извините, не сегодня - я тороплюсь.");
                case PassengerAttitude.Suspicious: return L.S("I don't know you. Step away from my car.", "Я тебя не знаю. Отойди от машины.");
                case PassengerAttitude.Cautious: return L.S("Sorry, I don't give rides to strangers.", "Извините, я не подвожу незнакомых.");
            }
            return L.S("No, sorry.", "Нет, извините.");
        }

        public static string StopLine(PassengerAttitude a, StopResponse r)
        {
            if (r == StopResponse.StopNow) return L.S("Okay, stopping here.", "Хорошо, останавливаюсь.");
            if (r == StopResponse.PullOverLater) return L.S("Ugh. Wait till I find a spot.", "Тьфу. Подожди, найду место.");
            return a == PassengerAttitude.Cautious
                ? L.S("Not here, it's not safe. I'll pull over just ahead.", "Тут небезопасно. Остановлюсь чуть дальше.")
                : L.S("Let me find a safe spot to pull over.", "Сейчас найду безопасное место.");
        }

        public static string AskOutLine(RideSituation s)
        {
            switch (s)
            {
                case RideSituation.Wanted: return L.S("The police are after you? Get out of my car!", "Тебя ищет полиция? Выходи из машины!");
                case RideSituation.WeaponOut: return L.S("A gun?! I'm stopping. Get out!", "Оружие?! Я останавливаюсь. Выходи!");
                case RideSituation.Shooting: return L.S("Are you crazy?! Get out!", "Ты с ума сошёл?! Выходи!");
                case RideSituation.VehicleOnFire: return L.S("The car's on fire! Get out, now!", "Машина горит! Выходи, быстро!");
            }
            return L.S("This is your stop.", "Вам выходить.");
        }

        public static string WarnLine(RideSituation s)
        {
            return s == RideSituation.Wanted
                ? L.S("Hey... are the police looking for you? I don't want trouble.", "Эй... тебя что, полиция ищет? Мне не нужны проблемы.")
                : L.S("Whoa, put that away in my car!", "Эй, убери это в моей машине!");
        }
    }

    public static class TaxiRules
    {
        public const double RoadFactor = 1.35; // straight line -> road distance estimate

        // Whole dollars, rounded up (GTA cash is an integer).
        public static int Fare(TaxiPricing p, double km, double waitingMinutes)
        {
            if (p == null) p = new TaxiPricing();
            double f = Math.Max(0, p.BaseFare) + Math.Max(0, km) * Math.Max(0, p.PerKm) + Math.Max(0, waitingMinutes) * Math.Max(0, p.PerMinuteWaiting);
            if (double.IsNaN(f) || double.IsInfinity(f)) return 0;
            return (int)Math.Ceiling(f - 1e-9);
        }

        public static int Estimate(TaxiPricing p, double straightKm)
        {
            return Fare(p, straightKm * RoadFactor, 0);
        }

        // Splits a fare into what the cash covers and what becomes unpaid debt.
        public static void Settle(int fare, int cash, out int paidNow, out int toDebt)
        {
            if (fare < 0) fare = 0;
            paidNow = Math.Min(Math.Max(0, cash), fare);
            toDebt = fare - paidNow;
        }
    }

    public class PassengerStats
    {
        public int Rides;
        public int Refusals;
        public double DistanceKm;
        public int TaxiRides;
        public double TaxiSpent;
    }
}
