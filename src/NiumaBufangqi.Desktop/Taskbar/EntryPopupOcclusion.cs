using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace NiumaBufangqi.Desktop.Taskbar;

/// <summary>
/// A topmost bit does not establish relative Z order. Explorer can raise the
/// taskbar above an already-topmost entry when another taskbar app is selected.
/// Repair only that Shell obstruction, leaving menus and other overlays alone.
/// </summary>
internal static class EntryPopupOcclusion
{
    private const uint NoMoveSizeActivateOrOwnerOrder = 0x0001 | 0x0002 | 0x0010 | 0x0200;

    internal static bool TryRestoreAboveTaskbar(IntPtr popup, IntPtr taskbar, Rectangle bounds, out bool restored)
    {
        restored = false;
        if (!IsCoveredByTaskbar(popup, taskbar, bounds)) return true;
        if (!TaskbarNative.SetWindowPos(popup, new IntPtr(-1), 0, 0, 0, 0, NoMoveSizeActivateOrOwnerOrder)) return false;
        restored = true;
        // Do not suppress the embedded child if the actual Shell obstruction
        // survived the repair, even when SetWindowPos itself returned success.
        return !IsCoveredByTaskbar(popup, taskbar, bounds);
    }

    internal static bool IsCoveredByTaskbar(IntPtr popup, IntPtr taskbar, Rectangle bounds)
    {
        if (popup == IntPtr.Zero || taskbar == IntPtr.Zero || popup == taskbar
            || bounds.Width <= 0 || bounds.Height <= 0
            || !TaskbarNative.IsWindow(popup) || !TaskbarNative.IsWindowVisible(popup)
            || !TaskbarNative.IsWindow(taskbar) || !TaskbarNative.IsWindowVisible(taskbar)
            || IsCloaked(taskbar) || !TaskbarNative.GetWindowRect(taskbar, out var native)) return false;
        var barBounds = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        var point = new TaskbarNative.NativePoint { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 };
        if (!barBounds.Contains(point.X, point.Y)) return false;
        var hit = WindowFromPoint(point);
        return hit != IntPtr.Zero && GetAncestor(hit, 2) == taskbar;
    }

    internal static bool IsCloaked(IntPtr handle) => handle != IntPtr.Zero
        && DwmGetWindowAttribute(handle, 14, out var cloaked, sizeof(int)) >= 0 && cloaked != 0;

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr WindowFromPoint(TaskbarNative.NativePoint point);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetAncestor(IntPtr handle, uint flags);
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int size);
}
