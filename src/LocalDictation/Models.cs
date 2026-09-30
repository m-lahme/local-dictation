using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LocalDictation
{
    public sealed class ModelFile { public string engine { get; set; } public string file { get; set; } public string url { get; set; } public string sha256 { get; set; } public long size { get; set; } }
    public static class Models
    {
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        static readonly SemaphoreSlim gate = new SemaphoreSlim(1);
        public static string Root => Environment.GetEnvironmentVariable("LOCAL_DICTATION_MODELS") ?? Path.Combine(Storage.DataDir, "native-models");
        public static ModelFile[] Catalog => JsonSerializer.Deserialize<ModelFile[]>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "model-catalog.json")));
        public static bool Installed(string engine) => Catalog.Where(f => f.engine == engine).All(f => File.Exists(Path.Combine(Root, f.engine, f.file)) && new FileInfo(Path.Combine(Root, f.engine, f.file)).Length == f.size);
        public static async Task Ensure(string engine, Action<string> progress, CancellationToken token)
        {
            await gate.WaitAsync(token); try
            {
                foreach (var f in Catalog.Where(f => f.engine == engine))
                {
                    string dir = Path.Combine(Root, f.engine); Directory.CreateDirectory(dir); string path = Path.Combine(dir, f.file);
                    if (File.Exists(path) && new FileInfo(path).Length == f.size) { using (var existing = File.OpenRead(path)) { if (Convert.ToHexString(await SHA256.HashDataAsync(existing, token)).Equals(f.sha256, StringComparison.OrdinalIgnoreCase)) continue; } }
                    string partial = path + ".partial"; string modelName = SidebarStatus.ModelName(engine); progress(modelName + " wird heruntergeladen …");
                    using (var response = await http.GetAsync(f.url, HttpCompletionOption.ResponseHeadersRead, token)) { response.EnsureSuccessStatusCode(); using (var input = await response.Content.ReadAsStreamAsync(token)) using (var output = File.Create(partial)) { byte[] buffer = new byte[1024 * 256]; long total = 0; int n; long last = 0; while ((n = await input.ReadAsync(buffer, token)) > 0) { await output.WriteAsync(buffer.AsMemory(0, n), token); total += n; if (total - last > 8 * 1024 * 1024) { progress(modelName + " wird heruntergeladen · " + (total * 100 / Math.Max(1, f.size)) + " %"); last = total; } } } }
                    using (var input = File.OpenRead(partial)) { string hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token)); if (!hash.Equals(f.sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Prüfsumme des Modelldownloads stimmt nicht: " + f.file); }
                    File.Move(partial, path, true);
                }
            }
            finally { gate.Release(); }
        }
        public static async Task Verify(string engine) { await Task.Run(() => { foreach (var f in Catalog.Where(x => x.engine == engine)) { using (var input = File.OpenRead(Path.Combine(Root, f.engine, f.file))) { var hash = Convert.ToHexString(SHA256.HashData(input)); if (!hash.Equals(f.sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Modelldatei beschädigt: " + f.file); } } }); }
        public static void Remove(string engine) { if (!gate.Wait(0)) throw new InvalidOperationException("Bitte den laufenden Modelldownload abwarten."); try { foreach (var f in Catalog.Where(x => x.engine == engine)) { var path = Path.Combine(Root, f.engine, f.file); if (File.Exists(path)) File.Delete(path); } } finally { gate.Release(); } }
    }
}
