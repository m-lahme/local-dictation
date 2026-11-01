using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalDictation
{
    public enum AppState { Idle, Recording, Waiting, Transcribing, Success, Review, Error }
    public enum OutputMode { Insert, Clipboard, Both }
    public static class SidebarStatus
    {
        public static string ModelName(string engine) => engine == "whisper" ? "Whisper" : "Parakeet";
        public static string Preparing(string engine) => ModelName(engine) + " wird vorbereitet …";
        public static string Ready(string engine) => "Bereit · " + ModelName(engine);
        public static string Completed(OutputMode mode) => mode == OutputMode.Clipboard ? "Text kopiert" : mode == OutputMode.Both ? "Text eingefügt und kopiert" : "Text eingefügt";
    }
    public sealed class ReplacementRule
    {
        public string Source { get; set; } = "";
        public string Target { get; set; } = "";
        public bool Whole { get; set; } = true;
        public bool CaseSensitive { get; set; }
    }
    // One physical microphone in the priority list; Windows may expose it through several endpoint ids.
    public sealed class MicrophoneEntry
    {
        public string Key { get; set; } = "";
        public List<string> Ids { get; set; } = new List<string>();
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Device { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Connection { get; set; } = "";
    }
    public sealed class Settings
    {
        public int Version { get; set; } = 2;
        public string Engine { get; set; } = "parakeet";
        public string Theme { get; set; } = "system";
        public string Device { get; set; } = "gpu";
        public bool AutomaticPowerModel { get; set; }
        public int WhisperIdleMinutes { get; set; } = 5;
        public string Microphone { get; set; } = "";
        public List<MicrophoneEntry> Microphones { get; set; } = new List<MicrophoneEntry>();
        public string Anchor { get; set; } = "bottom-center";
        public string Monitor { get; set; } = "";
        public string Hotkey { get; set; } = HotkeyInput.Binding;
        public int HoldMs { get; set; } = 400;
        public bool OverlayVisible { get; set; } = true;
        public bool CloseExplained { get; set; }
        public bool VoiceCommands { get; set; } = true;
        public int TypingWpm { get; set; } = 40;
        public OutputMode Output { get; set; } = OutputMode.Insert;
        public List<ReplacementRule> Rules { get; set; } = new List<ReplacementRule>();
    }
    public static class ModelSelection
    {
        public static void Normalize(Settings settings)
        {
            if (settings.Engine == "whisper") settings.Device = "gpu";
            else { settings.Engine = "parakeet"; settings.Device = "cpu"; }
        }
        public static void Normalize(ref string engine, ref string device)
        {
            if (engine == "whisper" && device == "gpu") return;
            engine = "parakeet"; device = "cpu";
        }
    }
    public static class Replacements
    {
        const int Limit = 10 * 1024 * 1024;
        public static string Apply(string text, IEnumerable<ReplacementRule> rules)
        {
            text = text.Normalize(NormalizationForm.FormC); int count = 0;
            foreach (var r in rules)
            {
                if (++count > 1000) throw new InvalidDataException("Maximal 1.000 Ersetzungsregeln erlaubt.");
                if (String.IsNullOrEmpty(r.Source)) continue;
                text = UnicodeReplacement.Apply(text, r);
                if (Encoding.UTF8.GetByteCount(text) > Limit) throw new InvalidDataException("Das Wörterbuch erzeugt zu viel Text. Das ursprüngliche Diktat bleibt im Verlauf.");
            }
            return text;
        }
    }
    public sealed class HistoryEntry
    {
        public string Id { get; set; } = "";
        public DateTime Time { get; set; }
        public int DurationMs { get; set; }
        public int ProcessingMs { get; set; }
        public string Engine { get; set; } = "";
        public string RawText { get; set; } = "";
        public string Text { get; set; } = "";
        public string Status { get; set; } = "complete";
        public double Confidence { get; set; } = 1;
        public bool ReviewRecommended { get; set; }
        public string AudioFile { get; set; } = "";
        public DateTime? AudioExpiresAtUtc { get; set; }
        public List<TranscriptVariant> Variants { get; set; } = new List<TranscriptVariant>();
    }
    public sealed class TranscriptVariant
    {
        public string Engine { get; set; } = "";
        public string RawText { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public double Confidence { get; set; } = 1;
        public bool ReviewRecommended { get; set; }
    }
    public static class JsonLite
    {
        public static string Escape(string s) { var json = JsonSerializer.Serialize(s ?? ""); return json.Substring(1, json.Length - 2); }
        public static string Value(string json, string key) { using (var d = JsonDocument.Parse(json)) { JsonElement e; return d.RootElement.TryGetProperty(key, out e) && e.ValueKind == JsonValueKind.String ? e.GetString() : ""; } }
        public static int IntValue(string json, string key) { using (var d = JsonDocument.Parse(json)) { JsonElement e; int n; return d.RootElement.TryGetProperty(key, out e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out n) ? n : 0; } }
        public static double DoubleValue(string json, string key) { using (var d = JsonDocument.Parse(json)) { JsonElement e; double n; return d.RootElement.TryGetProperty(key, out e) && e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out n) ? n : 0; } }
        public static bool BoolValue(string json, string key) { using (var d = JsonDocument.Parse(json)) { JsonElement e; return d.RootElement.TryGetProperty(key, out e) && e.ValueKind == JsonValueKind.True; } }
    }
    // Shared tap/hold state machine; callbacks never perform work in the keyboard hook.
    public sealed class HotkeyGesture
    {
        bool pressed, stopping, toggle; long started, lastTapReleased = Int64.MinValue;
        public int HoldMs = 400;
        public int DoubleTapMs = 320;
        public HotkeyGesture Copy() => (HotkeyGesture)MemberwiseClone();
        public string Down(long now) { if (pressed) return ""; pressed = true; started = now; stopping = toggle; if (toggle) { toggle = false; return now-lastTapReleased<=DoubleTapMs?"correction":"stop"; } return "start"; }
        public string Up(long now) { if (!pressed) return ""; pressed = false; if (stopping) { stopping = false; return ""; } if (now - started >= HoldMs) { toggle = false; return "stop"; } toggle = true; lastTapReleased=now; return ""; }
        public void Reset() { pressed = false; stopping = false; toggle = false; lastTapReleased=Int64.MinValue; }
    }
}
