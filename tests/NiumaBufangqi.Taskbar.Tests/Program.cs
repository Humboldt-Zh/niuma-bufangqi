using System.Drawing;
using NiumaBufangqi.Desktop.Taskbar;

// These tests link only pure data/geometry. They never instantiate a window or query Explorer.
var bar = new Rectangle(0, 1000, 1920, 48);
var weather = new Rectangle(0, 1000, 120, 48);
var clock = new DateTime(2026, 11, 9, 8, 0, 0, DateTimeKind.Utc);
var nativeHandle = new IntPtr(101);
TaskbarSnapshot Sample(double capturedSeconds = 0, int contentLeft = 800, IntPtr? handle = null, Rectangle? bounds = null, int dpi = 96) =>
    new(handle ?? nativeHandle, bounds ?? bar, weather, contentLeft, dpi, clock.AddSeconds(capturedSeconds));
TaskbarObservation Measured(TaskbarSnapshot snapshot) => new(TaskbarObservationKind.LayoutMeasured, snapshot);
TaskbarSnapshot? Resolve(TaskbarLayoutCache cache, TaskbarObservation? observation, double seconds,
    IntPtr? handle = null, Rectangle? bounds = null, int dpi = 96, bool visible = true, bool enabled = true) =>
    cache.Resolve(observation, handle ?? nativeHandle, bounds ?? bar, dpi, visible, enabled, clock.AddSeconds(seconds));
var tests = new (string Name, Action Run)[]
{
    ("A brief unknown layout preserves the existing entry until a fresh measurement arrives", () =>
    {
        var cache = new TaskbarLayoutCache();
        var first = Sample();
        var recovered = Sample(1.5);
        var states = new[]
        {
            Resolve(cache, Measured(first), 0),
            Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), .5),
            Resolve(cache, null, 1),
            Resolve(cache, Measured(recovered), 1.5),
            Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), 4.5)
        };
        Assert(states.All(snapshot => snapshot is not null), "A short provider transition must not hide a valid entry.");
        Assert(states.All(snapshot => TaskbarLayout.PlaceAdaptive(snapshot!.Bounds, snapshot.LeadingReserved, snapshot.ContentLeft, 180, 110, snapshot.Dpi) is not null), "Every retained snapshot must still represent the measured safe gap.");
        Equal(first.CapturedAt, states[2]!.CapturedAt);
        Equal(recovered, states[4]);
    }),
    ("Repeated unknown samples do not renew the four-second lease", () =>
    {
        var cache = new TaskbarLayoutCache();
        var original = Sample();
        Equal(original, Resolve(cache, Measured(original), 0));
        foreach (var second in new[] { 1d, 2d, 3.999d, 4d })
            Equal(original, Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), second));
        Equal<TaskbarSnapshot?>(null, Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), 4.001));
        Equal<TaskbarSnapshot?>(null, Resolve(cache, null, 5));
        var recovered = Sample(6);
        Equal(recovered, Resolve(cache, Measured(recovered), 6));
    }),
    ("No history and partial unknown snapshots cannot invent a placement", () =>
    {
        var cache = new TaskbarLayoutCache();
        Equal<TaskbarSnapshot?>(null, Resolve(cache, null, 0));
        Equal<TaskbarSnapshot?>(null, Resolve(cache, new(TaskbarObservationKind.LayoutUnknown, Sample()), .5));
        Equal<TaskbarSnapshot?>(null, Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), 1));
    }),
    ("Geometry and DPI changes discard the old layout before accepting any new measurement", () =>
    {
        var cache = new TaskbarLayoutCache();
        var moved = new Rectangle(-1920, 1000, 1920, 48);
        Resolve(cache, Measured(Sample()), 0);
        Equal<TaskbarSnapshot?>(null, Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), .2, bounds: moved));
        Equal<TaskbarSnapshot?>(null, Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), .3));
        var current = Sample(.4, bounds: moved);
        Equal(current, Resolve(cache, Measured(current), .4, bounds: moved));
        Equal<TaskbarSnapshot?>(null, Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), .5, bounds: moved, dpi: 144));
        Equal<TaskbarSnapshot?>(null, Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), .6, bounds: moved));
        Equal<TaskbarSnapshot?>(null, Resolve(cache, Measured(Sample(.7)), .7, bounds: moved));
    }),
    ("Native unavailability and a rebuilt taskbar require a fresh snapshot for the new handle", () =>
    {
        var cache = new TaskbarLayoutCache();
        var replacement = new IntPtr(202);
        Resolve(cache, Measured(Sample()), 0);
        Equal<TaskbarSnapshot?>(null, Resolve(cache, new(TaskbarObservationKind.NativeUnavailable), .2));
        Equal<TaskbarSnapshot?>(null, Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), .3));
        Equal<TaskbarSnapshot?>(null, Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), .4, handle: replacement));
        var fresh = Sample(.5, handle: replacement);
        Equal(fresh, Resolve(cache, Measured(fresh), .5, handle: replacement));
        Equal<TaskbarSnapshot?>(null, Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), .6, handle: nativeHandle));
    }),
    ("Native hiding and user disabling clear the lease rather than temporarily suppressing it", () =>
    {
        foreach (var disabling in new[] { false, true })
        {
            var cache = new TaskbarLayoutCache();
            Resolve(cache, Measured(Sample()), 0);
            Equal<TaskbarSnapshot?>(null, Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), .2,
                visible: disabling, enabled: !disabling));
            Equal<TaskbarSnapshot?>(null, Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), .3));
            var refreshed = Sample(.4);
            Equal(refreshed, Resolve(cache, Measured(refreshed), .4));
        }
    }),
    ("A confirmed insufficient gap replaces the old safe measurement and cannot revive it", () =>
    {
        var cache = new TaskbarLayoutCache();
        Resolve(cache, Measured(Sample()), 0);
        var blocked = Sample(1, contentLeft: 200);
        Equal(blocked, Resolve(cache, Measured(blocked), 1));
        Equal<TaskbarPlacement?>(null, TaskbarLayout.PlaceAdaptive(blocked.Bounds, blocked.LeadingReserved, blocked.ContentLeft, 180, 110, blocked.Dpi));
        var held = Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), 1.5);
        Equal(blocked, held);
        Equal<TaskbarPlacement?>(null, TaskbarLayout.PlaceAdaptive(held!.Bounds, held.LeadingReserved, held.ContentLeft, 180, 110, held.Dpi));
    }),
    ("Future measurements, wall-clock rollback and stale measurements fail closed", () =>
    {
        var cache = new TaskbarLayoutCache();
        Equal<TaskbarSnapshot?>(null, Resolve(cache, Measured(Sample(1)), 0));
        Equal<TaskbarSnapshot?>(null, Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), .5));
        Resolve(cache, Measured(Sample()), 0);
        Equal<TaskbarSnapshot?>(null, Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), -.001));
        Equal<TaskbarSnapshot?>(null, Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), .5));
        Equal<TaskbarSnapshot?>(null, Resolve(cache, Measured(Sample(-5)), 0));
    }),
    ("Invalid native contexts and malformed measured observations cannot retain a previous lease", () =>
    {
        foreach (var invalid in new[] { 0, 1, 2, 3 })
        {
            var cache = new TaskbarLayoutCache();
            Resolve(cache, Measured(Sample()), 0);
            TaskbarSnapshot? result = invalid switch
            {
                0 => Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), .2, handle: IntPtr.Zero),
                1 => Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), .2, bounds: Rectangle.Empty),
                2 => Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), .2, dpi: 0),
                _ => Resolve(cache, new(TaskbarObservationKind.LayoutMeasured), .2)
            };
            Equal<TaskbarSnapshot?>(null, result);
            Equal<TaskbarSnapshot?>(null, Resolve(cache, new(TaskbarObservationKind.LayoutUnknown), .3));
        }
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
