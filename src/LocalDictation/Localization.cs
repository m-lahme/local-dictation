using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LocalDictation
{
    // UI text is written in English in the code and translated through the tables in Strings/. A text without a translation stays English.
    public static class L
    {
        static string language = "en";
        static readonly Dictionary<string, string> german = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly List<string> conflicts = new List<string>();
        static L()
        {
            foreach (var table in new[] { AppStrings.German, MainWindowStrings.German, ServiceStrings.German })
                foreach (var (english, text) in table) { if (german.TryGetValue(english, out var known) && known != text) conflicts.Add(english); german[english] = text; }
        }
        public static string Language { get => language; set => language = value == "de" ? "de" : "en"; }
        public static bool German => language == "de";
        public static CultureInfo Culture => CultureInfo.GetCultureInfo(German ? "de-DE" : "en-US");
        // "system" follows the Windows display language; any language other than German or English falls back to English.
        public static string Resolve(string setting) => setting == "de" || setting == "en" ? setting : SystemLanguage;
        public static string SystemLanguage => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en";
        public static string T(string english) => German && german.TryGetValue(english, out var text) ? text : english;
        public static string F(string english, params object[] values) => String.Format(Culture, T(english), values);
        public static IReadOnlyDictionary<string, string> GermanTexts => german;
        public static IReadOnlyList<string> Conflicts => conflicts;
    }
}
