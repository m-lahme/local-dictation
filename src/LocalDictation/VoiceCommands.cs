using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LocalDictation
{
    public static class VoiceCommands
    {
        const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        static readonly Regex Escaped = new Regex(@"\bwörtlich\s+(Stichpunkt|neue Zeile|neuer Absatz)\b", Options);
        static readonly Regex BulletAtStart = new Regex(@"^\s*Stichpunkt\s*[,;:]?\s+", Options);
        static readonly Regex BulletAtBoundary = new Regex(@"(?<space>(?<=[.!?])\s+|[\r\n]+\s*)Stichpunkt\s*[,;:]?\s+", Options);
        static readonly Regex BulletInList = new Regex(@"\s+Stichpunkt\s*[,;:]?\s+", Options);
        static readonly Regex Paragraph = new Regex(@"(?<space>^|(?<=[.!?])\s+|[\r\n]+\s*)neuer Absatz\s*[,;:]?\s*", Options);
        static readonly Regex Line = new Regex(@"(?<space>^|(?<=[.!?])\s+|[\r\n]+\s*)neue Zeile\s*[,;:]?\s*", Options);

        public static string Apply(string input)
        {
            if (String.IsNullOrEmpty(input)) return input ?? "";

            var literals = new List<string>();
            string text = Escaped.Replace(input, match =>
            {
                int index = literals.Count;
                literals.Add(match.Groups[1].Value);
                return "\uE000" + index + "\uE001";
            });

            bool listStarted = BulletAtStart.IsMatch(text);
            text = BulletAtStart.Replace(text, "• ", 1);
            text = BulletAtBoundary.Replace(text, match => Boundary(match.Groups["space"].Value, "\r\n• "));
            if (listStarted) text = BulletInList.Replace(text, "\r\n• ");
            text = Paragraph.Replace(text, match => Boundary(match.Groups["space"].Value, "\r\n\r\n"));
            text = Line.Replace(text, match => Boundary(match.Groups["space"].Value, "\r\n"));

            for (int i = 0; i < literals.Count; i++) text = text.Replace("\uE000" + i + "\uE001", literals[i], StringComparison.Ordinal);
            return text;
        }

        static string Boundary(string consumedSpace, string replacement)
        {
            return consumedSpace.Length == 0 ? replacement : replacement;
        }
    }
}
