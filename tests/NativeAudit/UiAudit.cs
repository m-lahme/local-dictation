using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LocalDictation;

internal static class UiAudit
{
    public static void Run(string dataDirectory)
    {
        foreach (var (scheme, expected, name) in new[] { (PowerModelRouting.AsusPdSilent, "parakeet/cpu", "ASUS PD Silent"), (PowerModelRouting.AsusPdTurbo, "whisper/gpu", "ASUS PD Turbo"), (PowerModelRouting.AsusPdPerformance, "whisper/gpu", "ASUS PD Performance"), (PowerModelRouting.AsusPerformance, "whisper/gpu", "ASUS Performance") })
        {
            var route = PowerModelRouting.Resolve(scheme, expected == "whisper/gpu" ? PowerModelRouting.PowerSaver : PowerModelRouting.HighPerformance);
            Equal(expected, route?.Engine + "/" + route?.Device, name + " plan wins over the Windows personality");
        }

        Equal("0.2.0", MainWindow.VersionLabel("0.2.0+43eafd7c1e"), "version label drops the commit suffix");
        Equal("1.0.0-beta.1", MainWindow.VersionLabel("1.0.0-beta.1"), "version label keeps prerelease tags");
        Check(!MainWindow.AppVersion.Contains('+') && MainWindow.AppVersion.StartsWith(typeof(MainWindow).Assembly.GetName().Version.ToString(3)), "about card shows the assembly version [" + MainWindow.AppVersion + "]");

        Equal(@"C:\Program Files\Local Dictation\LocalDictation.exe", Autostart.Target("\"C:\\Program Files\\Local Dictation\\LocalDictation.exe\" --autostart"), "autostart target from quoted command");
        Equal(@"C:\Tools\LocalDictation.exe", Autostart.Target(@"C:\Tools\LocalDictation.exe --autostart"), "autostart target from unquoted command");
        Equal("", Autostart.Target(null), "missing autostart entry has no target");
        Check(Autostart.SameFile(@"C:\Tools\LocalDictation.exe", @"c:\tools\.\LOCALDICTATION.EXE"), "autostart compares normalized paths");
        Check(!Autostart.SameFile(@"C:\Old\LocalDictation.exe", @"C:\New\LocalDictation.exe"), "autostart for another copy is not this copy");

        var ctrl = ClipboardInserter.Virtual(0x11, 2).data.keyboard; var enter = ClipboardInserter.Virtual(0x0D, 0).data.keyboard; var paste = ClipboardInserter.Virtual(0x56, 0).data.keyboard;
        Check(ctrl.vk == 0x11 && ctrl.scan == 0x1D && ctrl.flags == 2, "synthesized Ctrl carries its scan code and keeps key-up flag");
        Check(enter.vk == 0x0D && enter.scan == 0x1C && enter.flags == 0, "synthesized Enter carries its scan code");
        Check(paste.vk == 0x56 && paste.scan != 0 && (paste.flags & 8) == 0, "synthesized V keeps virtual-key semantics with scan code");
        Equal(0x5Bu, ClipboardInserter.ScanCode(0x5B), "replayed Windows key scan code");

        Equal("Segoe Fluent Icons, Segoe MDL2 Assets", Theme.IconFont.Source, "icon font falls back to Segoe MDL2 Assets");
        var glyphs = new[] { 0xE713, 0xE721, 0xE72C, 0xE73E, 0xE74D, 0xE76F, 0xE77F, 0xE7E8, 0xE7F8, 0xE81C, 0xE890, 0xE8C8, 0xE8D2 }.Select(x => ((char)x).ToString()).Concat(new[] { "builtin", "headset", "webcam", "usb", "speakerphone", "virtual", "remote", "microphone" }.Select(MicrophoneCatalog.Glyph)).Distinct().ToList();
        foreach (string family in new[] { "Segoe MDL2 Assets", "Segoe Fluent Icons" })
        {
            bool installed = new Typeface(family).TryGetGlyphTypeface(out var face) && face.FamilyNames.Values.Contains(family);
            // Windows 10 has no Segoe Fluent Icons; there the fallback font alone must cover every glyph.
            if (!installed && family == "Segoe Fluent Icons") { Console.WriteLine("SKIP " + family + " is not installed"); continue; }
            Check(installed, family + " installed");
            var missing = glyphs.Where(x => !face.CharacterToGlyphMap.ContainsKey(x[0])).Select(x => ((int)x[0]).ToString("X4")).ToList();
            Check(missing.Count == 0, family + " contains all " + glyphs.Count + " icon glyphs" + (missing.Count == 0 ? "" : ": missing " + String.Join(",", missing)));
        }
        var menu = ApplicationMenus.Create(new Settings(), false, () => { }, () => { }, () => { }, () => { }, () => { });
        Check(menu.Items.OfType<MenuItem>().All(x => ((TextBlock)x.Icon).FontFamily == Theme.IconFont), "menu icons use the shared icon font");

        Task.Run(() => Downloads(Path.Combine(dataDirectory, "model-download-audit"))).GetAwaiter().GetResult();
        RuleEditing(dataDirectory);
        MainWindowLanguages(dataDirectory);
    }

    static async Task Downloads(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        var body = new byte[700_000]; new Random(7).NextBytes(body); string sha = Convert.ToHexString(SHA256.HashData(body));
        string path = Path.Combine(dir, "model.bin"), partial = path + ".partial";
        var closed = new TcpListener(IPAddress.Loopback, 0); closed.Start(); string unreachable = "http://127.0.0.1:" + ((IPEndPoint)closed.LocalEndpoint).Port + "/model.bin"; closed.Stop();
        using var server = new TestServer { Body = body };
        ModelFile Model(string url, string hash = null, long size = 0) => new ModelFile { engine = "audit", file = "model.bin", url = url, sha256 = hash ?? sha, size = size > 0 ? size : body.Length };
        Task Fetch(ModelFile f) => Models.Fetch(f, dir, "Testmodell", x => { }, CancellationToken.None);
        bool Complete() => File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(body);
        void Reset(byte[] data = null) { File.Delete(path); File.Delete(partial); if (data != null) File.WriteAllBytes(partial, data); server.Requests.Clear(); }

        await Fetch(Model(server.Url));
        Check(Complete() && !File.Exists(partial), "model download writes the verified file");
        Check(Models.Trusted(path, Model(server.Url)), "verified model gets a stamp");
        await Fetch(Model(unreachable));
        Equal(1, server.Requests.Count, "stamped model starts without download");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));
        Check(!Models.Trusted(path, Model(server.Url)), "changed write time invalidates the stamp");
        await Fetch(Model(unreachable));
        Check(Models.Trusted(path, Model(server.Url)), "full hash renews the stamp without download");
        Check(!Models.Trusted(path, Model(server.Url, new string('0', 64))), "catalog hash change invalidates the stamp");
        var damaged = File.ReadAllBytes(path); damaged[100] ^= 0xFF; File.WriteAllBytes(path, damaged); server.Requests.Clear();
        await Fetch(Model(server.Url));
        Check(Complete() && server.Requests.Count == 1, "damaged model of the right size is downloaded again");

        Reset(body.Take(300_000).ToArray()); await Fetch(Model(server.Url));
        Check(Complete() && server.Requests.SequenceEqual(new long[] { 300_000 }), "interrupted download resumes with a range request");
        server.HonorRange = false; Reset(body.Take(300_000).ToArray()); await Fetch(Model(server.Url));
        Check(Complete(), "server without range support restarts the download"); server.HonorRange = true;
        Reset(new byte[300_000]); await Fetch(Model(server.Url));
        Check(Complete() && server.Requests.SequenceEqual(new long[] { 300_000, 0 }), "resumed download with wrong hash retries once from the start");

        Reset();
        var mismatch = await Fails(Fetch(Model(server.Url, new string('0', 64))));
        Check(mismatch.InnerException is InvalidDataException && !File.Exists(partial) && !File.Exists(path), "hash mismatch deletes the partial download and activates nothing");

        Models.StallTimeout = TimeSpan.FromMilliseconds(400); server.StallAfter = 50_000;
        try
        {
            var stalled = await Fails(Fetch(Model(server.Url)));
            Check(stalled.InnerException is TimeoutException && stalled.Message.Contains("stalled") && new FileInfo(partial).Length == 50_000, "stalled download times out and keeps its partial file");
        }
        finally { Models.StallTimeout = TimeSpan.FromSeconds(60); server.StallAfter = -1; }
        server.Requests.Clear(); await Fetch(Model(server.Url));
        Check(Complete() && server.Requests.SequenceEqual(new long[] { 50_000 }), "stalled download resumes where it stopped");

        Reset(); server.Status = 404;
        Check((await Fails(Fetch(Model(server.Url)))).Message.Contains("HTTP 404"), "server error names the HTTP status");
        Reset(body.Take(1000).ToArray()); server.Status = 416;
        await Fails(Fetch(Model(server.Url)));
        Check(!File.Exists(partial), "unsatisfiable range deletes the partial download"); server.Status = 200; server.Requests.Clear();
        Check((await Fails(Fetch(Model(server.Url, size: 1L << 60)))).Message.Contains("disk space") && server.Requests.Count == 0, "free space is checked before downloading");
        Check((await Fails(Fetch(Model(unreachable)))).Message.Contains("internet connection"), "unreachable server is explained");
        Check(Models.Describe(new HttpRequestException("x", null, HttpStatusCode.ProxyAuthenticationRequired), "Whisper").Contains("proxy"), "proxy authentication failure is explained");
        Check(Models.Describe(new IOException("x", unchecked((int)0x80070070)), "Whisper").Contains("disk space"), "full disk is explained");
        string language = L.Language; L.Language = "de";
        try { Check(Models.Describe(new IOException("x", unchecked((int)0x80070070)), "Whisper") == "Zu wenig freier Speicherplatz für Whisper. Gib Speicherplatz frei und versuche es erneut." && Models.Describe(new TimeoutException(), "Whisper").Contains("ins Stocken geraten"), "download errors keep the German wording"); }
        finally { L.Language = language; }
    }

    static void RuleEditing(string dataDirectory)
    {
        var settings = new Settings { Theme = "dark" }; settings.Rules.Add(new ReplacementRule { Source = "alt", Target = "Ziel" }); Storage.SaveSettings(settings);
        var window = new MainWindow(settings, dataDirectory, x => { }, x => { });
        var area = SystemParameters.WorkArea;
        Check(window.Width <= area.Width - 32 && window.Height <= area.Height - 32 && window.MinWidth <= window.Width && window.MinHeight <= window.Height, "main window fits the work area");
        window.ShowDictionary();
        Check(Find<Button>(window).First(x => AutomationProperties.GetName(x) == "Delete rule 1").FontFamily == Theme.IconFont, "icon buttons use the shared icon font");
        var box = Find<TextBox>(window).First(x => AutomationProperties.GetName(x) == "Replace 1");
        string Saved() => Storage.LoadSettings().Rules[0].Source;
        box.Text = "neu";
        Equal("neu", settings.Rules[0].Source, "rule edit updates the settings at once");
        Equal("alt", Saved(), "rule edit does not write on every keystroke");
        Pump(800); Equal("neu", Saved(), "rule edit saves after a short pause");
        box.Text = "Fokus"; box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent)); Equal("Fokus", Saved(), "rule edit saves when the field loses focus");
        box.Text = "Seite"; window.ShowHistory(); Equal("Seite", Saved(), "rule edit saves on page change");
        window.Close();
    }

    // Every page of the main window follows the UI language, also after switching it while the window exists.
    static void MainWindowLanguages(string dataDirectory)
    {
        string previous = L.Language, dir = Path.Combine(dataDirectory, "main-window-languages"); MainWindow window = null;
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var time = DateTime.Today.AddHours(9).AddMinutes(7); var english = System.Globalization.CultureInfo.GetCultureInfo("en-US"); var german = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        Storage.Write(Path.Combine(dir, "history.json"), new[] { new HistoryEntry { Time = time, DurationMs = 4700, Engine = "whisper", Text = "a", ReviewRecommended = true, AudioFile = "audio.wav", AudioExpiresAtUtc = DateTime.UtcNow.AddMinutes(30) }, new HistoryEntry { Time = time.AddMinutes(-1), DurationMs = 1000, Engine = "parakeet", Text = "b", Status = "not-transcribed" } });
        var settings = new Settings { Theme = "dark" }; settings.Rules.Add(new ReplacementRule { Source = "x", Target = "y" });
        // Language names in the language selector are shown in their own language on purpose.
        var germanTexts = L.GermanTexts.Where(x => x.Key != x.Value && x.Value != "Deutsch").Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        List<string> Pages()
        {
            var texts = new List<string>();
            window.ShowHistory(); texts.AddRange(Texts(window)); window.ShowDictionary(); texts.AddRange(Texts(window)); window.ShowStatistics(); texts.AddRange(Texts(window));
            foreach (string key in new[] { "model", "audio", "output", "appearance", "general" }) { window.ShowSettings(key); texts.AddRange(Texts(window)); }
            window.ShowCorrection("Das ist ein Tset", "Das ist ein Test", CorrectionLearning.Analyze("Das ist ein Tset", "Das ist ein Test")); texts.AddRange(Texts(window));
            return texts;
        }
        void NoGerman(List<string> texts, string name) { var left = texts.Where(germanTexts.Contains).Distinct().ToList(); Check(left.Count == 0, name + (left.Count == 0 ? " [" + texts.Count + " texts]" : ": " + String.Join(" | ", left))); }
        try
        {
            L.Language = "en"; window = new MainWindow(settings, dir, x => { }, x => { }, (h, engine) => { });
            var en = Pages(); NoGerman(en, "main window pages show no German text in English");
            Check(en.Contains("◷  History") && en.Contains("Delete rule 1") && en.Contains("Replace 1") && en.Contains("1 rule") && en.Contains("Dictation hotkey") && en.Contains("Starting…"), "main window pages are English");
            Check(en.Any(x => x.StartsWith(time.ToString("MMM d · HH:mm", english) + "  ·  4.7 s  ·  Whisper  ·  Audio expires in ")) && en.Contains(time.AddMinutes(-1).ToString("MMM d · HH:mm", english) + "  ·  1.0 s  ·  Parakeet"), "English history uses the English date and number format");
            Check(en.Any(x => x.Contains("“bullet point”, “new line” and “new paragraph”") && x.Contains("“literally bullet point”")), "English voice-command description names the English commands");
            L.Language = "de"; window.RefreshTheme(); var shell = Texts(window);
            Check(shell.Contains("◷  Verlauf") && shell.Contains("Beenden") && shell.Contains("Wird gestartet …"), "language switch rebuilds the main window in German");
            var de = Pages();
            Check(de.Contains("Regel 1 löschen") && de.Contains("Ersetzen 1") && de.Contains("Tastenkombination") && de.Contains("Wörter transkribiert") && de.Contains("Korrektur lernen"), "main window pages are German");
            Check(de.Any(x => x.StartsWith(time.ToString("dd.MM. · HH:mm", german) + "  ·  4,7 s  ·  Whisper  ·  Audio noch ")) && de.Contains(time.AddMinutes(-1).ToString("dd.MM. · HH:mm", german) + "  ·  1,0 s  ·  Parakeet"), "German history keeps its date and number format");
            Check(de.Contains("Am Satz- oder Diktatbeginn werden „Stichpunkt“, „neue Zeile“ und „neuer Absatz“ formatiert. Mit „wörtlich Stichpunkt“ bleibt das Wort erhalten."), "German voice-command description keeps the German commands");
            L.Language = "en"; window.RefreshTheme(); shell = Texts(window);
            Check(shell.Contains("◷  History") && shell.Contains("Quit") && shell.Contains("Starting…"), "language switch rebuilds the main window in English");
            NoGerman(Pages(), "main window pages show no German text after switching back to English");
        }
        finally { L.Language = previous; window?.Close(); }
    }

    // Visible texts, tooltips, list values and automation names of the laid-out window content, including virtualized rows.
    static List<string> Texts(Window window)
    {
        var root = (FrameworkElement)window.Content; root.Measure(new Size(1040, 740)); root.Arrange(new Rect(0, 0, 1040, 740)); root.UpdateLayout();
        var texts = new List<string>(); var seen = new HashSet<DependencyObject>(); var pending = new Stack<DependencyObject>(); pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop(); if (!seen.Add(node)) continue;
            if (node is TextBlock block) texts.Add(block.Text);
            if (node is ContentControl control && control.Content is string content) texts.Add(content);
            if (node is FrameworkElement element && element.ToolTip is string tip) texts.Add(tip);
            if (node is ItemsControl items && items.ItemsSource is IEnumerable<string> values) texts.AddRange(values);
            string name = AutomationProperties.GetName(node); if (!String.IsNullOrEmpty(name)) texts.Add(name);
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) pending.Push(child);
            if (node is Visual) for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) pending.Push(VisualTreeHelper.GetChild(node, i));
        }
        return texts;
    }

    static async Task<ModelDownloadException> Fails(Task task)
    {
        try { await task; } catch (ModelDownloadException ex) { return ex; }
        throw new Exception("model download should have failed");
    }

    // Serves one byte array over HTTP/1.1 with optional range support, errors and a stall after some bytes.
    sealed class TestServer : IDisposable
    {
        readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0); readonly CancellationTokenSource stop = new CancellationTokenSource();
        public byte[] Body = Array.Empty<byte>(); public bool HonorRange = true; public int StallAfter = -1, Status = 200;
        public readonly List<long> Requests = new List<long>();
        public string Url => "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/model.bin";
        public TestServer() { listener.Start(); _ = Task.Run(Loop); }
        async Task Loop() { while (!stop.IsCancellationRequested) { TcpClient client; try { client = await listener.AcceptTcpClientAsync(stop.Token); } catch (Exception) { return; } _ = Task.Run(() => Serve(client)); } }
        async Task Serve(TcpClient client)
        {
            using (client) try
            {
                var stream = client.GetStream(); var head = new StringBuilder(); var one = new byte[1];
                while (!head.ToString().EndsWith("\r\n\r\n")) { if (await stream.ReadAsync(one, stop.Token) == 0) return; head.Append((char)one[0]); }
                var range = Regex.Match(head.ToString(), @"^Range: bytes=(\d+)-", RegexOptions.IgnoreCase | RegexOptions.Multiline); long from = range.Success ? long.Parse(range.Groups[1].Value) : 0;
                lock (Requests) Requests.Add(from);
                if (!HonorRange) from = 0;
                bool ok = Status == 200; string status = !ok ? Status + " Fehler" : from > 0 ? "206 Partial Content" : "200 OK";
                string reply = "HTTP/1.1 " + status + "\r\n" + (ok && from > 0 ? "Content-Range: bytes " + from + "-" + (Body.Length - 1) + "/" + Body.Length + "\r\n" : "") + "Content-Length: " + (ok ? Body.Length - from : 0) + "\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(reply), stop.Token);
                if (!ok) return;
                int count = (int)(Body.Length - from); if (StallAfter >= 0) count = Math.Min(count, StallAfter);
                await stream.WriteAsync(Body.AsMemory((int)from, count), stop.Token);
                if (StallAfter >= 0) await Task.Delay(5000, stop.Token);
            }
            catch (Exception) { }
        }
        public void Dispose() { stop.Cancel(); listener.Stop(); }
    }

    static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var found in Find<T>(child)) yield return found;
    }

    static void Pump(int ms)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
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
