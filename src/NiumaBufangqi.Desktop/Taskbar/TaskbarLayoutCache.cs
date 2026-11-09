using System;
using System.Drawing;

namespace NiumaBufangqi.Desktop.Taskbar;

/// <summary>
/// A bounded lease on an actual measurement, scoped to one native taskbar identity.
/// Unknown reads never extend that lease or revive a disabled/replaced taskbar.
/// A measured layout can legitimately contain no usable gap; it still replaces the old measurement.
/// </summary>
internal sealed class TaskbarLayoutCache
{
    private static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(4);
    private TaskbarSnapshot? lastMeasurement;

    internal TaskbarSnapshot? Resolve(TaskbarObservation? observation, IntPtr currentHandle,
        Rectangle currentBounds, int currentDpi, bool nativeVisible, bool enabled, DateTime now)
    {
        if (!enabled || !nativeVisible || currentHandle == IntPtr.Zero || currentBounds.Width <= 0
            || currentBounds.Height <= 0 || currentDpi <= 0)
        {
            Reset();
            return null;
        }

        if (lastMeasurement is not null && !MatchesNative(lastMeasurement, currentHandle, currentBounds, currentDpi)) Reset();

        if (observation?.Kind == TaskbarObservationKind.NativeUnavailable)
        {
            Reset();
            return null;
        }

        if (observation?.Kind == TaskbarObservationKind.LayoutMeasured)
        {
            var snapshot = observation.Snapshot;
            if (snapshot is null || !MatchesNative(snapshot, currentHandle, currentBounds, currentDpi) || !IsFresh(snapshot, now))
            {
                Reset();
                return null;
            }
            lastMeasurement = snapshot;
            return snapshot;
        }

        if (observation is not null && observation.Kind != TaskbarObservationKind.LayoutUnknown)
        {
            Reset();
            return null;
        }

        if (lastMeasurement is not null && IsFresh(lastMeasurement, now)) return lastMeasurement;
        Reset();
        return null;
    }

    internal void Reset() => lastMeasurement = null;

    private static bool MatchesNative(TaskbarSnapshot snapshot, IntPtr handle, Rectangle bounds, int dpi) =>
        snapshot.Handle == handle && snapshot.Bounds == bounds && snapshot.Dpi == dpi;

    private static bool IsFresh(TaskbarSnapshot snapshot, DateTime now) =>
        now >= snapshot.CapturedAt && now - snapshot.CapturedAt <= MaximumAge;
}
