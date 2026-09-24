using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VinylRipper.Windows.Controls;

/// <summary>Pide a DWM que pinte la barra de título nativa en oscuro (Windows 10 20H1+ / 11).</summary>
public static class DarkTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void Apply(Window window)
    {
        if (window.IsLoaded || new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Set(window);
        else
            window.SourceInitialized += (_, _) => Set(window);
    }

    private static void Set(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var on = 1;
        try
        {
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
        }
        catch (DllNotFoundException) { /* sin DWM: nos quedamos con la barra clara */ }
    }
}
