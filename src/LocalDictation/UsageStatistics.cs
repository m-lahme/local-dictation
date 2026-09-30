using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LocalDictation
{
    public sealed class UsageLedger
    {
        public int Version { get; set; } = 1;
        public UsageTotals Total { get; set; } = new UsageTotals();
        public List<UsageDay> Days { get; set; } = new List<UsageDay>();
        public List<CountedDictation> RecentDictations { get; set; } = new List<CountedDictation>();
    }

    public class UsageTotals
    {
        public long Dictations { get; set; }
        public long Words { get; set; }
        public long TypingCharacters { get; set; }
        public long DictationMs { get; set; }
        public long ProcessingMs { get; set; }
        public long SuccessfulOutputs { get; set; }
        public long Retranscriptions { get; set; }
    }

    public sealed class UsageDay : UsageTotals
    {
        public string Date { get; set; } = "";
    }

    public sealed class CountedDictation
    {
        public string Id { get; set; } = "";
        public DateTime TimeUtc { get; set; }
    }

    public static class UsageStatistics
    {
        static readonly Regex Words = new Regex(@"[\p{L}\p{M}\p{Nd}]+(?:['’\-][\p{L}\p{M}\p{Nd}]+)*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static int CountWords(string text) => String.IsNullOrWhiteSpace(text) ? 0 : Words.Matches(text).Count;

        public static int CountTypingCharacters(string text)
        {
            if (String.IsNullOrEmpty(text)) return 0;
            string normalized = text.Trim().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            return normalized.Length;
        }

        public static TimeSpan EstimateTypingTime(long typingCharacters, double typingWordsPerMinute)
        {
            if (typingCharacters <= 0 || !double.IsFinite(typingWordsPerMinute) || typingWordsPerMinute <= 0) return TimeSpan.Zero;
            return TimeSpan.FromMinutes(typingCharacters / 5.0 / typingWordsPerMinute);
        }

        public static TimeSpan EstimateTimeSaved(UsageTotals totals, double typingWordsPerMinute)
        {
            if (totals == null) throw new ArgumentNullException(nameof(totals));
            double savedMs = EstimateTypingTime(totals.TypingCharacters, typingWordsPerMinute).TotalMilliseconds - Math.Max(0, totals.DictationMs) - Math.Max(0, totals.ProcessingMs);
            return TimeSpan.FromMilliseconds(Math.Max(0, savedMs));
        }

        public static double CalculateTypingWordsPerMinute(int typedCharacters, TimeSpan elapsed)
        {
            if (typedCharacters <= 0 || elapsed <= TimeSpan.Zero) return 0;
            return typedCharacters / 5.0 / elapsed.TotalMinutes;
        }

        public static UsageTotals Summarize(IEnumerable<UsageDay> days)
        {
            var result = new UsageTotals();
            if (days == null) return result;
            foreach (UsageDay day in days.Where(x => x != null)) Add(result, day);
            return result;
        }

        public static double DictationWordsPerMinute(UsageTotals totals)
        {
            if (totals == null || totals.Words <= 0 || totals.DictationMs <= 0) return 0;
            return totals.Words / (totals.DictationMs / 60000.0);
        }

        static void Add(UsageTotals target, UsageTotals value)
        {
            target.Dictations += value.Dictations; target.Words += value.Words; target.TypingCharacters += value.TypingCharacters;
            target.DictationMs += value.DictationMs; target.ProcessingMs += value.ProcessingMs;
            target.SuccessfulOutputs += value.SuccessfulOutputs; target.Retranscriptions += value.Retranscriptions;
        }
    }
}
