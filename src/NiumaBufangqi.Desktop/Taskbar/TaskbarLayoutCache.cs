using System;
using System.Drawing;

namespace NiumaBufangqi.Desktop.Taskbar;

/// <summary>
/// A bounded lease on actual geometry, scoped to one native taskbar identity.
/// Unknown reads never extend that lease or revive a disabled/replaced taskbar.
/// Fresh occluded geometry can only confirm the complete slot of an established baseline.
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

        if (observation?.Kind == TaskbarObservationKind.LayoutOccluded)
        {
            var snapshot = observation.Snapshot;
            if (lastMeasurement is null || snapshot is null || now < lastMeasurement.CapturedAt
                || !MatchesNative(snapshot, currentHandle, currentBounds, currentDpi)
                || !IsFresh(snapshot, now) || !SameLayout(lastMeasurement, snapshot))
            {
                Reset();
                return null;
            }
            // This is a new, complete geometry confirmation. A changed slot is never
            // accepted merely because the provider marked its buttons offscreen.
            lastMeasurement = snapshot;
            return snapshot;
        }

        if (observation is not null && observation.Kind != TaskbarObservationKind.LayoutUnknown)
        {
            Reset();
            return null;
        }

        if (lastMeasurement is not null && IsFresh(lastMeasurement, now)) return lastMeasurement;
        // An expired baseline is not usable on its own. Keep only its slot identity
        // for a later fresh, matching geometry confirmation; Unknown cannot renew it.
        if (lastMeasurement is not null && now < lastMeasurement.CapturedAt) Reset();
        return null;
    }

    internal void Reset() => lastMeasurement = null;

    private static bool MatchesNative(TaskbarSnapshot snapshot, IntPtr handle, Rectangle bounds, int dpi) =>
        snapshot.Handle == handle && snapshot.Bounds == bounds && snapshot.Dpi == dpi;

    private static bool SameLayout(TaskbarSnapshot baseline, TaskbarSnapshot candidate) =>
        baseline.ContentLeft == candidate.ContentLeft && baseline.LeadingReserved == candidate.LeadingReserved;

    private static bool IsFresh(TaskbarSnapshot snapshot, DateTime now) =>
        now >= snapshot.CapturedAt && now - snapshot.CapturedAt <= MaximumAge;
}
