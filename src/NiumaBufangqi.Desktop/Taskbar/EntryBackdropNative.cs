using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NiumaBufangqi.Desktop.Taskbar;

/// <summary>
/// Compatibility Acrylic for a window that must never activate. DWM's transient
/// backdrop remained solid in the non-activating probe on Windows build 26300.
/// Keep this optional API isolated: every failure restores an opaque surface.
/// </summary>
internal sealed class EntryBackdropNative : IEntryBackdropNative
{
    private static readonly Guid EnergySaverStatus = new("E00958C0-C213-4ACE-AC77-FECCED2EEEA5");

    internal static IntPtr RegisterEnergySaverNotification(IntPtr handle)
    {
        var setting = EnergySaverStatus;
        return RegisterPowerSettingNotification(handle, ref setting, 0);
    }

    internal static void UnregisterEnergySaverNotification(IntPtr notification)
    {
        if (notification != IntPtr.Zero) UnregisterPowerSettingNotification(notification);
    }

    public bool TryEnable(IntPtr handle, uint abgrTint)
    {
        try
        {
            Disable(handle);
            var margins = new Margins();
            return DwmExtendFrameIntoClientArea(handle, ref margins) >= 0 && SetAccent(handle, 4, abgrTint);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or ExternalException)
        { return false; }
    }

    public void Disable(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        try
        {
            SetAccent(handle, 0, 0);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            {
                var none = 1;
                DwmSetWindowAttribute(handle, 38, ref none, sizeof(int));
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or ExternalException) { }
    }

    internal static EntryBackdropEnvironment ReadEnvironment(bool highContrast)
    {
        // A failed preference/composition query chooses a readable solid surface.
        var transparency = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            transparency = key?.GetValue("EnableTransparency") is int enabled && enabled == 1;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException) { }
        var composition = DwmIsCompositionEnabled(out var enabledComposition) >= 0 && enabledComposition;
        var energySaver = GetSystemPowerStatus(out var power) && power.SystemStatusFlag == 1;
        return new(Environment.OSVersion.Version.Build, highContrast, transparency, composition, energySaver);
    }

    private static bool SetAccent(IntPtr handle, int state, uint tint)
    {
        var policy = new AccentPolicy { State = state, Flags = 2, GradientColor = tint };
        var allocation = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
        try
        {
            Marshal.StructureToPtr(policy, allocation, false);
            var data = new CompositionAttributeData { Attribute = 19, Data = allocation, Size = (nuint)Marshal.SizeOf<AccentPolicy>() };
            return SetWindowCompositionAttribute(handle, ref data);
        }
        finally { Marshal.FreeHGlobal(allocation); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int State, Flags; public uint GradientColor; public int Animation; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionAttributeData { public int Attribute; public IntPtr Data; public nuint Size; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowCompositionAttribute(IntPtr handle, ref CompositionAttributeData data);
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr handle, ref Margins margins);
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr notification);
}
