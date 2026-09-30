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
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(exe) };
            info.ArgumentList.Add("--engine"); info.ArgumentList.Add(engine); info.ArgumentList.Add("--device"); info.ArgumentList.Add(device); info.ArgumentList.Add("--models"); info.ArgumentList.Add(Models.Root);
            process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += (s, e) => { if (e.Data == null || disposed) return; try { if (JsonLite.Value(e.Data, "event") == "model" && JsonLite.Value(e.Data, "state") == "ready") Ready = true; Event?.Invoke(e.Data); } catch (Exception ex) { Storage.Log("worker protocol: " + ex.Message); } };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) Storage.Log("native: " + e.Data); };
            process.Exited += (s, e) => { if (!disposed) Event?.Invoke(JsonSerializer.Serialize(new { @event = "crash", message = "Das Modell wurde unerwartet beendet." })); };
            process.Start(); process.StandardInput.AutoFlush = true; process.BeginOutputReadLine(); process.BeginErrorReadLine();
        }
        public Task Transcribe(float[] audio, int id) { return Task.Run(() => { byte[] bytes = new byte[audio.Length * 4]; Buffer.BlockCopy(audio, 0, bytes, 0, bytes.Length); lock (writeGate) { if (disposed) throw new ObjectDisposedException("Worker"); process.StandardInput.WriteLine(JsonSerializer.Serialize(new { command = "transcribe", id, audio = Convert.ToBase64String(bytes) })); } }); }
        public void Dispose() { disposed = true; Ready = false; try { if (process != null && !process.HasExited) { process.Kill(true); if (!process.WaitForExit(5000)) Storage.Log("worker stop: process did not exit within five seconds"); } } catch (Exception ex) { Storage.Log("worker stop: " + ex.Message); } process?.Dispose(); }
    }
    public static class OneShotTranscriber
    {
        public static async Task<string> RunAsync(string root, string engine, string audioPath)
        {
            string exe = Path.Combine(root, "native", "LocalDictation.Worker.exe");
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(exe) };
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
        readonly HookProc proc; readonly IntPtr hook; readonly Dispatcher dispatcher; readonly HotkeyInput input = new(); readonly bool allowInjected;
        bool escape; public bool Busy;
        public string Binding { get => input.SelectedBinding; set => input.SelectedBinding = value; }
        public int HoldMilliseconds { set { input.HoldMilliseconds = value; } }
        public event Action StartRequested, StopRequested, CancelRequested, CorrectionRequested, ProbeCancelled;
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, ClipboardInserter.INPUT[] inputs, int size);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc p, IntPtr mod, uint tid);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string n);
        public GlobalHotkey(int holdMs, bool allowInjectedForTesting=false) { dispatcher = Dispatcher.CurrentDispatcher; input.HoldMilliseconds = holdMs; allowInjected=allowInjectedForTesting;proc = Handle; hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0); if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        IntPtr Handle(int n, IntPtr w, IntPtr l)
        {
            if (n < 0) return CallNextHookEx(hook, n, w, l); int message = w.ToInt32(); bool down = message == 0x100 || message == 0x104, up = message == 0x101 || message == 0x105; int vk = Marshal.ReadInt32(l); if (Marshal.ReadIntPtr(l, 16) == (IntPtr)0x4C44484B || (!allowInjected && (Marshal.ReadInt32(l, 8) & 0x10) != 0)) return CallNextHookEx(hook, n, w, l);
            if (vk == 0x1B) { if (down && Busy) { escape = true; dispatcher.BeginInvoke(new Action(() => CancelRequested?.Invoke())); return (IntPtr)1; } if (up && escape) { escape = false; return (IntPtr)1; } }
            if (!down && !up) return CallNextHookEx(hook, n, w, l);
            uint messageTime = unchecked((uint)Marshal.ReadInt32(l, 12));
            long processedAt = Environment.TickCount64;
            long eventTime = HotkeyInput.EventTime(messageTime, processedAt);
            var result = input.ProcessEvent(vk, down, messageTime, processedAt);
            if (result.ReplayCurrent)
            {
                var replay = new List<ClipboardInserter.INPUT>();
                ClipboardInserter.INPUT Key(int key, uint scan, uint flags) => new ClipboardInserter.INPUT { type = 1, data = new ClipboardInserter.UNION { keyboard = new ClipboardInserter.KEYBDINPUT { vk = (ushort)key, scan = (ushort)scan, flags = flags, extra = (IntPtr)0x4C44484B } } };
                foreach (int win in result.ReplayWindows) replay.Add(Key(win, 0, 1));
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
        public void Reset() { input.Reset(); }
        public void Dispose() { UnhookWindowsHookEx(hook); }
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
        public sealed class DeliveryResult { public bool Success; public string Error = ""; public string Method = ""; }
        public static async Task<DeliveryResult> DeliverAsync(string text, OutputMode mode, Action started=null)
        {
            try {
                if(mode==OutputMode.Clipboard) { Clipboard.SetText(text); started?.Invoke(); return new DeliveryResult { Success=true, Method="clipboard" }; }
                if(!ModifiersReleased())return new DeliveryResult { Error="Lass die gedrückten Zusatztasten los und versuche es erneut. Das Diktat bleibt im Verlauf." };
                if(mode==OutputMode.Both) { Clipboard.SetText(text); bool pasted=Paste(out string error,started);return new DeliveryResult{Success=pasted,Error=error,Method="paste-and-keep"}; }
                if(!ClipboardTransfer.HasPending) {
                    using(var transfer=new ClipboardTransfer()) {
                        var pasted=await transfer.PasteAsync(text,started);
                        if(pasted.Attempted)return new DeliveryResult{Success=pasted.Success,Error=pasted.Error,Method="paste-and-restore"};
                    }
                }
                // Unsupported clipboard formats stay completely untouched.
                started?.Invoke(); bool sent=DeliverUnicode(text,out string message);return new DeliveryResult{Success=sent,Error=message,Method="unicode-fallback"};
            }catch(Exception ex){Storage.Log("output delivery: "+ex.Message);return new DeliveryResult{Error="Der Text konnte nicht ausgegeben werden und bleibt im Verlauf."};}
        }
        public static bool ModifiersReleased() { foreach(int key in new[]{0x11,0x10,0x12,0x5B,0x5C})if((GetAsyncKeyState(key)&0x8000)!=0)return false;return true; }
        public static bool CopySelection()
        {
            if(!ModifiersReleased())return false;
            INPUT Virtual(ushort vk,uint flags)=>new INPUT{type=1,data=new UNION{keyboard=new KEYBDINPUT{vk=vk,flags=flags}}};
            var keys=new[]{Virtual(0x11,0),Virtual(0x43,0),Virtual(0x43,2),Virtual(0x11,2)};
            return SendInput(4,keys,Marshal.SizeOf<INPUT>())==4;
        }
        public static bool Paste(out string error,Action started=null) {
            error="";if(!ModifiersReleased()){error="Lass die gedrückten Zusatztasten los und versuche es erneut. Das Diktat bleibt im Verlauf.";return false;}
            INPUT Virtual(ushort vk,uint flags)=>new INPUT{type=1,data=new UNION{keyboard=new KEYBDINPUT{vk=vk,flags=flags}}};
            var keys=new[]{Virtual(0x11,0),Virtual(0x56,0),Virtual(0x56,2),Virtual(0x11,2)};
            started?.Invoke();
            if(SendInput(4,keys,Marshal.SizeOf<INPUT>())==4)return true;
            // Balance modifiers if Windows accepted only a prefix.
            SendInput(2,new[]{Virtual(0x56,2),Virtual(0x11,2)},Marshal.SizeOf<INPUT>());
            error="Der Text konnte nicht eingefügt werden und bleibt im Verlauf.";return false;
        }
        public static bool DeliverUnicode(string text, out string error)
        {
            error = ""; try
            {
                foreach (int key in new[] { 0x11, 0x10, 0x12, 0x5B, 0x5C }) if ((GetAsyncKeyState(key) & 0x8000) != 0) { error = "Lass die gedrückten Zusatztasten los und versuche es erneut. Das Diktat bleibt im Verlauf."; return false; }
                // Unicode input never opens or replaces the clipboard, including images and private formats.
                var inputs=new List<INPUT>();INPUT Virtual(ushort vk,uint flags)=>new INPUT{type=1,data=new UNION{keyboard=new KEYBDINPUT{vk=vk,flags=flags}}};
                bool Flush(){if(inputs.Count==0)return true;var batch=inputs.ToArray();inputs.Clear();return SendInput((uint)batch.Length,batch,Marshal.SizeOf<INPUT>())==batch.Length;}
                foreach(char c in text){if(c=='\r')continue;if(c=='\n'){inputs.Add(Virtual(0x0D,0));inputs.Add(Virtual(0x0D,2));}else{inputs.Add(Key(c,4));inputs.Add(Key(c,6));}if(inputs.Count>=256&&!Flush()){error="Der Text konnte nicht vollständig eingefügt werden. Das Diktat bleibt im Verlauf.";return false;}}
                if(!Flush()){error="Der Text konnte nicht vollständig eingefügt werden. Das Diktat bleibt im Verlauf.";return false;}
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        static INPUT Key(char c, uint flags) { return new INPUT { type = 1, data = new UNION { keyboard = new KEYBDINPUT { scan = c, flags = flags } } }; }
    }
    public static class Autostart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool Enabled { get { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key)) return k?.GetValue("LocalDictation") != null; } }
        public static void Set(bool enabled) { using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key)) { if (enabled) k.SetValue("LocalDictation", "\"" + Environment.ProcessPath + "\" --autostart"); else k.DeleteValue("LocalDictation", false); } }
    }
}
