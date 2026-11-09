using System;
using System.Runtime.InteropServices;

namespace NiumaBufangqi.Desktop.Taskbar;

internal static class TaskbarNative
{
    internal const int GwlStyle = -16;
    internal const int GwlExStyle = -20;
    internal const int WsChild = 0x40000000;
    internal const int WsPopup = unchecked((int)0x80000000);
    internal const int WsExToolWindow = 0x00000080;
    internal const int WsExNoActivate = 0x08000000;
    internal const int WsExTopmost = 0x00000008;
    internal const int WsExLayered = 0x00080000;
    internal const int WmMouseActivate = 0x0021;
    internal const int MaNoActivate = 3;
    internal const int SwHide = 0;
    internal const int SwShowNoActivate = 4;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpNoSendChanging = 0x0400;
    internal const uint UlwAlpha = 2;
    internal const byte AcSrcAlpha = 1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindow(string? className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr handle, out NativeRect bounds);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")]
    internal static extern IntPtr GetParent(IntPtr handle);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll")]
    internal static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll")]
    internal static extern int SetWindowLong(IntPtr handle, int index, int value);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern IntPtr GetDC(IntPtr handle);
    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int ReleaseDC(IntPtr handle, IntPtr dc);
    [DllImport("gdi32.dll", ExactSpelling = true)]
    internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", ExactSpelling = true)]
    internal static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr item);
    [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateLayeredWindow(IntPtr handle, IntPtr destinationDc, IntPtr destinationPoint,
        ref NativeSize size, IntPtr sourceDc, ref NativePoint sourcePoint, int colorKey,
        ref BlendFunction blend, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect { internal int Left; internal int Top; internal int Right; internal int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint { internal int X; internal int Y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSize { internal int Width; internal int Height; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BlendFunction
    {
        internal byte Operation;
        internal byte Flags;
        internal byte Alpha;
        internal byte Format;
    }
}
