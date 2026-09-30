using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace LocalDictation
{
    public sealed class DictationApplication : Application
    {
        Settings settings; MainWindow main; OverlayWindow overlay; AudioCapture audio; WorkerClient worker; GlobalHotkey hotkey; Forms.NotifyIcon tray; System.Windows.Controls.ContextMenu menu; PowerModeMonitor powerMonitor; DispatcherTimer powerDebounce, workerIdle, retentionCleanup;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource(); readonly SemaphoreSlim workerTransitions = new SemaphoreSlim(1, 1); Mutex instance; EventWaitHandle activation, shutdownSignal; RegisteredWaitHandle activationWait, shutdownWait; bool delivering, recording, busy, quitting, incomplete, fallback, loading, pendingPowerRoute; int generation, id; float[] pending; string root; string activeEngine, activeDevice, lastInsertedText=""; DateTime lastInsertedAt; uint lastInsertedProcess; RetainedAudioReference retainedAudio; Guid activePowerScheme; Guid? powerPersonality;
        [STAThread] public static void Main() { var app = new DictationApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.Run(); }
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e); try
            {
                bool shutdownRequested = Array.IndexOf(e.Args, "--shutdown") >= 0;
                bool created; instance = new Mutex(true, "Local\\LocalDictation.Native.V2", out created); if (!created) { try { using (var signal = EventWaitHandle.OpenExisting(shutdownRequested ? "Local\\LocalDictation.Shutdown.V2" : "Local\\LocalDictation.Activate.V2")) signal.Set(); } catch { } Shutdown(); return; }
                if (shutdownRequested) { Shutdown(); return; }
                activation = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LocalDictation.Activate.V2"); activationWait = ThreadPool.RegisterWaitForSingleObject(activation, (a, b) => Dispatcher.BeginInvoke(new Action(() => { if (main != null) ShowMain(); })), null, -1, false);
                shutdownSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LocalDictation.Shutdown.V2"); shutdownWait = ThreadPool.RegisterWaitForSingleObject(shutdownSignal, (a, b) => Dispatcher.BeginInvoke(new Action(Shutdown)), null, -1, false);
                root = AppDomain.CurrentDomain.BaseDirectory; settings = Storage.LoadSettings(); RetainedAudioStore.Cleanup(Storage.DataDir, DateTime.UtcNow); UsageLedgerStore.SynchronizeHistory(Storage.DataDir, Storage.LoadHistory(Storage.DataDir)); main = new MainWindow(settings, Storage.DataDir, ApplySettings, RestartWorker, Retranscribe); main.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/app.ico")); main.Closing += (s, x) => { if (quitting) return; x.Cancel = true; main.Hide(); if (!settings.CloseExplained) { settings.CloseExplained = true; Save(); tray?.ShowBalloonTip(5000, "Local Dictation läuft weiter", "Zum Beenden das Tray-Menü öffnen und Beenden wählen.", Forms.ToolTipIcon.Info); } };
                overlay = new OverlayWindow(); overlay.ApplyTheme(settings.Theme); overlay.Show(); overlay.Place(settings.Anchor, settings.Monitor); if (!settings.OverlayVisible) overlay.Hide(); overlay.IdleReached += () => { if (!settings.OverlayVisible) overlay.Hide(); }; overlay.MenuRequested += ShowMenu;
                try { powerMonitor = new PowerModeMonitor(overlay); activePowerScheme = powerMonitor.ActiveScheme; powerPersonality = powerMonitor.Personality; powerMonitor.Changed += QueuePowerRoute; } catch (Exception ex) { Storage.Log("power monitor: " + ex.Message); }
                tray = new Forms.NotifyIcon { Visible = true, Text = "Local Dictation", Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath) }; tray.MouseClick += (s, x) => { if (x.Button == Forms.MouseButtons.Right) ShowMenu(); else if (x.Button == Forms.MouseButtons.Left) { if(main.IsVisible && main.WindowState != WindowState.Minimized)main.Hide();else ShowMain(); } };
                bool hotkeyProbe = false; hotkey = new GlobalHotkey(settings.HoldMs, Array.IndexOf(e.Args, "--allow-injected-hotkey") >= 0) { Binding = settings.Hotkey }; hotkey.StartRequested += () => { hotkeyProbe = !busy; StartDictation(); }; hotkey.ProbeCancelled += () => { if (hotkeyProbe && recording) CancelDictation(); hotkeyProbe = false; }; hotkey.StopRequested += StopDictation; hotkey.CancelRequested += CancelDictation; hotkey.CorrectionRequested += BeginCorrection;
                audio = new AudioCapture(settings.Microphone); audio.Levels += x => overlay.SetLevels(x); audio.Error += CaptureError; audio.LimitReached += () => {main.SetStatus("Die Aufnahme ist sehr lang und wird jetzt verarbeitet.");StopDictation();}; audio.Interrupted += Interrupted;
                main.MicrophoneChanged += x => audio.Select(x); main.RetryRequested += () => { if (!busy) RestartWorker(settings.Engine); };
                retentionCleanup = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) }; retentionCleanup.Tick += (s, x) => RetainedAudioStore.Cleanup(Storage.DataDir, DateTime.UtcNow); retentionCleanup.Start();
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemChanged;
                if (settings.AutomaticPowerModel) ApplyPowerRoute(false); if (!settings.OverlayVisible && Array.IndexOf(e.Args, "--autostart") < 0) main.Show(); RestartWorker(settings.Engine);
            }
            catch (Exception ex) { Storage.Log("startup: " + ex); MessageBox.Show("Local Dictation konnte nicht starten.\n\n" + ex.Message, "Local Dictation"); Shutdown(1); }
        }
        void SystemChanged(object s, Microsoft.Win32.UserPreferenceChangedEventArgs e) { Dispatcher.BeginInvoke(new Action(() => { if (settings.Theme == "system") { main.RefreshTheme(); overlay.ApplyTheme(settings.Theme); } overlay.Place(settings.Anchor, settings.Monitor); })); }
        void Save() { if (settings != null) Storage.SaveSettings(settings); }
        void ApplySettings(string mode) { Save(); if (hotkey != null) { hotkey.Binding = settings.Hotkey; hotkey.HoldMilliseconds = settings.HoldMs; } if (overlay != null) { overlay.ApplyTheme(mode); overlay.Place(settings.Anchor, settings.Monitor); if (!busy && overlay.State == AppState.Idle) { if (settings.OverlayVisible) overlay.Show(); else overlay.Hide(); } } if (settings.AutomaticPowerModel) ApplyPowerRoute(true); else { pendingPowerRoute = false; powerDebounce?.Stop(); } ApplyWhisperIdlePolicy(); }
        void QueuePowerRoute(Guid scheme, Guid? personality)
        {
            activePowerScheme = scheme; powerPersonality = personality;
            if (!settings.AutomaticPowerModel) return;
            if (powerDebounce == null) { powerDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) }; powerDebounce.Tick += (s, e) => { powerDebounce.Stop(); if (busy) pendingPowerRoute = true; else ApplyPowerRoute(true); }; }
            powerDebounce.Stop(); powerDebounce.Start();
        }
        void ApplyPowerRoute(bool restart)
        {
            try {
                if (!settings.AutomaticPowerModel) return;
                if (activePowerScheme == Guid.Empty) activePowerScheme = PowerModeMonitor.ReadActiveScheme();
                var route = PowerModelRouting.Resolve(activePowerScheme, powerPersonality);
                if (route == null) { Storage.Log("power route unchanged: scheme=" + activePowerScheme + " personality=" + (powerPersonality?.ToString() ?? "unknown")); return; }
                pendingPowerRoute = false;
                bool changed = settings.Engine != route.Engine || settings.Device != route.Device;
                settings.Engine = route.Engine; settings.Device = route.Device;
                if (changed) { Save(); main.RefreshModelSettings(); Storage.Log("power route: " + route.Reason + " -> " + route.Engine + "/" + route.Device); }
                if (restart && !busy && (worker == null && !loading || activeEngine != route.Engine || activeDevice != route.Device)) RestartWorker(route.Engine);
            } catch (Exception ex) { Storage.Log("power route error: " + ex); main.SetStatus("Der automatische Modellwechsel hat nicht geklappt. Das bisherige Modell bleibt aktiv."); }
        }
        async void RestartWorker(string engine) { if (busy) return; fallback = false; await StartWorker(engine, settings.Device); }
        async Task StartWorker(string engine, string device)
        {
            workerIdle?.Stop(); ModelSelection.Normalize(ref engine, ref device); loading = true; int current = ++generation; bool entered = false; try
            {
                await workerTransitions.WaitAsync(lifetime.Token); entered = true; if (quitting || current != generation) return;
                worker?.Dispose(); worker = null; activeEngine = engine; activeDevice = device;
                main.SetStatus(SidebarStatus.Preparing(engine)); await Models.Ensure(engine, x => Dispatcher.Invoke(() => main.SetStatus(x)), lifetime.Token); if (quitting || current != generation) return;
                var client = new WorkerClient(); worker = client; client.Event += line => Dispatcher.BeginInvoke(new Action(() => { if (current == generation && !quitting) Handle(line); })); client.Start(root, engine, device);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (current == generation) { main.SetStatus("Das Modell konnte nicht gestartet werden. Versuche es in den Einstellungen erneut."); Storage.Log("model: " + ex.Message); if (busy) Finish(AppState.Error); } }
            finally { if (entered) workerTransitions.Release(); if (current == generation) loading = false; }
        }
        void ApplyWhisperIdlePolicy()
        {
            workerIdle?.Stop();
            if (quitting || busy || loading || settings.Engine != "whisper") return;
            if (settings.WhisperIdleMinutes == 0) { if (worker == null) RestartWorker("whisper"); return; }
            if (worker == null || activeEngine != "whisper") return;
            if (workerIdle == null) { workerIdle = new DispatcherTimer(); workerIdle.Tick += async (s, e) => await UnloadWhisperIfIdle(); }
            workerIdle.Interval = TimeSpan.FromMinutes(settings.WhisperIdleMinutes); workerIdle.Start();
        }
        async Task UnloadWhisperIfIdle()
        {
            workerIdle?.Stop(); if (quitting || busy || loading || worker == null || activeEngine != "whisper" || settings.WhisperIdleMinutes == 0) return;
            bool entered = false; WorkerClient retired = null; int retiredGeneration = generation;
            try {
                await workerTransitions.WaitAsync(lifetime.Token); entered = true;
                if (quitting || busy || loading || worker == null || activeEngine != "whisper" || generation != retiredGeneration) return;
                retired = worker; worker = null; generation++; Storage.Log("worker idle unload: whisper after " + settings.WhisperIdleMinutes + " minutes");
                await Task.Run(() => retired.Dispose());
                if (!quitting && !busy && generation == retiredGeneration + 1) main.SetStatus("Bereit · Whisper wird bei Bedarf geladen");
            } catch (OperationCanceledException) { }
            catch (Exception ex) { Storage.Log("worker idle unload: " + ex.Message); }
            finally { if (entered) workerTransitions.Release(); }
        }
        void ShowMain() { main.Show(); if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal; main.Activate(); }
        void StartDictation() { if (busy) { Storage.Log("dictation start ignored: busy"); return; } try { Storage.Log("dictation: start requested"); workerIdle?.Stop(); audio.Start(); id++; recording = busy = true; incomplete = false; pending = null; retainedAudio = null; main.Busy = hotkey.Busy = true; overlay.Show(); overlay.SetState(AppState.Recording, 0); main.SetStatus(worker == null || !worker.Ready ? "Aufnahme läuft · Modell wird parallel geladen" : "Aufnahme läuft · Escape verwirft das Diktat"); if (worker == null && !loading) _ = StartWorker(settings.Engine, settings.Device); } catch (Exception ex) { Storage.Log("dictation start failed: " + ex.Message); hotkey.Reset(); main.SetStatus(ex is InvalidOperationException ? ex.Message : "Die Aufnahme konnte nicht gestartet werden. Versuche es erneut."); overlay.Show(); overlay.SetState(AppState.Error, 0); } }
        async void StopDictation() { if (!recording) return; recording = false; int current = id; try { var data = await audio.Stop(); if (current != id || !busy || pending != null) return; pending = data; if (data.Length < 1600) { Finish(AppState.Idle); return; } try{retainedAudio = await Task.Run(() => RetainedAudioStore.Save(Storage.DataDir, data, DateTime.UtcNow));}catch(Exception ex){Storage.Log("audio retention: "+ex.Message);retainedAudio=null;} await Submit(current); } catch (Exception ex) { Storage.Log("recording stop: " + ex); main.SetStatus("Die Aufnahme konnte nicht verarbeitet werden. Versuche es erneut."); Finish(AppState.Error); } }
        async Task Submit(int current) { if (current != id || pending == null) return; bool ready=worker != null && worker.Ready;Storage.Log("dictation: submit id="+current+" samples="+pending.Length+" workerReady="+ready);overlay.SetState(ready ? AppState.Transcribing : AppState.Waiting, 0); if (ready) await worker.Transcribe(pending, current); }
        async void Interrupted(float[] data, string reason) { if (!busy || pending != null) return; recording = false; incomplete = true; pending = data; main.SetStatus(reason); if (data.Length < 1600) { Finish(AppState.Error); return; } try { retainedAudio = await Task.Run(() => RetainedAudioStore.Save(Storage.DataDir, data, DateTime.UtcNow)); } catch (Exception ex) { Storage.Log("audio retention: " + ex.Message); } await Submit(id); }
        void CaptureError(string message) { main.SetStatus(message); Storage.Log("capture: " + message); }
        void CancelDictation() { if (!busy || delivering) return; bool processing = !recording && pending != null; id++; audio.Cancel(); pending = null; Finish(AppState.Idle); if (processing) { generation++; worker?.Dispose(); worker = null; _ = StartWorker(settings.Engine, fallback ? "cpu" : settings.Device); } }
        void Finish(AppState state) { recording = busy = false; pending = null; main.Busy = false; hotkey.Busy = false; hotkey.Reset(); if(overlay.State!=state)overlay.SetState(state, 0); if (pendingPowerRoute && settings.AutomaticPowerModel) Dispatcher.BeginInvoke(new Action(() => ApplyPowerRoute(true))); ApplyWhisperIdlePolicy(); }
        async void Handle(string json)
        {
            try
            {
                string ev = JsonLite.Value(json, "event"); if (ev == "model") { string modelState=JsonLite.Value(json,"state");Storage.Log("worker model: "+modelState+(modelState=="ready"?" load_ms="+JsonLite.IntValue(json,"load_ms")+" backend="+JsonLite.Value(json,"backend"):""));if (modelState == "ready") { main.SetStatus(SidebarStatus.Ready(activeEngine)); if (busy && !recording && pending != null) await Submit(id); else if (!busy) ApplyWhisperIdlePolicy(); } return; }
                if (ev == "state") { if (busy && JsonLite.IntValue(json, "id") == id) overlay.SetState(AppState.Transcribing, 0); return; }
                if (ev == "transcript")
                {
                    if (!busy || JsonLite.IntValue(json, "id") != id) return; string raw = JsonLite.Value(json, "text");Storage.Log("worker transcript: id="+id+" characters="+raw.Length);if (String.IsNullOrWhiteSpace(raw)) { main.SetStatus("Keine Sprache erkannt."); Finish(AppState.Idle); return; }
                    bool review = JsonLite.BoolValue(json, "review_recommended"); double confidence = JsonLite.DoubleValue(json, "confidence"); int processingMs = JsonLite.IntValue(json, "inference_ms");
                    string text = raw, error = ""; bool ok = false; uint targetProcess = 0;
                    try { if(settings.VoiceCommands)text=VoiceCommands.Apply(text);text = Replacements.Apply(text, settings.Rules); if (!incomplete) { targetProcess=SelectionCapture.ForegroundProcessId();delivering=true; hotkey.Busy=false; try { var delivery=await ClipboardInserter.DeliverAsync(text,settings.Output,()=>overlay.SetState(review?AppState.Review:AppState.Success,0));ok=delivery.Success;error=delivery.Error;Storage.Log("output: "+delivery.Method+" confidence="+confidence.ToString("0.000")+" review="+review); } finally { delivering=false; } } } catch (Exception ex) { error = ex.Message; text = raw; }
                    var entry=new HistoryEntry { Id=Guid.NewGuid().ToString("N"), Time = DateTime.Now, DurationMs = JsonLite.IntValue(json, "duration_ms"), ProcessingMs=processingMs, Engine = activeEngine, RawText=raw, Text = text, Status = incomplete ? "incomplete" : ok ? "complete" : "not-inserted", Confidence=confidence, ReviewRecommended=review, AudioFile=retainedAudio?.FileName??"", AudioExpiresAtUtc=retainedAudio?.ExpiresAtUtc };
                    entry.Variants.Add(new TranscriptVariant{Engine=activeEngine,RawText=raw,Text=text,CreatedAtUtc=DateTime.UtcNow,Confidence=confidence,ReviewRecommended=review});main.AddHistory(entry);UsageLedgerStore.Record(Storage.DataDir,entry);
                    if(ok&&settings.Output!=OutputMode.Clipboard){lastInsertedText=text;lastInsertedAt=DateTime.Now;lastInsertedProcess=targetProcess;}
                    main.SetStatus(incomplete ? "Die Aufnahme wurde unterbrochen. Der Text liegt im Verlauf." : ok ? review?"Text eingefügt · bitte kurz prüfen":SidebarStatus.Completed(settings.Output) : error); Finish(ok ? review?AppState.Review:AppState.Success : AppState.Error); if (!ok) { main.ShowHistory(); ShowMain(); }
                    return;
                }
                if (ev == "error" || ev == "crash") { var message = JsonLite.Value(json, "message"); Storage.Log("worker: " + message); if (activeEngine == "whisper" && !fallback) { fallback = true; settings.Engine = "parakeet"; settings.Device = "cpu"; try { Save(); } catch (Exception ex) { Storage.Log("fallback save: " + ex.Message); } main.RefreshModelSettings(); main.SetStatus("Whisper ist gerade nicht verfügbar. Parakeet übernimmt dieses Diktat."); if (busy && !recording) overlay.SetState(AppState.Waiting, 0); await StartWorker("parakeet", "cpu"); } else { main.SetStatus("Das Modell ist gerade nicht verfügbar. Starte es in den Einstellungen neu."); if (busy) Finish(AppState.Error); } }
            }
            catch (Exception ex) { Storage.Log("app event: " + ex); main.SetStatus("Das Diktat konnte nicht abgeschlossen werden. Versuche es erneut."); if (busy) Finish(AppState.Error); }
        }
        async void BeginCorrection()
        {
            if(recording)CancelDictation();
            if(String.IsNullOrWhiteSpace(lastInsertedText)||DateTime.Now-lastInsertedAt>TimeSpan.FromMinutes(30)){main.SetStatus("Es gibt kein aktuelles eingefügtes Diktat zum Vergleichen.");return;}
            var selected=await SelectionCapture.ReadFocusedSelectionAsync();
            if(!selected.Success){main.SetStatus("Die markierte Auswahl konnte in dieser App nicht gelesen werden.");return;}
            if(selected.ProcessId!=lastInsertedProcess){main.SetStatus("Markiere den korrigierten Text in derselben App wie das letzte Diktat.");return;}
            var analysis=CorrectionLearning.Analyze(lastInsertedText,selected.Text,settings.Rules);if(!analysis.Accepted){main.SetStatus(analysis.Message);return;}
            main.ShowCorrection(lastInsertedText,selected.Text,analysis);ShowMain();
        }
        async void Retranscribe(HistoryEntry requested,string engine)
        {
            if(busy||requested==null||String.IsNullOrWhiteSpace(requested.AudioFile))return;
            string path;try{path=RetainedAudioStore.ResolvePath(Storage.DataDir,requested.AudioFile);if(requested.AudioExpiresAtUtc<=DateTime.UtcNow||!File.Exists(path)){main.SetStatus("Die Aufnahme ist nicht mehr verfügbar.");main.ShowHistory();return;}}
            catch{main.SetStatus("Die Aufnahme ist nicht mehr verfügbar.");return;}
            busy=true;main.Busy=hotkey.Busy=true;overlay.Show();overlay.SetState(AppState.Waiting,0);main.SetStatus("Wird mit "+SidebarStatus.ModelName(engine)+" erneut transkribiert …");
            try
            {
                workerIdle?.Stop();generation++;worker?.Dispose();worker=null;await Models.Ensure(engine,x=>Dispatcher.Invoke(()=>main.SetStatus(x)),lifetime.Token);string raw=await OneShotTranscriber.RunAsync(root,engine,path);string text=settings.VoiceCommands?VoiceCommands.Apply(raw):raw;text=Replacements.Apply(text,settings.Rules);
                var entries=Storage.LoadHistory(Storage.DataDir);var entry=entries.FirstOrDefault(x=>x.Id==requested.Id);if(entry==null)throw new InvalidOperationException("Der Verlaufseintrag wurde nicht gefunden.");entry.Engine=engine;entry.RawText=raw;entry.Text=text;entry.Confidence=1;entry.ReviewRecommended=false;entry.Variants.Add(new TranscriptVariant{Engine=engine,RawText=raw,Text=text,CreatedAtUtc=DateTime.UtcNow});Storage.Write(Path.Combine(Storage.DataDir,"history.json"),entries);UsageLedgerStore.RecordRetranscription(Storage.DataDir,DateTime.Now);main.SetStatus("Erneute Transkription ist fertig.");overlay.SetState(AppState.Success,0);main.ShowHistory();
            }
            catch(Exception ex){Storage.Log("retranscription: "+ex.Message);main.SetStatus("Die erneute Transkription hat nicht geklappt.");overlay.SetState(AppState.Error,0);}
            finally{busy=false;main.Busy=hotkey.Busy=false;_ = StartWorker(settings.Engine,settings.Device);}
        }
        void ShowMenu(){if(menu!=null)menu.IsOpen=false;menu=ApplicationMenus.Create(settings,busy,()=>{main.ShowHistory();ShowMain();},()=>{main.ShowDictionary();ShowMain();},()=>{settings.OverlayVisible=!settings.OverlayVisible;ApplySettings(settings.Theme);},()=>{main.ShowSettings();ShowMain();},()=>Shutdown());menu.IsOpen=true;}        protected override void OnExit(ExitEventArgs e) { quitting = true; ClipboardTransfer.RestorePending(); lifetime.Cancel(); Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemChanged; try { Save(); } catch (Exception ex) { Storage.Log("save: " + ex.Message); } powerDebounce?.Stop(); workerIdle?.Stop(); retentionCleanup?.Stop(); powerMonitor?.Dispose(); audio?.Dispose(); hotkey?.Dispose(); worker?.Dispose(); if(menu!=null)menu.IsOpen=false; tray?.Dispose(); activationWait?.Unregister(null); shutdownWait?.Unregister(null); activation?.Dispose(); shutdownSignal?.Dispose(); instance?.Dispose(); base.OnExit(e); }
    }
}
