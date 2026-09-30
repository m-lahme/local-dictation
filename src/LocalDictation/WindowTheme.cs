using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
namespace LocalDictation
{
    public static class WindowTheme
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        public static void Apply(Window window, string theme) { var h = new WindowInteropHelper(window).Handle; if (h == IntPtr.Zero) return; int dark = Theme.IsDark(theme) ? 1 : 0; DwmSetWindowAttribute(h, 20, ref dark, 4); }
    }
}
