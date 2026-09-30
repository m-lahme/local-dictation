using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LocalDictation
{
    public sealed class ModelRoute
    {
        public string Engine { get; }
        public string Device { get; }
        public string Reason { get; }
        public ModelRoute(string engine, string device, string reason) { Engine = engine; Device = device; Reason = reason; }
    }

    public static class PowerModelRouting
    {
        public static readonly Guid AsusSilent = new Guid("64a64f24-65b9-4b56-befd-5ec1eaced9b3");
        public static readonly Guid AsusPerformance = new Guid("27fa6203-3987-4dcc-918d-748559d549ec");
        public static readonly Guid AsusTurbo = new Guid("6fecc5ae-f350-48a5-b669-b472cb895ccf");
        public static readonly Guid AsusPdPerformance = new Guid("db6638c8-3605-4d77-aa4d-df84a49947ac");
        public static readonly Guid PowerSaver = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a");
        public static readonly Guid HighPerformance = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        public static readonly Guid UltimatePerformance = new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61");

        public static ModelRoute Resolve(Guid scheme, Guid? personality)
        {
            if (scheme == AsusSilent) return new ModelRoute("parakeet", "cpu", "ASUS Leise");
            if (scheme == AsusPerformance) return new ModelRoute("whisper", "gpu", "ASUS Performance");
            if (scheme == AsusTurbo) return new ModelRoute("whisper", "gpu", "ASUS Turbo");
            if (scheme == AsusPdPerformance) return new ModelRoute("whisper", "gpu", "ASUS PD Performance");
            Guid intent = personality ?? scheme;
            if (intent == PowerSaver) return new ModelRoute("parakeet", "cpu", "Windows Energiesparen");
            if (intent == HighPerformance || intent == UltimatePerformance) return new ModelRoute("whisper", "gpu", "Windows Höchstleistung");
            return null;
        }
    }

    public sealed class PowerModeMonitor : IDisposable
    {
        const int WM_POWERBROADCAST = 0x0218, PBT_POWERSETTINGCHANGE = 0x8013;
        static readonly Guid ActiveSchemeSetting = new Guid("31f9f286-5084-42fe-b720-2b0264993763");
        static readonly Guid SchemePersonalitySetting = new Guid("245d8541-3943-4422-b025-13a784f679b7");
        readonly HwndSource source; IntPtr activeRegistration, personalityRegistration; bool disposed;
        public Guid ActiveScheme { get; private set; }
        public Guid? Personality { get; private set; }
        public event Action<Guid, Guid?> Changed;

        public PowerModeMonitor(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) throw new InvalidOperationException("Das Overlay-Fenster besitzt noch kein Windows-Handle.");
            source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("Windows-Nachrichtenquelle nicht verfügbar.");
            ActiveScheme = ReadActiveScheme();
            source.AddHook(WndProc);
            var activeSetting = ActiveSchemeSetting; var personalitySetting = SchemePersonalitySetting;
            activeRegistration = RegisterPowerSettingNotification(handle, ref activeSetting, 0);
            personalityRegistration = RegisterPowerSettingNotification(handle, ref personalitySetting, 0);
            if (activeRegistration == IntPtr.Zero || personalityRegistration == IntPtr.Zero)
            {
                Dispose();
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Benachrichtigungen zum Leistungsmodus konnten nicht registriert werden.");
            }
        }

        IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != WM_POWERBROADCAST || wParam.ToInt64() != PBT_POWERSETTINGCHANGE || lParam == IntPtr.Zero) return IntPtr.Zero;
            var setting = Marshal.PtrToStructure<PowerBroadcastSetting>(lParam);
            if (setting.DataLength != 16) return IntPtr.Zero;
            var value = Marshal.PtrToStructure<Guid>(IntPtr.Add(lParam, Marshal.SizeOf<PowerBroadcastSetting>()));
            if (setting.PowerSetting == ActiveSchemeSetting) ActiveScheme = value;
            else if (setting.PowerSetting == SchemePersonalitySetting) Personality = value;
            else return IntPtr.Zero;
            Changed?.Invoke(ActiveScheme, Personality);
            return IntPtr.Zero;
        }

        public static Guid ReadActiveScheme()
        {
            IntPtr pointer;
            uint result = PowerGetActiveScheme(IntPtr.Zero, out pointer);
            if (result != 0) throw new Win32Exception((int)result, "Aktiver Windows-Energiesparplan konnte nicht gelesen werden.");
            try { return Marshal.PtrToStructure<Guid>(pointer); }
            finally { LocalFree(pointer); }
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (activeRegistration != IntPtr.Zero) UnregisterPowerSettingNotification(activeRegistration);
            if (personalityRegistration != IntPtr.Zero) UnregisterPowerSettingNotification(personalityRegistration);
            source?.RemoveHook(WndProc);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)] struct PowerBroadcastSetting { public Guid PowerSetting; public uint DataLength; }
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref readonly Guid setting, uint flags);
        [DllImport("user32.dll", SetLastError = true)] static extern bool UnregisterPowerSettingNotification(IntPtr handle);
        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    }
}
