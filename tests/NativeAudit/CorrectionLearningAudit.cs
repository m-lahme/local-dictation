using System;
using System.Collections.Generic;
using System.Linq;
using LocalDictation;

internal static class CorrectionLearningAudit
{
    internal static void Run()
    {
        int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("Correction learning: " + name); count++; }

        var typo = CorrectionLearning.Analyze("Parakeet ist schnell.", "Paraket ist schnell.");
        Check(typo.Accepted && typo.Suggestions.Count == 1, "single-word correction accepted");
        Check(typo.Suggestions[0].Source == "Parakeet" && typo.Suggestions[0].Target == "Paraket" && typo.Suggestions[0].Selected && typo.Suggestions[0].Whole, "single-word proposal is safe and selected");

        var casing = CorrectionLearning.Analyze("ich nutze claude code täglich", "Ich nutze Claude Code täglich");
        Check(casing.Accepted && !casing.Suggestions.Any(x => x.Source == "ich"), "sentence-start casing is not learned globally");
        Check(casing.Suggestions.Any(x => x.Source == "claude" && x.Target == "Claude"), "proper-name casing detected");

        var unicode = CorrectionLearning.Analyze("Die Strasse führt nach Koln.", "Die Straße führt nach Köln.");
        Check(unicode.Accepted && unicode.Suggestions.Count == 2 && unicode.Suggestions.All(x => x.Selected), "Unicode word corrections stay separate");

        var joined = CorrectionLearning.Analyze("Ich nutze Git Hub jeden Tag.", "Ich nutze GitHub jeden Tag.");
        Check(joined.Accepted && joined.Suggestions.Single().Source == "Git Hub" && joined.Suggestions.Single().Selected, "split and join correction selected");

        var punctuation = CorrectionLearning.Analyze("Hallo Welt", "Hallo, Welt!");
        Check(punctuation.Accepted && punctuation.Suggestions.Count == 1 && !punctuation.Suggestions[0].Selected, "punctuation changes are offered for review");

        var insertion = CorrectionLearning.Analyze("Das ist gut.", "Das ist wirklich gut.");
        Check(insertion.Accepted && insertion.Suggestions.Count == 1 && insertion.Suggestions[0].Source == "gut." && insertion.Suggestions[0].Target == "wirklich gut." && !insertion.Suggestions[0].Selected, "insertions offer a nonempty contextual source for review");

        var deletion = CorrectionLearning.Analyze("Das ist wirklich gut.", "Das ist gut.");
        Check(deletion.Accepted && deletion.Suggestions.Count == 1 && !deletion.Suggestions[0].Selected && deletion.Suggestions[0].Target.Length==0, "deletions are offered but not preselected");

        var dotnet = CorrectionLearning.Analyze("Ich nutze .NET für dieses Tool.", "Ich nutze dot net für dieses Tool.");
        Check(dotnet.Accepted && dotnet.Suggestions.Count == 1 && dotnet.Suggestions.Any(x=>x.Source==".NET"&&x.Target=="dot net"&&x.Selected), "punctuation and split spelling correction is proposed");

        var rewrite = CorrectionLearning.Analyze("Heute scheint die Sonne über Berlin.", "Völlig anderer Inhalt ohne Zusammenhang.");
        Check(!rewrite.Accepted && rewrite.Suggestions.Count == 0, "unrelated selection rejected");

        var phrase = CorrectionLearning.Analyze("Das lokale Modell macht Fehler und wurde sofort geprüft.", "Das schnelle System zeigt Ergebnisse und wurde sofort geprüft.");
        Check(phrase.Accepted && phrase.Suggestions.Count == 1 && phrase.Suggestions.All(x => !x.Selected), "broad phrase changes require review");

        var existing = new[] { new ReplacementRule { Source = "cloud", Target = "Claude", Whole = true }, new ReplacementRule { Source = "Repo", Target = "Repository", Whole = true } };
        var duplicate = CorrectionLearning.Analyze("cloud und Repo", "Claude und Repositorium", existing);
        Check(duplicate.Suggestions.Count == 1 && duplicate.Suggestions[0].Source == "Repo" && duplicate.Suggestions[0].ConflictsWithExisting && !duplicate.Suggestions[0].Selected, "existing duplicate removed and conflict marked");

        var normalized = CorrectionLearning.Analyze("Cafe\u0301 ist offen.", "Café ist offen.");
        Check(normalized.Accepted && normalized.Suggestions.Count == 0, "canonical Unicode normalization avoids false rule");

        var editable = typo.Suggestions[0]; editable.Whole = false; editable.CaseSensitive = true;
        var rule = editable.ToRule();
        Check(!rule.Whole && rule.CaseSensitive && rule.Source == editable.Source && rule.Target == editable.Target, "editable suggestion converts to replacement rule");
        Console.WriteLine("PASS correction learning " + count);
    }
}
