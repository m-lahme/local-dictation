using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LocalDictation
{
    public sealed class ModelFile { public string engine { get; set; } public string file { get; set; } public string url { get; set; } public string sha256 { get; set; } public long size { get; set; } }
    public sealed class ModelDownloadException : Exception { public ModelDownloadException(string message, Exception inner = null) : base(message, inner) { } }
    public static class Models
    {
        static readonly HttpClient http = new HttpClient(new HttpClientHandler { DefaultProxyCredentials = CredentialCache.DefaultCredentials }) { Timeout = TimeSpan.FromMinutes(30) };
        static readonly SemaphoreSlim gate = new SemaphoreSlim(1);
        const long SpaceReserve = 64L * 1024 * 1024;
        public static TimeSpan StallTimeout = TimeSpan.FromSeconds(60);
        public static string Root => Environment.GetEnvironmentVariable("LOCAL_DICTATION_MODELS") ?? Path.Combine(Storage.DataDir, "native-models");
        public static ModelFile[] Catalog => JsonSerializer.Deserialize<ModelFile[]>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "model-catalog.json")));
        public static bool Installed(string engine) => Catalog.Where(f => f.engine == engine).All(f => File.Exists(Path.Combine(Root, f.engine, f.file)) && new FileInfo(Path.Combine(Root, f.engine, f.file)).Length == f.size);
        public static async Task Ensure(string engine, Action<string> progress, CancellationToken token)
        {
            await gate.WaitAsync(token); try { foreach (var f in Catalog.Where(f => f.engine == engine)) await Fetch(f, Path.Combine(Root, f.engine), SidebarStatus.ModelName(engine), progress, token); }
            finally { gate.Release(); }
        }
        // Skips files whose verification stamp still matches. Network failures keep the .partial file so the next attempt resumes; every other failure deletes it.
        public static async Task Fetch(ModelFile f, string dir, string modelName, Action<string> progress, CancellationToken token)
        {
            Directory.CreateDirectory(dir); string path = Path.Combine(dir, f.file), partial = path + ".partial";
            if (File.Exists(path) && new FileInfo(path).Length == f.size && (Trusted(path, f) || await Matches(path, f, true, token))) return;
            try
            {
                for (int attempt = 0; ; attempt++)
                {
                    bool resumed = await Download(f, partial, modelName, progress, token);
                    if (await Matches(partial, f, false, token)) break;
                    File.Delete(partial);
                    // A resumed file can combine two server revisions; one fresh download decides.
                    if (!resumed || attempt > 0) throw new InvalidDataException("Prüfsumme des Modelldownloads stimmt nicht: " + f.file);
                }
                File.Move(partial, path, true); Stamp(path, f);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (ModelDownloadException) { throw; }
            catch (Exception ex)
            {
                Storage.Log("model download: " + f.file + ": " + ex.GetType().Name + ": " + ex.Message);
                bool network = ex is TimeoutException || ex is OperationCanceledException || ex is HttpRequestException web && web.StatusCode != HttpStatusCode.RequestedRangeNotSatisfiable;
                if (!network) try { File.Delete(partial); } catch (Exception cleanup) { Storage.Log("model download cleanup: " + cleanup.Message); }
                throw new ModelDownloadException(Describe(ex, modelName), ex);
            }
        }
        static async Task<bool> Download(ModelFile f, string partial, string modelName, Action<string> progress, CancellationToken token)
        {
            long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0; if (offset >= f.size) { File.Delete(partial); offset = 0; }
            EnsureSpace(Path.GetDirectoryName(partial), f.size - offset, modelName);
            progress(offset > 0 ? L.F("Resuming the download of {0} …", modelName) : L.F("Downloading {0} …", modelName));
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token); idle.CancelAfter(StallTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, f.url); if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
            HttpResponseMessage response; try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token); } catch (OperationCanceledException ex) when (!token.IsCancellationRequested) { throw new TimeoutException("Keine Antwort vom Download-Server.", ex); }
            using (response)
            {
                bool resumed = offset > 0 && response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange?.From == offset;
                if (!resumed) { response.EnsureSuccessStatusCode(); if (response.StatusCode == HttpStatusCode.PartialContent) throw new InvalidDataException("Der Server hat einen falschen Dateiabschnitt geliefert: " + f.file); offset = 0; }
                using (var input = await response.Content.ReadAsStreamAsync(idle.Token)) using (var output = resumed ? new FileStream(partial, FileMode.Append, FileAccess.Write) : File.Create(partial))
                {
                    byte[] buffer = new byte[1024 * 256]; long total = offset, last = offset; int n;
                    while (true)
                    {
                        // The idle timeout restarts for every read, so only a stalled connection ends the download.
                        idle.CancelAfter(StallTimeout);
                        try { n = await input.ReadAsync(buffer, idle.Token); }
                        catch (OperationCanceledException ex) when (!token.IsCancellationRequested) { throw new TimeoutException("Der Download ist ins Stocken geraten.", ex); }
                        catch (IOException ex) { throw new HttpRequestException(ex.Message, ex); }
                        if (n == 0) break;
                        if ((total += n) > f.size) throw new InvalidDataException("Der Modelldownload ist größer als erwartet: " + f.file);
                        await output.WriteAsync(buffer.AsMemory(0, n), token);
                        if (total - last > 8 * 1024 * 1024) { progress(L.F("Downloading {0} · {1}%", modelName, total * 100 / Math.Max(1, f.size))); last = total; }
                    }
                    if (total < f.size) throw new HttpRequestException("Der Download wurde vorzeitig beendet: " + f.file);
                }
                return resumed;
            }
        }
        static void EnsureSpace(string dir, long needed, string modelName)
        {
            DriveInfo drive; long free; try { drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))); free = drive.AvailableFreeSpace; } catch (Exception) { return; }
            if (free < needed + SpaceReserve) throw new ModelDownloadException(L.F("Not enough free disk space for {0}: about {1:N0} MB more is needed, {2} has {3:N0} MB free.", modelName, Megabytes(needed + SpaceReserve), drive.Name, Megabytes(free)));
        }
        static long Megabytes(long bytes) => (bytes + 1024 * 1024 - 1) / (1024 * 1024);
        public static string Describe(Exception ex, string modelName)
        {
            if (ex is ModelDownloadException) return ex.Message;
            if (ex is TimeoutException || ex is OperationCanceledException) return L.F("The download of {0} has stalled. Check the internet connection and try again; parts already downloaded will be reused.", modelName);
            if (ex is HttpRequestException web)
            {
                if (web.StatusCode == HttpStatusCode.ProxyAuthenticationRequired) return L.F("The proxy server requires a sign-in that cannot happen automatically. {0} could not be downloaded.", modelName);
                if (web.StatusCode is HttpStatusCode code) return L.F("The download server did not deliver {0} (HTTP {1}). Try again later.", modelName, (int)code);
                return L.T("No connection to the download server. Check the internet connection and try again; parts already downloaded will be reused.");
            }
            if (ex is InvalidDataException) return L.F("The download of {0} was damaged and has been discarded. Try again.", modelName);
            if (ex is UnauthorizedAccessException) return L.F("No write access to the model folder {0}.", Root);
            if (ex is IOException && (ex.HResult & 0xFFFF) is 0x27 or 0x70) return L.F("Not enough free disk space for {0}. Free up some space and try again.", modelName);
            if (ex is IOException) return L.F("{0} could not be saved: {1}", modelName, ex.Message);
            return L.F("{0} could not be downloaded: {1}", modelName, ex.Message);
        }
        // The stamp records size, write time and hash of a fully verified file, so later starts skip hashing several hundred megabytes.
        static string StampPath(string path) => path + ".verified";
        static string Signature(FileInfo info, ModelFile f) => info.Length + " " + info.LastWriteTimeUtc.Ticks + " " + f.sha256.ToLowerInvariant();
        public static bool Trusted(string path, ModelFile f) { try { var info = new FileInfo(path); return info.Exists && info.Length == f.size && File.ReadAllText(StampPath(path)).Trim() == Signature(info, f); } catch (Exception) { return false; } }
        static void Stamp(string path, ModelFile f) { try { File.WriteAllText(StampPath(path), Signature(new FileInfo(path), f)); } catch (Exception ex) { Storage.Log("model stamp: " + ex.Message); } }
        static void Unstamp(string path) { try { File.Delete(StampPath(path)); } catch (Exception ex) { Storage.Log("model stamp: " + ex.Message); } }
        // The read handle blocks writers while hashing, so the stamp describes exactly the hashed content.
        static async Task<bool> Matches(string path, ModelFile f, bool stamp, CancellationToken token)
        {
            using (var input = File.OpenRead(path)) { if (!Convert.ToHexString(await SHA256.HashDataAsync(input, token)).Equals(f.sha256, StringComparison.OrdinalIgnoreCase)) { if (stamp) Unstamp(path); return false; } if (stamp) Stamp(path, f); return true; }
        }
        public static async Task Verify(string engine) { await Task.Run(() => { foreach (var f in Catalog.Where(x => x.engine == engine)) { string path = Path.Combine(Root, f.engine, f.file); using (var input = File.OpenRead(path)) { var hash = Convert.ToHexString(SHA256.HashData(input)); if (!hash.Equals(f.sha256, StringComparison.OrdinalIgnoreCase)) { Unstamp(path); throw new InvalidDataException(L.F("Model file damaged: {0}. “{1}” downloads it again.", f.file, L.T("Download / verify"))); } Stamp(path, f); } } }); }
        public static void Remove(string engine) { if (!gate.Wait(0)) throw new InvalidOperationException(L.T("Please wait for the running model download to finish.")); try { foreach (var f in Catalog.Where(x => x.engine == engine)) { var path = Path.Combine(Root, f.engine, f.file); foreach (var file in new[] { path, StampPath(path), path + ".partial" }) if (File.Exists(file)) File.Delete(file); } } finally { gate.Release(); } }
    }
}
