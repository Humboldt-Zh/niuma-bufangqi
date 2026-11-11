using NiumaBufangqi.Desktop.Taskbar;

internal static class EntryBackdropTests
{
    private static readonly IntPtr Handle = new(101);
    private static readonly EntryBackdropEnvironment Supported = new(26300, false, true, true, false);

    internal static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("Acrylic tint preserves alpha and packs distinct red and blue channels in native ABGR order", () =>
        {
            Equal(0x85AECFBEu, EntryBackdropController.Tint);
            Equal(0x7F0000FFu, EntryBackdropController.PackAbgr(127, 255, 0, 0));
            Equal(0x7FFF0000u, EntryBackdropController.PackAbgr(127, 0, 0, 255));
        });

        yield return ("Repeated presentation refreshes do not reapply an unchanged Acrylic material", () =>
        {
            var native = new Native();
            var controller = new EntryBackdropController(native);
            var result = controller.Apply(Handle, Supported with { WindowsBuild = 22000 });
            Equal(new EntryBackdropResult(EntryBackdropMode.Acrylic, "accent-acrylic"), result);
            for (var i = 0; i < 100; i++)
                Equal(result, controller.Apply(Handle, Supported with { WindowsBuild = 22000 }));
            Sequence(native, ("enable", Handle));
            Equal(EntryBackdropController.Tint, native.LastTint);
        });

        yield return ("A rejected native material is disabled before solid fallback and retries only when forced", () =>
        {
            var native = new Native { EnableSucceeds = false };
            var controller = new EntryBackdropController(native);
            var result = controller.Apply(Handle, Supported);
            Equal(new EntryBackdropResult(EntryBackdropMode.Solid, "native-unavailable"), result);
            Assert(!native.Enabled, "A partial native application must be cleaned up before solid fallback.");
            Sequence(native, ("enable", Handle), ("disable", Handle));
            native.EnableSucceeds = true;
            Equal(result, controller.Apply(Handle, Supported));
            Equal(2, native.Calls.Count);
            Equal(EntryBackdropMode.Acrylic, controller.Apply(Handle, Supported, force: true).Mode);
            Sequence(native, ("enable", Handle), ("disable", Handle), ("enable", Handle));
            Assert(native.Enabled, "A later forced composition refresh must recover the real material.");
        });

        yield return ("An explicit composition refresh reapplies Acrylic without requiring a changed environment", () =>
        {
            var native = new Native();
            var controller = new EntryBackdropController(native);
            controller.Apply(Handle, Supported);
            controller.Apply(Handle, Supported, force: true);
            Sequence(native, ("enable", Handle), ("enable", Handle));
        });

        var fallbacks = new (string Reason, EntryBackdropEnvironment Environment, EntryBackdropMode Mode)[]
        {
            ("high-contrast", Supported with { HighContrast = true }, EntryBackdropMode.HighContrast),
            ("unsupported-windows", Supported with { WindowsBuild = 21999 }, EntryBackdropMode.Solid),
            ("transparency-disabled", Supported with { TransparencyEnabled = false }, EntryBackdropMode.Solid),
            ("composition-disabled", Supported with { CompositionEnabled = false }, EntryBackdropMode.Solid),
            ("energy-saver", Supported with { EnergySaver = true }, EntryBackdropMode.Solid)
        };
        foreach (var fallback in fallbacks)
        {
            yield return ($"Environment change {fallback.Reason} removes Acrylic and recovery restores it", () =>
            {
                var native = new Native();
                var controller = new EntryBackdropController(native);
                controller.Apply(Handle, Supported);
                var result = controller.Apply(Handle, fallback.Environment);
                Equal(new EntryBackdropResult(fallback.Mode, fallback.Reason), result);
                Assert(!native.Enabled, "A disallowed material must not remain active beneath the fallback surface.");
                for (var i = 0; i < 10; i++) Equal(result, controller.Apply(Handle, fallback.Environment));
                Sequence(native, ("enable", Handle), ("disable", Handle));
                Equal(EntryBackdropMode.Acrylic, controller.Apply(Handle, Supported).Mode);
                Sequence(native, ("enable", Handle), ("disable", Handle), ("enable", Handle));
            });
        }

        yield return ("High contrast has priority when several material restrictions are active", () =>
        {
            var native = new Native();
            var controller = new EntryBackdropController(native);
            var blocked = new EntryBackdropEnvironment(19045, true, false, false, true);
            Equal(new EntryBackdropResult(EntryBackdropMode.HighContrast, "high-contrast"), controller.Apply(Handle, blocked));
            Sequence(native, ("disable", Handle));
        });

        yield return ("A missing handle never reaches native composition and invalidates the previous handle", () =>
        {
            var native = new Native();
            var controller = new EntryBackdropController(native);
            var noHandle = new EntryBackdropResult(EntryBackdropMode.Solid, "no-handle");
            Equal(noHandle, controller.Apply(IntPtr.Zero, Supported));
            Equal(0, native.Calls.Count);
            controller.Apply(Handle, Supported);
            Equal(noHandle, controller.Apply(IntPtr.Zero, Supported));
            Sequence(native, ("enable", Handle), ("disable", Handle));
            Equal(EntryBackdropMode.Acrylic, controller.Apply(Handle, Supported).Mode);
            Sequence(native, ("enable", Handle), ("disable", Handle), ("enable", Handle));
        });

        yield return ("Replacing a native handle clears the old material before applying the new one", () =>
        {
            var native = new Native();
            var controller = new EntryBackdropController(native);
            var replacement = new IntPtr(202);
            controller.Apply(Handle, Supported);
            controller.Apply(replacement, Supported);
            Sequence(native, ("enable", Handle), ("disable", Handle), ("enable", replacement));
            native.Calls.Clear();
            native.EnableSucceeds = false;
            Equal(EntryBackdropMode.Solid, controller.Apply(Handle, Supported).Mode);
            Sequence(native, ("disable", replacement), ("enable", Handle), ("disable", Handle));
        });

        yield return ("Release is idempotent and a reused handle cannot inherit the old material cache", () =>
        {
            var native = new Native();
            var controller = new EntryBackdropController(native);
            controller.Release();
            Equal(0, native.Calls.Count);
            controller.Apply(Handle, Supported);
            controller.Release();
            controller.Release();
            Sequence(native, ("enable", Handle), ("disable", Handle));
            Assert(!native.Enabled, "Disposal must clear a previously enabled material.");
            Equal(EntryBackdropMode.Acrylic, controller.Apply(Handle, Supported).Mode);
            Sequence(native, ("enable", Handle), ("disable", Handle), ("enable", Handle));
        });
    }

    private sealed class Native : IEntryBackdropNative
    {
        internal readonly List<(string Kind, IntPtr Handle)> Calls = [];
        internal bool EnableSucceeds = true;
        internal bool Enabled;
        internal uint LastTint;

        public bool TryEnable(IntPtr handle, uint abgrTint)
        {
            Calls.Add(("enable", handle));
            LastTint = abgrTint;
            // Model an adapter that changes native state before reporting failure.
            Enabled = true;
            return EnableSucceeds;
        }

        public void Disable(IntPtr handle)
        {
            Calls.Add(("disable", handle));
            Enabled = false;
        }
    }

    private static void Sequence(Native native, params (string Kind, IntPtr Handle)[] expected) =>
        Assert(native.Calls.SequenceEqual(expected), "Expected " + string.Join(", ", expected) + "; got " + string.Join(", ", native.Calls));
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual) =>
        Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; got {actual}.");
}
