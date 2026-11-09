using System;
using System.Drawing;

namespace NiumaBufangqi.Desktop.Taskbar;

internal sealed record TaskbarSnapshot(IntPtr Handle, Rectangle Bounds, Rectangle? LeadingReserved,
    int ContentLeft, int Dpi, DateTime CapturedAt);

/// <summary>An unreadable UIA tree is different from a missing native taskbar.</summary>
internal enum TaskbarObservationKind
{
    NativeUnavailable,
    LayoutUnknown,
    LayoutMeasured
}

internal sealed record TaskbarObservation(TaskbarObservationKind Kind, TaskbarSnapshot? Snapshot = null);
