using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace NiumaBufangqi.Desktop.Taskbar;

/// <summary>Explorer UI Automation is queried on a background MTA task, never on the UI thread.</summary>
internal sealed class TaskbarMonitor : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private TaskbarObservation? latest;
    private AutomationElement? cachedFrame;
    private IntPtr cachedHandle;
    private Rectangle cachedBounds;
    private int cachedDpi;
    private long sequence;
    private int disposed;
    internal TaskbarObservation? Latest => Volatile.Read(ref latest);

    internal TaskbarMonitor() => worker = Task.Run(WatchAsync);

    private async Task WatchAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            var captureClock = Stopwatch.StartNew();
            TaskbarObservation observation;
            try { observation = Capture(); }
            catch (Exception exception) when (exception is ElementNotAvailableException or COMException or InvalidOperationException
                or ArgumentException or UnauthorizedAccessException or TimeoutException)
            {
                // A provider failure is missing layout data, not proof that the taskbar
                // disappeared or that its previously measured gap is no longer usable.
                InvalidateFrame();
                observation = Unknown(exception switch
                {
                    ElementNotAvailableException => "capture-element-unavailable",
                    COMException => "capture-com",
                    UnauthorizedAccessException => "capture-access-denied",
                    TimeoutException => "capture-timeout",
                    ArgumentException => "capture-invalid-argument",
                    _ => "capture-invalid-operation"
                });
            }
            captureClock.Stop();
            observation = observation with { CaptureMilliseconds = captureClock.ElapsedMilliseconds, Sequence = Interlocked.Increment(ref sequence) };
            Volatile.Write(ref latest, observation);
            try { await Task.Delay(1000, stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private TaskbarObservation Capture()
    {
        var handle = TaskbarNative.FindWindow("Shell_TrayWnd", null);
        if (handle == IntPtr.Zero) return NativeUnavailable("native-missing");
        if (!TaskbarNative.IsWindowVisible(handle)) return NativeUnavailable("native-hidden");
        if (!TaskbarNative.GetWindowRect(handle, out var native)) return NativeUnavailable("native-geometry-unavailable");
        var bounds = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.Height > bounds.Width)
            return NativeUnavailable("native-geometry-unsupported");
        var dpi = Math.Max(96, (int)TaskbarNative.GetDpiForWindow(handle));
        if (cachedHandle != handle || cachedBounds != bounds || cachedDpi != dpi)
        {
            InvalidateFrame();
            cachedHandle = handle; cachedBounds = bounds; cachedDpi = dpi;
        }
        if (stop.IsCancellationRequested) return Unknown("capture-cancelled");

        AutomationElement? frame = null;
        if (cachedFrame is not null)
        {
            var current = cachedFrame.Current;
            if (current.ProcessId != Environment.ProcessId && current.AutomationId == "TaskbarFrameRepeater") frame = cachedFrame;
            else cachedFrame = null;
        }
        var budget = 160;
        if (frame is null)
        {
            frame = FindFrame(AutomationElement.FromHandle(handle), 0, ref budget);
            if (frame is null && budget <= 0) return Unknown("frame-budget");
            if (stop.IsCancellationRequested) return Unknown("capture-cancelled");
            cachedFrame = frame;
        }
        Rectangle? leading = null;
        int firstButton = bounds.Right;
        var geometryOnly = false;
        if (frame is not null)
        {
            var button = TreeWalker.RawViewWalker.GetFirstChild(frame);
            var count = 0;
            while (button is not null && count++ < 100 && !stop.IsCancellationRequested)
            {
                var current = button.Current;
                if (current.ProcessId != Environment.ProcessId && current.ControlType == ControlType.Button
                    && TryBounds(current.BoundingRectangle, bounds, out var rect))
                {
                    // Start/Search overlays can change IsOffscreen while the button's
                    // native taskbar geometry remains intact. Keep that geometry as
                    // an explicitly weaker observation; the cache accepts it only
                    // when it agrees with a previous fully visible measurement.
                    if (!TaskbarLayout.IsButtonWithinTaskbar(rect, bounds))
                    {
                        cachedFrame = null;
                        return Unknown("invalid-button-geometry");
                    }
                    geometryOnly |= current.IsOffscreen;
                    if (current.AutomationId == "WidgetsButton") leading = rect;
                    else firstButton = Math.Min(firstButton, rect.Left);
                }
                button = TreeWalker.RawViewWalker.GetNextSibling(button);
            }
            if (stop.IsCancellationRequested) return Unknown("capture-cancelled");
            if (button is not null)
            {
                cachedFrame = null;
                return Unknown("button-budget");
            }
        }
        else
        {
            // Classic taskbars often have no free leading gap. Only use a positively
            // measured gap inside their task-list region; otherwise stay tray-only.
            var rebar = TaskbarNative.FindWindowEx(handle, IntPtr.Zero, "ReBarWindow32", null);
            var taskList = rebar == IntPtr.Zero ? IntPtr.Zero : TaskbarNative.FindWindowEx(rebar, IntPtr.Zero, "MSTaskSwWClass", null);
            if (taskList == IntPtr.Zero) taskList = TaskbarNative.FindWindowEx(handle, IntPtr.Zero, "MSTaskSwWClass", null);
            if (taskList == IntPtr.Zero || !TaskbarNative.GetWindowRect(taskList, out var listNative))
                return Unknown(rebar == IntPtr.Zero ? "no-frame" : "classic-missing");
            var listBounds = Rectangle.FromLTRB(listNative.Left, listNative.Top, listNative.Right, listNative.Bottom);
            if (listBounds.Width <= 0 || !bounds.IntersectsWith(listBounds))
                return Unknown("classic-invalid-geometry");
            leading = Rectangle.FromLTRB(bounds.Left, bounds.Top, Math.Max(bounds.Left, listBounds.Left), bounds.Bottom);
            budget = 100;
            CollectFirstButton(AutomationElement.FromHandle(taskList), bounds, 0, ref budget, ref firstButton);
            if (stop.IsCancellationRequested) return Unknown("capture-cancelled");
            if (budget <= 0) return Unknown("classic-button-budget");
        }
        if (firstButton >= bounds.Right)
        {
            cachedFrame = null;
            return Unknown("no-buttons");
        }
        return new TaskbarObservation(geometryOnly ? TaskbarObservationKind.LayoutOccluded : TaskbarObservationKind.LayoutMeasured,
            new TaskbarSnapshot(handle, bounds, leading, firstButton, dpi, DateTime.UtcNow),
            geometryOnly ? "occluded-geometry" : frame is null ? "measured-classic" : "measured-frame");
    }

    private AutomationElement? FindFrame(AutomationElement node, int depth, ref int budget)
    {
        if (--budget < 0 || stop.IsCancellationRequested) return null;
        var current = node.Current;
        // Our layered entry is itself an Explorer child. Traversing its UIA tree
        // would call back into our UI thread and add work unrelated to Explorer's layout.
        if (current.ProcessId == Environment.ProcessId) return null;
        if (current.AutomationId == "TaskbarFrameRepeater") return node;
        if (depth >= 5) return null;
        var child = TreeWalker.RawViewWalker.GetFirstChild(node);
        while (child is not null && budget > 0 && !stop.IsCancellationRequested)
        {
            var found = FindFrame(child, depth + 1, ref budget);
            if (found is not null) return found;
            child = TreeWalker.RawViewWalker.GetNextSibling(child);
        }
        return null;
    }

    private void CollectFirstButton(AutomationElement node, Rectangle taskbar, int depth, ref int budget, ref int firstButton)
    {
        if (--budget < 0 || stop.IsCancellationRequested) return;
        var current = node.Current;
        if (current.ProcessId == Environment.ProcessId) return;
        if (current.ControlType is { } type && (type == ControlType.Button || type == ControlType.ListItem)
            && !current.IsOffscreen && TryBounds(current.BoundingRectangle, taskbar, out var bounds))
            firstButton = Math.Min(firstButton, bounds.Left);
        if (depth >= 4) return;
        var child = TreeWalker.RawViewWalker.GetFirstChild(node);
        while (child is not null && budget > 0 && !stop.IsCancellationRequested)
        {
            CollectFirstButton(child, taskbar, depth + 1, ref budget, ref firstButton);
            child = TreeWalker.RawViewWalker.GetNextSibling(child);
        }
    }

    private static TaskbarObservation Unknown(string reason) => new(TaskbarObservationKind.LayoutUnknown, null, reason);

    private TaskbarObservation NativeUnavailable(string reason)
    {
        InvalidateFrame();
        return new TaskbarObservation(TaskbarObservationKind.NativeUnavailable, null, reason);
    }

    private void InvalidateFrame()
    {
        cachedFrame = null;
        cachedHandle = IntPtr.Zero;
        cachedBounds = Rectangle.Empty;
        cachedDpi = 0;
    }

    private static bool TryBounds(System.Windows.Rect value, Rectangle taskbar, out Rectangle result)
    {
        result = Rectangle.Empty;
        if (value.IsEmpty || !double.IsFinite(value.Left) || !double.IsFinite(value.Top) || !double.IsFinite(value.Right) || !double.IsFinite(value.Bottom)
            || value.Width <= 0 || value.Height <= 0 || value.Left < int.MinValue || value.Top < int.MinValue || value.Right > int.MaxValue || value.Bottom > int.MaxValue) return false;
        result = Rectangle.FromLTRB((int)Math.Floor(value.Left), (int)Math.Floor(value.Top), (int)Math.Ceiling(value.Right), (int)Math.Ceiling(value.Bottom));
        return taskbar.IntersectsWith(result);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        // A blocked provider must never hold up the WPF shutdown thread.
        _ = worker.ContinueWith(_ => stop.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
