using System.Drawing;
using NiumaBufangqi.Desktop.Taskbar;

internal static class EntryElevationTests
{
    internal static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("A visible popup replaces the embedded entry only after it is ready", () =>
        {
            var log = new List<string>();
            var embedded = new Embedded(log);
            var popup = new Popup(log);
            var coordinator = new EntryElevationCoordinator(embedded, popup);
            var allocated = embedded.Slot;
            coordinator.Synchronize(true);
            Sequence(log, "popup.show", "embedded.hide");
            Equal(EntryElevationMode.Raised, coordinator.Mode);
            Assert(!embedded.Visible && popup.Visible, "Exactly the raised surface should be visible.");
            Equal(allocated, embedded.Slot);
            log.Clear();
            coordinator.Synchronize(false);
            Sequence(log, "embedded.restore", "popup.hide");
            Equal(EntryElevationMode.Embedded, coordinator.Mode);
            Assert(embedded.Visible && !popup.Visible, "Closing must restore the embedded entry before removing the popup.");
        });

        yield return ("Repeated layout refreshes never briefly re-show a raised child", () =>
        {
            var log = new List<string>();
            var embedded = new Embedded(log);
            var popup = new Popup(log);
            var coordinator = new EntryElevationCoordinator(embedded, popup);
            coordinator.Synchronize(true);
            log.Clear();
            for (var i = 0; i < 100; i++) coordinator.Synchronize(true);
            Assert(log.All(item => item is "popup.show" or "embedded.hide"), "A refresh must not expose both entries or hide a valid popup.");
            Assert(!embedded.Visible && popup.Visible, "The raised surface must remain visible through refreshes.");
        });

        yield return ("Popup creation failure restores the embedded entry and a later refresh can retry", () =>
        {
            var log = new List<string>();
            var embedded = new Embedded(log);
            var popup = new Popup(log);
            var coordinator = new EntryElevationCoordinator(embedded, popup);
            coordinator.Synchronize(true);
            popup.ShowSucceeds = false;
            log.Clear();
            coordinator.Synchronize(true);
            Sequence(log, "popup.show", "embedded.restore", "popup.hide");
            Equal(EntryElevationMode.Embedded, coordinator.Mode);
            Assert(embedded.Visible && !popup.Visible, "A failed refresh must leave the ordinary entry usable.");
            popup.ShowSucceeds = true;
            coordinator.Synchronize(true);
            Equal(EntryElevationMode.Raised, coordinator.Mode);
            Assert(!embedded.Visible && popup.Visible, "Failure must not permanently prevent elevation.");
        });

        yield return ("Native context loss removes the popup until a valid new slot is available", () =>
        {
            var log = new List<string>();
            var embedded = new Embedded(log);
            var popup = new Popup(log);
            var coordinator = new EntryElevationCoordinator(embedded, popup);
            coordinator.Synchronize(true);
            embedded.Available = false;
            var showsBeforeLoss = popup.ShowCalls;
            coordinator.Synchronize(true);
            coordinator.Synchronize(true);
            Equal(EntryElevationMode.Unavailable, coordinator.Mode);
            Equal(showsBeforeLoss, popup.ShowCalls);
            Assert(!embedded.Visible && !popup.Visible, "No native slot must mean no floating overlay.");
            embedded.Slot = new EntrySlot(new Rectangle(-1890, 984, 244, 60), 120, false);
            embedded.Available = true;
            coordinator.Synchronize(true);
            Equal(embedded.Slot, popup.LastSlot);
            Equal(EntryElevationMode.Raised, coordinator.Mode);
            Assert(popup.Visible, "Fresh native geometry should recover elevation.");
        });

        yield return ("Elevation preserves the held slot through UIA gaps but obeys reliable no-space evidence", () =>
        {
            var log = new List<string>();
            var embedded = new Embedded(log) { Available = false };
            var popup = new Popup(log);
            var coordinator = new EntryElevationCoordinator(embedded, popup);
            var cache = new TaskbarLayoutCache();
            var taskbar = new Rectangle(0, 1000, 1920, 48);
            var weather = new Rectangle(0, 1000, 120, 48);
            var handle = new IntPtr(101);
            var epoch = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);
            TaskbarObservation Measured(double seconds, int contentLeft, long sequence) =>
                new(TaskbarObservationKind.LayoutMeasured,
                    new TaskbarSnapshot(handle, taskbar, weather, contentLeft, 96, epoch.AddSeconds(seconds)), Sequence: sequence);
            void Observe(TaskbarObservation observation, double seconds, int width = 180)
            {
                var placement = cache.ResolvePlacement(observation, handle, taskbar, 96, true, true,
                    epoch.AddSeconds(seconds), width, 110);
                embedded.Available = placement.HasValue;
                if (placement is { } p)
                    embedded.Slot = new EntrySlot(new Rectangle(taskbar.Left + p.Bounds.Left,
                        taskbar.Top + p.Bounds.Top, p.Bounds.Width, p.Bounds.Height), 96, p.Compact);
                coordinator.Synchronize(true);
            }
            Observe(Measured(0, 800, 1), 0);
            var original = popup.LastSlot;
            foreach (var second in new[] { 1d, 5d, 60d, 600d })
            {
                Observe(new(TaskbarObservationKind.LayoutUnknown, Sequence: (long)second + 1), second, 1000);
                Equal(original, popup.LastSlot);
                Assert(popup.Visible && !embedded.Visible, "An accessibility outage must not hide or enlarge an owned native slot.");
            }
            Observe(Measured(601, 200, 1000), 601);
            Equal(EntryElevationMode.Unavailable, coordinator.Mode);
            Assert(!popup.Visible, "A new reliable no-space measurement must remove the popup.");
            var attempts = popup.ShowCalls;
            Observe(new(TaskbarObservationKind.LayoutUnknown, Sequence: 1001), 660);
            Equal(attempts, popup.ShowCalls);
            Observe(Measured(661, 800, 1002), 661);
            Assert(popup.Visible && !embedded.Visible, "A new safe measurement should restore the raised entry.");
        });

        yield return ("A popup exception restores both surfaces before returning the original failure", () =>
        {
            var log = new List<string>();
            var embedded = new Embedded(log);
            var popup = new Popup(log);
            var coordinator = new EntryElevationCoordinator(embedded, popup);
            coordinator.Synchronize(true);
            var failure = new InvalidOperationException("simulated popup failure");
            popup.OnShow = _ => throw failure;
            log.Clear();
            var caught = Throws<InvalidOperationException>(() => coordinator.Synchronize(true));
            Assert(ReferenceEquals(failure, caught), "Native failure identity should not be hidden by normal recovery.");
            Sequence(log, "popup.show", "embedded.restore", "popup.hide");
            Assert(embedded.Visible && !popup.Visible, "An exception cannot strand the entry between surfaces.");
            Equal(EntryElevationMode.Embedded, coordinator.Mode);
        });

        yield return ("A child suppression failure also removes the already shown popup", () =>
        {
            var log = new List<string>();
            var embedded = new Embedded(log);
            var popup = new Popup(log);
            var coordinator = new EntryElevationCoordinator(embedded, popup);
            embedded.OnSet = suppressed => { if (suppressed) throw new InvalidOperationException("hide failed"); };
            Throws<InvalidOperationException>(() => coordinator.Synchronize(true));
            Assert(embedded.Visible && !popup.Visible, "The show-before-hide transaction must roll back if suppression fails.");
            Equal(EntryElevationMode.Embedded, coordinator.Mode);
        });

        yield return ("Popup cleanup is attempted even when restoring the embedded surface fails", () =>
        {
            var log = new List<string>();
            var embedded = new Embedded(log);
            var popup = new Popup(log);
            var coordinator = new EntryElevationCoordinator(embedded, popup);
            coordinator.Synchronize(true);
            embedded.OnSet = suppressed => { if (!suppressed) throw new InvalidOperationException("restore failed"); };
            Throws<AggregateException>(() => coordinator.Synchronize(false));
            Assert(!popup.Visible && log.Contains("popup.hide"), "A failing child operation must not skip popup removal.");
        });

        yield return ("A close requested during popup creation is drained before synchronization returns", () =>
        {
            var log = new List<string>();
            var embedded = new Embedded(log);
            var popup = new Popup(log);
            var coordinator = new EntryElevationCoordinator(embedded, popup);
            popup.OnShow = _ => { popup.OnShow = null; coordinator.Synchronize(false); };
            coordinator.Synchronize(true);
            Equal(EntryElevationMode.Embedded, coordinator.Mode);
            Assert(embedded.Visible && !popup.Visible, "A native reentrant close cannot be lost behind the in-flight open.");
            coordinator.Synchronize(true);
            Assert(popup.Visible && !embedded.Visible, "The reentrant request must not wedge subsequent openings.");
        });

        yield return ("Identical reentrant allocation and show notifications settle without repeated native work", () =>
        {
            var log = new List<string>();
            var embedded = new Embedded(log);
            var popup = new Popup(log);
            var coordinator = new EntryElevationCoordinator(embedded, popup);
            embedded.OnRead = () => coordinator.Synchronize(true);
            embedded.OnSet = _ => coordinator.Synchronize(true);
            popup.OnShow = _ => coordinator.Synchronize(true);
            coordinator.Synchronize(true);
            Equal(1, popup.ShowCalls);
            Equal(2, embedded.ReadCalls);
            Equal(EntryElevationMode.Raised, coordinator.Mode);
            Assert(popup.Visible && !embedded.Visible, "Duplicate native callbacks must settle on one raised entry.");
        });

        yield return ("A changed slot arriving during show is applied after the in-flight request", () =>
        {
            var log = new List<string>();
            var embedded = new Embedded(log);
            var popup = new Popup(log);
            var coordinator = new EntryElevationCoordinator(embedded, popup);
            var oldSlot = embedded.Slot;
            var newSlot = new EntrySlot(new Rectangle(-2000, -72, 270, 72), 144, true);
            popup.OnShow = _ => { popup.OnShow = null; embedded.Slot = newSlot; coordinator.Synchronize(true); };
            coordinator.Synchronize(true);
            Equal(2, popup.ShowCalls);
            Assert(popup.ShownSlots.SequenceEqual([oldSlot, newSlot]), "Only allocated slots may be sent to the popup, in event order.");
            Equal(newSlot, popup.LastSlot);
            Assert(popup.Visible && !embedded.Visible, "The updated slot must remain elevated without flashing the child.");
        });

        yield return ("Disposal during popup creation cannot resurrect the popup after cleanup", () =>
        {
            var log = new List<string>();
            var embedded = new Embedded(log);
            var popup = new Popup(log);
            var coordinator = new EntryElevationCoordinator(embedded, popup);
            // The fake deliberately finishes showing after invoking its callback.
            popup.OnShow = _ => coordinator.Dispose();
            coordinator.Synchronize(true);
            Equal(EntryElevationMode.Unavailable, coordinator.Mode);
            Assert(embedded.Visible && !popup.Visible, "A native show finishing after disposal must be rolled back.");
            var calls = log.Count;
            coordinator.Synchronize(true);
            coordinator.Dispose();
            Equal(calls, log.Count);
        });

        yield return ("Disposal while querying the slot never touches a popup that may already be closed", () =>
        {
            var log = new List<string>();
            var embedded = new Embedded(log);
            var popup = new Popup(log);
            var coordinator = new EntryElevationCoordinator(embedded, popup);
            embedded.OnRead = () => coordinator.Dispose();
            coordinator.Synchronize(true);
            Equal(0, popup.ShowCalls);
            Equal(EntryElevationMode.Unavailable, coordinator.Mode);
            Assert(embedded.Visible && !popup.Visible, "The disposed coordinator must leave the ordinary entry available.");
        });

        yield return ("Disposing a raised coordinator restores the child once and ignores further requests", () =>
        {
            var log = new List<string>();
            var embedded = new Embedded(log);
            var popup = new Popup(log);
            var coordinator = new EntryElevationCoordinator(embedded, popup);
            coordinator.Synchronize(true);
            log.Clear();
            coordinator.Dispose();
            Sequence(log, "embedded.restore", "popup.hide");
            Assert(embedded.Visible && !popup.Visible, "Disposal must leave no detached floating surface behind.");
            coordinator.Dispose();
            coordinator.Synchronize(true);
            Sequence(log, "embedded.restore", "popup.hide");
        });

        yield return ("Raised geometry uses physical DPI and stays inside the exact allocated slot", () =>
        {
            Equal(new Rectangle(14, 1003, 210, 42), EntryElevationPlacement.Calculate(new(new Rectangle(12, 1000, 214, 48), 96, false)));
            foreach (var dpi in new[] { 96, 120, 144, 192 })
            {
                int Px(int dip) => (dip * dpi + 48) / 96;
                var slot = new EntrySlot(new Rectangle(-2560, -Px(48), Px(214), Px(48)), dpi, false);
                var raised = EntryElevationPlacement.Calculate(slot);
                Inside(slot.Bounds, raised);
                Assert(raised.Height >= Px(36), "The inset must retain enough height for the normal two-line presentation.");
                Assert(raised.Width < slot.Bounds.Width, "The raised card should leave a horizontal breathing margin.");
            }
        });

        yield return ("Compact and very narrow allocated slots are never shrunk to an empty rectangle", () =>
        {
            foreach (var dpi in new[] { 96, 120, 144, 192 })
            {
                var height = (32 * dpi + 48) / 96;
                foreach (var width in new[] { 1, 2, 3, 30 })
                {
                    var slot = new EntrySlot(new Rectangle(-20, 1000, width, height), dpi, true);
                    var raised = EntryElevationPlacement.Calculate(slot);
                    Inside(slot.Bounds, raised);
                    Equal(slot.Bounds.Top, raised.Top);
                    Equal(slot.Bounds.Height, raised.Height);
                }
            }
            var barelyTall = EntryElevationPlacement.Calculate(new(new Rectangle(20, 30, 100, 38), 96, false));
            Equal(36, barelyTall.Height);
        });

        yield return ("Invalid or overflowing slot geometry cannot become an elevated desktop overlay", () =>
        {
            var malformed = new[]
            {
                new EntrySlot(Rectangle.Empty, 96, false),
                new EntrySlot(new Rectangle(0, 0, 100, 48), 0, false),
                new EntrySlot(new Rectangle(0, 0, 100, 48), -1, false),
                new EntrySlot(new Rectangle(0, 0, -1, 48), 96, false),
                new EntrySlot(new Rectangle(0, 0, 100, -1), 96, false),
                new EntrySlot(new Rectangle(int.MaxValue - 1, 0, 10, 48), 96, false),
                new EntrySlot(new Rectangle(0, int.MaxValue - 1, 100, 48), 96, false)
            };
            foreach (var slot in malformed) Equal(Rectangle.Empty, EntryElevationPlacement.Calculate(slot));
            var log = new List<string>();
            var embedded = new Embedded(log) { Slot = malformed[^1] };
            var popup = new Popup(log);
            var coordinator = new EntryElevationCoordinator(embedded, popup);
            coordinator.Synchronize(true);
            Equal(0, popup.ShowCalls);
            Equal(EntryElevationMode.Unavailable, coordinator.Mode);
            var extreme = new EntrySlot(new Rectangle(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue), int.MaxValue, false);
            Inside(extreme.Bounds, EntryElevationPlacement.Calculate(extreme));
        });
    }

    private sealed class Embedded(List<string> log) : IEmbeddedEntry
    {
        internal EntrySlot Slot = new(new Rectangle(12, 1000, 214, 48), 96, false);
        internal bool Available = true;
        internal bool Suppressed;
        internal int ReadCalls;
        internal Action? OnRead;
        internal Action<bool>? OnSet;
        internal bool Visible => Available && !Suppressed;
        public bool TryGetAllocatedSlot(out EntrySlot slot)
        {
            ReadCalls++;
            OnRead?.Invoke();
            slot = Slot;
            return Available;
        }
        public void SetSuppressed(bool suppressed)
        {
            log.Add(suppressed ? "embedded.hide" : "embedded.restore");
            Suppressed = suppressed;
            OnSet?.Invoke(suppressed);
        }
    }

    private sealed class Popup(List<string> log) : IElevatedEntry
    {
        internal bool ShowSucceeds = true;
        internal bool Visible;
        internal int ShowCalls;
        internal EntrySlot LastSlot;
        internal readonly List<EntrySlot> ShownSlots = [];
        internal Action<EntrySlot>? OnShow;
        public bool TryShow(EntrySlot slot)
        {
            log.Add("popup.show");
            ShowCalls++;
            LastSlot = slot;
            ShownSlots.Add(slot);
            OnShow?.Invoke(slot);
            Visible = ShowSucceeds;
            return ShowSucceeds;
        }
        public void Hide() { log.Add("popup.hide"); Visible = false; }
    }

    private static void Sequence(List<string> actual, params string[] expected) =>
        Assert(actual.SequenceEqual(expected), "Expected " + string.Join(", ", expected) + "; got " + string.Join(", ", actual));
    private static void Inside(Rectangle slot, Rectangle raised) =>
        Assert(raised.Width > 0 && raised.Height > 0 && raised.Left >= slot.Left && raised.Top >= slot.Top
            && (long)raised.Left + raised.Width <= (long)slot.Left + slot.Width
            && (long)raised.Top + raised.Height <= (long)slot.Top + slot.Height, "Raised bounds must be a nonempty subset of the allocated slot.");
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual) =>
        Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; got {actual}.");
}
