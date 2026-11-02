using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace LocalDictation
{
    public sealed class WorkerClient : IDisposable
    {
        Process process; bool disposed; readonly object writeGate = new object();
        public bool Ready { get; private set; }
        public event Action<string> Event;
        public void Start(string root, string engine, string device)
        {
            string exe = Path.Combine(root, "native", "LocalDictation.Worker.exe");
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(exe) }; info.Environment["LOCAL_DICTATION_LANGUAGE"] = L.Language;
            info.ArgumentList.Add("--engine"); info.ArgumentList.Add(engine); info.ArgumentList.Add("--device"); info.ArgumentList.Add(device); info.ArgumentList.Add("--models"); info.ArgumentList.Add(Models.Root);
            process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += (s, e) => { if (e.Data == null || disposed) return; try { if (JsonLite.Value(e.Data, "event") == "model" && JsonLite.Value(e.Data, "state") == "ready") Ready = true; Event?.Invoke(e.Data); } catch (Exception ex) { Storage.Log("worker protocol: " + ex.Message); } };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) Storage.Log("native: " + e.Data); };
            process.Exited += (s, e) => { if (!disposed) Event?.Invoke(JsonSerializer.Serialize(new { @event = "crash", message = L.T("The model quit unexpectedly.") })); };
            process.Start(); process.StandardInput.AutoFlush = true; process.BeginOutputReadLine(); process.BeginErrorReadLine();
        }
        public Task Transcribe(float[] audio, int id) { return Task.Run(() => { byte[] bytes = new byte[audio.Length * 4]; Buffer.BlockCopy(audio, 0, bytes, 0, bytes.Length); lock (writeGate) { if (disposed) throw new ObjectDisposedException("Worker"); process.StandardInput.WriteLine(JsonSerializer.Serialize(new { command = "transcribe", id, audio = Convert.ToBase64String(bytes) })); } }); }
        public void Dispose() { disposed = true; Ready = false; try { if (process != null && !process.HasExited) { process.Kill(true); if (!process.WaitForExit(5000)) Storage.Log("worker stop: process did not exit within five seconds"); } } catch (Exception ex) { Storage.Log("worker stop: " + ex.Message); } process?.Dispose(); }
    }
    // Asks a short-lived worker whether Whisper's dedicated Vulkan GPU and VC++ runtime are usable; returns null if they are, otherwise the reason.
    public static class GpuProbe
    {
        public static async Task<string> RunAsync(string root)
        {
            string exe = Path.Combine(root, "native", "LocalDictation.Worker.exe");
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(exe) }; info.Environment["LOCAL_DICTATION_LANGUAGE"] = L.Language;
            info.ArgumentList.Add("--probe");
            using var process = new Process { StartInfo = info }; using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
            process.Start(); _ = process.StandardError.ReadToEndAsync();
            try
            {
                string output = await process.StandardOutput.ReadToEndAsync(timeout.Token); await process.WaitForExitAsync(timeout.Token);
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries)) { try { if (JsonLite.Value(line, "event") != "probe") continue; return JsonLite.Value(line, "state") == "available" ? null : JsonLite.Value(line, "message"); } catch (JsonException) { } }
                return "The GPU probe returned no result.";
            }
            catch (OperationCanceledException) { try { process.Kill(true); } catch { } return "The GPU probe did not respond."; }
        }
    }
    public static class OneShotTranscriber
    {
        public static async Task<string> RunAsync(string root, string engine, string audioPath)
        {
            string exe = Path.Combine(root, "native", "LocalDictation.Worker.exe");
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(exe) }; info.Environment["LOCAL_DICTATION_LANGUAGE"] = L.Language;
            foreach (string value in new[] { "--engine", engine, "--device", engine == "whisper" ? "gpu" : "cpu", "--models", Models.Root, "--file", audioPath }) info.ArgumentList.Add(value);
            using var process = new Process { StartInfo = info };
            process.Start();
            string transcript = "", error = "";
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            while (await process.StandardOutput.ReadLineAsync() is string line)
            {
                string ev = JsonLite.Value(line, "event");
                if (ev == "transcript") transcript = JsonLite.Value(line, "text");
                else if (ev == "error") error = JsonLite.Value(line, "message");
            }
            await process.WaitForExitAsync(); error += await stderr;
            if (process.ExitCode != 0 || String.IsNullOrWhiteSpace(transcript)) throw new InvalidOperationException(String.IsNullOrWhiteSpace(error) ? "Die erneute Transkription ist fehlgeschlagen." : error.Trim());
            return transcript.Trim();
        }
    }
    public sealed class GlobalHotkey : IDisposable
    {
        const int WH_KEYBOARD_LL = 13; delegate IntPtr HookProc(int n, IntPtr w, IntPtr l);
        readonly HookProc proc; IntPtr hook; readonly Dispatcher dispatcher; readonly HotkeyInput input = new(); readonly bool allowInjected; readonly System.Threading.Thread thread; uint threadId;
        bool escape; volatile bool busy; public bool Busy { get => busy; set => busy = value; }
        public string Binding { get { lock (input) return input.SelectedBinding; } set { lock (input) input.SelectedBinding = value; } }
        public int HoldMilliseconds { set { lock (input) input.HoldMilliseconds = value; } }
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
        [DllImport("user32.dll")] static extern int GetMessage(out MSG message, IntPtr window, uint min, uint max);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint message, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        public event Action StartRequested, StopRequested, CancelRequested, CorrectionRequested, ProbeCancelled;
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, ClipboardInserter.INPUT[] inputs, int size);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc p, IntPtr mod, uint tid);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string n);
        // The hook runs on its own thread: Windows skips a low-level hook whose thread is busy (audio start, device changes,
        // rendering), and a skipped key release used to leave that key "held" so the shortcut stopped working.
        public GlobalHotkey(int holdMs, bool allowInjectedForTesting=false)
        {
            dispatcher = Dispatcher.CurrentDispatcher; input.HoldMilliseconds = holdMs; allowInjected=allowInjectedForTesting;proc = Handle; int error = 0;
            using (var ready = new System.Threading.ManualResetEventSlim())
            {
                thread = new System.Threading.Thread(() => { threadId = GetCurrentThreadId(); hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0); error = Marshal.GetLastWin32Error(); ready.Set(); if (hook == IntPtr.Zero) return; while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { } UnhookWindowsHookEx(hook); }) { IsBackground = true, Name = "Hotkey hook", Priority = System.Threading.ThreadPriority.Highest };
                thread.Start(); ready.Wait();
            }
            if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(error);
            Microsoft.Win32.SystemEvents.SessionSwitch += SessionSwitched;
        }
        void SessionSwitched(object sender, Microsoft.Win32.SessionSwitchEventArgs e) { lock (input) { input.ClearKeys(); escape = false; } }
        static bool Held(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
        IntPtr Handle(int n, IntPtr w, IntPtr l)
        {
            if (n < 0) return CallNextHookEx(hook, n, w, l); int message = w.ToInt32(); bool down = message == 0x100 || message == 0x104, up = message == 0x101 || message == 0x105; int vk = Marshal.ReadInt32(l); if (Marshal.ReadIntPtr(l, 16) == (IntPtr)0x4C44484B || (!allowInjected && (Marshal.ReadInt32(l, 8) & 0x10) != 0)) return CallNextHookEx(hook, n, w, l);
            if (vk == 0x1B) lock (input) { if (down && Busy) { escape = true; dispatcher.BeginInvoke(new Action(() => CancelRequested?.Invoke())); return (IntPtr)1; } if (up && escape) { escape = false; return (IntPtr)1; } }
            if (!down && !up) return CallNextHookEx(hook, n, w, l);
            uint messageTime = unchecked((uint)Marshal.ReadInt32(l, 12));
            long processedAt = Environment.TickCount64;
            long eventTime = HotkeyInput.EventTime(messageTime, processedAt);
            HotkeyInput.Result result; lock (input) result = input.ProcessEvent(vk, down, messageTime, processedAt, Held);
            if (result.ReplayCurrent)
            {
                var replay = new List<ClipboardInserter.INPUT>();
                ClipboardInserter.INPUT Key(int key, uint scan, uint flags) => new ClipboardInserter.INPUT { type = 1, data = new ClipboardInserter.UNION { keyboard = new ClipboardInserter.KEYBDINPUT { vk = (ushort)key, scan = (ushort)scan, flags = flags, extra = (IntPtr)0x4C44484B } } };
                foreach (int win in result.ReplayWindows) replay.Add(Key(win, ClipboardInserter.ScanCode(win), 1));
                replay.Add(Key(vk, (uint)Marshal.ReadInt32(l, 4), ((Marshal.ReadInt32(l, 8) & 1) != 0 ? 1u : 0u) | (up ? 2u : 0u)));
                if (SendInput((uint)replay.Count, replay.ToArray(), Marshal.SizeOf<ClipboardInserter.INPUT>()) != replay.Count)
                    dispatcher.BeginInvoke(new Action(() => Storage.Log("hotkey: Windows shortcut forwarding failed")));
            }
            if (result.Action.Length > 0) dispatcher.BeginInvoke(new Action(() => {
                Storage.Log("hotkey: " + result.Action + " event_lag_ms=" + (Environment.TickCount64 - eventTime));
                if (result.Action == "start") StartRequested?.Invoke();
                else if (result.Action == "stop") StopRequested?.Invoke();
                else if (result.Action == "correction") CorrectionRequested?.Invoke();
                else if (result.Action == "discard-probe") ProbeCancelled?.Invoke();
            }));
            return result.Suppress ? (IntPtr)1 : CallNextHookEx(hook, n, w, l);
        }
        public void Reset() { lock (input) input.Reset(); }
        public void Dispose() { Microsoft.Win32.SystemEvents.SessionSwitch -= SessionSwitched; PostThreadMessage(threadId, 0x12, IntPtr.Zero, IntPtr.Zero); thread.Join(1000); }
    }
    public static class ClipboardInserter
    {
        // INPUT's union includes MOUSEINPUT: on x64 the complete structure is 40 bytes.
        [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public UNION data; }
        [StructLayout(LayoutKind.Explicit)] public struct UNION { [FieldOffset(0)] public KEYBDINPUT keyboard; [FieldOffset(0)] public MOUSEINPUT mouse; }
        [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int x, y; public uint mouseData, flags, time; public IntPtr extra; }
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")] static extern uint MapVirtualKey(uint code, uint mapType);
        public static uint ScanCode(int vk) => MapVirtualKey((uint)vk, 0);
        // Keyboards report a scan code with every virtual key; remote desktops, VM consoles and some apps read only the scan code.
        public static INPUT Virtual(ushort vk, uint flags) => new INPUT { type = 1, data = new UNION { keyboard = new KEYBDINPUT { vk = vk, scan = (ushort)ScanCode(vk), flags = flags } } };
        public sealed class DeliveryResult { public bool Success; public string Error = ""; public string Method = ""; public string Signals = ""; }
        public static async Task<DeliveryResult> DeliverAsync(string text, OutputMode mode, Action started=null)
        {
            try {
                if(mode==OutputMode.Clipboard) { Clipboard.SetText(text); started?.Invoke(); return new DeliveryResult { Success=true, Method="clipboard" }; }
                if(!ModifiersReleased())return new DeliveryResult { Error=L.T("Release the held modifier keys and try again. The dictation stays in History.") };
                if(mode==OutputMode.Both) { Clipboard.SetText(text); bool pasted=Paste(out string error,started);return new DeliveryResult{Success=pasted,Error=error,Method="paste-and-keep"}; }
                if(!ClipboardTransfer.HasPending) {
                    using(var transfer=new ClipboardTransfer()) {
                        var pasted=await transfer.PasteAsync(text,started);
                        if(pasted.Attempted)return new DeliveryResult{Success=pasted.Success,Error=pasted.Error,Method="paste-and-restore",Signals=pasted.Signals};
                    }
                }
                // Unsupported clipboard formats stay completely untouched.
                started?.Invoke(); bool sent=DeliverUnicode(text,out string message);return new DeliveryResult{Success=sent,Error=message,Method="unicode-fallback"};
            }catch(Exception ex){Storage.Log("output delivery: "+ex.Message);return new DeliveryResult{Error=L.T("The text could not be output and stays in History.")};}
        }
        public static bool ModifiersReleased() { foreach(int key in new[]{0x11,0x10,0x12,0x5B,0x5C})if((GetAsyncKeyState(key)&0x8000)!=0)return false;return true; }
        // Text that could not be inserted stays on the clipboard so Ctrl+V works without opening the history.
        public static bool CopyText(string text) { for(int i=0;i<5;i++){try{Clipboard.SetText(text);return true;}catch(ExternalException ex){Storage.Log("clipboard copy: "+ex.Message);System.Threading.Thread.Sleep(50);}}return false; }
        public static bool CopySelection()
        {
            if(!ModifiersReleased())return false;
            var keys=new[]{Virtual(0x11,0),Virtual(0x43,0),Virtual(0x43,2),Virtual(0x11,2)};
            return SendInput(4,keys,Marshal.SizeOf<INPUT>())==4;
        }
        public static bool Paste(out string error,Action started=null) {
            error="";if(!ModifiersReleased()){error=L.T("Release the held modifier keys and try again. The dictation stays in History.");return false;}
            var keys=new[]{Virtual(0x11,0),Virtual(0x56,0),Virtual(0x56,2),Virtual(0x11,2)};
            started?.Invoke();
            if(SendInput(4,keys,Marshal.SizeOf<INPUT>())==4)return true;
            // Balance modifiers if Windows accepted only a prefix.
            SendInput(2,new[]{Virtual(0x56,2),Virtual(0x11,2)},Marshal.SizeOf<INPUT>());
            error=L.T("The text could not be inserted and stays in History.");return false;
        }
        public static bool DeliverUnicode(string text, out string error)
        {
            error = ""; try
            {
                foreach (int key in new[] { 0x11, 0x10, 0x12, 0x5B, 0x5C }) if ((GetAsyncKeyState(key) & 0x8000) != 0) { error = L.T("Release the held modifier keys and try again. The dictation stays in History."); return false; }
                // Unicode input never opens or replaces the clipboard, including images and private formats.
                var inputs=new List<INPUT>();
                bool Flush(){if(inputs.Count==0)return true;var batch=inputs.ToArray();inputs.Clear();return SendInput((uint)batch.Length,batch,Marshal.SizeOf<INPUT>())==batch.Length;}
                foreach(char c in text){if(c=='\r')continue;if(c=='\n'){inputs.Add(Virtual(0x0D,0));inputs.Add(Virtual(0x0D,2));}else{inputs.Add(Key(c,4));inputs.Add(Key(c,6));}if(inputs.Count>=256&&!Flush()){error=L.T("The text could not be inserted completely. The dictation stays in History.");return false;}}
                if(!Flush()){error=L.T("The text could not be inserted completely. The dictation stays in History.");return false;}
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        static INPUT Key(char c, uint flags) { return new INPUT { type = 1, data = new UNION { keyboard = new KEYBDINPUT { scan = c, flags = flags } } }; }
    }
    public static class Autostart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        static string Command { get { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key)) return k?.GetValue("LocalDictation") as string; } }
        // Only an entry that starts this copy counts as enabled; an entry for a moved or different copy is reported separately.
        public static bool Enabled => SameFile(Target(Command), Environment.ProcessPath);
        public static string Other { get { string target = Target(Command); return target.Length == 0 || SameFile(target, Environment.ProcessPath) ? null : target; } }
        public static void Set(bool enabled) { using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key)) { if (enabled) k.SetValue("LocalDictation", "\"" + Environment.ProcessPath + "\" --autostart"); else k.DeleteValue("LocalDictation", false); } }
        // For app start: an entry whose program no longer exists moves to this copy; an entry for another existing copy stays untouched.
        public static void Repair() { try { string target = Target(Command); if (target.Length == 0 || File.Exists(target) || SameFile(target, Environment.ProcessPath)) return; Set(true); Storage.Log("autostart: entry for missing " + target + " now starts " + Environment.ProcessPath); } catch (Exception ex) { Storage.Log("autostart repair: " + ex.Message); } }
        public static string Target(string command) { command = (command ?? "").Trim(); if (command.StartsWith("\"")) { int end = command.IndexOf('"', 1); return end < 0 ? command.Substring(1) : command.Substring(1, end - 1); } int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); return exe < 0 ? command : command.Substring(0, exe + 4); }
        public static bool SameFile(string a, string b) { if (String.IsNullOrWhiteSpace(a) || String.IsNullOrWhiteSpace(b)) return false; try { return String.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); } catch (Exception) { return false; } }
    }
}
