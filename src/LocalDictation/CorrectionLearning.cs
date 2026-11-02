using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LocalDictation
{
    public sealed class RuleSuggestion
    {
        public string Source { get; set; } = "";
        public string Target { get; set; } = "";
        public bool Whole { get; set; } = true;
        public bool CaseSensitive { get; set; }
        public bool Selected { get; set; }
        public bool ConflictsWithExisting { get; internal set; }
        public string Reason { get; internal set; } = "";

        public ReplacementRule ToRule() => new ReplacementRule
        {
            Source = Source,
            Target = Target,
            Whole = Whole,
            CaseSensitive = CaseSensitive
        };
    }

    public sealed class CorrectionAnalysis
    {
        public bool Accepted { get; internal set; }
        public double Similarity { get; internal set; }
        public string Message { get; internal set; } = "";
        public IReadOnlyList<RuleSuggestion> Suggestions { get; internal set; } = Array.Empty<RuleSuggestion>();
    }

    public static class CorrectionLearning
    {
        enum TokenKind { Word, Punctuation }
        sealed class Token
        {
            public string Value = "";
            public int Start, End;
            public TokenKind Kind;
        }
        readonly struct Match
        {
            public readonly int Original, Corrected;
            public Match(int original, int corrected) { Original = original; Corrected = corrected; }
        }

        public const double DefaultMinimumSimilarity = .35;

        public static CorrectionAnalysis Analyze(string original, string corrected, IEnumerable<ReplacementRule> existingRules = null, double minimumSimilarity = DefaultMinimumSimilarity)
        {
            original = (original ?? "").Normalize(NormalizationForm.FormC);
            corrected = (corrected ?? "").Normalize(NormalizationForm.FormC);
            minimumSimilarity = Math.Clamp(minimumSimilarity, 0, 1);
            if (String.IsNullOrWhiteSpace(original) || String.IsNullOrWhiteSpace(corrected))
                return Rejected(L.T("The comparison needs both the inserted and the corrected text."));

            var before = Tokenize(original); var after = Tokenize(corrected);
            var matches = MyersMatches(before, after);
            double similarity = Similarity(before, after, matches);
            if (similarity < minimumSimilarity)
                return new CorrectionAnalysis { Similarity = similarity, Message = L.T("The selection is not similar enough to the last dictation. Select the entire corrected text.") };

            var suggestions = new List<RuleSuggestion>();
            int bi = 0, ai = 0;
            foreach (var match in matches.Concat(new[] { new Match(before.Count, after.Count) }))
            {
                AddHunk(suggestions, original, corrected, before, after, bi, match.Original, ai, match.Corrected);
                if (match.Original < before.Count)
                {
                    var left = before[match.Original]; var right = after[match.Corrected];
                    if (left.Kind == TokenKind.Word && left.Value != right.Value && !IsSentenceStart(before, match.Original))
                        suggestions.Add(NewSuggestion(left.Value, right.Value, true, L.T("Capitalization adjusted")));
                }
                bi = match.Original + 1; ai = match.Corrected + 1;
            }

            var broad = new List<RuleSuggestion>();
            AddBroadSuggestion(broad, original, corrected);
            // Keep compact names such as .NET together instead of learning a
            // punctuation-only replacement ("." -> "dot") from token alignment.
            if (broad.Count == 1 && broad[0].Selected &&
                !broad[0].Source.Any(Char.IsWhiteSpace) && broad[0].Source.Any(Char.IsLetterOrDigit))
                suggestions = broad;
            else if (suggestions.Count == 0)
                suggestions.AddRange(broad);

            suggestions = RemoveDuplicatesAndMarkConflicts(suggestions, existingRules ?? Array.Empty<ReplacementRule>());
            return new CorrectionAnalysis
            {
                Accepted = true,
                Similarity = similarity,
                Message = suggestions.Count == 0 ? L.T("No suitable dictionary rules found.") : suggestions.Count == 1 ? L.F("{0} rule suggested.", suggestions.Count) : L.F("{0} rules suggested.", suggestions.Count),
                Suggestions = suggestions
            };
        }

        static CorrectionAnalysis Rejected(string message) => new CorrectionAnalysis { Message = message };

        static List<Token> Tokenize(string text)
        {
            var result = new List<Token>(); int index = 0; Token current = null;
            foreach (var rune in text.EnumerateRunes())
            {
                int length = rune.Utf16SequenceLength; var kind = Kind(rune);
                if (kind == null) { current = null; index += length; continue; }
                if (current != null && current.Kind == kind.Value && kind == TokenKind.Word) { current.Value += rune.ToString(); current.End = index + length; }
                else { current = new Token { Value = rune.ToString(), Start = index, End = index + length, Kind = kind.Value }; result.Add(current); }
                index += length;
            }
            return result;
        }

        static TokenKind? Kind(Rune rune)
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.SpaceSeparator || category == UnicodeCategory.LineSeparator || category == UnicodeCategory.ParagraphSeparator || category == UnicodeCategory.Control) return null;
            if (category == UnicodeCategory.UppercaseLetter || category == UnicodeCategory.LowercaseLetter || category == UnicodeCategory.TitlecaseLetter || category == UnicodeCategory.ModifierLetter || category == UnicodeCategory.OtherLetter || category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark || category == UnicodeCategory.DecimalDigitNumber || category == UnicodeCategory.LetterNumber || category == UnicodeCategory.OtherNumber || rune.Value == '_') return TokenKind.Word;
            return TokenKind.Punctuation;
        }

        static bool Same(Token a, Token b) => a.Kind == b.Kind && String.Equals(a.Value, b.Value, a.Kind == TokenKind.Word ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        static bool IsSentenceStart(IReadOnlyList<Token> tokens, int index)
        {
            for (int i = index - 1; i >= 0; i--)
            {
                if (tokens[i].Kind == TokenKind.Word) return false;
                if (tokens[i].Value == "." || tokens[i].Value == "!" || tokens[i].Value == "?" || tokens[i].Value == ":") return true;
            }
            return true;
        }

        // Myers' shortest-edit-path algorithm. Only equal-token coordinates are retained.
        static List<Match> MyersMatches(IReadOnlyList<Token> a, IReadOnlyList<Token> b)
        {
            int n = a.Count, m = b.Count, max = n + m, offset = max + 1;
            var v = Enumerable.Repeat(-1, max * 2 + 3).ToArray(); v[offset + 1] = 0;
            var trace = new List<int[]>(); int distance = 0;
            for (int d = 0; d <= max; d++)
            {
                trace.Add((int[])v.Clone());
                for (int k = -d; k <= d; k += 2)
                {
                    int x = k == -d || k != d && v[offset + k - 1] < v[offset + k + 1] ? v[offset + k + 1] : v[offset + k - 1] + 1;
                    int y = x - k;
                    while (x < n && y < m && Same(a[x], b[y])) { x++; y++; }
                    v[offset + k] = x;
                    if (x >= n && y >= m) { distance = d; goto Complete; }
                }
            }
        Complete:
            var matches = new List<Match>(); int cx = n, cy = m;
            for (int d = distance; d > 0; d--)
            {
                var previous = trace[d]; int k = cx - cy;
                int previousK = k == -d || k != d && previous[offset + k - 1] < previous[offset + k + 1] ? k + 1 : k - 1;
                int px = previous[offset + previousK], py = px - previousK;
                while (cx > px && cy > py) { matches.Add(new Match(--cx, --cy)); }
                cx = px; cy = py;
            }
            while (cx > 0 && cy > 0 && Same(a[cx - 1], b[cy - 1])) matches.Add(new Match(--cx, --cy));
            matches.Reverse(); return matches;
        }

        static void AddHunk(List<RuleSuggestion> output, string original, string corrected, List<Token> before, List<Token> after, int b0, int b1, int a0, int a1)
        {
            if (b0 >= b1) return; // An insertion has no stable dictionary source.
            var oldWords = before.Skip(b0).Take(b1 - b0).Where(x => x.Kind == TokenKind.Word).ToList();
            var newWords = after.Skip(a0).Take(a1 - a0).Where(x => x.Kind == TokenKind.Word).ToList();
            if (oldWords.Count > 10 || newWords.Count > 10) return;
            string source = Slice(original, before, b0, b1), target = Slice(corrected, after, a0, a1);
            if (String.IsNullOrWhiteSpace(source) || source == target) return;

            double likeness = TokenGroupSimilarity(oldWords, newWords);
            bool simple = oldWords.Count == 1 && newWords.Count == 1;
            bool splitOrJoin = !simple && String.Equals(String.Concat(oldWords.Select(x => x.Value)), String.Concat(newWords.Select(x => x.Value)), StringComparison.OrdinalIgnoreCase);
            bool selected = newWords.Count>0&&(simple ? likeness >= .35 : splitOrJoin || oldWords.Count <= 3 && newWords.Count <= 3 && likeness >= .35);
            string reason = newWords.Count==0?L.T("Removal suggested · please check"):simple ? L.T("Word corrected") : splitOrJoin ? L.T("Split or joined words corrected") : L.T("Several words changed · please check");
            output.Add(NewSuggestion(source, target, selected, reason));
        }

        static void AddBroadSuggestion(List<RuleSuggestion> output,string original,string corrected)
        {
            int prefix=0,limit=Math.Min(original.Length,corrected.Length);while(prefix<limit&&original[prefix]==corrected[prefix])prefix++;
            if(prefix==original.Length&&prefix==corrected.Length)return;
            while(prefix>0&&!Char.IsWhiteSpace(original[prefix-1])&&!Char.IsWhiteSpace(corrected[prefix-1]))prefix--;
            int oldEnd=original.Length,newEnd=corrected.Length;while(oldEnd>prefix&&newEnd>prefix&&original[oldEnd-1]==corrected[newEnd-1]){oldEnd--;newEnd--;}
            while(oldEnd<original.Length&&!Char.IsWhiteSpace(original[oldEnd]))oldEnd++;while(newEnd<corrected.Length&&!Char.IsWhiteSpace(corrected[newEnd]))newEnd++;
            string source=original.Substring(prefix,oldEnd-prefix).Trim(),target=corrected.Substring(prefix,newEnd-prefix).Trim();
            if(String.IsNullOrWhiteSpace(source)||source==target||source.Length>120||target.Length>120)return;
            int oldWords=Tokenize(source).Count(x=>x.Kind==TokenKind.Word),newWords=Tokenize(target).Count(x=>x.Kind==TokenKind.Word);if(oldWords>10||newWords>10)return;
            if(output.Any(x=>String.Equals(x.Source,source,StringComparison.Ordinal)&&String.Equals(x.Target,target,StringComparison.Ordinal)))return;
            bool compactNotation=newWords>0&&oldWords<=3&&newWords<=3&&source.Length>0&&!Char.IsLetterOrDigit(source[0]);
            output.Add(NewSuggestion(source,target,compactNotation,L.T("Spelling or short phrase changed")));
        }

        static string Slice(string text, List<Token> tokens, int start, int end) => start < end ? text.Substring(tokens[start].Start, tokens[end - 1].End - tokens[start].Start) : "";

        static RuleSuggestion NewSuggestion(string source, string target, bool selected, string reason) => new RuleSuggestion { Source = source, Target = target, Whole = true, CaseSensitive = false, Selected = selected, Reason = reason };

        static double Similarity(List<Token> before, List<Token> after, List<Match> matches)
        {
            var bw = before.Where(x => x.Kind == TokenKind.Word).ToList(); var aw = after.Where(x => x.Kind == TokenKind.Word).ToList();
            if (bw.Count + aw.Count == 0) return 0;
            int exact = matches.Count(x => before[x.Original].Kind == TokenKind.Word);
            double credit = exact;
            int bi = 0, ai = 0;
            foreach (var match in matches.Concat(new[] { new Match(before.Count, after.Count) }))
            {
                var left = before.Skip(bi).Take(match.Original - bi).Where(x => x.Kind == TokenKind.Word).ToList();
                var right = after.Skip(ai).Take(match.Corrected - ai).Where(x => x.Kind == TokenKind.Word).ToList();
                if (left.Count > 0 && right.Count > 0) credit += Math.Min(left.Count, right.Count) * TokenGroupSimilarity(left, right);
                bi = match.Original + 1; ai = match.Corrected + 1;
            }
            return Math.Min(1, 2 * credit / (bw.Count + aw.Count));
        }

        static double TokenGroupSimilarity(List<Token> left, List<Token> right)
        {
            string a = String.Concat(left.Select(x => x.Value)).ToUpperInvariant(); string b = String.Concat(right.Select(x => x.Value)).ToUpperInvariant();
            if (a.Length == 0 || b.Length == 0) return 0;
            int distance = Levenshtein(a, b); return 1.0 - (double)distance / Math.Max(a.Length, b.Length);
        }

        static int Levenshtein(string a, string b)
        {
            if (a.Length > b.Length) { var swap = a; a = b; b = swap; }
            var row = Enumerable.Range(0, a.Length + 1).ToArray();
            for (int j = 1; j <= b.Length; j++)
            {
                int diagonal = row[0]; row[0] = j;
                for (int i = 1; i <= a.Length; i++) { int old = row[i]; row[i] = Math.Min(Math.Min(row[i] + 1, row[i - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1)); diagonal = old; }
            }
            return row[a.Length];
        }

        static List<RuleSuggestion> RemoveDuplicatesAndMarkConflicts(IEnumerable<RuleSuggestion> suggestions, IEnumerable<ReplacementRule> existing)
        {
            var result = new List<RuleSuggestion>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var rules = existing.Where(x => x != null).ToList();
            foreach (var suggestion in suggestions)
            {
                string key = suggestion.Source.Normalize(NormalizationForm.FormC) + "\0" + suggestion.Target.Normalize(NormalizationForm.FormC) + "\0" + suggestion.Whole + "\0" + suggestion.CaseSensitive;
                if (!seen.Add(key)) continue;
                var sameSource = rules.Where(x => x.Whole == suggestion.Whole && x.CaseSensitive == suggestion.CaseSensitive && String.Equals((x.Source ?? "").Normalize(NormalizationForm.FormC), suggestion.Source, StringComparison.OrdinalIgnoreCase)).ToList();
                if (sameSource.Any(x => String.Equals((x.Target ?? "").Normalize(NormalizationForm.FormC), suggestion.Target, StringComparison.Ordinal))) continue;
                if (sameSource.Count > 0) { suggestion.ConflictsWithExisting = true; suggestion.Selected = false; suggestion.Reason = L.T("Another rule already exists for this expression"); }
                result.Add(suggestion);
            }
            return result;
        }
    }
}
