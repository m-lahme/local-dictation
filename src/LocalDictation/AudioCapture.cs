using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Windows.Threading;

namespace LocalDictation
{
    public sealed class CaptureBuffer
    {
        MemoryStream data = new MemoryStream(); readonly object gate = new object(); bool active;
        public int MaxBytes = Array.MaxLength;
        public void Start() { lock (gate) { data.Dispose();data=new MemoryStream(); active = true; } }
        public bool Append(byte[] bytes, int count) { lock (gate) { if (!active) return false; int n = Math.Min(count, MaxBytes - (int)data.Length); data.Write(bytes,0,n); return data.Length == MaxBytes; } }
        public byte[] Stop() { lock (gate) { active = false; var result = data.ToArray(); data.Dispose();data=new MemoryStream(); return result; } }
        public void Cancel() { lock (gate) { active = false; data.Dispose();data=new MemoryStream(); } }
    }
    // Averages any number of interleaved channels into mono, as StereoToMonoSampleProvider does for two.
    sealed class ChannelDownmix : ISampleProvider
    {
        readonly ISampleProvider source; readonly int channels; float[] frames = Array.Empty<float>();
        public WaveFormat WaveFormat { get; }
        public ChannelDownmix(ISampleProvider source) { this.source = source; channels = source.WaveFormat.Channels; WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1); }
        public int Read(float[] buffer, int offset, int count) { int needed = count * channels; if (frames.Length < needed) frames = new float[needed]; int read = source.Read(frames, 0, needed) / channels; for (int i = 0; i < read; i++) { float sum = 0; for (int c = 0; c < channels; c++) sum += frames[i * channels + c]; buffer[offset + i] = sum / channels; } return read; }
    }
    public sealed class AudioCapture : IDisposable, IMMNotificationClient
    {
        const string NotReady = "Das Mikrofon ist noch nicht bereit. Versuche es gleich noch einmal.", NoMicrophone = "Es wurde kein Mikrofon gefunden. Schließe ein Mikrofon an oder aktiviere es in den Windows-Soundeinstellungen.";
        public const string SilenceHint = "Das Mikrofon liefert nur Stille. Prüfe die Stummschaltung, die Mikrofonauswahl und in den Windows-Einstellungen unter Datenschutz und Sicherheit > Mikrofon den Zugriff für Desktop-Apps.";
        readonly Dispatcher dispatcher; readonly MMDeviceEnumerator devices = new MMDeviceEnumerator();
        WasapiCapture capture; MMDevice endpoint; int generation; bool disposed, active, streamRunning; int captureId;
        readonly CaptureBuffer buffer = new CaptureBuffer(); readonly Stopwatch clock = Stopwatch.StartNew(); long lastPacket = -10000; readonly Func<IEnumerable<MicrophoneEntry>> priority;
        readonly DispatcherTimer watchdog, rescan; WaveFormat format;
        public event Action<float[], string> Interrupted; public event Action<double> Level; public event Action<double[]> Levels; public event Action<string> Error; public event Action LimitReached; public event Action DevicesChanged; public event Action<string> Switched;
        public bool Ready { get { return capture != null; } }
        public string CurrentId { get; private set; } = "";
        // Why no capture graph is open; shown when a Dictation cannot start.
        public string Unavailable { get; private set; } = NotReady;
        public AudioCapture(Func<IEnumerable<MicrophoneEntry>> microphones = null) { dispatcher = Dispatcher.CurrentDispatcher; priority = microphones ?? (() => Array.Empty<MicrophoneEntry>()); devices.RegisterEndpointNotificationCallback(this); watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; watchdog.Tick += (s, e) => { if (!disposed && active && (!streamRunning || clock.ElapsedMilliseconds - lastPacket > 2000)) Recover("Das Mikrofon hat keine Audiodaten geliefert."); }; watchdog.Start(); rescan = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) }; rescan.Tick += (s, e) => { rescan.Stop(); if (disposed) return; DevicesChanged?.Invoke(); Reevaluate(); }; Open(); }
        // Switches to the highest-priority connected microphone while idle; during a Dictation the next capture graph picks it up.
        public void Reevaluate() { if (disposed || active) return; var best = Candidates().FirstOrDefault() ?? ""; if (capture == null || best != CurrentId) { Storage.Log("audio: microphone priority changed"); RefreshGraph(); } }
        List<string> Candidates() { var connected = new List<string>(); string fallback = ""; try { foreach (var x in devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)) { connected.Add(x.ID); x.Dispose(); } if (devices.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)) using (var d = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)) fallback = d.ID; } catch (Exception ex) { Storage.Log("audio devices: " + ex.Message); } return MicrophonePriority.Candidates(priority(), connected, fallback); }
        void Open() { string previous = CurrentId; CurrentId = ""; var ids = Candidates(); int failure = 0; foreach (var id in ids) if (TryOpen(id, ref failure)) break; if (capture == null) { Unavailable = ids.Count == 0 ? NoMicrophone : Explain(failure) ?? NotReady; Storage.Log("audio: no microphone available"); Error?.Invoke(Unavailable); } else if (CurrentId != previous) { Storage.Log("audio: microphone " + endpoint.FriendlyName); Switched?.Invoke(CurrentId); } }
        bool TryOpen(string id, ref int failure) { try { endpoint = devices.GetDevice(id); capture = new WasapiCapture(endpoint, true, 20); format = capture.WaveFormat; var meter=new MicrophoneBands(format); int current = ++generation; capture.DataAvailable += (s, e) => { if (current != generation || !active) return; lastPacket = clock.ElapsedMilliseconds; if (buffer.Append(e.Buffer, e.BytesRecorded)) dispatcher.BeginInvoke(new Action(() => LimitReached?.Invoke())); double level=MicrophoneMeter.Measure(e.Buffer,e.BytesRecorded,format);var bands=meter.Measure(e.Buffer,e.BytesRecorded); dispatcher.BeginInvoke(new Action(() => { if(current==generation && active){Level?.Invoke(level);Levels?.Invoke(bands);} })); }; capture.RecordingStopped += (s, e) => { if (current == generation && !disposed && active) { if (e.Exception != null) Storage.Log("audio interrupted: " + e.Exception.Message); dispatcher.BeginInvoke(new Action(() => Recover("Die Verbindung zum Mikrofon wurde unterbrochen."))); } }; lastPacket = -10000; streamRunning=false; CurrentId = id; return true; } catch (Exception ex) { CloseGraph(); Storage.Log("audio: " + ex.Message + " (0x" + ex.HResult.ToString("X8") + ")"); if (Explain(failure) == null) failure = ex.HResult; return false; } }
        // A failed start (e.g. an endpoint invalidated by sleep or a driver restart) gets one retry on a freshly opened graph.
        public void Start() { if (!Ready) Open(); if (!Ready) { Storage.Log("audio start rejected: not ready"); throw new InvalidOperationException(Unavailable); } if(IsMuted()){Storage.Log("audio start rejected: endpoint muted");throw new InvalidOperationException("Das Mikrofon ist in Windows stummgeschaltet.");} captureId++; buffer.Start(); active = true; var error = StartStream(); if (error != null) { CloseGraph(); Open(); if (Ready) { Storage.Log("audio: retrying start with a fresh capture graph"); error = StartStream(); if (error != null) RefreshGraph(); } } if (error != null) { active=false;buffer.Cancel();throw new InvalidOperationException(Ready ? Explain(error.HResult) ?? "Das Mikrofon konnte nicht gestartet werden. Prüfe die Auswahl in den Einstellungen." : Unavailable,error); } }
        Exception StartStream() { lastPacket=clock.ElapsedMilliseconds; try { capture.StartRecording(); streamRunning=true;Storage.Log("audio: capture started"); return null; } catch(Exception ex) { Storage.Log("audio start failed: "+ex.Message+" (0x"+ex.HResult.ToString("X8")+")"); return ex; } }
        public static string Explain(int hresult) { switch ((uint)hresult) { case 0x80070005: return "Windows blockiert den Zugriff auf das Mikrofon. Erlaube ihn in den Windows-Einstellungen unter Datenschutz und Sicherheit > Mikrofon für Desktop-Apps."; case 0x8889000A: return "Ein anderes Programm verwendet das Mikrofon exklusiv. Schließe es oder wähle in den Einstellungen ein anderes Mikrofon."; default: return null; } }
        public static bool IsDigitalSilence(float[] samples) { return samples.Length >= 16000 && Array.TrueForAll(samples, x => x == 0); }
        public async Task<float[]> Stop() { int current = captureId; await Task.Delay(60); if (current != captureId) return Array.Empty<float>(); active = false;StopStream();var samples = Convert(buffer.Stop(), format); double peak=0,sum=0;foreach(float sample in samples){double value=Math.Abs(sample);if(value>peak)peak=value;sum+=value*value;}double rms=samples.Length==0?0:Math.Sqrt(sum/samples.Length);Storage.Log("audio: capture stopped with " + samples.Length + " samples; peak=" + peak.ToString("0.000000",System.Globalization.CultureInfo.InvariantCulture) + "; rms=" + rms.ToString("0.000000",System.Globalization.CultureInfo.InvariantCulture));RefreshGraph();return samples; }
        public void Cancel() { captureId++; active = false;StopStream();buffer.Cancel();RefreshGraph(); }
        public static float[] Convert(byte[] bytes, WaveFormat format) { if (bytes.Length == 0) return Array.Empty<float>(); if (format is WaveFormatExtensible extensible) format = extensible.AsStandardWaveFormat(); using (var stream = new RawSourceWaveStream(new MemoryStream(bytes), format)) { ISampleProvider s = stream.ToSampleProvider(); if (s.WaveFormat.Channels == 2) s = new StereoToMonoSampleProvider(s); else if (s.WaveFormat.Channels > 2) s = new ChannelDownmix(s); if (s.WaveFormat.SampleRate != 16000) s = new WdlResamplingSampleProvider(s, 16000); var output = new List<float>(); var block = new float[16000]; int n; while ((n = s.Read(block, 0, block.Length)) > 0) for (int i = 0; i < n; i++) output.Add(block[i]); return output.ToArray(); } }
        void Recover(string reason) { if (disposed) return; Storage.Log("audio recover: " + reason); if (active) { captureId++; active = false; StopStream();float[] samples; try { samples = Convert(buffer.Stop(), format); } catch (Exception ex) { Storage.Log("audio recover conversion: " + ex.Message); samples = Array.Empty<float>(); } Interrupted?.Invoke(samples, "Aufnahme unterbrochen. " + reason); } RefreshGraph(); }
        void StopStream(){if(capture!=null&&streamRunning){try{capture.StopRecording();}catch(Exception ex){Storage.Log("audio stop: "+ex.Message);}streamRunning=false;}}
        bool IsMuted(){try{return endpoint != null && endpoint.AudioEndpointVolume.Mute;}catch(Exception ex){Storage.Log("audio mute state unavailable: "+ex.Message);return false;}}
        void RefreshGraph(){CloseGraph();if(!disposed)Open();}
        void CloseGraph() { generation++; if (capture != null) { try { if(streamRunning)capture.StopRecording(); capture.Dispose(); } catch { } capture = null;streamRunning=false; } endpoint?.Dispose(); endpoint = null; }
        void Changed() { dispatcher.BeginInvoke(new Action(() => Recover("Das Audiogerät wurde geändert."))); Rescan(); }
        // Device notifications arrive in bursts on a COM thread; the list is re-read once they settle.
        void Rescan() { dispatcher.BeginInvoke(new Action(() => { if (disposed) return; rescan.Stop(); rescan.Start(); })); }
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id) { if (flow == DataFlow.Capture && role == Role.Multimedia) Rescan(); }
        public void OnDeviceStateChanged(string id, DeviceState state) { if (id == CurrentId && state != DeviceState.Active) Changed(); else Rescan(); }
        public void OnDeviceAdded(string id) { Rescan(); }
        public void OnDeviceRemoved(string id) { if (id == CurrentId) Changed(); else Rescan(); }
        public void OnPropertyValueChanged(string id, PropertyKey key) { }
        public void Dispose() { disposed = true; watchdog.Stop(); rescan.Stop(); Cancel(); devices.UnregisterEndpointNotificationCallback(this); CloseGraph(); devices.Dispose(); }
    }
}
