using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace NiumaBufangqi.Desktop.Taskbar;

internal sealed record TaskbarSnapshot(IntPtr Handle, Rectangle Bounds, Rectangle? LeadingReserved,
    int ContentLeft, int Dpi, DateTime CapturedAt);

/// <summary>Explorer UI Automation is queried on a background MTA task, never on the UI thread.</summary>
internal sealed class TaskbarMonitor : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private TaskbarSnapshot? latest;
    private int disposed;
    internal TaskbarSnapshot? Latest => Volatile.Read(ref latest);

    internal TaskbarMonitor() => worker = Task.Run(WatchAsync);

    private async Task WatchAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            TaskbarSnapshot? snapshot = null;
            try { snapshot = Capture(); }
            catch (Exception exception) when (exception is ElementNotAvailableException or COMException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
            { /* Explorer can replace its UIA tree while it is being read. Discard the partial result. */ }
            Volatile.Write(ref latest, snapshot);
            try { await Task.Delay(1000, stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private TaskbarSnapshot? Capture()
    {
        var handle = TaskbarNative.FindWindow("Shell_TrayWnd", null);
        if (handle == IntPtr.Zero || !TaskbarNative.IsWindowVisible(handle) || !TaskbarNative.GetWindowRect(handle, out var native)) return null;
        var bounds = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.Height > bounds.Width) return null;
        var root = AutomationElement.FromHandle(handle);
        var budget = 160;
        var frame = FindFrame(root, 0, ref budget);
        Rectangle? leading = null;
        int firstButton = bounds.Right;
        if (frame is not null)
        {
            var button = TreeWalker.RawViewWalker.GetFirstChild(frame);
            var count = 0;
            while (button is not null && count++ < 100 && !stop.IsCancellationRequested)
            {
                var current = button.Current;
                if (current.ControlType == ControlType.Button && !current.IsOffscreen && TryBounds(current.BoundingRectangle, bounds, out var rect))
                {
                    if (current.AutomationId == "WidgetsButton") leading = rect;
                    else firstButton = Math.Min(firstButton, rect.Left);
                }
                button = TreeWalker.RawViewWalker.GetNextSibling(button);
            }
        }
        else
        {
            // Classic taskbars often have no free leading gap. Only use a positively
            // measured gap inside their task-list region; otherwise stay tray-only.
            var rebar = TaskbarNative.FindWindowEx(handle, IntPtr.Zero, "ReBarWindow32", null);
            var taskList = rebar == IntPtr.Zero ? IntPtr.Zero : TaskbarNative.FindWindowEx(rebar, IntPtr.Zero, "MSTaskSwWClass", null);
            if (taskList == IntPtr.Zero) taskList = TaskbarNative.FindWindowEx(handle, IntPtr.Zero, "MSTaskSwWClass", null);
            if (taskList == IntPtr.Zero || !TaskbarNative.GetWindowRect(taskList, out var listNative)) return null;
            var listBounds = Rectangle.FromLTRB(listNative.Left, listNative.Top, listNative.Right, listNative.Bottom);
            if (listBounds.Width <= 0 || !bounds.IntersectsWith(listBounds)) return null;
            leading = Rectangle.FromLTRB(bounds.Left, bounds.Top, Math.Max(bounds.Left, listBounds.Left), bounds.Bottom);
            budget = 100;
            CollectFirstButton(AutomationElement.FromHandle(taskList), bounds, 0, ref budget, ref firstButton);
        }
        if (firstButton >= bounds.Right) return null;
        var dpi = Math.Max(96, (int)TaskbarNative.GetDpiForWindow(handle));
        return new TaskbarSnapshot(handle, bounds, leading, firstButton, dpi, DateTime.UtcNow);
    }

    private AutomationElement? FindFrame(AutomationElement node, int depth, ref int budget)
    {
        if (--budget < 0 || stop.IsCancellationRequested) return null;
        if (node.Current.AutomationId == "TaskbarFrameRepeater") return node;
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
