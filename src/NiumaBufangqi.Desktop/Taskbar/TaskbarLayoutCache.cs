using System;
using System.Drawing;

namespace NiumaBufangqi.Desktop.Taskbar;

/// <summary>
/// Keeps the last confirmed slot while its native owner remains unchanged. UIA is a
/// source of layout updates, not a visibility lease. Missing or old provider data
/// cannot hide an existing native entry or enlarge its last allocated rectangle.
/// </summary>
internal sealed class TaskbarLayoutCache
{
    private static readonly TimeSpan MaximumObservationAge = TimeSpan.FromSeconds(4);
    private TaskbarSnapshot? lastMeasurement;
    private TaskbarPlacement? lastPlacement;
    private bool allocationKnown;
    private bool contextKnown;
    private IntPtr contextHandle;
    private Rectangle contextBounds;
    private int contextDpi;
    private DateTime contextChangedAt = DateTime.MinValue;
    private DateTime lastUnsequencedMeasurement = DateTime.MinValue;
    private long highestSequence;

    internal bool MeasurementUpdated { get; private set; }
    internal string Decision { get; private set; } = "no-baseline";

    internal TaskbarSnapshot? Resolve(TaskbarObservation? observation, IntPtr currentHandle,
        Rectangle currentBounds, int currentDpi, bool nativeVisible, bool enabled, DateTime now)
    {
        MeasurementUpdated = false;
        if (!enabled || !nativeVisible || currentHandle == IntPtr.Zero || currentBounds.Width <= 0
            || currentBounds.Height <= 0 || currentDpi <= 0)
        {
            Invalidate(now);
            Decision = "no-native-context";
            return null;
        }

        if (contextKnown && (contextHandle != currentHandle || contextBounds != currentBounds || contextDpi != currentDpi))
            Invalidate(now);
        contextKnown = true;
        contextHandle = currentHandle;
        contextBounds = currentBounds;
        contextDpi = currentDpi;

        var newSequence = true;
        if (observation is { Sequence: > 0 })
        {
            newSequence = observation.Sequence > highestSequence;
            highestSequence = Math.Max(highestSequence, observation.Sequence);
        }
        else if (observation is not null && (observation.Sequence < 0 || highestSequence > 0))
            newSequence = false;

        if (newSequence && observation is not null
            && observation.Kind is TaskbarObservationKind.LayoutMeasured or TaskbarObservationKind.LayoutOccluded
            && observation.Snapshot is { } candidate
            && candidate.Handle == currentHandle && candidate.Bounds == currentBounds && candidate.Dpi == currentDpi
            && (observation.Sequence > 0 || candidate.CapturedAt >= contextChangedAt) && IsFresh(candidate, now)
            && (observation.Sequence > 0 || candidate.CapturedAt > lastUnsequencedMeasurement))
        {
            var trusted = observation.Kind == TaskbarObservationKind.LayoutMeasured
                || lastMeasurement is not null && SameLayout(lastMeasurement, candidate);
            if (trusted)
            {
                lastMeasurement = candidate;
                lastUnsequencedMeasurement = candidate.CapturedAt;
                MeasurementUpdated = true;
                Decision = observation.Kind == TaskbarObservationKind.LayoutMeasured
                    ? "new-measured-layout" : "confirmed-occluded-layout";
                return candidate;
            }
        }

        // NativeUnavailable is also just a background observation here. The fresh
        // nativeVisible/currentHandle arguments above are the visibility authority.
        Decision = lastMeasurement is null ? "no-baseline" : "held-layout";
        return lastMeasurement;
    }

    internal TaskbarPlacement? ResolvePlacement(TaskbarObservation? observation, IntPtr currentHandle,
        Rectangle currentBounds, int currentDpi, bool nativeVisible, bool enabled, DateTime now,
        int normalContentWidth, int compactContentWidth)
    {
        var snapshot = Resolve(observation, currentHandle, currentBounds, currentDpi, nativeVisible, enabled, now);
        if (snapshot is null) return null;
        if (MeasurementUpdated)
        {
            lastPlacement = TaskbarLayout.PlaceAdaptive(snapshot.Bounds, snapshot.LeadingReserved, snapshot.ContentLeft,
                normalContentWidth, compactContentWidth, snapshot.Dpi);
            allocationKnown = true;
            if (lastPlacement is null) Decision = "no-safe-gap";
        }
        else if (!allocationKnown) Decision = "no-allocated-slot";
        else if (lastPlacement is null) Decision = "held-no-safe-gap";
        return allocationKnown ? lastPlacement : null;
    }

    internal void Reset()
    {
        lastMeasurement = null;
        lastPlacement = null;
        allocationKnown = false;
        contextKnown = false;
        MeasurementUpdated = false;
        Decision = "no-baseline";
        // Keep provider ordering across native hide/show. The monitor continues its
        // sequence, so a replayed sample cannot become new merely because we hid.
    }

    private void Invalidate(DateTime now)
    {
        Reset();
        contextChangedAt = now;
        lastUnsequencedMeasurement = DateTime.MinValue;
    }

    private static bool SameLayout(TaskbarSnapshot baseline, TaskbarSnapshot candidate) =>
        baseline.ContentLeft == candidate.ContentLeft && baseline.LeadingReserved == candidate.LeadingReserved;

    private static bool IsFresh(TaskbarSnapshot snapshot, DateTime now) =>
        now >= snapshot.CapturedAt && now - snapshot.CapturedAt <= MaximumObservationAge;
}
