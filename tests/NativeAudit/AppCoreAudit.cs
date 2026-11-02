using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using LocalDictation;
using NAudio.Wave;

internal static class AppCoreAudit
{
    public static void Run()
    {
        Downmix();
        Microphone();
        Equal("Whisper ist gerade nicht verfügbar: Keine dedizierte Vulkan-GPU verfügbar. Parakeet übernimmt dieses Diktat.", "Whisper ist gerade nicht verfügbar" + DictationApplication.Reason("Keine dedizierte Vulkan-GPU verfügbar.") + " Parakeet übernimmt dieses Diktat.", "worker reason appears in the status");
        Equal(".", DictationApplication.Reason(" "), "missing worker reason keeps the sentence");
        Equal(": a b.", DictationApplication.Reason("a\r\nb"), "worker reason stays on one line");
        Check(DictationApplication.Reason(new string('x', 500)).Length < 210, "long worker reason is shortened");
        string previous = Storage.DataDir; Storage.DataDir = Path.Combine(previous, "appcore-audit");
        if (Directory.Exists(Storage.DataDir)) Directory.Delete(Storage.DataDir, true);
        Directory.CreateDirectory(Storage.DataDir);
        try { Recovery(); Retry(); Ledger(); }
        finally { Storage.DataDir = previous; }
    }

    static void Downmix()
    {
        float[] gains = { .8f, .4f, 0, -.4f }; var bytes = new byte[48000 * 4 * 4];
        for (int i = 0; i < 48000; i++) { double tone = Math.Sin(2 * Math.PI * 440 * i / 48000); for (int c = 0; c < 4; c++) BitConverter.GetBytes((float)(gains[c] * tone)).CopyTo(bytes, (i * 4 + c) * 4); }
        foreach (var format in new[] { WaveFormat.CreateIeeeFloatWaveFormat(48000, 4), new WaveFormatExtensible(48000, 32, 4) })
        {
            var mono = AudioCapture.Convert(bytes, format); string name = format is WaveFormatExtensible ? " (extensible)" : "";
            Check(Math.Abs(mono.Length - 16000) <= 2, "four-channel 48 kHz float becomes one second of 16 kHz mono" + name + " [" + mono.Length + "]");
            double rms = Math.Sqrt(mono.Skip(200).Take(15600).Average(x => (double)x * x));
            Check(Math.Abs(rms - .2 / Math.Sqrt(2)) < .01, "four-channel downmix averages all channels" + name + " [" + rms.ToString("0.0000", CultureInfo.InvariantCulture) + "]");
        }
        var pcm = new byte[22050 * 6 * 2];
        for (int i = 0; i < 22050; i++) BitConverter.GetBytes((short)(12000 * Math.Sin(2 * Math.PI * 300 * i / 44100))).CopyTo(pcm, i * 12);
        var six = AudioCapture.Convert(pcm, new WaveFormat(44100, 16, 6)); float peak = six.Max(x => Math.Abs(x));
        Check(Math.Abs(six.Length - 8000) <= 2 && peak > .055 && peak < .068, "six-channel PCM16 at 44.1 kHz downmixes to 16 kHz mono [" + six.Length + ", " + peak.ToString("0.000", CultureInfo.InvariantCulture) + "]");
        var stereo = new byte[16000 * 2 * 4];
        for (int i = 0; i < 16000; i++) { var sample = BitConverter.GetBytes((float)(.5 * Math.Sin(2 * Math.PI * 440 * i / 16000))); sample.CopyTo(stereo, i * 8); sample.CopyTo(stereo, i * 8 + 4); }
        var two = AudioCapture.Convert(stereo, WaveFormat.CreateIeeeFloatWaveFormat(16000, 2));
        Check(two.Length == 16000 && Math.Abs(two.Max() - .5) < .01, "stereo downmix is unchanged");
    }

    static void Microphone()
    {
        Check(AudioCapture.Explain(new UnauthorizedAccessException().HResult)?.Contains("Datenschutz") == true, "denied microphone access points to the Windows privacy settings");
        Check(AudioCapture.Explain(new COMException("in use", unchecked((int)0x8889000A)).HResult)?.Contains("exklusiv") == true, "exclusive use by another program is named");
        Check(AudioCapture.Explain(unchecked((int)0x88890004)) == null, "other start failures keep the generic message");
        Check(AudioCapture.IsDigitalSilence(new float[16000]), "one second of digital zeros is reported as silence");
        Check(!AudioCapture.IsDigitalSilence(new float[15999]), "short recordings are not reported as silence");
        var quiet = new float[32000]; quiet[20000] = 1e-6f;
        Check(!AudioCapture.IsDigitalSilence(quiet), "any signal is not digital silence");
    }

    static void Recovery()
    {
        string dir = Storage.DataDir, settingsPath = Path.Combine(dir, "settings.json"), broken = "{ \"Theme\": \"dark\", \"Rules\": [ { \"Source\": \"cloud\"";
        File.WriteAllText(settingsPath, broken); var loaded = Storage.LoadSettings();
        Check(loaded.Rules.Count == 0 && loaded.Theme == new Settings().Theme, "unreadable settings start with defaults");
        Check(!File.Exists(settingsPath), "unreadable settings are moved aside");
        string backup = Directory.GetFiles(dir, "settings.json.corrupt-*").Single();
        Equal(broken, File.ReadAllText(backup), "unreadable settings are kept byte for byte");
        Check(Storage.Recovered.Contains(Path.GetFileName(backup)), "settings backup is reported for the startup notice");
        Storage.SaveSettings(loaded); Equal(broken, File.ReadAllText(backup), "saving defaults leaves the backup alone");
        File.WriteAllText(settingsPath, "null"); Storage.LoadSettings();
        Equal(2, Directory.GetFiles(dir, "settings.json.corrupt-*").Length, "an empty settings document gets its own backup");
        File.WriteAllText(settingsPath, "{\"Version\":3}"); bool newer = false;
        try { Storage.LoadSettings(); } catch (InvalidDataException) { newer = true; }
        Check(newer && File.Exists(settingsPath), "settings from a newer version still stop the start and stay in place");
        File.Delete(settingsPath);
        string historyPath = Path.Combine(dir, "history.json"); File.WriteAllText(historyPath, "[{\"Time\":");
        Equal(0, Storage.LoadHistory(dir).Count, "unreadable history starts empty");
        Equal("[{\"Time\":", File.ReadAllText(Directory.GetFiles(dir, "history.json.corrupt-*").Single()), "unreadable history is kept byte for byte");
        Equal(0, Storage.LoadHistory(dir).Count, "replacement history is readable");
        File.WriteAllText(historyPath, "[null,{\"Time\":\"" + DateTime.Now.ToString("o") + "\",\"Text\":\"ok\"}]");
        Equal(1, Storage.LoadHistory(dir).Count, "null history entries are skipped");
    }

    static void Retry()
    {
        string path = Path.Combine(Storage.DataDir, "retry.json"), log = Path.Combine(Storage.DataDir, "diagnostics.log"); Storage.Write(path, new[] { 1 });
        bool Retried() { try { using var f = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var r = new StreamReader(f); return r.ReadToEnd().Contains("write retry retry.json"); } catch (IOException) { return false; } }
        // The file stays held like a virus scanner would hold it until the first failed attempt is logged.
        var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = new Thread(() => { var watch = System.Diagnostics.Stopwatch.StartNew(); while (!Retried() && watch.ElapsedMilliseconds < 5000) Thread.Sleep(5); held.Dispose(); }); release.Start();
        Storage.Write(path, new[] { 2 }); release.Join();
        Check(File.ReadAllText(path).Contains("2"), "save succeeds once another program releases the file");
        Check(Retried(), "save retried while the file was held");
    }

    static void Ledger()
    {
        string dir = Storage.DataDir; var culture = CultureInfo.CurrentCulture; DateTime today = DateTime.Today;
        string Key(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            var first = new HistoryEntry { Id = "a", Time = today.AddHours(9), Text = "eins zwei drei", Status = "complete", DurationMs = 1000 };
            UsageLedgerStore.Record(dir, first); UsageLedgerStore.RecordRetranscription(dir, today.AddDays(-1).AddHours(9));
            Check(UsageLedgerStore.Load(dir).Days.Select(x => x.Date).SequenceEqual(new[] { Key(today.AddDays(-1)), Key(today) }), "usage day keys are Gregorian under a Thai culture");
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            UsageLedgerStore.SynchronizeHistory(dir, new[] { first, new HistoryEntry { Id = "b", Time = today.AddHours(10), Text = "vier fünf sechs", Status = "complete", DurationMs = 1000 } });
            var ledger = UsageLedgerStore.Load(dir);
            Equal(1L, ledger.Days.Single(x => x.Date == Key(today.AddDays(-1))).Retranscriptions, "history rebuild keeps retranscription days under a Saudi culture");
            Equal(6L, UsageLedgerStore.ForPeriod(ledger, 1, today.AddHours(18)).Words, "today's period counts today's dictations");
            ledger.Days.Add(new UsageDay { Date = "2569-11-02", Words = 100 }); ledger.Days.Add(new UsageDay { Date = "kaputt", Words = 1000 });
            Equal(6L, UsageLedgerStore.ForPeriod(ledger, 7, today.AddHours(18)).Words, "future and unreadable day keys stay out of periods");
            CultureInfo.CurrentCulture = culture; File.Delete(Path.Combine(dir, "usage.json"));
            var kept = new HistoryEntry { Id = "c", Time = today.AddHours(8), Text = "eins zwei", Status = "complete", DurationMs = 1000 };
            UsageLedgerStore.Record(dir, kept); UsageLedgerStore.RecordRetranscription(dir, today.AddHours(8)); long total = UsageLedgerStore.Load(dir).Total.Retranscriptions;
            UsageLedgerStore.SynchronizeHistory(dir, new[] { kept, new HistoryEntry { Id = "d", Time = today.AddHours(9), Status = "not-transcribed", DurationMs = 3000 } });
            var synced = UsageLedgerStore.Load(dir);
            Check(synced.Total.Dictations == 1 && synced.Total.Retranscriptions == total && synced.RecentDictations.All(x => x.Id == "c"), "untranscribed recordings neither count nor trigger a statistics rebuild");
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }

    static void Equal<T>(T expected, T actual, string name)
    {
        Check(EqualityComparer<T>.Default.Equals(expected, actual), name + " [" + actual + "]");
    }
}
