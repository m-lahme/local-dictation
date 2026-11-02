using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LocalDictation;

internal static class LocalizationAudit
{
    static readonly Regex Call = new Regex(@"\bL\.(?:T|F)\(\s*""((?:[^""\\]|\\.)*)""");
    static readonly Regex Placeholder = new Regex(@"\{\d+(?:[:,][^}]*)?\}");

    // Every literal text passed to L.T or L.F needs a German translation that keeps the same placeholders.
    public static void Run(string root)
    {
        string previous = L.Language;
        try
        {
            var sources = Directory.GetFiles(Path.Combine(root, "src", "LocalDictation"), "*.cs", SearchOption.AllDirectories).Where(x => !x.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !x.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));
            var texts = sources.SelectMany(x => Call.Matches(File.ReadAllText(x)).Select(m => Regex.Unescape(m.Groups[1].Value))).Distinct(StringComparer.Ordinal).ToList();
            var missing = texts.Where(x => !L.GermanTexts.ContainsKey(x)).ToList();
            Check(missing.Count == 0, "every localized text has a German translation" + (missing.Count == 0 ? " [" + texts.Count + "]" : ": " + String.Join(" | ", missing.Take(20))));
            Check(L.Conflicts.Count == 0, "translation tables agree on shared texts" + (L.Conflicts.Count == 0 ? "" : ": " + String.Join(" | ", L.Conflicts)));
            var broken = L.GermanTexts.Where(x => !Placeholder.Matches(x.Key).Select(m => m.Value).OrderBy(v => v).SequenceEqual(Placeholder.Matches(x.Value).Select(m => m.Value).OrderBy(v => v))).Select(x => x.Key).ToList();
            Check(broken.Count == 0, "German translations keep the format placeholders" + (broken.Count == 0 ? "" : ": " + String.Join(" | ", broken)));
            Check(L.Resolve("de") == "de" && L.Resolve("en") == "en" && L.Resolve("fr") == L.SystemLanguage && L.Resolve("system") == L.SystemLanguage, "language setting resolves explicit choices and the system language");
            var ui = CultureInfo.CurrentUICulture;
            try { CultureInfo.CurrentUICulture = new CultureInfo("fr-FR"); Check(L.Resolve("system") == "en", "other system languages fall back to English"); CultureInfo.CurrentUICulture = new CultureInfo("de-AT"); Check(L.Resolve("system") == "de", "German system variants select German"); }
            finally { CultureInfo.CurrentUICulture = ui; }
            L.Language = "de"; Check(L.T("Language") == "Sprache" && L.T("not translated") == "not translated", "German uses the table and keeps unknown texts");
            L.Language = "en"; Check(L.T("Language") == "Language", "English shows the source text");
        }
        finally { L.Language = previous; }
    }

    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }
}
