using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using NiumaBufangqi.Core;
using Forms = System.Windows.Forms;

namespace NiumaBufangqi.Desktop;

internal static class NativePositioning
{
    private static readonly IntPtr Topmost = new(-1);
    private const uint NoActivate = 0x0010;

    public static void PlacePanel(Window panel, Forms.Screen screen, System.Drawing.Rectangle? entryBounds)
    {
        var placement = PanelPlacement.Calculate(screen.WorkingArea, entryBounds, ScaleFor(screen));
        Place(panel, placement.Left, placement.Top, placement.Width, placement.Height);
    }

    public static void Place(Window window, int x, int y, int width, int height)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        if (!SetWindowPos(handle, Topmost, x, y, width, height, NoActivate))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private static double ScaleFor(Forms.Screen screen)
    {
        var point = new NativePoint { X = screen.WorkingArea.Left + 1, Y = screen.WorkingArea.Top + 1 };
        var monitor = MonitorFromPoint(point, 2);
        return GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX / 96d : 1d;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
