using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;

namespace LocalDictation
{
    // Only documented HGLOBAL formats are copied. Unknown/private/OLE/GDI formats take the Unicode route.
    public sealed class ClipboardSnapshot
    {
        public List<KeyValuePair<uint, byte[]>> Formats { get; } = new();
        public uint Sequence { get; internal set; }
    }
    public sealed class ClipboardPasteResult
    {
        public bool Attempted, Success;
        public string Error = "", Signals = "";
    }
    public sealed class ClipboardTransfer : IDisposable
    {
        static readonly uint OleObject=RegisterClipboardFormat("DataObject"), OlePrivate=RegisterClipboardFormat("Ole Private Data"), HistoryItemId=RegisterClipboardFormat("ClipboardHistoryItemId");
        const uint Unicode = 13; const long MaxBytes = 64 * 1024 * 1024;
        static readonly uint History = RegisterClipboardFormat("CanIncludeInClipboardHistory"), Cloud = RegisterClipboardFormat("CanUploadToCloudClipboard"), Exclude = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
        static readonly HashSet<string> SafeNames = new(StringComparer.OrdinalIgnoreCase) { "HTML Format", "text/html", "Rich Text Format", "Rich Text Format Without Objects", "PNG", "image/png", "JFIF", "image/svg+xml", "text/x-moz-url", "UniformResourceLocator", "UniformResourceLocatorW", "Bookmark List", "WebKit Smart Paste Format", "Chromium Web Custom MIME Data Format", "Chromium Pepper MIME Data Format", "Chromium internal source RFH token", "Chromium internal source URL" };
        readonly HwndSource window; readonly TaskCompletionSource<bool> requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly List<(uint format, IntPtr memory)> restore = new();
        byte[] text; uint sequence, targetPid, readerPid; bool replaced, disposing, restoreStarted; static ClipboardTransfer pending; System.Windows.Threading.DispatcherTimer retry;
        public static bool HasPending => pending!=null;
        public static uint CurrentSequence => GetClipboardSequenceNumber();
        [DllImport("user32.dll", SetLastError=true)] static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError=true)] static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError=true)] static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll", SetLastError=true)] static extern IntPtr SetClipboardData(uint format, IntPtr memory);
        [DllImport("user32.dll")] static extern uint EnumClipboardFormats(uint format);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClipboardFormatName(uint format,StringBuilder name,int capacity);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern uint RegisterClipboardFormat(string name);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] static extern IntPtr GetClipboardOwner();
        [DllImport("user32.dll")] static extern IntPtr GetOpenClipboardWindow();
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr GlobalAlloc(uint flags,UIntPtr bytes);
        [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr memory);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr memory);
        public ClipboardTransfer() { window=new HwndSource(new HwndSourceParameters("Local Dictation Clipboard") { Width=0,Height=0,WindowStyle=0 }); window.AddHook(Message); }
        static bool IsControl(uint f) => f==History || f==Cloud || f==Exclude;
        public static bool IsKnownMemoryFormat(string name) => !String.IsNullOrEmpty(name) && (SafeNames.Contains(name) || name.StartsWith("Web ",StringComparison.Ordinal));
        static bool IsSafe(uint f) { if(f==1 || f==7 || f==8 || f==13 || f==15 || f==16 || f==17 || IsControl(f))return true; var name=new StringBuilder(256);return f>=0xC000 && GetClipboardFormatName(f,name,name.Capacity)>0 && IsKnownMemoryFormat(name.ToString()); }
        [DllImport("ole32.dll")] static extern int OleGetClipboard(out System.Runtime.InteropServices.ComTypes.IDataObject data);
        static bool IsTransport(uint format)=>format==OleObject || format==OlePrivate || format==HistoryItemId;
        bool ValidateOleFormats(){
            System.Runtime.InteropServices.ComTypes.IDataObject data=null;System.Runtime.InteropServices.ComTypes.IEnumFORMATETC formats=null;
            try{int status=OleGetClipboard(out data);if(status!=0){if(status==unchecked((int)0x800401D0))SnapshotReason="busy";else SnapshotReason="OLE error 0x"+status.ToString("X8");return false;}formats=data.EnumFormatEtc(System.Runtime.InteropServices.ComTypes.DATADIR.DATADIR_GET);var item=new System.Runtime.InteropServices.ComTypes.FORMATETC[1];int count=0;while(formats.Next(1,item,null)==0){var f=item[0];uint id=unchecked((ushort)f.cfFormat);bool safe=IsTransport(id)||IsSafe(id);if(f.ptd!=IntPtr.Zero){Marshal.FreeCoTaskMem(f.ptd);if(safe)continue;SnapshotReason="OLE target-specific format "+FormatName(id);return false;}if(++count>128){SnapshotReason="more than 128 OLE formats";return false;}if(safe)continue;if(f.lindex!=-1 || f.dwAspect!=System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT || (f.tymed & System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL)==0){SnapshotReason="OLE representation not restorable for "+FormatName(id);return false;}SnapshotReason="unsupported OLE format "+FormatName(id);return false;}return true;}catch(Exception ex){SnapshotReason="OLE inspection failed: "+ex.Message;return false;}finally{if(formats!=null&&Marshal.IsComObject(formats))Marshal.ReleaseComObject(formats);if(data!=null&&Marshal.IsComObject(data))Marshal.ReleaseComObject(data);}
        }
        static string FormatName(uint format){var name=new StringBuilder(256);return GetClipboardFormatName(format,name,name.Capacity)>0?format+" "+name:format.ToString();}
        public string SnapshotReason {get;private set;}="";
        public static string[] DescribeFormats(){var names=new List<string>();if(!OpenClipboard(IntPtr.Zero))return new[]{"Clipboard busy"};try{uint f=0;while((f=EnumClipboardFormats(f))!=0){var name=new StringBuilder(256);GetClipboardFormatName(f,name,256);names.Add(f+": "+name);}}finally{CloseClipboard();}return names.ToArray();}
        public bool TrySnapshot(out ClipboardSnapshot snapshot)
        {
            snapshot=null;SnapshotReason="";uint validated=GetClipboardSequenceNumber();if(!ValidateOleFormats()){if(SnapshotReason.Length==0)SnapshotReason="OLE content not safely representable";return false;} if(!OpenClipboard(window.Handle)){SnapshotReason="busy";return false;}
            try {
                if(GetClipboardSequenceNumber()!=validated){SnapshotReason="busy";return false;}
                var formats=new List<uint>();uint f=0;while((f=EnumClipboardFormats(f))!=0) { if(IsTransport(f))continue; if(formats.Count>=128 || !IsSafe(f)){var name=new StringBuilder(256);GetClipboardFormatName(f,name,256);SnapshotReason="unsupported format "+f+" "+name;return false;}formats.Add(f); }
                var saved=new ClipboardSnapshot();long total=0;
                foreach(var format in formats) {
                    IntPtr handle=GetClipboardData(format);if(handle==IntPtr.Zero){SnapshotReason="format unavailable "+FormatName(format);return false;}
                    ulong size=GlobalSize(handle).ToUInt64();if(size==0){SnapshotReason="format is not a memory block "+FormatName(format);return false;}if(size>(ulong)MaxBytes || (total+=(long)size)>MaxBytes){SnapshotReason="clipboard exceeds 64 MiB";return false;}
                    IntPtr data=GlobalLock(handle);if(data==IntPtr.Zero){SnapshotReason="format cannot be locked "+FormatName(format);return false;}
                    try { var bytes=new byte[(int)size];Marshal.Copy(data,bytes,0,bytes.Length);saved.Formats.Add(new(format,bytes)); } finally { GlobalUnlock(handle); }
                }
                saved.Sequence=GetClipboardSequenceNumber();snapshot=saved;return true;
            } catch { return false; } finally { CloseClipboard(); }
        }
        static IntPtr Allocate(byte[] bytes) { var memory=GlobalAlloc(0x42,(UIntPtr)bytes.Length);if(memory==IntPtr.Zero)throw new OutOfMemoryException();var p=GlobalLock(memory);if(p==IntPtr.Zero){GlobalFree(memory);throw new Win32Exception();}try{Marshal.Copy(bytes,0,p,bytes.Length);}finally{GlobalUnlock(memory);}return memory; }
        static void Put(uint format,byte[] bytes) { var p=Allocate(bytes);if(SetClipboardData(format,p)==IntPtr.Zero){GlobalFree(p);throw new Win32Exception(Marshal.GetLastWin32Error());} }
        static void ProtectHistory() { Put(Exclude,new byte[4]);Put(History,new byte[4]);Put(Cloud,new byte[4]); }
        // Also used by the native integration harness; never touches the history database.
        public void SetExcluded(IReadOnlyList<KeyValuePair<uint,byte[]>> formats) { int tries=0;while(!OpenClipboard(window.Handle)){if(tries++>=20)throw new Win32Exception();System.Threading.Thread.Sleep(10);}try{if(!EmptyClipboard())throw new Win32Exception();ProtectHistory();foreach(var f in formats)if(!IsControl(f.Key))Put(f.Key,f.Value);}finally{CloseClipboard();} }
        IntPtr Message(IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled)
        {
            if(msg==0x305 && (uint)w.ToInt64()==Unicode && text!=null) {
                try { GetWindowThreadProcessId(GetOpenClipboardWindow(),out uint reader);readerPid=reader;Put(Unicode,text);sequence=GetClipboardSequenceNumber();requested.TrySetResult(reader==targetPid); }catch(Exception ex){Storage.Log("clipboard render: "+ex.Message);requested.TrySetException(ex);} handled=true;
            }
            return IntPtr.Zero;
        }
        static string ProcessName(uint id) { try { using var process=Process.GetProcessById((int)id);return process.ProcessName; } catch { return "pid "+id; } }
        public async Task<ClipboardPasteResult> PasteAsync(string value,Action started=null)
        {
            // Browsers and many apps read the clipboard on Ctrl+V even when no text field has focus, so reading alone does not prove insertion.
            bool caret=SelectionCapture.CaretInForeground();var probe=Task.Run(SelectionCapture.FocusedTextInput);bool? input=await Task.WhenAny(probe,Task.Delay(300))==probe?probe.Result:null;
            var result=new ClipboardPasteResult{Signals="caret="+caret+" input="+(input?.ToString()??"unknown")};ClipboardSnapshot backup;int attempts=0;while(!TrySnapshot(out backup)){if(SnapshotReason=="busy" && attempts++<3){await Task.Delay(40);continue;}Storage.Log("clipboard fallback: "+SnapshotReason);return result;}
            // Allocate restoration buffers before replacing anything. Preserve format order and exact bytes.
            restore.Add((Exclude,Allocate(new byte[4])));restore.Add((History,Allocate(new byte[4])));restore.Add((Cloud,Allocate(new byte[4])));
            foreach(var f in backup.Formats)if(!IsControl(f.Key))restore.Add((f.Key,Allocate(f.Value)));
            text=Encoding.Unicode.GetBytes(value+"\0");GetWindowThreadProcessId(GetForegroundWindow(),out targetPid);
            if(!OpenClipboard(window.Handle))return result;
            try {
                if(GetClipboardSequenceNumber()!=backup.Sequence)return result;
                if(!EmptyClipboard())return result;
                replaced=true;pending=this;result.Attempted=true;
                ProtectHistory();SetClipboardData(Unicode,IntPtr.Zero);sequence=GetClipboardSequenceNumber();
            } finally { sequence=GetClipboardSequenceNumber();CloseClipboard(); }
            try {
                if(!ClipboardInserter.Paste(out string error,started)){result.Error=error;return result;}
                var completed=await Task.WhenAny(requested.Task,Task.Delay(2500));
                if(completed!=requested.Task) { result.Signals+=" reader=none";result.Error="Der Text konnte nicht eingefügt werden und bleibt im Verlauf.";return result; }
                bool targetRead=await requested.Task;result.Signals+=" reader="+(targetRead?"target":ProcessName(readerPid));
                if(!caret && input==false) { result.Error="Es war kein Textfeld aktiv.";return result; }
                // The render message establishes retrieval, not completion of the target's entire edit operation.
                await Task.Delay(targetRead?180:1000);
                result.Success=true;return result;
            } finally { if(!await RestoreAsync()) { result.Success=false;result.Error="Text eingefügt. Die bisherige Zwischenablage wird gleich wiederhergestellt."; } }
        }
        public bool RestoreNow() {
            if(!replaced)return true;
            if(!OpenClipboard(window.Handle))return false;
            try {
                // A newer copy wins. Delayed rendering only updates our own recorded sequence.
                if(GetClipboardOwner()!=window.Handle || GetClipboardSequenceNumber()!=sequence){replaced=false;return true;}
                if(!restoreStarted){if(!EmptyClipboard())return false;restoreStarted=true;}
                for(int i=0;i<restore.Count;i++){var item=restore[i];if(item.memory==IntPtr.Zero)continue;if(SetClipboardData(item.format,item.memory)==IntPtr.Zero)return false;restore[i]=(item.format,IntPtr.Zero);}
                replaced=false;return true;
            } finally { if(GetClipboardOwner()==window.Handle)sequence=GetClipboardSequenceNumber();CloseClipboard(); }
        }
        async Task<bool> RestoreAsync(){for(int i=0;i<20;i++){if(RestoreNow())return true;await Task.Delay(50);}return false;}
        public static void RestorePending(){try{pending?.RestoreNow();}catch(Exception ex){Storage.Log("clipboard shutdown: "+ex.Message);}}
        public void Dispose(){
            if(disposing)return;
            if(replaced && !RestoreNow()) {
                if(retry==null){retry=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};retry.Tick+=(s,e)=>{try{if(RestoreNow()){retry.Stop();Dispose();}}catch(Exception ex){Storage.Log("clipboard retry: "+ex.Message);}};};retry.Start();return;
            }
            disposing=true;retry?.Stop();if(pending==this)pending=null;foreach(var f in restore)if(f.memory!=IntPtr.Zero)GlobalFree(f.memory);window.Dispose();
        }
    }
}
