using System;
using System.Collections.Generic;
using System.IO;
using LocalDictation;
using NAudio.Wave;

internal static class FeatureAudit
{
    public static void Run(string dataDirectory)
    {
        Equal("• Milch kaufen.", VoiceCommands.Apply("Stichpunkt Milch kaufen."), "voice command creates bullet");
        Equal("• Milch.\r\n• Brot.", VoiceCommands.Apply("Stichpunkt Milch. Stichpunkt Brot."), "voice command creates list");
        Equal("Der Stichpunkt bleibt stehen.", VoiceCommands.Apply("Der Stichpunkt bleibt stehen."), "ordinary bullet word stays literal");
        Equal("Stichpunkt und neue Zeile und neuer Absatz", VoiceCommands.Apply("wörtlich Stichpunkt und wörtlich neue Zeile und wörtlich neuer Absatz"), "literal voice commands are escaped");
        Equal("Erster Satz.\r\nZweiter Satz.", VoiceCommands.Apply("Erster Satz. Neue Zeile Zweiter Satz."), "new line command");
        Equal("Erster Satz.\r\n\r\nZweiter Absatz.", VoiceCommands.Apply("Erster Satz. Neuer Absatz Zweiter Absatz."), "new paragraph command");

        string audioData = Path.Combine(dataDirectory, "retained-audio-audit");
        if (Directory.Exists(audioData)) Directory.Delete(audioData, true);
        DateTime now = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
        var reference = RetainedAudioStore.Save(audioData, new float[] { -1, -.5f, 0, .5f, 1, float.NaN }, now);
        Check(RetainedAudioStore.IsSafeFileName(reference.FileName), "retained audio uses safe relative name");
        Check(!RetainedAudioStore.IsSafeFileName("..\\recording.wav"), "retained audio rejects traversal");
        string path = RetainedAudioStore.ResolvePath(audioData, reference.FileName);
        using (var reader = new WaveFileReader(path))
        {
            Equal(16000, reader.WaveFormat.SampleRate, "retained audio sample rate");
            Equal(1, reader.WaveFormat.Channels, "retained audio mono");
            Equal(16, reader.WaveFormat.BitsPerSample, "retained audio PCM16");
            Equal(12L, reader.Length, "retained audio sample count");
        }
        Equal(0, RetainedAudioStore.Cleanup(audioData, now.AddMinutes(59)), "retained audio stays before expiry");
        Equal(1, RetainedAudioStore.Cleanup(audioData, now.AddHours(1)), "retained audio expires after one hour");
        Check(!File.Exists(path), "retained audio cleanup deletes file");

        Equal(5, UsageStatistics.CountWords("Straße, café und Claude-Code 2."), "Unicode word count");
        Equal(10, UsageStatistics.CountTypingCharacters(" Hallo\r\nWelt "), "typing character normalization");
        Equal(40.0, UsageStatistics.CalculateTypingWordsPerMinute(100, TimeSpan.FromSeconds(30)), "typing WPM calibration");
        var totals = new UsageTotals { TypingCharacters = 600, DictationMs = 60000, ProcessingMs = 5000, Words = 100 };
        Equal(TimeSpan.FromSeconds(55), UsageStatistics.EstimateTimeSaved(totals, 60), "estimated saved time includes processing");
        Equal(100.0, UsageStatistics.DictationWordsPerMinute(totals), "dictation words per minute");
        var summary = UsageStatistics.Summarize(new[] { new UsageDay { Words = 10, Dictations = 1 }, new UsageDay { Words = 15, Dictations = 2 } });
        Equal(25L, summary.Words, "usage days sum words"); Equal(3L, summary.Dictations, "usage days sum dictations");
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
