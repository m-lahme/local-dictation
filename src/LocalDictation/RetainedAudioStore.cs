using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LocalDictation
{
    public sealed class RetainedAudioReference
    {
        public string FileName { get; set; } = "";
        public DateTime ExpiresAtUtc { get; set; }
    }

    public static class RetainedAudioStore
    {
        public const int SampleRate = 16000;
        public static readonly TimeSpan DefaultRetention = TimeSpan.FromHours(1);

        public static RetainedAudioReference Save(string dataDirectory, ReadOnlySpan<float> samples, DateTime utcNow, TimeSpan? retention = null)
        {
            if (dataDirectory == null) throw new ArgumentNullException(nameof(dataDirectory));
            TimeSpan keep = retention ?? DefaultRetention;
            if (keep <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retention));
            if ((long)samples.Length * 2 > UInt32.MaxValue - 44) throw new InvalidDataException("Die Aufnahme ist zu groß für eine WAV-Datei.");

            string directory = Path.Combine(dataDirectory, "audio");
            Directory.CreateDirectory(directory);
            string fileName = Guid.NewGuid().ToString("N") + ".wav";
            string path = ResolvePath(dataDirectory, fileName);
            string temporary = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(stream))
                {
                    int dataBytes = checked(samples.Length * 2);
                    writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                    writer.Write(36 + dataBytes);
                    writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                    writer.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                    writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                    writer.Write(SampleRate); writer.Write(SampleRate * 2); writer.Write((short)2); writer.Write((short)16);
                    writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); writer.Write(dataBytes);
                    foreach (float value in samples) writer.Write(ToPcm16(value));
                    stream.Flush(true);
                }
                File.Move(temporary, path);
                File.SetLastWriteTimeUtc(path, utcNow);
                return new RetainedAudioReference { FileName = fileName, ExpiresAtUtc = utcNow.Add(keep) };
            }
            catch
            {
                try { File.Delete(temporary); } catch { }
                throw;
            }
        }

        public static string ResolvePath(string dataDirectory, string fileName)
        {
            if (dataDirectory == null) throw new ArgumentNullException(nameof(dataDirectory));
            if (!IsSafeFileName(fileName)) throw new InvalidDataException("Ungültiger Dateiname für eine Aufnahme.");
            return Path.Combine(Path.GetFullPath(dataDirectory), "audio", fileName);
        }

        public static bool IsSafeFileName(string fileName)
        {
            if (String.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName || !fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return false;
            return Guid.TryParseExact(Path.GetFileNameWithoutExtension(fileName), "N", out _);
        }

        public static int Cleanup(string dataDirectory, DateTime utcNow, TimeSpan? retention = null, IEnumerable<string> protectedFileNames = null)
        {
            string directory = Path.Combine(Path.GetFullPath(dataDirectory ?? throw new ArgumentNullException(nameof(dataDirectory))), "audio");
            if (!Directory.Exists(directory)) return 0;
            TimeSpan keep = retention ?? DefaultRetention;
            if (keep <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retention));
            var protectedNames = new HashSet<string>((protectedFileNames ?? Array.Empty<string>()).Where(IsSafeFileName), StringComparer.OrdinalIgnoreCase);
            int removed = 0;
            foreach (string path in Directory.EnumerateFiles(directory))
            {
                string name = Path.GetFileName(path);
                bool temporary = name.EndsWith(".wav.tmp", StringComparison.OrdinalIgnoreCase);
                if (!temporary && (!IsSafeFileName(name) || protectedNames.Contains(name))) continue;
                TimeSpan maxAge = temporary ? TimeSpan.FromMinutes(10) : keep;
                if (File.GetLastWriteTimeUtc(path) > utcNow.Subtract(maxAge)) continue;
                try { File.Delete(path); removed++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return removed;
        }

        static short ToPcm16(float value)
        {
            if (!float.IsFinite(value)) value = 0;
            value = Math.Clamp(value, -1f, 1f);
            if (value <= -1f) return Int16.MinValue;
            if (value >= 1f) return Int16.MaxValue;
            return (short)MathF.Round(value * 32768f);
        }
    }
}
