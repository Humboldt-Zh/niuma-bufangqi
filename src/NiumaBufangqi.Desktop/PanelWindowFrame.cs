using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace NiumaBufangqi.Desktop;

/// <summary>Keeps the opaque WebView host inside a system-composed window frame.</summary>
internal static class PanelWindowFrame
{
    // Preview stroke: rgba(81, 98, 69, .32) over the panel's #fffef9 paper.
    private static readonly Color Stroke = Color.FromRgb(199, 204, 191);
    private const int WindowCornerPreference = 33;
    private const int BorderColor = 34;
    private const int RoundCorners = 2;
    private const int CompositionChanged = 0x031E;

    internal static void Configure(Window window, Border fallback, Action<string> diagnostic, Color? borderColor = null)
    {
        var stroke = borderColor ?? Stroke;
        // Preserve native frame styles for DWM without adding a draggable caption
        // or resize hit targets. A nonzero glass margin lets WindowChrome clear
        // its legacy window region, so Windows 11 can round the actual HWND.
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        fallback.BorderBrush = new SolidColorBrush(stroke);
        fallback.BorderThickness = new Thickness(1);
        HwndSource? source = null;
        string? lastDiagnostic = null;

        void ApplyNativeFrame()
        {
            var handle = new WindowInteropHelper(window).Handle;
            var borderApplied = false;
            var cornersRequested = false;
            if (handle != IntPtr.Zero && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                var corners = RoundCorners;
                cornersRequested = DwmSetWindowAttribute(handle, WindowCornerPreference, ref corners, sizeof(int)) >= 0;
                var color = stroke.R | stroke.G << 8 | stroke.B << 16;
                borderApplied = DwmSetWindowAttribute(handle, BorderColor, ref color, sizeof(int)) >= 0;
            }
            // Never paint a second inset stroke over a working system border.
            fallback.BorderThickness = new Thickness(borderApplied ? 0 : 1);
            var state = $"panel-frame border={(borderApplied ? "system" : "fallback")} corners={(cornersRequested ? "requested" : "unavailable")}";
            if (state != lastDiagnostic) { lastDiagnostic = state; diagnostic(state); }
        }

        IntPtr WindowProcedure(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == CompositionChanged) ApplyNativeFrame();
            return IntPtr.Zero;
        }

        window.SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.AddHook(WindowProcedure);
            ApplyNativeFrame();
        };
        window.Closed += (_, _) =>
        {
            if (source is { IsDisposed: false }) source.RemoveHook(WindowProcedure);
            source = null;
        };
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}
