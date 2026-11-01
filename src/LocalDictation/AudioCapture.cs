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
    public sealed class AudioCapture : IDisposable, IMMNotificationClient
    {
        readonly Dispatcher dispatcher; readonly MMDeviceEnumerator devices = new MMDeviceEnumerator();
        WasapiCapture capture; MMDevice endpoint; int generation; bool disposed, active, streamRunning; int captureId;
        readonly CaptureBuffer buffer = new CaptureBuffer(); readonly Stopwatch clock = Stopwatch.StartNew(); long lastPacket = -10000; readonly Func<IEnumerable<MicrophoneEntry>> priority;
        readonly DispatcherTimer watchdog, rescan; WaveFormat format;
        public event Action<float[], string> Interrupted; public event Action<double> Level; public event Action<double[]> Levels; public event Action<string> Error; public event Action LimitReached; public event Action DevicesChanged; public event Action<string> Switched;
        public bool Ready { get { return capture != null; } }
        public string CurrentId { get; private set; } = "";
        public AudioCapture(Func<IEnumerable<MicrophoneEntry>> microphones = null) { dispatcher = Dispatcher.CurrentDispatcher; priority = microphones ?? (() => Array.Empty<MicrophoneEntry>()); devices.RegisterEndpointNotificationCallback(this); watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; watchdog.Tick += (s, e) => { if (!disposed && active && (!streamRunning || clock.ElapsedMilliseconds - lastPacket > 2000)) Recover("Das Mikrofon hat keine Audiodaten geliefert."); }; watchdog.Start(); rescan = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) }; rescan.Tick += (s, e) => { rescan.Stop(); if (disposed) return; DevicesChanged?.Invoke(); Reevaluate(); }; Open(); }
        // Switches to the highest-priority connected microphone while idle; during a Dictation the next capture graph picks it up.
        public void Reevaluate() { if (disposed || active) return; var best = Candidates().FirstOrDefault() ?? ""; if (capture == null || best != CurrentId) { Storage.Log("audio: microphone priority changed"); RefreshGraph(); } }
        List<string> Candidates() { var connected = new List<string>(); string fallback = ""; try { foreach (var x in devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)) { connected.Add(x.ID); x.Dispose(); } if (devices.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)) using (var d = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)) fallback = d.ID; } catch (Exception ex) { Storage.Log("audio devices: " + ex.Message); } return MicrophonePriority.Candidates(priority(), connected, fallback); }
        void Open() { string previous = CurrentId; CurrentId = ""; foreach (var id in Candidates()) if (TryOpen(id)) break; if (capture == null) { Storage.Log("audio: no microphone available"); Error?.Invoke("Es ist kein Mikrofon verfügbar."); } else if (CurrentId != previous) { Storage.Log("audio: microphone " + endpoint.FriendlyName); Switched?.Invoke(CurrentId); } }
        bool TryOpen(string id) { try { endpoint = devices.GetDevice(id); capture = new WasapiCapture(endpoint, true, 20); format = capture.WaveFormat; var meter=new MicrophoneBands(format); int current = ++generation; capture.DataAvailable += (s, e) => { if (current != generation || !active) return; lastPacket = clock.ElapsedMilliseconds; if (buffer.Append(e.Buffer, e.BytesRecorded)) dispatcher.BeginInvoke(new Action(() => LimitReached?.Invoke())); double level=MicrophoneMeter.Measure(e.Buffer,e.BytesRecorded,format);var bands=meter.Measure(e.Buffer,e.BytesRecorded); dispatcher.BeginInvoke(new Action(() => { if(current==generation && active){Level?.Invoke(level);Levels?.Invoke(bands);} })); }; capture.RecordingStopped += (s, e) => { if (current == generation && !disposed && active) { if (e.Exception != null) Storage.Log("audio interrupted: " + e.Exception.Message); dispatcher.BeginInvoke(new Action(() => Recover("Die Verbindung zum Mikrofon wurde unterbrochen."))); } }; lastPacket = -10000; streamRunning=false; CurrentId = id; return true; } catch (Exception ex) { CloseGraph(); Storage.Log("audio: " + ex.Message); return false; } }
        public void Start() { if (!Ready) { Storage.Log("audio start rejected: not ready"); throw new InvalidOperationException("Das Mikrofon ist noch nicht bereit. Versuche es gleich noch einmal."); } if(IsMuted()){Storage.Log("audio start rejected: endpoint muted");throw new InvalidOperationException("Das Mikrofon ist in Windows stummgeschaltet.");} captureId++; buffer.Start(); active = true; lastPacket=clock.ElapsedMilliseconds; try { capture.StartRecording(); streamRunning=true;Storage.Log("audio: capture started"); } catch(Exception ex) { active=false;buffer.Cancel();CloseGraph();Open();Storage.Log("audio start failed: "+ex.Message);throw new InvalidOperationException("Das Mikrofon konnte nicht gestartet werden. Prüfe die Auswahl in den Einstellungen.",ex); } }
        public async Task<float[]> Stop() { int current = captureId; await Task.Delay(60); if (current != captureId) return Array.Empty<float>(); active = false;StopStream();var samples = Convert(buffer.Stop(), format); double peak=0,sum=0;foreach(float sample in samples){double value=Math.Abs(sample);if(value>peak)peak=value;sum+=value*value;}double rms=samples.Length==0?0:Math.Sqrt(sum/samples.Length);Storage.Log("audio: capture stopped with " + samples.Length + " samples; peak=" + peak.ToString("0.000000",System.Globalization.CultureInfo.InvariantCulture) + "; rms=" + rms.ToString("0.000000",System.Globalization.CultureInfo.InvariantCulture));RefreshGraph();return samples; }
        public void Cancel() { captureId++; active = false;StopStream();buffer.Cancel();RefreshGraph(); }
        public static float[] Convert(byte[] bytes, WaveFormat format) { if (bytes.Length == 0) return Array.Empty<float>(); using (var stream = new RawSourceWaveStream(new MemoryStream(bytes), format)) { ISampleProvider s = stream.ToSampleProvider(); if (s.WaveFormat.Channels == 2) s = new StereoToMonoSampleProvider(s); else if (s.WaveFormat.Channels != 1) throw new InvalidDataException("Mehr als zwei Mikrofonkanäle werden nicht unterstützt."); if (s.WaveFormat.SampleRate != 16000) s = new WdlResamplingSampleProvider(s, 16000); var output = new List<float>(); var block = new float[16000]; int n; while ((n = s.Read(block, 0, block.Length)) > 0) for (int i = 0; i < n; i++) output.Add(block[i]); return output.ToArray(); } }
        void Recover(string reason) { if (disposed) return; Storage.Log("audio recover: " + reason); if (active) { captureId++; active = false; StopStream();var samples = Convert(buffer.Stop(), format); Interrupted?.Invoke(samples, "Aufnahme unterbrochen. " + reason); } RefreshGraph(); }
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
