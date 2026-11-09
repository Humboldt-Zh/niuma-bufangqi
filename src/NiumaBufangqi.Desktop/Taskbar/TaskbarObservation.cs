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
    LayoutMeasured,
    // Complete geometry reported while UIA marks taskbar buttons offscreen.
    // It may confirm an existing measured layout, never establish a new one.
    LayoutOccluded
}

/// <summary>Diagnostic metadata contains fixed codes and timings, never UI text or salary data.</summary>
internal sealed record TaskbarObservation(TaskbarObservationKind Kind, TaskbarSnapshot? Snapshot = null,
    string Reason = "", long CaptureMilliseconds = 0, long Sequence = 0);
