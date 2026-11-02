using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace LocalDictation
{
    public static class Storage
    {
        static readonly object Gate = new object();
        public static string DataDir = Environment.GetEnvironmentVariable("LOCAL_DICTATION_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalDictation");
        // Backup names of unreadable files that were moved aside since startup.
        public static readonly List<string> Recovered = new List<string>();
        public static void Write<T>(string path, T value)
        {
            lock (Gate)
            {
                // Virus scanners and sync tools briefly hold freshly written files, so a locked target gets two short retries.
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        string temp = path + ".new";
                        using (var f = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(f, value, new JsonSerializerOptions { WriteIndented = true }); f.Flush(true); }
                        if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                        return;
                    }
                    catch (Exception ex) when (attempt < 3 && (ex is IOException || ex is UnauthorizedAccessException)) { Log("write retry " + Path.GetFileName(path) + ": " + ex.Message); Thread.Sleep(50 * attempt); }
                }
            }
        }
        // An unreadable file is moved aside instead of being overwritten later, so the app can start with defaults without losing it.
        static T ReadOrRecover<T>(string path) where T : class
        {
            string json = File.ReadAllText(path);
            try { return JsonSerializer.Deserialize<T>(json) ?? throw new JsonException("The document is null."); }
            catch (JsonException ex)
            {
                string stamp = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), backup = stamp; for (int n = 2; File.Exists(backup); n++) backup = stamp + "-" + n;
                File.Move(path, backup); Log("storage: " + Path.GetFileName(path) + " is unreadable and was kept as " + Path.GetFileName(backup) + ": " + ex.Message);
                lock (Gate) Recovered.Add(Path.GetFileName(backup)); return null;
            }
        }
        public static Settings LoadSettings()
        {
            Directory.CreateDirectory(DataDir); var path = Path.Combine(DataDir, "settings.json");
            Settings result;
            if (File.Exists(path)) result = ReadOrRecover<Settings>(path) ?? new Settings();
            else
            {
                result = new Settings(); var old = Path.Combine(DataDir, "settings.txt");
                if (File.Exists(old)) foreach (var line in File.ReadAllLines(old)) { var x = line.Split(new[] { '=' }, 2); if (x.Length < 2) continue; if (x[0] == "engine") result.Engine = x[1]; if (x[0] == "theme") result.Theme = x[1]; int n; if (x[0] == "output" && int.TryParse(x[1], out n) && n >= 0 && n <= 2) result.Output = (OutputMode)n; }
                old = Path.Combine(DataDir, "rules.tsv"); if (File.Exists(old)) foreach (var line in File.ReadAllLines(old)) { var x = line.Split('\t'); if (x.Length >= 4) result.Rules.Add(new ReplacementRule { Whole = x[0] == "whole", CaseSensitive = x[1] == "case", Source = x[2], Target = x[3] }); }
            }
            if (result.Version > 2) throw new InvalidDataException(L.T("The settings were saved by a newer version of the app. Please use that version."));
            if (result.Engine != "whisper" && result.Engine != "parakeet") result.Engine = "parakeet";
            ModelSelection.Normalize(result);
            if (!Enum.IsDefined(typeof(OutputMode), result.Output)) result.Output = OutputMode.Insert;
            result.Rules = result.Rules ?? new List<ReplacementRule>(); result.Microphones = (result.Microphones ?? new List<MicrophoneEntry>()).Where(x => x != null && x.Ids != null && x.Ids.Count > 0).ToList(); result.HoldMs = Math.Clamp(result.HoldMs, 200, 1000);
            result.TypingWpm = Math.Clamp(result.TypingWpm, 10, 200);
            if (result.Hotkey != "ctrl-win" && result.Hotkey != "ctrl-shift-space") result.Hotkey = HotkeyInput.Binding;
            if (!new[] { 0, 3, 5, 10 }.Contains(result.WhisperIdleMinutes)) result.WhisperIdleMinutes = 5;
            return result;
        }
        public static void SaveSettings(Settings s) { Write(Path.Combine(DataDir, "settings.json"), s); }
        public static List<HistoryEntry> LoadHistory(string dir)
        {
            string file = Path.Combine(dir, "history.json"); var entries = new List<HistoryEntry>();
            if (File.Exists(file)) entries = ReadOrRecover<List<HistoryEntry>>(file) ?? entries;
            else { file = Path.Combine(dir, "history.tsv"); if (File.Exists(file)) foreach (var line in File.ReadAllLines(file)) { var x = line.Split(new[] { '\t' }, 5); DateTime t; int d; if (x.Length == 5 && DateTime.TryParse(x[0], out t) && int.TryParse(x[1], out d)) entries.Add(new HistoryEntry { Time = t, DurationMs = d, Engine = x[2], Status = x[3], Text = x[4] }); } }
            entries = entries.Where(x => x != null && x.Time > DateTime.Now.AddDays(-30)).OrderByDescending(x => x.Time).ToList();
            foreach (var entry in entries)
            {
                if (String.IsNullOrWhiteSpace(entry.Id)) entry.Id = LegacyHistoryId(entry);
                entry.RawText ??= ""; entry.Text ??= ""; entry.Engine ??= ""; entry.Status ??= "complete";
                if (entry.RawText.Length == 0) entry.RawText = entry.Text;
                entry.Variants ??= new List<TranscriptVariant>();
                if (entry.Variants.Count == 0 && entry.Text.Length > 0) entry.Variants.Add(new TranscriptVariant { Engine = entry.Engine, RawText = entry.RawText, Text = entry.Text, CreatedAtUtc = entry.Time.ToUniversalTime(), Confidence = entry.Confidence, ReviewRecommended = entry.ReviewRecommended });
            }
            Write(Path.Combine(dir, "history.json"), entries); return entries;
        }
        static string LegacyHistoryId(HistoryEntry entry)
        {
            string identity=entry.Time.ToUniversalTime().Ticks+"\n"+entry.DurationMs+"\n"+(entry.Engine??"")+"\n"+(entry.Text??"");
            return "legacy-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant().Substring(0,24);
        }
        public static void Log(string message) { try { Directory.CreateDirectory(DataDir); string p = Path.Combine(DataDir, "diagnostics.log"); if (File.Exists(p) && new FileInfo(p).Length > 1024 * 1024) File.Move(p, p + ".previous", true); File.AppendAllText(p, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine); } catch { } }
    }
}
