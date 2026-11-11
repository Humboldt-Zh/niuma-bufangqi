using System;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NiumaBufangqi.Core;

namespace NiumaBufangqi.Desktop.Taskbar;

/// <summary>A non-activating child of Explorer. It never falls back to a desktop overlay.</summary>
internal sealed class TaskbarEntry : IDisposable, IEmbeddedEntry
{
    private readonly Action togglePanel;
    private readonly Action openMenu;
    private readonly ToolTip tooltip = new();
    private readonly System.Windows.Forms.Timer placementTimer = new() { Interval = 500 };
    private readonly TaskbarMonitor monitor = new();
    private readonly TaskbarLayoutCache layoutCache = new();
    private Surface? surface;
    private TaskbarPresentation presentation = new("牛马不放器", "牛马不放器", "点击设置薪资");
    private TaskbarPalette palette = TaskbarPalette.Read();
    private Rectangle screenBounds;
    private Rectangle taskbarBounds;
    private int allocatedDpi;
    private IntPtr parent;
    private Size surfaceSize;
    private int dpi = 96;
    private bool compact;
    private bool mouseInside;
    private bool panelVisible;
    private bool attached;
    private bool suppressed;
    private bool started;
    private bool enabled = true;
    private bool disposed;
    private bool refreshing;
    private string? diagnosticState;
    private long lastObservationSequence = -1;
    private long lastObservationTick;
    private string? lastObservationReason;
    private bool observationStallReported;
    private long lastHealthTick;

    internal TaskbarEntry(Action togglePanel, Action openMenu)
    {
        this.togglePanel = togglePanel;
        this.openMenu = openMenu;
        placementTimer.Tick += (_, _) => RefreshPlacement();
    }

    internal event EventHandler? AttachmentChanged;
    internal event EventHandler? PlacementChecked;
    internal event Action<string>? Diagnostic;
    internal IntPtr ParentHwnd => attached ? parent : IntPtr.Zero;
    internal TaskbarPresentation Presentation => presentation;
    internal bool IsPointerOverEntry => TryGetScreenBounds(out var bounds) && bounds.Contains(Cursor.Position);

    internal void Start()
    {
        if (disposed || started) return;
        started = true;
        RefreshPlacement();
        placementTimer.Start();
    }

    internal void Update(AppState state, SalarySnapshot? snapshot)
    {
        if (disposed) return;
        enabled = state.TaskbarEntryVisible;
        presentation = TaskbarPresentation.Create(state, snapshot);
        if (surface is { IsDisposed: false })
            tooltip.SetToolTip(surface, $"牛马不放器\n{presentation.Amount} · {presentation.Subtitle}\n点击展开或收起，右键打开菜单");
        if (started) RefreshPlacement();
    }

    internal void SetPanelVisible(bool visible)
    {
        if (disposed || panelVisible == visible) return;
        panelVisible = visible;
        if (!suppressed) RenderSurface();
    }

    bool IEmbeddedEntry.TryGetAllocatedSlot(out EntrySlot slot) => TryGetAllocatedSlot(out slot);
    void IEmbeddedEntry.SetSuppressed(bool value) => SetSuppressed(value);

    internal bool TryGetAllocatedSlot(out EntrySlot slot)
    {
        slot = default;
        // A hidden child can still own its slot while the popup is showing. The
        // native taskbar identity/geometry, not the child's visibility, validates it.
        if (disposed || !enabled || !attached || surface is null || surface.IsDisposed || !surface.IsHandleCreated
            || !TaskbarNative.IsWindow(surface.Handle) || TaskbarNative.GetParent(surface.Handle) != parent
            || TaskbarNative.FindWindow("Shell_TrayWnd", null) != parent || !TaskbarNative.IsWindow(parent)
            || !TaskbarNative.IsWindowVisible(parent) || !TaskbarNative.GetWindowRect(parent, out var actual)
            || Math.Max(96, (int)TaskbarNative.GetDpiForWindow(parent)) != allocatedDpi) return false;
        var bounds = Rectangle.FromLTRB(actual.Left, actual.Top, actual.Right, actual.Bottom);
        var visiblePart = Rectangle.Intersect(Screen.FromHandle(parent).Bounds, bounds);
        if (bounds != taskbarBounds || visiblePart.Height < Math.Max(16, 24 * allocatedDpi / 96)
            || screenBounds.Width <= 0 || screenBounds.Height <= 0) return false;
        slot = new(screenBounds, allocatedDpi, compact);
        return true;
    }

    internal void SetSuppressed(bool value)
    {
        if (disposed || suppressed == value) return;
        suppressed = value;
        if (surface is not { IsDisposed: false, IsHandleCreated: true } || !TaskbarNative.IsWindow(surface.Handle)) return;
        if (suppressed)
        {
            tooltip.Hide(surface);
            TaskbarNative.ShowWindow(surface.Handle, TaskbarNative.SwHide);
            if (TaskbarNative.IsWindowVisible(surface.Handle))
                throw new InvalidOperationException("The embedded entry could not be suppressed.");
        }
        else if (TryGetAllocatedSlot(out _))
        {
            if (!RenderSurface()) { HideSurface("restore-render-failed"); return; }
            TaskbarNative.ShowWindow(surface.Handle, TaskbarNative.SwShowNoActivate);
        }
        else TaskbarNative.ShowWindow(surface.Handle, TaskbarNative.SwHide);
    }

    internal bool TryGetScreenBounds(out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (suppressed || !TryGetAllocatedSlot(out _) || surface is null || !surface.IsHandleCreated || surface.IsDisposed
            || !TaskbarNative.IsWindow(surface.Handle) || TaskbarNative.GetParent(surface.Handle) != parent
            || !TaskbarNative.IsWindowVisible(surface.Handle) || !TaskbarNative.IsWindowVisible(parent)
            || !TaskbarNative.GetWindowRect(surface.Handle, out var rect)) return false;
        bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    internal void RefreshPlacement()
    {
        if (disposed || !started || refreshing) return;
        refreshing = true;
        try
        {
            if (!enabled) { layoutCache.Reset(); HideSurface("disabled"); return; }
            // Always validate native identity and geometry independently of UIA. A
            // transient accessibility-tree rebuild must not prevent the normal
            // redraw/z-order restoration below from running.
            var currentHandle = TaskbarNative.FindWindow("Shell_TrayWnd", null);
            if (currentHandle == IntPtr.Zero || !TaskbarNative.IsWindow(currentHandle)
                || !TaskbarNative.IsWindowVisible(currentHandle)
                || !TaskbarNative.GetWindowRect(currentHandle, out var actual))
            { layoutCache.Reset(); HideSurface("native-unavailable"); return; }
            var actualBounds = Rectangle.FromLTRB(actual.Left, actual.Top, actual.Right, actual.Bottom);
            var currentDpi = Math.Max(96, (int)TaskbarNative.GetDpiForWindow(currentHandle));
            var visiblePart = Rectangle.Intersect(Screen.FromHandle(currentHandle).Bounds, actualBounds);
            if (visiblePart.Height < Math.Max(16, 24 * currentDpi / 96))
            { layoutCache.Reset(); HideSurface("native-hidden"); return; }
            var observation = monitor.Latest;
            ObserveDiagnostics(observation);
            dpi = currentDpi;
            palette = TaskbarPalette.Read();
            var widths = TaskbarRenderer.Measure(presentation, dpi);
            var placement = layoutCache.ResolvePlacement(observation, currentHandle, actualBounds, currentDpi,
                nativeVisible: true, enabled: true, DateTime.UtcNow, widths.Normal, widths.Compact);
            if (placement is null) { HideSurface(layoutCache.Decision); return; }
            EnsureSurface();
            if (surface is null || !Attach(currentHandle)) { HideSurface("attach-failed"); return; }
            var target = placement.Value.Bounds;
            compact = placement.Value.Compact;
            surfaceSize = target.Size;
            if (!TaskbarNative.SetWindowPos(surface.Handle, IntPtr.Zero, target.X, target.Y, target.Width, target.Height,
                TaskbarNative.SwpNoActivate | TaskbarNative.SwpNoSendChanging)) { HideSurface("position-failed"); return; }
            if (!suppressed && !RenderSurface()) { HideSurface("render-failed"); return; }
            TaskbarNative.ShowWindow(surface.Handle, suppressed ? TaskbarNative.SwHide : TaskbarNative.SwShowNoActivate);
            var newBounds = new Rectangle(actualBounds.Left + target.X, actualBounds.Top + target.Y, target.Width, target.Height);
            var changed = !attached || parent != currentHandle || screenBounds != newBounds
                || taskbarBounds != actualBounds || allocatedDpi != dpi;
            parent = currentHandle; screenBounds = newBounds; taskbarBounds = actualBounds; allocatedDpi = dpi; attached = true;
            ReportDiagnostic("shown-native-entry");
            var healthTick = Stopwatch.GetTimestamp();
            if (lastHealthTick == 0 || Stopwatch.GetElapsedTime(lastHealthTick, healthTick) >= TimeSpan.FromSeconds(10))
            {
                lastHealthTick = healthTick;
                Diagnostic?.Invoke(suppressed ? "health-embedded-suppressed"
                    : TryGetScreenBounds(out _) ? "health-native-visible" : "health-native-not-visible");
            }
            if (changed) AttachmentChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or ExternalException)
        { HideSurface("refresh-error"); }
        finally
        {
            refreshing = false;
            PlacementChecked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void EnsureSurface()
    {
        if (surface is { IsDisposed: false, Disposing: false })
        {
            if (!surface.IsHandleCreated || TaskbarNative.IsWindow(surface.Handle)) return;
            surface.Dispose();
        }
        surface = new Surface();
        surface.MouseUp += (_, args) =>
        {
            if (args.Button == MouseButtons.Left) togglePanel();
            else if (args.Button == MouseButtons.Right) openMenu();
        };
        surface.MouseEnter += (_, _) => { mouseInside = true; if (!suppressed) RenderSurface(); };
        surface.MouseLeave += (_, _) => { mouseInside = false; if (!suppressed) RenderSurface(); };
        tooltip.SetToolTip(surface, "牛马不放器 · 点击展开或收起，右键打开菜单");
        // Complete WinForms' top-level creation/load path before changing the native
        // parent. A layered 1px surface has no bitmap yet and is parked off-screen.
        surface.Show();
        TaskbarNative.ShowWindow(surface.Handle, TaskbarNative.SwHide);
    }

    private bool Attach(IntPtr taskbar)
    {
        if (surface is null) return false;
        var handle = surface.Handle;
        if (TaskbarNative.GetParent(handle) == taskbar) return true;
        TaskbarNative.ShowWindow(handle, TaskbarNative.SwHide);
        var style = TaskbarNative.GetWindowLong(handle, TaskbarNative.GwlStyle);
        TaskbarNative.SetWindowLong(handle, TaskbarNative.GwlStyle, (style | TaskbarNative.WsChild) & ~TaskbarNative.WsPopup);
        var extended = TaskbarNative.GetWindowLong(handle, TaskbarNative.GwlExStyle);
        TaskbarNative.SetWindowLong(handle, TaskbarNative.GwlExStyle,
            (extended | TaskbarNative.WsExNoActivate | TaskbarNative.WsExToolWindow | TaskbarNative.WsExLayered) & ~TaskbarNative.WsExTopmost);
        TaskbarNative.SetParent(handle, taskbar);
        return TaskbarNative.GetParent(handle) == taskbar;
    }

    private void HideSurface(string reason)
    {
        if (surface is { IsDisposed: false, IsHandleCreated: true }) TaskbarNative.ShowWindow(surface.Handle, TaskbarNative.SwHide);
        var changed = attached;
        attached = false; parent = IntPtr.Zero; screenBounds = Rectangle.Empty; taskbarBounds = Rectangle.Empty; allocatedDpi = 0;
        ReportDiagnostic("hidden-" + reason);
        if (changed) AttachmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReportDiagnostic(string state)
    {
        if (diagnosticState == state) return;
        diagnosticState = state;
        Diagnostic?.Invoke(state);
    }

    private void ObserveDiagnostics(TaskbarObservation? observation)
    {
        var tick = Stopwatch.GetTimestamp();
        var sequence = observation?.Sequence ?? 0;
        if (sequence != lastObservationSequence)
        {
            lastObservationSequence = sequence;
            lastObservationTick = tick;
            observationStallReported = false;
            var reason = observation is null ? "not-yet-captured" : observation.Kind + "/" + observation.Reason;
            if (reason != lastObservationReason || observation is { CaptureMilliseconds: >= 1500 })
                Diagnostic?.Invoke($"uia-observation reason={reason} elapsed_ms={observation?.CaptureMilliseconds ?? 0} sequence={sequence}");
            lastObservationReason = reason;
        }
        else if (!observationStallReported && lastObservationTick != 0 && Stopwatch.GetElapsedTime(lastObservationTick, tick) >= TimeSpan.FromSeconds(3))
        {
            observationStallReported = true;
            Diagnostic?.Invoke($"uia-observation-stalled sequence={sequence}");
        }
    }

    private bool RenderSurface()
    {
        if (disposed || surface is null || surface.IsDisposed || !surface.IsHandleCreated
            || !TaskbarNative.IsWindow(surface.Handle) || surfaceSize.Width <= 0 || surfaceSize.Height <= 0) return false;
        using var bitmap = TaskbarRenderer.Render(presentation, surfaceSize, dpi, compact, mouseInside || panelVisible, palette);
        var screenDc = TaskbarNative.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return false;
        var memoryDc = TaskbarNative.CreateCompatibleDC(screenDc);
        if (memoryDc == IntPtr.Zero) { TaskbarNative.ReleaseDC(IntPtr.Zero, screenDc); return false; }
        IntPtr bitmapHandle = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
            previous = TaskbarNative.SelectObject(memoryDc, bitmapHandle);
            var size = new TaskbarNative.NativeSize { Width = surfaceSize.Width, Height = surfaceSize.Height };
            var source = new TaskbarNative.NativePoint();
            var blend = new TaskbarNative.BlendFunction { Alpha = 255, Format = TaskbarNative.AcSrcAlpha };
            return TaskbarNative.UpdateLayeredWindow(surface.Handle, IntPtr.Zero, IntPtr.Zero, ref size, memoryDc, ref source, 0, ref blend, TaskbarNative.UlwAlpha);
        }
        finally
        {
            if (previous != IntPtr.Zero) TaskbarNative.SelectObject(memoryDc, previous);
            if (bitmapHandle != IntPtr.Zero) TaskbarNative.DeleteObject(bitmapHandle);
            TaskbarNative.DeleteDC(memoryDc);
            TaskbarNative.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        placementTimer.Stop(); placementTimer.Dispose();
        monitor.Dispose(); tooltip.Dispose();
        layoutCache.Reset();
        surface?.Dispose(); surface = null;
        attached = false; parent = IntPtr.Zero; screenBounds = Rectangle.Empty; taskbarBounds = Rectangle.Empty; allocatedDpi = 0;
    }

    private sealed class Surface : Form
    {
        internal Surface()
        {
            Text = "牛马不放器任务栏入口";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(1, 1);
            Location = new Point(-32000, -32000);
            Cursor = Cursors.Hand;
            AccessibleName = "牛马不放器任务栏入口";
            AccessibleDescription = "点击打开新版面板，再次点击收起；右键打开菜单";
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= TaskbarNative.WsExNoActivate | TaskbarNative.WsExToolWindow | TaskbarNative.WsExLayered;
                parameters.ExStyle &= ~TaskbarNative.WsExTopmost;
                return parameters;
            }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == TaskbarNative.WmMouseActivate) { message.Result = new IntPtr(TaskbarNative.MaNoActivate); return; }
            base.WndProc(ref message);
        }
    }
}
