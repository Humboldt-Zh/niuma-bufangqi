using System.Drawing;
using NiumaBufangqi.Desktop.Taskbar;

// Pure replay tests: no window creation, UI Automation or calls to Explorer.
var bar = new Rectangle(0, 1000, 1920, 48);
var weather = new Rectangle(0, 1000, 120, 48);
var clock = new DateTime(2026, 11, 9, 20, 34, 4, DateTimeKind.Utc);
var nativeHandle = new IntPtr(101);
TaskbarSnapshot Sample(double seconds = 0, int contentLeft = 800, IntPtr? handle = null, Rectangle? bounds = null, int dpi = 96) =>
    new(handle ?? nativeHandle, bounds ?? bar, weather, contentLeft, dpi, clock.AddSeconds(seconds));
TaskbarObservation Measured(TaskbarSnapshot snapshot, long sequence = 0) => new(TaskbarObservationKind.LayoutMeasured, snapshot, Sequence: sequence);
TaskbarObservation Occluded(TaskbarSnapshot snapshot, long sequence = 0) => new(TaskbarObservationKind.LayoutOccluded, snapshot, Sequence: sequence);
TaskbarObservation Unknown(long sequence = 0) => new(TaskbarObservationKind.LayoutUnknown, Sequence: sequence);
TaskbarSnapshot? Resolve(TaskbarLayoutCache cache, TaskbarObservation? observation, double seconds,
    IntPtr? handle = null, Rectangle? bounds = null, int dpi = 96, bool visible = true, bool enabled = true) =>
    cache.Resolve(observation, handle ?? nativeHandle, bounds ?? bar, dpi, visible, enabled, clock.AddSeconds(seconds));
TaskbarPlacement? Place(TaskbarLayoutCache cache, TaskbarObservation? observation, double seconds,
    int normal = 180, int compact = 110, IntPtr? handle = null, Rectangle? bounds = null,
    int dpi = 96, bool visible = true, bool enabled = true) =>
    cache.ResolvePlacement(observation, handle ?? nativeHandle, bounds ?? bar, dpi, visible, enabled,
        clock.AddSeconds(seconds), normal, compact);
var tests = new (string Name, Action Run)[]
{
    ("The observed Edge-switch failure no longer turns missing samples into a hide", () =>
    {
        var cache = new TaskbarLayoutCache();
        var first = Place(cache, Measured(Sample(), 1), 0);
        Assert(first is not null, "A reliable first sample must allocate the entry.");
        // Actual failure timeline: 20:34:05 Unknown, 20:34:08 old hide, 20:34:13 recovery.
        foreach (var second in new[] { 1d, 4d, 8.9d })
            Equal(first, Place(cache, Unknown((long)second + 2), second));
        Equal(first, Place(cache, Measured(Sample(9), 20), 9));
        Equal("new-measured-layout", cache.Decision);
    }),
    ("Ten minutes of unavailable UIA retain exactly the last native slot", () =>
    {
        var cache = new TaskbarLayoutCache();
        var first = Place(cache, Measured(Sample(), 1), 0);
        foreach (var second in new[] { 1d, 4.001d, 15d, 60d, 120d, 600d })
        {
            Equal(first, Place(cache, Unknown((long)second + 2), second));
            Equal(false, cache.MeasurementUpdated);
            Equal("held-layout", cache.Decision);
        }
    }),
    ("A repeatedly replayed measured observation cannot expire an allocated entry", () =>
    {
        var cache = new TaskbarLayoutCache();
        var observation = Measured(Sample(), 10);
        var first = Place(cache, observation, 0);
        foreach (var second in new[] { .5d, 3d, 4.001d, 60d })
        {
            Equal(first, Place(cache, observation, second));
            Equal(false, cache.MeasurementUpdated);
        }
    }),
    ("Text growth during an observation outage cannot enlarge or move the drawn rectangle", () =>
    {
        var cache = new TaskbarLayoutCache();
        var first = Place(cache, Measured(Sample(), 1), 0);
        Equal(first, Place(cache, Unknown(2), 1, normal: 600, compact: 500));
        Equal(first, Place(cache, Unknown(3), 30, normal: 1000, compact: 900));
        var confirmed = Place(cache, Measured(Sample(31), 4), 31, normal: 200, compact: 110);
        Equal(new Rectangle(128, 0, 216, 48), Require(confirmed).Bounds);
    }),
    ("An unknown initial layout or offscreen-only geometry cannot invent an entry", () =>
    {
        var cache = new TaskbarLayoutCache();
        Equal<TaskbarPlacement?>(null, Place(cache, null, 0));
        Equal<TaskbarPlacement?>(null, Place(cache, Unknown(1), 1));
        Equal<TaskbarPlacement?>(null, Place(cache, Occluded(Sample(2), 2), 2));
        Equal<TaskbarPlacement?>(null, Place(cache, new(TaskbarObservationKind.LayoutMeasured, Sequence: 3), 3));
    }),
    ("Weak geometry differences do not erase the currently owned native slot", () =>
    {
        var cache = new TaskbarLayoutCache();
        var first = Place(cache, Measured(Sample(), 1), 0);
        Equal(first, Place(cache, Occluded(Sample(1, contentLeft: 790), 2), 1));
        Equal(first, Place(cache, Occluded(Sample(2) with { LeadingReserved = null }, 3), 2));
        Equal(first, Place(cache, Unknown(4), 60));
        var changed = Sample(61) with { LeadingReserved = new Rectangle(0, 1000, 130, 48) };
        Equal(138, Require(Place(cache, Measured(changed, 5), 61)).Bounds.Left);
    }),
    ("A new reliable no-space measurement hides immediately and old safe data cannot revive it", () =>
    {
        var cache = new TaskbarLayoutCache();
        Place(cache, Measured(Sample(), 1), 0);
        Equal<TaskbarPlacement?>(null, Place(cache, Measured(Sample(1, contentLeft: 200), 2), 1));
        Equal("no-safe-gap", cache.Decision);
        Equal<TaskbarPlacement?>(null, Place(cache, Unknown(3), 60));
        Equal("held-no-safe-gap", cache.Decision);
        Equal<TaskbarPlacement?>(null, Place(cache, Measured(Sample(60), 1), 60));
        Equal<TaskbarPlacement?>(null, Place(cache, Occluded(Sample(61), 4), 61));
        Assert(Place(cache, Measured(Sample(62), 5), 62) is not null, "Only new reliable recovery data should allocate again.");
    }),
    ("Native owner replacement invalidates placement before any provider recovery", () =>
    {
        var cache = new TaskbarLayoutCache();
        var replacement = new IntPtr(202);
        Place(cache, Measured(Sample(), 1), 0);
        Equal<TaskbarPlacement?>(null, Place(cache, Unknown(2), .2, handle: replacement));
        Equal<TaskbarPlacement?>(null, Place(cache, Measured(Sample(.3), 3), .3, handle: replacement));
        Equal<TaskbarPlacement?>(null, Place(cache, Occluded(Sample(.4, handle: replacement), 4), .4, handle: replacement));
        Assert(Place(cache, Measured(Sample(.5, handle: replacement), 5), .5, handle: replacement) is not null, "A fresh measurement must establish the replacement owner.");
    }),
    ("Native geometry and DPI changes cannot retain the previous slot", () =>
    {
        foreach (var changeDpi in new[] { false, true })
        {
            var cache = new TaskbarLayoutCache();
            var moved = new Rectangle(-1920, 1000, 1920, 48);
            Place(cache, Measured(Sample(), 1), 0);
            Equal<TaskbarPlacement?>(null, Place(cache, Unknown(2), 1, bounds: changeDpi ? bar : moved, dpi: changeDpi ? 144 : 96));
            Equal<TaskbarPlacement?>(null, Place(cache, Unknown(3), 2));
            Assert(Place(cache, Measured(Sample(3), 4), 3) is not null, "Recovery needs a new sample in the actual current context.");
        }
    }),
    ("Actual hiding, disabling and invalid native contexts clear both measurement and allocation", () =>
    {
        foreach (var scenario in Enumerable.Range(0, 5))
        {
            var cache = new TaskbarLayoutCache();
            Place(cache, Measured(Sample(), 1), 0);
            var hidden = scenario switch
            {
                0 => Place(cache, Unknown(2), 1, visible: false),
                1 => Place(cache, Unknown(2), 1, enabled: false),
                2 => Place(cache, Unknown(2), 1, handle: IntPtr.Zero),
                3 => Place(cache, Unknown(2), 1, bounds: Rectangle.Empty),
                _ => Place(cache, Unknown(2), 1, dpi: 0)
            };
            Equal<TaskbarPlacement?>(null, hidden);
            Equal<TaskbarPlacement?>(null, Place(cache, Unknown(3), 2));
            Assert(Place(cache, Measured(Sample(3), 4), 3) is not null, "A reliable sample can restore a genuinely valid native owner.");
        }
    }),
    ("An old background NativeUnavailable observation cannot contradict current visible native state", () =>
    {
        var cache = new TaskbarLayoutCache();
        var first = Place(cache, Measured(Sample(), 1), 0);
        Equal(first, Place(cache, new(TaskbarObservationKind.NativeUnavailable, Sequence: 2), 1));
        Equal(first, Place(cache, Unknown(3), 30));
    }),
    ("Malformed, future and stale provider samples are ignored rather than hiding the entry", () =>
    {
        var cache = new TaskbarLayoutCache();
        var first = Place(cache, Measured(Sample(), 1), 0);
        Equal(first, Place(cache, Measured(Sample(100), 2), 1));
        Equal(first, Place(cache, Measured(Sample(-10), 3), 2));
        Equal(first, Place(cache, new(TaskbarObservationKind.LayoutMeasured, Sequence: 4), 3));
        Equal(first, Place(cache, new((TaskbarObservationKind)999, Sequence: 5), 4));
        Equal(first, Place(cache, Measured(Sample(5, contentLeft: 200), -1), 5));
        Equal(first, Place(cache, Unknown(6), 60));
    }),
    ("Clock rollback does not control native visibility and newer sequence data can recover", () =>
    {
        var cache = new TaskbarLayoutCache();
        var first = Place(cache, Measured(Sample(), 10), 0);
        Equal(first, Place(cache, Unknown(11), -1));
        Equal(first, Place(cache, Measured(Sample(1), 12), -1));
        var confirmed = Sample(-.5);
        Equal(first, Place(cache, Measured(confirmed, 13), -.5));
        Equal(true, cache.MeasurementUpdated);
    }),
    ("A newer sequence can establish a changed native context after a system clock rollback", () =>
    {
        var cache = new TaskbarLayoutCache();
        Place(cache, Measured(Sample(), 1), 0);
        var moved = new Rectangle(0, 1001, 1920, 48);
        Equal<TaskbarPlacement?>(null, Place(cache, Unknown(2), 10, bounds: moved));
        var afterRollback = Sample(1, bounds: moved) with { LeadingReserved = new Rectangle(0, 1001, 130, 48) };
        var allocation = Place(cache, Measured(afterRollback, 3), 1, normal: 200, bounds: moved);
        Assert(allocation is not null, "Native identity and a new sequence must remain usable after the wall clock moves backwards.");
        Equal(new Rectangle(138, 0, 216, 48), Require(allocation).Bounds);
        Equal(true, cache.MeasurementUpdated);
        Equal(allocation, Place(cache, Measured(Sample(2, bounds: moved), 2), 2, bounds: moved));
    }),
    ("Provider sequence prevents late or replayed samples from overriding a newer decision", () =>
    {
        var cache = new TaskbarLayoutCache();
        var first = Place(cache, Measured(Sample(), 10), 0);
        Equal(first, Place(cache, Unknown(12), 1));
        Equal(first, Place(cache, Measured(Sample(2, contentLeft: 200), 11), 2));
        Equal<TaskbarPlacement?>(null, Place(cache, Measured(Sample(3, contentLeft: 200), 13), 3));
        Equal<TaskbarPlacement?>(null, Place(cache, Measured(Sample(3.1), 12), 3.1));
        Equal<TaskbarPlacement?>(null, Place(cache, Measured(Sample(3.2), 13), 3.2));
    }),
    ("Unsequenced replay tests use capture order without expiring the held slot", () =>
    {
        var cache = new TaskbarLayoutCache();
        var first = Place(cache, Measured(Sample()), 0);
        Equal(first, Place(cache, Measured(Sample()), 1, normal: 600, compact: 500));
        Equal(false, cache.MeasurementUpdated);
        var refreshed = Place(cache, Measured(Sample(.5)), 1, normal: 200, compact: 110);
        Equal(216, Require(refreshed).Bounds.Width);
    }),
    ("Explicit reset clears the slot while retained sequence rejects its old replay", () =>
    {
        var cache = new TaskbarLayoutCache();
        Place(cache, Measured(Sample(), 10), 0);
        cache.Reset();
        Equal<TaskbarPlacement?>(null, Place(cache, Measured(Sample(), 10), .1));
        Equal<TaskbarPlacement?>(null, Place(cache, Unknown(11), .2));
        Assert(Place(cache, Measured(Sample(.3), 12), .3) is not null, "New evidence can establish a slot after reset.");
    }),
    ("A measurement from another native context cannot erase this context's valid entry", () =>
    {
        var cache = new TaskbarLayoutCache();
        var first = Place(cache, Measured(Sample(), 1), 0);
        Equal(first, Place(cache, Measured(Sample(1, handle: new IntPtr(202)), 2), 1));
        Equal(first, Place(cache, Measured(Sample(2, bounds: new Rectangle(-1920, 1000, 1920, 48)), 3), 2));
        Equal(first, Place(cache, Measured(Sample(3, dpi: 144), 4), 3));
    }),
    ("Weak malformed geometry does not erase a prior confirmed placement", () =>
    {
        var cache = new TaskbarLayoutCache();
        var first = Place(cache, Measured(Sample(), 1), 0);
        Equal(first, Place(cache, new(TaskbarObservationKind.LayoutOccluded, Sequence: 2), 1));
        Equal(first, Place(cache, Occluded(Sample(-20), 3), 2));
        Equal(first, Place(cache, Occluded(Sample(20), 4), 3));
        Equal(first, Place(cache, Unknown(5), 60));
    }),
    ("A snapshot that was never allocated cannot invent a drawn placement during an outage", () =>
    {
        var cache = new TaskbarLayoutCache();
        Assert(Resolve(cache, Measured(Sample(), 1), 0) is not null, "This helper records only a measurement.");
        Equal<TaskbarPlacement?>(null, Place(cache, Unknown(2), 1));
        Equal("no-allocated-slot", cache.Decision);
        Assert(Place(cache, Measured(Sample(2), 3), 2) is not null, "A newly accepted measurement can allocate the actual slot.");
    }),
    ("Agreeing fresh occluded geometry may update only an established complete slot", () =>
    {
        var cache = new TaskbarLayoutCache();
        var first = Place(cache, Measured(Sample(), 1), 0);
        foreach (var second in Enumerable.Range(1, 15))
            Equal(first, Place(cache, Occluded(Sample(second), second + 1), second));
        Equal("confirmed-occluded-layout", cache.Decision);
        Equal(first, Place(cache, Unknown(30), 60));
    }),
    ("An overlay-sized accessibility rectangle cannot masquerade as a taskbar button", () =>
    {
        Assert(TaskbarLayout.IsButtonWithinTaskbar(new Rectangle(600, 1000, 48, 48), bar), "A genuine taskbar button should be accepted.");
        Assert(TaskbarLayout.IsButtonWithinTaskbar(new Rectangle(600, 999, 48, 50), bar), "Subpixel rounding should have a bounded tolerance.");
        Assert(!TaskbarLayout.IsButtonWithinTaskbar(new Rectangle(0, 0, 1920, 1048), bar), "A Start overlay extending above the bar is not button geometry.");
        Assert(!TaskbarLayout.IsButtonWithinTaskbar(new Rectangle(1900, 1000, 100, 48), bar), "Geometry extending beyond the bar must not create a false free gap.");
    }),
    ("Entry uses the measured taskbar gap beside weather", () =>
    {
        var value = Require(TaskbarLayout.PlaceAdaptive(bar, weather, 800, 180, 110, 96));
        Equal(false, value.Compact); Equal(new Rectangle(128, 0, 196, 48), value.Bounds);
        Assert(bar.Left + value.Bounds.Right < 800, "The taskbar entry must not cover the first app button.");
    }),
    ("A narrow measured gap switches to compact text without becoming a floating card", () =>
    {
        var value = Require(TaskbarLayout.PlaceAdaptive(bar, weather, 347, 180, 110, 96));
        Equal(true, value.Compact); Equal(new Rectangle(128, 0, 126, 48), value.Bounds);
    }),
    ("Insufficient space returns no taskbar placement", () =>
    {
        Equal<TaskbarPlacement?>(null, TaskbarLayout.PlaceAdaptive(bar, weather, 200, 180, 110, 96));
    }),
    ("Short taskbars use a single line", () =>
    {
        var value = Require(TaskbarLayout.PlaceAdaptive(new Rectangle(0, 1000, 1920, 32), null, 800, 180, 110, 96));
        Equal(true, value.Compact); Equal(32, value.Bounds.Height);
    }),
    ("Negative-coordinate monitors and supported DPI scales stay inside the taskbar gap", () =>
    {
        foreach (var dpi in new[] { 96, 120, 144, 192 })
        {
            int Px(int value) => (value * dpi + 48) / 96;
            var taskbar = new Rectangle(-2560, -48, 2560, Px(48));
            var reserved = new Rectangle(-2560, -48, Px(120), Px(48));
            var firstApp = -2560 + Px(800);
            var value = Require(TaskbarLayout.PlaceAdaptive(taskbar, reserved, firstApp, Px(180), Px(110), dpi));
            Assert(value.Bounds.Left >= 0 && value.Bounds.Right <= taskbar.Width && value.Bounds.Bottom <= taskbar.Height, "Child bounds must remain taskbar-relative.");
            Assert(taskbar.Left + value.Bounds.Left >= reserved.Right, "The weather button must remain unobscured.");
            Assert(taskbar.Left + value.Bounds.Right <= firstApp - Px(24), "The first app button needs its full safety margin.");
        }
    }),
    ("Unrelated weather bounds do not move this taskbar entry", () =>
    {
        var value = Require(TaskbarLayout.PlaceAdaptive(bar, new Rectangle(0, 0, 120, 48), 800, 180, 110, 96));
        Equal(12, value.Bounds.Left);
    }),
    ("Invalid taskbar geometry and vertical bars stay tray-only", () =>
    {
        Equal<TaskbarPlacement?>(null, TaskbarLayout.PlaceAdaptive(Rectangle.Empty, null, 800, 180, 110, 96));
        Equal<TaskbarPlacement?>(null, TaskbarLayout.PlaceAdaptive(bar, weather, 800, 180, 110, 0));
        Equal<TaskbarPlacement?>(null, TaskbarLayout.PlaceAdaptive(new Rectangle(0, 0, 48, 1080), null, 40, 180, 110, 96));
        Equal<Rectangle?>(null, TaskbarLayout.Place(bar, weather, bar.Left, 180, 96));
        Equal<Rectangle?>(null, TaskbarLayout.Place(bar, weather, bar.Right + 1, 180, 96));
    }),
    ("Extreme text widths cannot overflow into apparent free space", () =>
    {
        Equal<Rectangle?>(null, TaskbarLayout.Place(bar, weather, 800, int.MaxValue, 96));
        Equal<Rectangle?>(null, TaskbarLayout.Place(bar, weather, 800, 180, int.MaxValue));
    })
};
var failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception exception) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + exception.Message); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} taskbar data/geometry tests passed.");
return failed == 0 ? 0 : 1;

static TaskbarPlacement Require(TaskbarPlacement? value) => value ?? throw new InvalidOperationException("Expected a safe taskbar placement.");
static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; got {actual}."); }

