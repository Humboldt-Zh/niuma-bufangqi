using System;

namespace NiumaBufangqi.Desktop.Taskbar;

internal readonly record struct EntryBackdropEnvironment(int WindowsBuild, bool HighContrast,
    bool TransparencyEnabled, bool CompositionEnabled, bool EnergySaver);

internal enum EntryBackdropMode { Acrylic, Solid, HighContrast }

internal readonly record struct EntryBackdropResult(EntryBackdropMode Mode, string Reason);

internal interface IEntryBackdropNative
{
    bool TryEnable(IntPtr handle, uint abgrTint);
    void Disable(IntPtr handle);
}

/// <summary>Material selection and native-failure rollback, independent of WPF and Explorer.</summary>
internal sealed class EntryBackdropController(IEntryBackdropNative native)
{
    // AccentPolicy uses ABGR, unlike WPF Color. This is the preview's 52% sage tint;
    // the independent WPF veil is a light surface, not a second copy of this tint.
    internal static readonly uint Tint = PackAbgr(133, 190, 207, 174);
    private IntPtr previousHandle;
    private EntryBackdropEnvironment? previousEnvironment;
    private EntryBackdropResult previousResult;

    internal static uint PackAbgr(byte alpha, byte red, byte green, byte blue) =>
        (uint)alpha << 24 | (uint)blue << 16 | (uint)green << 8 | red;

    internal EntryBackdropResult Apply(IntPtr handle, EntryBackdropEnvironment environment, bool force = false)
    {
        if (!force && handle == previousHandle && environment == previousEnvironment) return previousResult;
        if (previousHandle != IntPtr.Zero && previousHandle != handle) native.Disable(previousHandle);

        var result = Select(environment);
        if (handle == IntPtr.Zero) result = new(EntryBackdropMode.Solid, "no-handle");
        else if (result.Mode == EntryBackdropMode.Acrylic)
        {
            if (!native.TryEnable(handle, Tint))
            {
                native.Disable(handle);
                result = new(EntryBackdropMode.Solid, "native-unavailable");
            }
        }
        else native.Disable(handle);

        previousHandle = handle;
        previousEnvironment = environment;
        previousResult = result;
        return result;
    }

    internal void Release()
    {
        if (previousHandle != IntPtr.Zero) native.Disable(previousHandle);
        previousHandle = IntPtr.Zero;
        previousEnvironment = null;
    }

    private static EntryBackdropResult Select(EntryBackdropEnvironment environment)
    {
        if (environment.HighContrast) return new(EntryBackdropMode.HighContrast, "high-contrast");
        if (environment.WindowsBuild < 22000) return new(EntryBackdropMode.Solid, "unsupported-windows");
        if (!environment.TransparencyEnabled) return new(EntryBackdropMode.Solid, "transparency-disabled");
        if (!environment.CompositionEnabled) return new(EntryBackdropMode.Solid, "composition-disabled");
        if (environment.EnergySaver) return new(EntryBackdropMode.Solid, "energy-saver");
        return new(EntryBackdropMode.Acrylic, "accent-acrylic");
    }
}
