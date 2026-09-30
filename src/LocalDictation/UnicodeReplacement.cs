using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LocalDictation
{
    public static class UnicodeReplacement
    {
        static readonly Dictionary<string, string> fold = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "casefold.json")));
        sealed class Folded
        {
            public string Text; public List<int> Starts = new List<int>(), Ends = new List<int>();
            public Folded(string input, bool sensitive) { var b = new StringBuilder(); int index = 0; foreach (var rune in input.EnumerateRunes()) { string value = rune.ToString(); string transformed = !sensitive && fold.TryGetValue(value, out var mapped) ? mapped : value; for (int j = 0; j < transformed.Length; j++) { Starts.Add(j == 0 ? index : -1); Ends.Add(j == transformed.Length - 1 ? index + value.Length : -1); } b.Append(transformed); index += value.Length; } Text = b.ToString(); }
        }
        static bool Word(string text, int index) { if (index < 0 || index >= text.Length) return false; if (char.IsLowSurrogate(text[index]) && index > 0) index--; var c = CharUnicodeInfo.GetUnicodeCategory(text, index); return c == UnicodeCategory.UppercaseLetter || c == UnicodeCategory.LowercaseLetter || c == UnicodeCategory.TitlecaseLetter || c == UnicodeCategory.ModifierLetter || c == UnicodeCategory.OtherLetter || c == UnicodeCategory.NonSpacingMark || c == UnicodeCategory.SpacingCombiningMark || c == UnicodeCategory.DecimalDigitNumber || c == UnicodeCategory.LetterNumber || c == UnicodeCategory.OtherNumber || text[index] == '_'; }
        public static string Apply(string input, ReplacementRule rule) { string source = rule.Source.Normalize(NormalizationForm.FormC); var text = new Folded(input, rule.CaseSensitive); string needle = new Folded(source, rule.CaseSensitive).Text; var output = new StringBuilder(); int search = 0, copied = 0; while (search <= text.Text.Length - needle.Length) { int at = text.Text.IndexOf(needle, search, StringComparison.Ordinal); if (at < 0) break; int endAt = at + needle.Length - 1; int start = text.Starts[at], end = text.Ends[endAt]; if (start >= copied && end >= 0 && (!rule.Whole || !Word(input, start - 1) && !Word(input, end))) { output.Append(input, copied, start - copied); output.Append(rule.Target ?? ""); copied = end; search = endAt + 1; if (output.Length > 10 * 1024 * 1024) throw new InvalidDataException("Das Wörterbuch erzeugt zu viel Text."); } else search = at + 1; } output.Append(input, copied, input.Length - copied); return output.ToString(); }
    }
}
