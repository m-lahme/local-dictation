using System.Diagnostics;
using System.Runtime.InteropServices;

// whisper.cpp needs the Visual C++ runtime, which is not shipped. Check it before Whisper.net tries to load whisper.dll and fails without a reason.
internal static class VisualCppRuntime
{
    const string Download = "https://aka.ms/vc14/vc_redist.x64.exe";
    public static void Require()
    {
        var missing = new[] { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "vcomp140.dll" }.FirstOrDefault(name => !NativeLibrary.TryLoad(name, out _));
        if (missing != null) throw new InvalidOperationException(Messages.T("Whisper needs the Microsoft Visual C++ Runtime (x64), but " + missing + " is missing. Install it from " + Download + " and then start Whisper again.", "Whisper benötigt die Microsoft Visual C++ Runtime (x64), aber " + missing + " fehlt. Installiere sie von " + Download + " und starte Whisper danach erneut."));
        // The whisper.cpp DLLs are built with MSVC 14.4x. Older msvcp140.dll versions crash them in std::mutex.
        using var process = Process.GetCurrentProcess(); var msvcp = process.Modules.Cast<ProcessModule>().FirstOrDefault(x => x.ModuleName.Equals("msvcp140.dll", StringComparison.OrdinalIgnoreCase))?.FileVersionInfo;
        if (msvcp is { FileMajorPart: 14, FileMinorPart: < 40 }) throw new InvalidOperationException(Messages.T("Whisper needs a newer Microsoft Visual C++ Runtime (x64) than " + msvcp.FileVersion + ". Install it from " + Download + " and then start Whisper again.", "Whisper benötigt eine neuere Microsoft Visual C++ Runtime (x64) als " + msvcp.FileVersion + ". Installiere sie von " + Download + " und starte Whisper danach erneut."));
    }
}
