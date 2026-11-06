// Realistic Life Simulator - English / Russian text.
// Every visible string is written as L.S("English", "Русский") at its call site, so a
// translation can never be missing. Source files are UTF-8; build.ps1 passes /codepage:65001.

using System;
using System.Globalization;

namespace RealisticLifeSimulator.Core
{
    public enum LanguageSetting { Auto, English, Russian }

    public enum UiLanguage { English, Russian }

    public static class L
    {
        private static UiLanguage current = UiLanguage.English;
        private static CultureInfo culture = CultureInfo.GetCultureInfo("en-US");

        public static UiLanguage Current { get { return current; } }
        public static bool IsRussian { get { return current == UiLanguage.Russian; } }
        public static CultureInfo Culture { get { return culture; } }

        public static void Set(UiLanguage lang)
        {
            current = lang;
            culture = CultureInfo.GetCultureInfo(lang == UiLanguage.Russian ? "ru-RU" : "en-US");
        }

        // Auto: Russian if the game runs in Russian, or if Windows uses a Russian locale.
        public static UiLanguage Resolve(LanguageSetting setting, bool gameIsRussian)
        {
            if (setting == LanguageSetting.English) return UiLanguage.English;
            if (setting == LanguageSetting.Russian) return UiLanguage.Russian;
            if (gameIsRussian) return UiLanguage.Russian;
            string c = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
            string ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return (c == "ru" || ui == "ru") ? UiLanguage.Russian : UiLanguage.English;
        }

        public static string S(string english, string russian)
        {
            return current == UiLanguage.Russian ? russian : english;
        }

        public static string F(string english, string russian, params object[] args)
        {
            return string.Format(culture, S(english, russian), args);
        }

        public static string Money(double amount)
        {
            string sign = amount < 0 ? "-" : "";
            // ru-RU groups digits with a non-breaking space, which GTA's fonts may not draw.
            string n = Math.Abs(amount).ToString(Math.Abs(amount) >= 1000 ? "N0" : "N2", culture)
                .Replace(' ', ' ').Replace(' ', ' ');
            return sign + "$" + n;
        }

        public static string Liters(double liters)
        {
            return liters.ToString("0.0", culture) + S(" L", " л");
        }

        public static string OnOff(bool on)
        {
            return on ? S("On", "Вкл") : S("Off", "Выкл");
        }

        public static string LanguageName(LanguageSetting s)
        {
            switch (s)
            {
                case LanguageSetting.English: return "English";
                case LanguageSetting.Russian: return "Русский";
            }
            return S("Automatic", "Автоматически");
        }

        public static string DateTimeLocal(DateTime utc)
        {
            return utc.ToLocalTime().ToString(IsRussian ? "dd.MM HH:mm" : "MM/dd HH:mm", culture);
        }
    }
}
