using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LocalDictation
{
    public static class UsageLedgerStore
    {
        static readonly object Gate = new object();
        const string DayFormat = "yyyy-MM-dd";
        static string PathFor(string directory) => Path.Combine(directory, "usage.json");
        // Day keys are Gregorian whatever calendar the current culture uses.
        static bool TryParseDay(string text, out DateTime date) => DateTime.TryParseExact(text, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

        public static UsageLedger Load(string directory)
        {
            lock (Gate)
            {
                string path = PathFor(directory);
                if (!File.Exists(path)) return new UsageLedger();
                try { return JsonSerializer.Deserialize<UsageLedger>(File.ReadAllText(path)) ?? new UsageLedger(); }
                catch { return new UsageLedger(); }
            }
        }

        public static void SynchronizeHistory(string directory, IEnumerable<HistoryEntry> history)
        {
            lock (Gate)
            {
                // Recordings no model could transcribe are not dictations yet; they count once a retranscription gives them text.
                var entries=history.Where(x=>x.Status!="not-transcribed").OrderBy(x=>x.Time).ToList();var ledger=Load(directory);var expected=entries.Select(x=>x.Id).ToHashSet(StringComparer.Ordinal);
                bool complete=ledger.RecentDictations.Count==expected.Count&&ledger.RecentDictations.All(x=>expected.Contains(x.Id));
                if(complete)return;
                long retranscriptions=ledger.Total.Retranscriptions;var retryDays=ledger.Days.ToDictionary(x=>x.Date,x=>x.Retranscriptions,StringComparer.Ordinal);ledger=new UsageLedger();
                foreach (var entry in entries) AddDictation(ledger, entry);
                ledger.Total.Retranscriptions=retranscriptions;foreach(var pair in retryDays)if(TryParseDay(pair.Key,out var date))Day(ledger,date).Retranscriptions=pair.Value;
                Trim(ledger, DateTime.UtcNow);
                Storage.Write(PathFor(directory), ledger);
            }
        }

        public static void Record(string directory, HistoryEntry entry)
        {
            lock (Gate)
            {
                var ledger = Load(directory); AddDictation(ledger, entry); Trim(ledger, DateTime.UtcNow); Storage.Write(PathFor(directory), ledger);
            }
        }

        public static void RecordRetranscription(string directory, DateTime localTime)
        {
            lock (Gate)
            {
                var ledger = Load(directory); ledger.Total.Retranscriptions++;
                Day(ledger, localTime).Retranscriptions++;
                Trim(ledger, DateTime.UtcNow); Storage.Write(PathFor(directory), ledger);
            }
        }

        public static UsageTotals ForPeriod(UsageLedger ledger, int days, DateTime localNow)
        {
            if (days <= 0) return ledger.Total;
            DateTime first = localNow.Date.AddDays(1 - days);
            return UsageStatistics.Summarize(ledger.Days.Where(x => TryParseDay(x.Date, out var date) && date >= first && date <= localNow.Date));
        }

        static void AddDictation(UsageLedger ledger, HistoryEntry entry)
        {
            string id = String.IsNullOrWhiteSpace(entry.Id) ? entry.Time.ToUniversalTime().Ticks + ":" + entry.DurationMs : entry.Id;
            if (ledger.RecentDictations.Any(x => x.Id == id)) return;
            int words = UsageStatistics.CountWords(entry.Text), characters = UsageStatistics.CountTypingCharacters(entry.Text);
            void Add(UsageTotals target) { target.Dictations++; target.Words += words; target.TypingCharacters += characters; target.DictationMs += Math.Max(0, entry.DurationMs); target.ProcessingMs += Math.Max(0, entry.ProcessingMs); if (entry.Status == "complete") target.SuccessfulOutputs++; }
            Add(ledger.Total); Add(Day(ledger, entry.Time));
            ledger.RecentDictations.Add(new CountedDictation { Id = id, TimeUtc = entry.Time.ToUniversalTime() });
        }

        static UsageDay Day(UsageLedger ledger, DateTime localTime)
        {
            string date = localTime.ToString(DayFormat, CultureInfo.InvariantCulture);
            var day = ledger.Days.FirstOrDefault(x => x.Date == date);
            if (day == null) { day = new UsageDay { Date = date }; ledger.Days.Add(day); }
            return day;
        }

        static void Trim(UsageLedger ledger, DateTime utcNow)
        {
            ledger.RecentDictations.RemoveAll(x => x.TimeUtc < utcNow.AddDays(-31));
            ledger.Days = ledger.Days.OrderBy(x => x.Date).ToList();
        }
    }
}
