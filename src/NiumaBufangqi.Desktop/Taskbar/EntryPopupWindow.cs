using System;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Imaging = System.Windows.Media.Imaging;
using Media = System.Windows.Media;

namespace NiumaBufangqi.Desktop.Taskbar;

/// <summary>A non-activating owned Acrylic window used only while the panel is open.</summary>
internal sealed class EntryPopupWindow : Window, IElevatedEntry, IDisposable
{
    private const int WsExAppWindow = 0x00040000;
    private static readonly Media.SolidColorBrush SolidSurface = CreateBrush(255, 218, 229, 205);
    private static readonly Media.SolidColorBrush LightVeil = CreateBrush(71, 247, 250, 242);
    private readonly EntryBackdropController backdrop = new(new EntryBackdropNative());
    private readonly Window panel;
    private readonly Action<string> diagnostic;
    private readonly Image image;
    private readonly Border frame;
    private readonly DispatcherTimer layerRefreshTimer;
    private TaskbarPresentation presentation = new("牛马不放器", "牛马不放器", "点击设置薪资");
    private HwndSource? source;
    private EntryLayerMonitor? layerMonitor;
    private IntPtr energySaverNotification;
    private EntrySlot? lastSlot;
    private Drawing.Rectangle renderBounds;
    private bool mouseInside;
    private bool closed;
    private bool disposed;
    private bool backdropRefreshPending;
    private bool layerRefreshPending;
    private int layerChecksRemaining;
    private string? lastDiagnostic;
    private string? lastBackdropDiagnostic;

    internal EntryPopupWindow(Window owner, Action togglePanel, Action openMenu, Action<string> diagnostic)
    {
        panel = owner;
        this.diagnostic = diagnostic;
        layerRefreshTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(20) };
        layerRefreshTimer.Tick += OnLayerRefreshTick;
        Title = "牛马不放器浮起入口";
        Width = 1;
        Height = 1;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = SolidSurface;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Cursor = Cursors.Hand;

        image = new Image { Stretch = Media.Stretch.Fill, SnapsToDevicePixels = true };
        frame = new Border { Child = image, Background = SolidSurface, SnapsToDevicePixels = true };
        Content = frame;
        PanelWindowFrame.Configure(this, frame, state => diagnostic("entry-popup-" + state), Media.Color.FromRgb(217, 226, 209));
        // Accent Acrylic works with a zero client-frame extension in this WPF
        // host. Keep Chrome in agreement so later layout/theme updates do not
        // replace the material with the one-pixel glass margin used by panels.
        WindowChrome.GetWindowChrome(this)!.GlassFrameThickness = new Thickness(0);
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var extended = TaskbarNative.GetWindowLong(handle, TaskbarNative.GwlExStyle);
            TaskbarNative.SetWindowLong(handle, TaskbarNative.GwlExStyle,
                (extended | TaskbarNative.WsExNoActivate | TaskbarNative.WsExToolWindow) & ~WsExAppWindow);
            source = HwndSource.FromHwnd(handle);
            source?.AddHook(WindowProcedure);
            energySaverNotification = EntryBackdropNative.RegisterEnergySaverNotification(handle);
            layerMonitor = new EntryLayerMonitor(QueueLayerRefresh);
            layerMonitor.ObserveTaskbar(TaskbarNative.FindWindow("Shell_TrayWnd", null));
            RefreshBackdrop();
        };
        MouseUp += (_, args) =>
        {
            if (closed || disposed) return;
            if (args.ChangedButton == MouseButton.Left) { args.Handled = true; togglePanel(); }
            else if (args.ChangedButton == MouseButton.Right) { args.Handled = true; openMenu(); }
        };
        MouseEnter += (_, _) => { mouseInside = true; RenderVisible(); };
        MouseLeave += (_, _) => { mouseInside = false; RenderVisible(); };
    }

    internal bool IsPointerOverEntry => TryGetScreenBounds(out var bounds)
        && bounds.Contains(System.Windows.Forms.Cursor.Position);

    internal event EventHandler? LayerRefreshRequested;

    internal void Update(TaskbarPresentation value)
    {
        if (closed || disposed || presentation == value) return;
        presentation = value;
        RenderVisible(preservePictureOnFailure: false);
    }

    public bool TryShow(EntrySlot slot)
    {
        if (closed || disposed || !panel.IsVisible || panel.WindowState == WindowState.Minimized)
        {
            Hide();
            return false;
        }
        try
        {
            var panelHandle = new WindowInteropHelper(panel).Handle;
            var bounds = EntryElevationPlacement.Calculate(slot);
            if (panelHandle == IntPtr.Zero || !TaskbarNative.IsWindow(panelHandle)
                || !TaskbarNative.IsWindowVisible(panelHandle) || EntryPopupOcclusion.IsCloaked(panelHandle) || bounds.IsEmpty)
            {
                Hide();
                return false;
            }
            // The panel is shown before the owned window is created. Ownership
            // never points at the Explorer child and is not changed per click.
            if (Owner is null) Owner = panel;
            else if (!ReferenceEquals(Owner, panel)) { Hide(); return false; }

            var boundsChanged = bounds != renderBounds || lastSlot is not { } previous || previous.Dpi != slot.Dpi;
            renderBounds = bounds;
            lastSlot = slot;
            Width = bounds.Width * 96d / slot.Dpi;
            Height = bounds.Height * 96d / slot.Dpi;
            var handle = new WindowInteropHelper(this).EnsureHandle();
            if (!IsVisible) RefreshBackdrop(force: true);
            RenderPicture();
            var wasVisible = IsVisible && TaskbarNative.IsWindowVisible(handle);
            var extended = TaskbarNative.GetWindowLong(handle, TaskbarNative.GwlExStyle);
            var topmostMissing = (extended & TaskbarNative.WsExTopmost) == 0;
            var nativeBoundsMatch = TaskbarNative.GetWindowRect(handle, out var actual)
                && actual.Left == bounds.Left && actual.Top == bounds.Top
                && actual.Right == bounds.Right && actual.Bottom == bounds.Bottom;
            if (!wasVisible || boundsChanged || topmostMissing || !nativeBoundsMatch)
                NativePositioning.Place(this, bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            if (!IsVisible)
            {
                Show();
                // WPF's first Show can apply its initial size/position. Reapply
                // physical slot bounds without activating either window.
                NativePositioning.Place(this, bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            }
            else if (!TaskbarNative.IsWindowVisible(handle))
            {
                // WPF and native visibility can temporarily disagree after a
                // Shell operation; Show() alone would be a WPF no-op here.
                TaskbarNative.ShowWindow(handle, TaskbarNative.SwShowNoActivate);
            }
            extended = TaskbarNative.GetWindowLong(handle, TaskbarNative.GwlExStyle);
            var required = TaskbarNative.WsExNoActivate | TaskbarNative.WsExToolWindow | TaskbarNative.WsExTopmost;
            if ((extended & required) != required
                || (extended & (WsExAppWindow | TaskbarNative.WsExLayered)) != 0
                || !TryGetScreenBounds(out _))
            {
                Hide();
                Report("entry-popup-native-unavailable");
                return false;
            }
            var taskbar = TaskbarNative.FindWindow("Shell_TrayWnd", null);
            layerMonitor?.ObserveTaskbar(taskbar);
            if (!EntryPopupOcclusion.TryRestoreAboveTaskbar(handle, taskbar, bounds, out var restored))
            {
                Hide();
                Report("entry-popup-taskbar-layer-unavailable");
                return false;
            }
            if (restored) diagnostic("entry-popup-taskbar-layer-restored");
            Report("entry-popup-visible");
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or Win32Exception or ExternalException)
        {
            Hide();
            Report("entry-popup-show-failed-" + exception.GetType().Name);
            return false;
        }
    }

    internal bool TryGetScreenBounds(out Drawing.Rectangle bounds)
    {
        bounds = Drawing.Rectangle.Empty;
        if (closed || disposed || !IsVisible) return false;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !TaskbarNative.IsWindow(handle) || !TaskbarNative.IsWindowVisible(handle)
            || EntryPopupOcclusion.IsCloaked(handle)
            || !TaskbarNative.GetWindowRect(handle, out var native)) return false;
        bounds = Drawing.Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    public new void Hide()
    {
        if (closed || disposed || !IsVisible) return;
        try { base.Hide(); }
        catch (InvalidOperationException) { }
        mouseInside = false;
        layerRefreshTimer.Stop();
        layerChecksRemaining = 0;
        Report("entry-popup-hidden");
    }

    private void RenderVisible(bool preservePictureOnFailure = true)
    {
        if (closed || disposed || !IsVisible || lastSlot is null) return;
        try { RenderPicture(); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or Win32Exception or ExternalException)
        {
            // A hover failure may retain its frame, but a changed presentation
            // must never expose a previous unmasked amount after privacy changed.
            if (!preservePictureOnFailure) image.Source = null;
            Report("entry-popup-render-failed-" + exception.GetType().Name);
        }
    }

    private void RenderPicture()
    {
        if (closed || disposed || lastSlot is not { } slot || renderBounds.IsEmpty) return;
        var palette = ReadPalette();
        using var bitmap = TaskbarRenderer.Render(presentation, renderBounds.Size, slot.Dpi, slot.Compact, mouseInside, palette);
        var rectangle = new Drawing.Rectangle(Drawing.Point.Empty, bitmap.Size);
        var locked = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        var stride = checked(bitmap.Width * 4);
        var pixels = new byte[checked(stride * bitmap.Height)];
        try
        {
            for (var row = 0; row < bitmap.Height; row++)
                Marshal.Copy(IntPtr.Add(locked.Scan0, row * locked.Stride), pixels, row * stride, stride);
        }
        finally { bitmap.UnlockBits(locked); }
        var picture = Imaging.BitmapSource.Create(bitmap.Width, bitmap.Height, slot.Dpi, slot.Dpi,
            Media.PixelFormats.Pbgra32, null, pixels, stride);
        picture.Freeze();
        image.Source = picture;
    }

    private TaskbarPalette ReadPalette()
    {
        if (SystemParameters.HighContrast)
        {
            var ink = Drawing.SystemColors.WindowText;
            var foreground = Media.Color.FromRgb(ink.R, ink.G, ink.B);
            return new(Drawing.SystemColors.Window.GetBrightness() >= .5f, foreground, foreground, foreground);
        }
        // Foreground stays opaque and sharp; only the native backdrop and its
        // light veil are translucent. Darker secondary ink also works over dark
        // desktop content without sampling or capturing that content.
        return new(true, Media.Color.FromRgb(36, 48, 31), Media.Color.FromRgb(54, 69, 44), Media.Color.FromRgb(62, 83, 47));
    }

    private void RefreshBackdrop(bool force = false)
    {
        if (closed || disposed || source is not { IsDisposed: false }) return;
        var result = backdrop.Apply(source.Handle, EntryBackdropNative.ReadEnvironment(SystemParameters.HighContrast), force);
        source.CompositionTarget.BackgroundColor = Media.Colors.Transparent;
        Background = result.Mode switch
        {
            EntryBackdropMode.Acrylic => Media.Brushes.Transparent,
            EntryBackdropMode.HighContrast => SystemColors.WindowBrush,
            _ => SolidSurface
        };
        frame.Background = result.Mode == EntryBackdropMode.Acrylic ? LightVeil : Background;
        var state = "entry-backdrop-" + result.Reason;
        if (state != lastBackdropDiagnostic) { lastBackdropDiagnostic = state; diagnostic(state); }
    }

    private void QueueBackdropRefresh()
    {
        if (closed || disposed || backdropRefreshPending) return;
        backdropRefreshPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            backdropRefreshPending = false;
            if (closed || disposed) return;
            RefreshBackdrop(force: true);
            RenderVisible(preservePictureOnFailure: false);
        }));
    }

    private void QueueLayerRefresh()
    {
        if (closed || disposed || !IsVisible || !panel.IsVisible) return;
        // Foreground can be reported before Shell finishes raising its band.
        // Check immediately and in a short bounded burst afterwards, then stop. The normal
        // placement timer remains the fallback if no event was delivered.
        layerChecksRemaining = 6;
        if (layerRefreshPending) return;
        layerRefreshPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
        {
            layerRefreshPending = false;
            CheckLayerRefresh();
            if (layerChecksRemaining > 0) layerRefreshTimer.Start();
        }));
    }

    private void OnLayerRefreshTick(object? sender, EventArgs args) => CheckLayerRefresh();

    private void CheckLayerRefresh()
    {
        if (closed || disposed || !IsVisible || !panel.IsVisible || layerChecksRemaining <= 0)
        {
            layerChecksRemaining = 0;
            layerRefreshTimer.Stop();
            return;
        }
        layerChecksRemaining--;
        var taskbar = TaskbarNative.FindWindow("Shell_TrayWnd", null);
        if (EntryPopupOcclusion.IsCoveredByTaskbar(new WindowInteropHelper(this).Handle, taskbar, renderBounds))
            LayerRefreshRequested?.Invoke(this, EventArgs.Empty);
        if (layerChecksRemaining <= 0) layerRefreshTimer.Stop();
    }

    private IntPtr WindowProcedure(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x001A or 0x031A or 0x031E or 0x0218) QueueBackdropRefresh();
        if (message == TaskbarNative.WmMouseActivate)
        {
            handled = true;
            return new IntPtr(TaskbarNative.MaNoActivate);
        }
        return IntPtr.Zero;
    }

    private void Report(string state)
    {
        if (lastDiagnostic == state) return;
        lastDiagnostic = state;
        diagnostic(state);
    }

    protected override void OnClosed(EventArgs args)
    {
        closed = true;
        layerRefreshTimer.Stop();
        layerRefreshTimer.Tick -= OnLayerRefreshTick;
        layerMonitor?.Dispose();
        layerMonitor = null;
        EntryBackdropNative.UnregisterEnergySaverNotification(energySaverNotification);
        energySaverNotification = IntPtr.Zero;
        backdrop.Release();
        if (source is { IsDisposed: false }) source.RemoveHook(WindowProcedure);
        source = null;
        image.Source = null;
        base.OnClosed(args);
    }

    public void Dispose()
    {
        if (disposed) return;
        Hide();
        disposed = true;
        if (!closed) Close();
    }

    private static Media.SolidColorBrush CreateBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new Media.SolidColorBrush(Media.Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }
}
