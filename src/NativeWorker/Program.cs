using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SherpaOnnx;
using Whisper.net;
using Whisper.net.LibraryLoader;

// Native model DLLs live in this isolated process. No microphone or user files are opened here.
internal static class Program
{
    static readonly object OutputLock = new();
    static void Emit(object value) { lock (OutputLock) Console.WriteLine(JsonSerializer.Serialize(value)); }
    static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8; Console.OutputEncoding = new UTF8Encoding(false);
        string Get(string key, string fallback) { var i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
        // Quick check without loading a model, used by the app to return to Whisper once the GPU is available again.
        if (args.Contains("--probe")) { try { VisualCppRuntime.Require(); Emit(new { @event = "probe", state = "available", adapter = VulkanDevice.SelectDiscrete() }); } catch (Exception ex) { Emit(new { @event = "probe", state = "unavailable", message = ex.Message }); } return 0; }
        string engine = Get("--engine", "whisper"), device = Get("--device", "cpu");
        string depot = Get("--models", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalDictation", "native-models"));
        try
        {
            if (engine == "whisper" && device != "gpu") throw new InvalidOperationException(Messages.T("Whisper on the CPU is disabled. Use Whisper/GPU or Parakeet/CPU.", "Whisper auf der CPU ist deaktiviert. Verwende Whisper/GPU oder Parakeet/CPU."));
            using var logger = Whisper.net.Logger.LogProvider.AddLogger((level, message) => { Console.Error.Write(message); WhisperLog.Add(message); }); var load = Stopwatch.StartNew();
            Emit(new { @event = "model", state = "loading", engine });
            using var backend = new Backend(engine, device, depot);
            await backend.WarmUp(); Emit(new { @event = "model", state = "ready", engine, backend = backend.Name, load_ms = load.ElapsedMilliseconds });
            if (args.Contains("--file"))
            {
                foreach (var file in Get("--file", "").Split('|'))
                {
                    using var reader = new AudioFileReader(file);
                    ISampleProvider samples = reader;
                    if (samples.WaveFormat.Channels == 2) samples = new StereoToMonoSampleProvider(samples);
                    if (samples.WaveFormat.SampleRate != 16000) samples = new WdlResamplingSampleProvider(samples, 16000);
                    var audio = new List<float>(); var buffer = new float[16000]; int read;
                    while ((read = samples.Read(buffer, 0, buffer.Length)) > 0) audio.AddRange(buffer.Take(read));
                    var watch = Stopwatch.StartNew(); var result = await backend.Transcribe(audio.ToArray());
                    Emit(new { @event = "transcript", file, text = result.Text, engine, backend = backend.Name, inference_ms = watch.ElapsedMilliseconds, duration_ms = audio.Count / 16, confidence = result.Confidence, avg_logprob = result.AverageLogProbability, low_token_ratio = result.LowTokenRatio, segment_count = result.SegmentCount, chunk_count = result.Chunks.Count, chunks = result.Chunks, style_score = result.StyleScore, quality = result.Quality, review_recommended = result.Review, style_retry_attempted = result.RetryAttempted, style_retry = result.RetryUsed });
                }
                return 0;
            }
            while (await Console.In.ReadLineAsync() is { } line)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
                    if (root.GetProperty("command").GetString() == "quit") break;
                    var id = root.GetProperty("id").GetInt32();
                    byte[] bytes = Convert.FromBase64String(root.GetProperty("audio").GetString()!);
                    if (bytes.Length % 4 != 0) throw new InvalidDataException(Messages.T("Invalid audio length", "Ungültige Audiolänge"));
                    var audio = new float[bytes.Length / 4]; Buffer.BlockCopy(bytes, 0, audio, 0, bytes.Length);
                    if (audio.Any(x => !float.IsFinite(x))) throw new InvalidDataException(Messages.T("Invalid audio samples", "Ungültige Audiosamples"));
                    Emit(new { @event = "state", value = "transcribing", id });
                    var watch = Stopwatch.StartNew();
                    var result = audio.Length < 1600 || audio.All(x => Math.Abs(x) < 0.0001) ? TranscriptResult.Empty : await backend.Transcribe(audio);
                    Emit(new { @event = "transcript", text = result.Text, id, engine, backend = backend.Name, inference_ms = watch.ElapsedMilliseconds, duration_ms = audio.Length / 16, confidence = result.Confidence, avg_logprob = result.AverageLogProbability, low_token_ratio = result.LowTokenRatio, segment_count = result.SegmentCount, chunk_count = result.Chunks.Count, chunks = result.Chunks, style_score = result.StyleScore, quality = result.Quality, review_recommended = result.Review, style_retry_attempted = result.RetryAttempted, style_retry = result.RetryUsed });
                }
                catch (Exception ex) { Emit(new { @event = "error", message = ex.Message }); }
            }
            return 0;
        }
        catch (Exception ex) { Emit(new { @event = "error", where = "model", message = ex.Message }); Console.Error.WriteLine(ex); return 1; }
    }
    sealed class Backend : IDisposable
    {
        const string StylePrompt = "Guten Morgen. Dies ist ein korrekt geschriebener Satz. Good morning. This is a correctly written sentence.";
        readonly WhisperFactory? factory; readonly WhisperProcessor? whisper; WhisperProcessor? whisperStyle; readonly OfflineRecognizer? parakeet; readonly string? gpu;
        public string Name { get; }
        public Backend(string engine, string device, string depot)
        {
            if (engine == "whisper")
            {
                VisualCppRuntime.Require(); string adapter = device == "gpu" ? VulkanDevice.SelectDiscrete() : "CPU"; RuntimeOptions.RuntimeLibraryOrder = device == "gpu" ? [RuntimeLibrary.Vulkan] : [RuntimeLibrary.Cpu];
                WhisperFactory Load(int index) => WhisperFactory.FromPath(Path.Combine(depot, "whisper", "ggml-large-v3-turbo-q5_0.bin"), new WhisperFactoryOptions { UseGpu = device == "gpu", GpuDevice = index, UseFlashAttention = true });
                factory = Load(device == "gpu" ? VulkanDevice.Index : 0);
                // GpuDevice indexes ggml's GPU list, which can skip Vulkan devices. Reload once with ggml's index of the selected adapter.
                if (device == "gpu" && WhisperLog.IndexOf(adapter) is int index && index != VulkanDevice.Index) { factory.Dispose(); factory = Load(index); }
                whisper = CreateWhisperProcessor(); gpu = device == "gpu" ? adapter : null;
                Name = "whisper.cpp / " + RuntimeOptions.LoadedLibrary + " / " + adapter;
            }
            else
            {
                string FileFor(string name) { var path = Path.Combine(depot, "parakeet", name); return File.Exists(path) ? path : throw new FileNotFoundException(Messages.T("Parakeet model file is missing: " + path, "Parakeet-Modelldatei fehlt: " + path), path); }
                var config = new OfflineRecognizerConfig();
                config.FeatConfig.SampleRate = 16000; config.FeatConfig.FeatureDim = 128;
                config.ModelConfig.Transducer.Encoder = FileFor("encoder.int8.onnx");
                config.ModelConfig.Transducer.Decoder = FileFor("decoder.int8.onnx");
                config.ModelConfig.Transducer.Joiner = FileFor("joiner.int8.onnx");
                config.ModelConfig.Tokens = FileFor("tokens.txt");
                config.ModelConfig.ModelType = "nemo_transducer";
                config.ModelConfig.Provider = "cpu"; config.ModelConfig.NumThreads = Math.Min(6, Environment.ProcessorCount);
                config.DecodingMethod = "greedy_search";
                parakeet = new OfflineRecognizer(config); Name = "sherpa-onnx / CPU int8";
            }
        }
        WhisperProcessor CreateWhisperProcessor(bool styled = false)
        {
            var builder = factory!.CreateBuilder().WithLanguage("auto").WithNoContext().WithProbabilities().WithThreads(Math.Min(8, Environment.ProcessorCount));
            if (styled) builder.WithPrompt(StylePrompt).WithCarryInitialPrompt();
            return builder.Build();
        }
        public async Task WarmUp()
        {
            await Transcribe(new float[16000], true);
            if (gpu != null && WhisperLog.UsedDevice != gpu) throw new InvalidOperationException(Messages.T("whisper.cpp is using " + (WhisperLog.UsedDevice ?? "the CPU") + " instead of the GPU " + gpu + ".", "whisper.cpp nutzt " + (WhisperLog.UsedDevice ?? "die CPU") + " statt der GPU " + gpu + "."));
        }
        public async Task<TranscriptResult> Transcribe(float[] audio, bool warmup = false)
        {
            if (!warmup && (audio.Length < 1600 || audio.All(x => Math.Abs(x) < 0.0001))) return TranscriptResult.Empty;
            if (whisper != null)
            {
                var chunks = new List<SelectedChunk>(); bool retryAttempted = false, retryUsed = false;
                if (audio.Length > 16000 * 35)
                {
                    for (int offset = 0; offset < audio.Length;)
                    {
                        int end = Math.Min(audio.Length, offset + 16000 * 30);
                        if (end < audio.Length)
                        {
                            double best = double.MaxValue; int split = end;
                            for (int n = offset + 16000 * 25; n < end - 1600; n += 800)
                            {
                                double energy = 0; for (int j = n; j < n + 1600; j++) energy += audio[j] * audio[j];
                                if (energy < best) { best = energy; split = n + 800; }
                            }
                            end = split;
                        }
                        var part = new float[end - offset]; Array.Copy(audio, offset, part, 0, part.Length);
                        var selected = await ProcessWhisper(part, whisper); bool attempted = false, used = false;
                        if (!warmup && TranscriptQuality.IsSuspicious(selected.Text))
                        {
                            attempted = retryAttempted = true; whisperStyle ??= CreateWhisperProcessor(true);
                            var retry = await ProcessWhisper(part, whisperStyle);
                            if (TranscriptQuality.ShouldUseRetry(selected.Text, retry.Text, StylePrompt)) { selected = retry; used = retryUsed = true; }
                        }
                        chunks.Add(new SelectedChunk(selected, attempted, used)); offset = end;
                    }
                }
                else
                {
                    var selected = await ProcessWhisper(audio, whisper); bool attempted = false, used = false;
                    if (!warmup && TranscriptQuality.IsSuspicious(selected.Text))
                    {
                        attempted = retryAttempted = true; whisperStyle ??= CreateWhisperProcessor(true);
                        var retry = await ProcessWhisper(audio, whisperStyle);
                        if (TranscriptQuality.ShouldUseRetry(selected.Text, retry.Text, StylePrompt)) { selected = retry; used = retryUsed = true; }
                    }
                    chunks.Add(new SelectedChunk(selected, attempted, used));
                }
                return TranscriptResult.FromWhisper(chunks, retryAttempted, retryUsed);
            }
            using var stream = parakeet!.CreateStream(); if (stream.Handle == IntPtr.Zero) throw new InvalidOperationException(Messages.T("Parakeet could not be loaded: sherpa-onnx rejected the model files.", "Parakeet konnte nicht geladen werden, sherpa-onnx hat die Modelldateien abgelehnt."));
            stream.AcceptWaveform(16000, audio); parakeet.Decode(stream);
            return TranscriptResult.FromParakeet(stream.Result.Text.Trim());
        }
        static async Task<WhisperPass> ProcessWhisper(float[] audio, WhisperProcessor processor)
        {
            var text = new StringBuilder(); double logSum = 0; int tokenCount = 0, lowTokens = 0, segments = 0;
            await foreach (var segment in processor.ProcessAsync(audio))
            {
                text.Append(segment.Text); segments++;
                foreach (var token in segment.Tokens ?? [])
                {
                    if (!TranscriptQuality.IsLexicalToken(token.Text) || !float.IsFinite(token.ProbabilityLog)) continue;
                    logSum += token.ProbabilityLog; tokenCount++; if (token.Probability < 0.25f) lowTokens++;
                }
            }
            return new WhisperPass(text.ToString().Trim(), logSum, tokenCount, lowTokens, segments);
        }
        public void Dispose() { whisperStyle?.Dispose(); whisper?.Dispose(); factory?.Dispose(); parakeet?.Dispose(); }
    }

    // whisper.cpp silently runs on the CPU when GpuDevice matches no GPU. Its log lists ggml's Vulkan devices and the backend each state really uses.
    static class WhisperLog
    {
        static readonly System.Text.RegularExpressions.Regex Line = new(@"^(?:ggml_vulkan: (?<index>\d+) = (?<name>.+) \([^()]*\) \| uma|whisper_backend_init_gpu: (?:using Vulkan(?<used>\d+) backend|no GPU found))", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Multiline);
        static readonly Dictionary<int, string> devices = []; static int used = -1;
        public static void Add(string? message)
        {
            if (message == null) return;
            lock (devices) foreach (System.Text.RegularExpressions.Match match in Line.Matches(message)) if (match.Groups["name"].Success) devices[int.Parse(match.Groups["index"].Value)] = match.Groups["name"].Value; else used = match.Groups["used"].Success ? int.Parse(match.Groups["used"].Value) : -1;
        }
        public static int? IndexOf(string adapter) { lock (devices) return devices.Where(x => x.Value == adapter).Select(x => (int?)x.Key).Min(); }
        public static string? UsedDevice { get { lock (devices) return used < 0 ? null : devices.GetValueOrDefault(used, "Vulkan" + used); } }
    }

    sealed record WhisperPass(string Text, double LogProbabilitySum, int TokenCount, int LowTokens, int SegmentCount);
    sealed record SelectedChunk(WhisperPass Pass, bool RetryAttempted, bool RetryUsed);
    sealed record ChunkQuality(int Words, int Segments, double? Confidence, double? AverageLogProbability, double? LowTokenRatio, int StyleScore, bool Review, bool RetryAttempted, bool RetryUsed);

    sealed record TranscriptResult(string Text, double? Confidence, double? AverageLogProbability, double? LowTokenRatio, int SegmentCount, int StyleScore, string Quality, bool Review, bool RetryAttempted, bool RetryUsed, IReadOnlyList<ChunkQuality> Chunks)
    {
        public static readonly TranscriptResult Empty = new("", null, null, null, 0, 0, "good", false, false, false, []);
        public static TranscriptResult FromParakeet(string text) => new(text, null, null, null, 0, TranscriptQuality.Score(text), "good", false, false, false, []);
        public static TranscriptResult FromWhisper(IReadOnlyList<SelectedChunk> selectedChunks, bool attempted, bool used)
        {
            var chunks = selectedChunks.Select(x => x.Pass).ToList();
            string text = String.Join(" ", chunks.Select(x => x.Text).Where(x => x.Length > 0));
            int tokens = chunks.Sum(x => x.TokenCount), low = chunks.Sum(x => x.LowTokens), segments = chunks.Sum(x => x.SegmentCount);
            double? avgLog = tokens > 0 ? chunks.Sum(x => x.LogProbabilitySum) / tokens : null;
            double? lowRatio = tokens > 0 ? (double)low / tokens : null;
            double? confidence = avgLog.HasValue ? Math.Clamp(Math.Exp(avgLog.Value), 0, 1) : null;
            int style = TranscriptQuality.Score(text);
            bool review = style >= 2 || (avgLog.HasValue && avgLog.Value < -0.85) || (lowRatio.HasValue && lowRatio.Value > 0.25);
            var qualityChunks = selectedChunks.Select(x =>
            {
                var c = x.Pass; double? cLog = c.TokenCount > 0 ? c.LogProbabilitySum / c.TokenCount : null, cLow = c.TokenCount > 0 ? (double)c.LowTokens / c.TokenCount : null;
                double? cConfidence = cLog.HasValue ? Math.Clamp(Math.Exp(cLog.Value), 0, 1) : null; int cStyle = TranscriptQuality.Score(c.Text);
                bool cReview = cStyle >= 2 || (cLog.HasValue && cLog.Value < -0.85) || (cLow.HasValue && cLow.Value > 0.25);
                return new ChunkQuality(TranscriptQuality.WordCount(c.Text), c.SegmentCount, cConfidence, cLog, cLow, cStyle, cReview, x.RetryAttempted, x.RetryUsed);
            }).ToList();
            return new(text, confidence, avgLog, lowRatio, segments, style, review ? "review" : "good", review, attempted, used, qualityChunks);
        }
    }

    static class TranscriptQuality
    {
        static readonly System.Text.RegularExpressions.Regex Word = new(@"\p{L}[\p{L}\p{M}\p{N}'’_-]*", System.Text.RegularExpressions.RegexOptions.Compiled);
        static readonly System.Text.RegularExpressions.Regex SentenceMark = new(@"[.!?…։。！？؟।۔]", System.Text.RegularExpressions.RegexOptions.Compiled);
        public static bool IsLexicalToken(string? text) => !String.IsNullOrWhiteSpace(text) && !text.Contains("<|", StringComparison.Ordinal) && text.Any(Char.IsLetterOrDigit);
        public static int WordCount(string text) => Word.Matches(text).Count;
        public static bool IsSuspicious(string text) => Score(text) >= 2;
        public static int Score(string text)
        {
            var words = Word.Matches(text); if (words.Count < 15 || !UsesLetterCase(text)) return 0;
            int upper = words.Count(x => x.Value.Any(Char.IsUpper)), punctuation = SentenceMark.Matches(text).Count, score = 0;
            if (upper == 0) score += 3;
            if (punctuation == 0 && upper <= 1) score += 3;
            if (HasAnchoredLowCaseRun(text, words)) score += 2;
            return score;
        }
        // All style penalties look for missing capitals. Arabic, Hebrew, Indic, CJK and Georgian (Mkhedruli is formally lowercase) text has none to miss.
        static bool UsesLetterCase(string text)
        {
            int letters = 0, cased = 0; foreach (char c in text) if (Char.IsLetter(c)) { letters++; if ((Char.IsUpper(c) || Char.IsLower(c)) && c is < '\u10D0' or > '\u10FF') cased++; }
            return cased * 2 >= letters;
        }
        static bool HasAnchoredLowCaseRun(string text, System.Text.RegularExpressions.MatchCollection words)
        {
            var anchors = new List<int>();
            if (words.Count > 0 && words[0].Value.Any(Char.IsLower) && !words[0].Value.Any(Char.IsUpper)) anchors.Add(0);
            foreach (System.Text.RegularExpressions.Match mark in SentenceMark.Matches(text))
            {
                for (int i = 0; i < words.Count; i++) if (words[i].Index > mark.Index) { if (!words[i].Value.Any(Char.IsUpper)) anchors.Add(i); break; }
            }
            foreach (int start in anchors.Distinct())
            {
                if (start + 18 > words.Count) continue;
                int upper = 0; for (int i = start; i < start + 18; i++) if (words[i].Value.Any(Char.IsUpper)) upper++;
                int from = words[start].Index, to = words[start + 17].Index + words[start + 17].Length;
                if (upper <= 1 && !SentenceMark.IsMatch(text.Substring(from, to - from))) return true;
            }
            return false;
        }
        public static bool ShouldUseRetry(string original, string retry, string prompt)
        {
            int originalScore = Score(original), retryScore = Score(retry); if (retryScore >= originalScore) return false;
            var a = NormalizedWords(original); var b = NormalizedWords(retry); if (a.Count == 0) return false;
            double ratio = (double)b.Count / a.Count; if (ratio < 0.80 || ratio > 1.25) return false;
            if ((double)EditDistance(a, b) / Math.Max(a.Count, b.Count) > 0.25) return false;
            return !ContainsPromptLeak(a, b, NormalizedWords(prompt));
        }
        static List<string> NormalizedWords(string text) => Word.Matches(text).Select(x => x.Value.ToLowerInvariant()).ToList();
        static bool ContainsPromptLeak(IReadOnlyList<string> original, IReadOnlyList<string> retry, IReadOnlyList<string> prompt)
        {
            var originalFour = Ngrams(original, 4); foreach (string phrase in Ngrams(prompt, 4)) if (!originalFour.Contains(phrase) && Ngrams(retry, 4).Contains(phrase)) return true; return false;
        }
        static HashSet<string> Ngrams(IReadOnlyList<string> words, int size)
        {
            var result = new HashSet<string>(StringComparer.Ordinal); for (int i = 0; i + size <= words.Count; i++) result.Add(String.Join("\u001f", words.Skip(i).Take(size))); return result;
        }
        static int EditDistance(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            var previous = Enumerable.Range(0, b.Count + 1).ToArray(); var current = new int[b.Count + 1];
            for (int i = 1; i <= a.Count; i++) { current[0] = i; for (int j = 1; j <= b.Count; j++) current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1)); (previous, current) = (current, previous); }
            return previous[b.Count];
        }
    }
}
