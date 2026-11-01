using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Forms = System.Windows.Forms;

namespace LocalDictation
{
    // Live view of one Windows capture endpoint.
    public sealed class MicrophoneInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Device { get; set; } = "";
        public string Container { get; set; } = "";
        public string Bus { get; set; } = "";
        public int FormFactor { get; set; } = 10;
        public Guid Jack { get; set; }
        public bool HeadphoneSibling { get; set; }
        public string Kind { get; set; } = "";
        public string Connection { get; set; } = "";
        public DeviceState State { get; set; } = DeviceState.Active;
        public bool IsDefault { get; set; }
        public bool Active => State == DeviceState.Active;
    }

    public static class MicrophoneCatalog
    {
        static readonly Guid EndpointProperties = new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), DeviceProperties = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), ParentProperties = new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc");
        static readonly PropertyKey FormFactorKey = new PropertyKey(EndpointProperties, 0), JackKey = new PropertyKey(EndpointProperties, 8), DescriptionKey = new PropertyKey(DeviceProperties, 2), EnumeratorKey = new PropertyKey(DeviceProperties, 24);
        static readonly PropertyKey InterfaceKey = new PropertyKey(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 2), ContainerKey = new PropertyKey(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
        // Parent device node; audio offload drivers such as Intel Smart Sound name the physical USB or Bluetooth device separately.
        static readonly PropertyKey ParentKey = new PropertyKey(ParentProperties, 2), OffloadedKey = new PropertyKey(ParentProperties, 39);
        static readonly PropertyKey[] CaptureKeys = { new PropertyKey(DeviceProperties, 14), DescriptionKey, InterfaceKey, ContainerKey, FormFactorKey, JackKey, OffloadedKey, ParentKey, EnumeratorKey }, RenderKeys = { FormFactorKey, ContainerKey };
        const string MachineContainer = "00000000-0000-0000-ffff-ffffffffffff";
        public static readonly Guid MicrophoneJack = new Guid("DFF21BE1-F70F-11D0-B917-00A0C9223196"), PersonalMicrophone = new Guid("DFF21BE3-F70F-11D0-B917-00A0C9223196"), MicrophoneArray = new Guid("DFF21BE5-F70F-11D0-B917-00A0C9223196"), ProcessingArray = new Guid("DFF21BE6-F70F-11D0-B917-00A0C9223196");
        public static readonly Guid HeadsetJack = new Guid("DFF21DE2-F70F-11D0-B917-00A0C9223196"), Speakerphone = new Guid("DFF21DE3-F70F-11D0-B917-00A0C9223196"), EchoSuppressingSpeakerphone = new Guid("DFF21DE4-F70F-11D0-B917-00A0C9223196"), EchoCancelingSpeakerphone = new Guid("DFF21DE5-F70F-11D0-B917-00A0C9223196");
        const string Virtual = @"virtual|stereo ?mix|voicemeeter|vb-audio|cable output|nvidia broadcast|steam streaming|krisp|wave link|\bobs\b|\bcamo\b|loopback";
        const string Webcam = @"webcam|\bcam\b|camera|kamera|facecam|streamcam|brio|lifecam|kiyo|insta360|obsbot|emeet|\bc(270|310|505|615|920|922|925|930|980)\b";
        const string Headset = @"headset|hands-?free|kopfhörer|headphone|earbuds|\bbuds\b|airpods";
        const string Standalone = @"\bmic\b|microphone|mikrofon|yeti|snowball|r[oø]de|shure|quadcast|solocast|seiren|samson|fifine|at2020|wave:? ?\d|podmic|maono|tonor";
        static readonly Lazy<bool> Laptop = new Lazy<bool>(() => { try { return Forms.SystemInformation.PowerStatus.BatteryChargeStatus != Forms.BatteryChargeStatus.NoSystemBattery; } catch { return false; } });

        public static List<MicrophoneInfo> Scan()
        {
            using (var devices = new MMDeviceEnumerator())
            {
                string fallback = ""; try { if (devices.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)) using (var d = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)) fallback = d.ID; } catch { }
                var headphones = new HashSet<string>();
                foreach (var x in devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All)) { try { var v = Read(x, RenderKeys); int f = Number(v[0]); string c = v[1].ToLowerInvariant(); if ((f == 3 || f == 5) && c.Length > 0 && c != MachineContainer) headphones.Add(c); } catch { } x.Dispose(); }
                var list = new List<MicrophoneInfo>();
                foreach (var x in devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.All))
                {
                    try
                    {
                        var v = Read(x, CaptureKeys); var m = new MicrophoneInfo { Id = x.ID, Name = v[0], State = x.State, IsDefault = x.ID == fallback };
                        m.Description = v[1]; m.Device = v[2]; m.Container = v[3].ToLowerInvariant(); m.FormFactor = Number(v[4]);
                        Guid jack; if (Guid.TryParse(v[5], out jack)) m.Jack = jack;
                        m.Bus = Bus(v[6], v[7], v[8]); m.HeadphoneSibling = headphones.Contains(m.Container);
                        Classify(m); list.Add(m);
                    }
                    catch (Exception ex) { Storage.Log("microphone scan: " + ex.Message); }
                    x.Dispose();
                }
                return list;
            }
        }
        // NAudio finds a property by enumerating the whole store, which takes about a second for all endpoints; the device's store is queried by key instead.
        static readonly FieldInfo DeviceField = typeof(MMDevice).GetField("deviceInterface", BindingFlags.NonPublic | BindingFlags.Instance);
        static string[] Read(MMDevice device, PropertyKey[] keys)
        {
            var values = new string[keys.Length]; IEndpointProperties store = null;
            if (DeviceField?.GetValue(device) is IEndpoint endpoint && endpoint.OpenPropertyStore(0, out store) == 0)
            {
                try { for (int k = 0; k < keys.Length; k++) { var key = keys[k]; if (store.GetValue(ref key, out var value) != 0) continue; try { values[k] = Convert.ToString(value.Value); } catch { } PropVariantClear(ref value); } }
                finally { Marshal.ReleaseComObject(store); }
            }
            else
            {
                var p = device.Properties;
                for (int i = 0; i < p.Count; i++) { var key = p.Get(i); int k = Array.FindIndex(keys, x => x.formatId == key.formatId && x.propertyId == key.propertyId); if (k >= 0) try { values[k] = Convert.ToString(p[i].Value); } catch { } }
            }
            for (int k = 0; k < values.Length; k++) values[k] ??= "";
            return values;
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface IEndpoint { int Activate(ref Guid iid, int context, IntPtr parameters, out IntPtr result); [PreserveSig] int OpenPropertyStore(int access, out IEndpointProperties store); }
        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface IEndpointProperties { int GetCount(out int count); int GetAt(int index, out PropertyKey key); [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value); }
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
        static int Number(string value) { int n; return int.TryParse(value, out n) ? n : 10; }
        public static string Bus(string offloaded, string parent, string enumerator)
        {
            foreach (var path in new[] { offloaded, parent })
            {
                if (String.IsNullOrWhiteSpace(path)) continue;
                string value = Regex.Replace(path, @"^\{\d+\}\.", ""); int end = value.IndexOf('\\');
                return (end < 0 ? value : value.Substring(0, end)).ToUpperInvariant();
            }
            return (enumerator ?? "").ToUpperInvariant();
        }
        public static void Classify(MicrophoneInfo m)
        {
            string name = (m.Description + " " + m.Device + " " + m.Name).ToLowerInvariant(), device = m.Device.ToLowerInvariant();
            string connection = m.Bus == "USB" ? "usb" : m.Bus.StartsWith("BTH") ? "bluetooth" : m.Bus == "" || m.Bus == "SWD" || m.Bus == "ROOT" ? "software" : "internal";
            if (m.FormFactor == 0 || Regex.IsMatch(name, @"remote ?audio")) Set(m, "remote", "remote");
            else if (connection == "software" || Regex.IsMatch(name, Virtual)) Set(m, "virtual", "software");
            else if (connection == "bluetooth") Set(m, "headset", connection);
            else if (Regex.IsMatch(name, Webcam)) Set(m, "webcam", connection);
            else if (m.FormFactor == 5 || m.Jack == HeadsetJack || connection == "internal" && m.Jack == PersonalMicrophone || Regex.IsMatch(name, Headset)) Set(m, "headset", connection == "internal" ? "jack" : connection);
            else if (connection != "internal" && Regex.IsMatch(device, Standalone)) Set(m, "microphone", connection);
            else if (m.HeadphoneSibling) Set(m, "headset", connection == "internal" ? "jack" : connection);
            else if (m.Jack == Speakerphone || m.Jack == EchoSuppressingSpeakerphone || m.Jack == EchoCancelingSpeakerphone) Set(m, "speakerphone", connection);
            else if (connection == "internal") { if (m.Jack == MicrophoneArray || m.Jack == ProcessingArray || Regex.IsMatch(name, @"array|intern|integr|built-?in|eingebaut")) Set(m, "builtin", "internal"); else if (m.FormFactor == 2 || Regex.IsMatch(name, @"\bline\b")) Set(m, "line", "jack"); else Set(m, "input", "internal"); }
            else Set(m, "usb", connection);
        }
        static void Set(MicrophoneInfo m, string kind, string connection) { m.Kind = kind; m.Connection = connection; }

        static readonly HashSet<string> GenericDescriptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Mikrofon", "Microphone", "Mikrofonarray", "Microphone Array", "Freisprechtelefon", "Hands-Free", "Hands-Free AG Audio", "Headset", "Headset-Mikrofon", "Headset Microphone", "Kopfhörer", "Headphones", "Line", "Line In", "Line-In", "Eingang", "Input", "Externes Mikrofon", "External Microphone" };
        static string Hardware(MicrophoneEntry m) => Regex.Replace(m.Device ?? "", @"^\d+-\s*", "").Trim();
        // External devices are best known by their hardware name; internal ones and renamed endpoints by the Windows endpoint name.
        public static string Title(MicrophoneEntry m)
        {
            string hardware = Hardware(m), description = (m.Description ?? "").Trim();
            if (description.Length > 0 && (m.Connection == "internal" || m.Connection == "jack" || hardware.Length == 0 || !GenericDescriptions.Contains(description))) return description;
            return hardware.Length > 0 ? hardware : m.Name;
        }
        public static string Details(MicrophoneEntry m)
        {
            var parts = new List<string> { KindLabel(m.Kind) };
            string connection = ConnectionLabel(m.Connection); if (connection.Length > 0 && m.Kind != "usb" && m.Kind != "virtual" && m.Kind != "remote") parts.Add(connection);
            string hardware = Hardware(m); if (hardware.Length > 0 && Title(m) != hardware) parts.Add(hardware);
            return String.Join(" · ", parts);
        }
        public static string KindLabel(string kind) => kind switch { "builtin" => Laptop.Value ? "Im Laptop eingebaut" : "Eingebaut", "headset" => "Headset", "webcam" => "Webcam", "microphone" => "Externes Mikrofon", "usb" => "USB-Audiogerät", "speakerphone" => "Freisprecheinrichtung", "line" => "Line-In", "virtual" => "Virtuelles Gerät", "remote" => "Remotedesktop", _ => "Audioeingang" };
        public static string ConnectionLabel(string connection) => connection switch { "usb" => "USB", "bluetooth" => "Bluetooth", "jack" => "Klinke", _ => "" };
        public static string Glyph(string kind) => kind switch { "builtin" => Laptop.Value ? "" : "", "headset" => "", "webcam" => "", "usb" => "", "speakerphone" => "", "virtual" => "", "remote" => "", _ => "" };
    }

    public static class MicrophonePriority
    {
        static readonly string[] DefaultOrder = { "microphone", "headset", "usb", "webcam", "speakerphone", "builtin", "bluetooth", "input", "line", "virtual", "remote" };
        public static string Key(MicrophoneInfo m) => String.IsNullOrEmpty(m.Container) ? m.Id : m.Container + "|" + m.Description.Trim().ToLowerInvariant();
        // Bluetooth headsets rank below built-in microphones by default because recording switches them to the low-quality hands-free profile.
        public static int Rank(MicrophoneEntry m) { int i = Array.IndexOf(DefaultOrder, m.Connection == "bluetooth" ? "bluetooth" : m.Kind); return i < 0 ? DefaultOrder.Length : i; }
        public static bool Synchronize(Settings settings, IList<MicrophoneInfo> found)
        {
            if (settings.Microphones.Count > 0) return Merge(settings.Microphones, found, false);
            settings.Microphones = Initial(settings.Microphone, found); return settings.Microphones.Count > 0;
        }
        // First run: every microphone Windows remembers, the previously selected one first, the rest by device type.
        public static List<MicrophoneEntry> Initial(string preferredId, IEnumerable<MicrophoneInfo> found)
        {
            var list = new List<MicrophoneEntry>(); Merge(list, found, true);
            return list.OrderBy(x => !String.IsNullOrEmpty(preferredId) && x.Ids.Contains(preferredId) ? 0 : 1).ThenBy(Rank).ToList();
        }
        // Newly connected microphones are appended at the lowest priority; endpoints of a known device join its entry.
        public static bool Merge(List<MicrophoneEntry> list, IEnumerable<MicrophoneInfo> found, bool includeAbsent)
        {
            bool changed = false;
            foreach (var m in found.OrderBy(x => x.Active ? 0 : 1))
            {
                string key = Key(m); var entry = list.FirstOrDefault(x => x.Ids.Contains(m.Id)) ?? list.FirstOrDefault(x => x.Key == key); bool created = entry == null;
                if (created) { if (!m.Active && (!includeAbsent || m.State == DeviceState.Disabled)) continue; entry = new MicrophoneEntry(); list.Add(entry); changed = true; }
                if (!entry.Ids.Contains(m.Id)) { entry.Ids.Add(m.Id); changed = true; }
                if (!created && !m.Active) continue;
                if (entry.Key != key || entry.Name != m.Name || entry.Description != m.Description || entry.Device != m.Device || entry.Kind != m.Kind || entry.Connection != m.Connection) changed = true;
                entry.Key = key; entry.Name = m.Name; entry.Description = m.Description; entry.Device = m.Device; entry.Kind = m.Kind; entry.Connection = m.Connection;
            }
            return changed;
        }
        // Endpoint ids to open, best first: listed microphones by priority, then the Windows default, then any other connected one.
        public static List<string> Candidates(IEnumerable<MicrophoneEntry> list, IList<string> connected, string fallback)
        {
            var result = new List<string>();
            foreach (var entry in list) foreach (var id in entry.Ids) if (connected.Contains(id) && !result.Contains(id)) result.Add(id);
            if (!String.IsNullOrEmpty(fallback) && connected.Contains(fallback) && !result.Contains(fallback)) result.Add(fallback);
            foreach (var id in connected) if (!result.Contains(id)) result.Add(id);
            return result;
        }
    }
}
