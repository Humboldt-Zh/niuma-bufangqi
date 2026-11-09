using System.Drawing;
using NiumaBufangqi.Desktop.Taskbar;

// These tests link only pure geometry. They never instantiate a window or query Explorer.
var bar = new Rectangle(0, 1000, 1920, 48);
var weather = new Rectangle(0, 1000, 120, 48);
var tests = new (string Name, Action Run)[]
{
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
Console.WriteLine($"{tests.Length - failed}/{tests.Length} taskbar geometry tests passed.");
return failed == 0 ? 0 : 1;

static TaskbarPlacement Require(TaskbarPlacement? value) => value ?? throw new InvalidOperationException("Expected a safe taskbar placement.");
static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; got {actual}."); }
