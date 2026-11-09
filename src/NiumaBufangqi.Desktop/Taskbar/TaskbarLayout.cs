using System;
using System.Drawing;

namespace NiumaBufangqi.Desktop.Taskbar;

internal readonly record struct TaskbarPlacement(Rectangle Bounds, bool Compact);

/// <summary>Pure geometry: only use the measured gap before the first application button.</summary>
internal static class TaskbarLayout
{
    internal static TaskbarPlacement? PlaceAdaptive(Rectangle taskbar, Rectangle? leadingReserved,
        int contentLeft, int normalContentWidth, int compactContentWidth, int dpi)
    {
        if (dpi <= 0 || taskbar.Width <= 0 || taskbar.Height <= 0 || taskbar.Height > taskbar.Width) return null;
        if (taskbar.Height >= Scale(40, dpi) && Place(taskbar, leadingReserved, contentLeft, normalContentWidth, dpi) is { } normal)
            return new TaskbarPlacement(normal, false);
        return Place(taskbar, leadingReserved, contentLeft, compactContentWidth, dpi) is { } compact
            ? new TaskbarPlacement(compact, true) : null;
    }

    internal static Rectangle? Place(Rectangle taskbar, Rectangle? leadingReserved, int contentLeft, int textWidth, int dpi)
    {
        if (taskbar.Width <= 0 || taskbar.Height <= 0 || textWidth <= 0 || dpi <= 0) return null;
        long left = taskbar.Left, top = taskbar.Top;
        long right = left + taskbar.Width, bottom = top + taskbar.Height;
        if (contentLeft <= left || contentLeft > right) return null;
        long start = left + Scale(12, dpi);
        if (leadingReserved is { Width: > 0, Height: > 0 } leading)
        {
            long leadingRight = (long)leading.Left + leading.Width;
            long leadingBottom = (long)leading.Top + leading.Height;
            if (leading.Left < right && leadingRight > left && leading.Top < bottom && leadingBottom > top)
                start = leadingRight + Scale(8, dpi);
        }
        long width = (long)textWidth + Scale(16, dpi);
        // Leave a full half-button of breathing room if Explorer's centered list changes.
        long availableRight = (long)contentLeft - Scale(24, dpi);
        if (start < left || width > int.MaxValue || start + width > availableRight) return null;
        return new Rectangle((int)(start - left), 0, (int)width, taskbar.Height);
    }

    private static long Scale(int value, int dpi) => ((long)value * dpi + 48) / 96;
}
