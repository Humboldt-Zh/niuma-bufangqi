using System.Drawing;
using System.Text.Json;
using NiumaBufangqi.Core;

var defaults = new SalarySettings();
var tests = new (string Name, Action Run)[]
{
    ("A hidden old floating card does not disable the new taskbar entry", () =>
    {
        var oldProfile = JsonSerializer.Deserialize<AppState>("{\"schemaVersion\":1,\"widgetVisible\":false}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Equal(false, oldProfile.WidgetVisible);
        Equal(true, oldProfile.TaskbarEntryVisible);
        var explicitlyHidden = JsonSerializer.Deserialize<AppState>("{\"schemaVersion\":1,\"taskbarEntryVisible\":false}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Equal(false, explicitlyHidden.TaskbarEntryVisible);
    }),
    ("Panel opens directly above the bottom taskbar entry with no floating-card reservation", () =>
    {
        var workArea = new Rectangle(0, 0, 1920, 1040);
        var entry = new Rectangle(250, 1040, 200, 40);
        var panel = PanelPlacement.Calculate(workArea, entry, 1);
        Equal(new Rectangle(250, 272, 560, 760), panel);
        Equal(8, entry.Top - panel.Bottom);
        AssertInside(workArea, panel);
    }),
    ("Panel clamps the right edge on negative and offset monitor coordinates", () =>
    {
        var leftMonitor = new Rectangle(-1920, 0, 1920, 1040);
        var leftPanel = PanelPlacement.Calculate(leftMonitor, new Rectangle(-80, 1040, 70, 40), 1);
        Equal(-568, leftPanel.Left); Equal(-8, leftPanel.Right);
        AssertInside(leftMonitor, leftPanel);
        var rightMonitor = new Rectangle(1920, -200, 1280, 1000);
        var rightPanel = PanelPlacement.Calculate(rightMonitor, new Rectangle(3180, 800, 20, 40), 1);
        Equal(new Rectangle(2632, 32, 560, 760), rightPanel);
        AssertInside(rightMonitor, rightPanel);
    }),
    ("High-DPI and very short working areas shrink the panel without crossing screen bounds", () =>
    {
        var workArea = new Rectangle(0, 0, 1280, 720);
        var panel = PanelPlacement.Calculate(workArea, new Rectangle(16, 720, 280, 60), 2);
        Equal(new Rectangle(16, 16, 1120, 688), panel);
        Equal(16, workArea.Bottom - panel.Bottom);
        AssertInside(workArea, panel);
        var tinyArea = new Rectangle(-2, -1, 2, 1);
        var tinyPanel = PanelPlacement.Calculate(tinyArea, null, 2);
        Equal(tinyArea, tinyPanel);
        AssertInside(tinyArea, tinyPanel);
    }),
    ("Tray fallback and top taskbars stay in the working area and reject invalid geometry", () =>
    {
        var workArea = new Rectangle(100, 50, 1000, 900);
        var fromTray = PanelPlacement.Calculate(workArea, null, 1);
        Equal(new Rectangle(108, 182, 560, 760), fromTray);
        Equal(8, workArea.Bottom - fromTray.Bottom);
        AssertInside(workArea, fromTray);
        var belowTopTaskbar = new Rectangle(0, 40, 1920, 1040);
        var topEntry = new Rectangle(250, 0, 200, 40);
        var belowEntry = PanelPlacement.Calculate(belowTopTaskbar, topEntry, 1);
        Equal(8, belowEntry.Top - topEntry.Bottom);
        AssertInside(belowTopTaskbar, belowEntry);
        foreach (var invalidScale in new[] { 0d, -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Throws<ArgumentOutOfRangeException>(() => PanelPlacement.Calculate(workArea, null, invalidScale));
        Throws<ArgumentOutOfRangeException>(() => PanelPlacement.Calculate(new Rectangle(0, 0, 0, 100), null, 1));
        Throws<ArgumentOutOfRangeException>(() => PanelPlacement.Calculate(new Rectangle(0, 0, 100, -1), null, 1));
        Throws<ArgumentOutOfRangeException>(() => PanelPlacement.Calculate(new Rectangle(int.MaxValue - 5, 0, 100, 100), null, 1));
    }),
    ("Taskbar entry clicks alternate visibility despite focus moving over the entry", () =>
    {
        var states = PanelSequence(false, true,
            (PanelInteraction.EntryClick, false),
            (PanelInteraction.LostFocus, true),
            (PanelInteraction.EntryClick, true),
            (PanelInteraction.LostFocus, true),
            (PanelInteraction.EntryClick, true));
        Assert(states.SequenceEqual([true, true, false, false, true]),
            "The second entry click must close the panel instead of reopening it after premature dismissal.");
    }),
    ("Outside focus loss dismisses once and the next taskbar click reopens", () =>
    {
        var states = PanelSequence(false, true,
            (PanelInteraction.EntryClick, true),
            (PanelInteraction.LostFocus, false),
            (PanelInteraction.LostFocus, false),
            (PanelInteraction.EntryClick, true));
        Assert(states.SequenceEqual([true, false, false, true]),
            "An outside click should hide the panel without consuming the next entry click.");
    }),
    ("First configuration survives focus loss but an explicit close still wins", () =>
    {
        var states = PanelSequence(false, false,
            (PanelInteraction.OpenRequested, false),
            (PanelInteraction.LostFocus, false),
            (PanelInteraction.CloseRequested, false),
            (PanelInteraction.LostFocus, false),
            (PanelInteraction.EntryClick, true));
        Assert(states.SequenceEqual([true, true, false, false, true]),
            "First-run setup should not vanish during initialization, while the user can explicitly close it.");
    }),
    ("Tray open requests are idempotent and do not toggle an already open panel", () =>
    {
        var states = PanelSequence(false, true,
            (PanelInteraction.OpenRequested, false),
            (PanelInteraction.OpenRequested, false),
            (PanelInteraction.CloseRequested, false),
            (PanelInteraction.CloseRequested, false),
            (PanelInteraction.OpenRequested, false));
        Assert(states.SequenceEqual([true, true, false, false, true]),
            "Open and close requests must be explicit intents rather than additional taskbar toggles.");
    }),
    ("Entry dismissal is stable for either native click and focus event ordering", () =>
    {
        var focusFirst = PanelSequence(true, true,
            (PanelInteraction.LostFocus, true),
            (PanelInteraction.EntryClick, true),
            (PanelInteraction.LostFocus, false));
        var clickFirst = PanelSequence(true, true,
            (PanelInteraction.EntryClick, true),
            (PanelInteraction.LostFocus, true),
            (PanelInteraction.LostFocus, false));
        Assert(!focusFirst[^1] && !clickFirst[^1], "A second click must leave the panel closed in either native event ordering.");
        Equal(true, PanelVisibilityPolicy.GetNextVisibility(false, PanelInteraction.EntryClick, configured: true));
    }),
    ("First launch is explicitly unconfigured", () => Equal<SalarySettings?>(null, new AppState().Settings)),
    ("Default schedule and fractional paid days are valid", () =>
    {
        Equal(0, SalaryCalculator.Validate(defaults).Count);
        Equal(0, SalaryCalculator.Validate(defaults with { PaidDays = 21.75m, WorkDays = [1, 1, 2] }).Count);
    }),
    ("Before work salary is zero while wall time includes waiting", () =>
    {
        var result = At(8, 0);
        Equal("before", result.Status); Equal(0m, result.Earned);
        Equal(28_800d, result.RemainingPaidSeconds); Equal(36_000d, result.RemainingWallSeconds);
    }),
    ("Working pay uses net paid time", () =>
    {
        var result = At(10, 0);
        Equal("working", result.Status); Equal(68.18m, result.Earned);
        Equal(545.45m, result.DailySalary); Equal(68.18m, result.HourlySalary);
        Equal(.125d, result.Progress); Equal(25_200d, result.RemainingPaidSeconds); Equal(28_800d, result.RemainingWallSeconds);
    }),
    ("Lunch freezes then resumes exactly at 13:00", () =>
    {
        var start = At(12, 0); var middle = At(12, 30); var finish = At(13, 0);
        Equal("lunch", start.Status); Equal("lunch", middle.Status); Equal("working", finish.Status);
        Equal(204.55m, start.Earned); Equal(start.Earned, middle.Earned); Equal(start.Earned, finish.Earned);
        Equal(18_000d, middle.RemainingPaidSeconds); Equal(19_800d, middle.RemainingWallSeconds);
    }),
    ("Closing time caps salary without overtime", () =>
    {
        var result = At(18, 0);
        Equal("after", result.Status); Equal(545.45m, result.Earned); Equal(1d, result.Progress);
        Equal(0d, result.RemainingPaidSeconds); Equal(0d, result.RemainingWallSeconds);
        Equal(result.Earned, At(23, 59).Earned);
    }),
    ("Rest day is zero unless that weekday is configured", () =>
    {
        var saturday = new DateTime(2026, 11, 14, 10, 0, 0);
        var result = SalaryCalculator.Calculate(defaults, saturday);
        Equal("rest", result.Status); Equal(0m, result.Earned); Equal(0d, result.Progress);
        Equal(0d, result.RemainingPaidSeconds); Equal(0d, result.RemainingWallSeconds);
        Equal("working", SalaryCalculator.Calculate(defaults with { WorkDays = [6] }, saturday).Status);
    }),
    ("Disabled lunch ignores invalid unused fields", () =>
    {
        var setting = defaults with { LunchEnabled = false, LunchStart = "bad", LunchEnd = "also bad" };
        Equal(0, SalaryCalculator.Validate(setting).Count);
        var result = SalaryCalculator.Calculate(setting, new DateTime(2026, 11, 9, 12, 0, 0));
        Equal("working", result.Status); Equal(32_400d, result.PaidSeconds); Equal(181.82m, result.Earned);
    }),
    ("Decimal amounts round midpoint away from zero", () =>
    {
        var setting = defaults with { MonthlySalary = 1m, PaidDays = 8m, LunchEnabled = false, StartTime = "09:00", EndTime = "10:00" };
        var result = SalaryCalculator.Calculate(setting, new DateTime(2026, 11, 9, 10, 0, 0));
        Equal(.13m, result.Earned); Equal(.13m, result.DailySalary); Equal(.13m, result.HourlySalary);
    }),
    ("Subsecond calculation remains monotonic", () =>
    {
        var result = SalaryCalculator.Calculate(defaults, new DateTime(2026, 11, 9, 9, 0, 1, 500));
        Equal(1.5d, result.ElapsedPaidSeconds);
        Assert(result.Progress > 0 && result.Progress < .001, "Subsecond progress should be a small positive fraction.");
    }),
    ("Midpoint pay is exact even when progress is a repeating fraction", () =>
    {
        var setting = defaults with { MonthlySalary = .375m, PaidDays = 1m, LunchEnabled = false, StartTime = "09:00", EndTime = "12:00" };
        var result = SalaryCalculator.Calculate(setting, new DateTime(2026, 11, 9, 10, 0, 0));
        Equal(.13m, result.Earned);
        Equal(.13m, result.HourlySalary);
    }),
    ("Pay and paid-day bounds are enforced", () =>
    {
        Invalid(defaults with { MonthlySalary = 0 }, "monthlySalary");
        Invalid(defaults with { MonthlySalary = 10_000_000.01m }, "monthlySalary");
        Invalid(defaults with { PaidDays = 0 }, "paidDays");
        Invalid(defaults with { PaidDays = 31.01m }, "paidDays");
        Equal(0, SalaryCalculator.Validate(defaults with { MonthlySalary = 10_000_000m, PaidDays = 31m }).Count);
    }),
    ("Unrepresentable decimal rates are rejected instead of crashing", () =>
    {
        Invalid(defaults with { PaidDays = .0000000000000000000000000001m }, "paidDays");
        Throws<SettingsValidationException>(() => SalaryCalculator.Calculate(defaults with { PaidDays = .0000000000000000000000000001m }, DateTime.Now));
    }),
    ("Strict times, overnight shifts and invalid lunch are rejected", () =>
    {
        Invalid(defaults with { StartTime = "9:00" }, "startTime");
        Invalid(defaults with { StartTime = "24:00" }, "startTime");
        Invalid(defaults with { EndTime = "09:00" }, "endTime");
        Invalid(defaults with { StartTime = "22:00", EndTime = "06:00" }, "endTime");
        Invalid(defaults with { LunchStart = "08:00" }, "lunchStart");
        Invalid(defaults with { LunchEnd = "19:00" }, "lunchEnd");
        Invalid(defaults with { LunchStart = "13:00", LunchEnd = "12:00" }, "lunchEnd");
        Invalid(defaults with { LunchStart = "09:00", LunchEnd = "18:00" }, "lunchEnd");
        Invalid(defaults with { WorkDays = [] }, "workDays");
        Invalid(defaults with { WorkDays = [7] }, "workDays");
        Throws<SettingsValidationException>(() => SalaryCalculator.Calculate(defaults with { PaidDays = -1 }, DateTime.Now));
    }),
    ("Saving is versioned, camelCase and leaves no temporary file", () => WithDirectory(directory =>
    {
        var store = new LocalStateStore(directory);
        store.Save(new AppState { Settings = defaults with { WorkDays = [5, 1, 1, 2] }, Privacy = true });
        var loaded = store.Load();
        Assert(loaded.Privacy, "Privacy should persist.");
        Assert(loaded.Settings!.WorkDays.SequenceEqual([1, 2, 5]), "Work days should be normalized and deduplicated.");
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "state.json")));
        Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Equal(12_000m, json.RootElement.GetProperty("settings").GetProperty("monthlySalary").GetDecimal());
        Equal(0, Directory.GetFiles(directory, "*.tmp").Length);
    })),
    ("Second save keeps the previous complete backup", () => WithDirectory(directory =>
    {
        var store = new LocalStateStore(directory);
        store.Save(new AppState { Settings = defaults, Privacy = true });
        store.Save(new AppState { Settings = defaults with { MonthlySalary = 18_000m }, Privacy = false });
        Equal(18_000m, store.Load().Settings!.MonthlySalary);
        using var backup = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "state.json.bak")));
        Assert(backup.RootElement.GetProperty("privacy").GetBoolean(), "Backup should contain the previous privacy value.");
        Equal(12_000m, backup.RootElement.GetProperty("settings").GetProperty("monthlySalary").GetDecimal());
    })),
    ("Corrupt main recovers backup without rewriting either file", () => WithDirectory(directory =>
    {
        var store = new LocalStateStore(directory);
        store.Save(new AppState { Settings = defaults, Privacy = true });
        store.Save(new AppState { Settings = defaults with { MonthlySalary = 18_000m } });
        var main = Path.Combine(directory, "state.json");
        File.WriteAllText(main, "{broken");
        Equal(12_000m, store.Load().Settings!.MonthlySalary);
        Assert(store.LastWarning is not null, "Recovery should be visible to the host.");
        Equal("{broken", File.ReadAllText(main));
        store.Save(new AppState { Settings = defaults with { MonthlySalary = 20_000m } });
        Equal(20_000m, store.Load().Settings!.MonthlySalary);
        var damaged = Directory.GetFiles(directory, "state.corrupt-*.json");
        Equal(1, damaged.Length); Equal("{broken", File.ReadAllText(damaged[0]));
        Assert(File.Exists(main + ".bak"), "A valid recovery backup must survive the repair save.");
    })),
    ("Missing main recovers backup", () => WithDirectory(directory =>
    {
        var store = new LocalStateStore(directory);
        store.Save(new AppState { Privacy = true }); store.Save(new AppState());
        File.Delete(Path.Combine(directory, "state.json"));
        Assert(store.Load().Privacy, "Missing main should use its backup.");
        Assert(store.LastWarning is not null, "Backup load should be reported.");
    })),
    ("Invalid salary file stays untouched and is reported", () => WithDirectory(directory =>
    {
        var file = Path.Combine(directory, "state.json");
        var original = "{\"schemaVersion\":1,\"settings\":{\"monthlySalary\":-1}}";
        File.WriteAllText(file, original);
        var store = new LocalStateStore(directory);
        Equal<SalarySettings?>(null, store.Load().Settings);
        Assert(store.LastWarning is not null, "Invalid configuration should produce a warning.");
        Equal(original, File.ReadAllText(file));
    })),
    ("Newer schema cannot be loaded or overwritten", () => WithDirectory(directory =>
    {
        var file = Path.Combine(directory, "state.json");
        var original = "{\"schemaVersion\":2,\"privacy\":true}";
        File.WriteAllText(file, original);
        var store = new LocalStateStore(directory);
        Throws<UnsupportedStateVersionException>(() => store.Load());
        Throws<UnsupportedStateVersionException>(() => store.Save(new AppState()));
        Equal(original, File.ReadAllText(file));
        Equal(0, Directory.GetFiles(directory, "*.tmp").Length);
    })),
    ("Newer backup schema is protected when main is absent", () => WithDirectory(directory =>
    {
        File.WriteAllText(Path.Combine(directory, "state.json.bak"), "{\"schemaVersion\":2}");
        var store = new LocalStateStore(directory);
        Throws<UnsupportedStateVersionException>(() => store.Load());
        Throws<UnsupportedStateVersionException>(() => store.Save(new AppState()));
        Assert(!File.Exists(Path.Combine(directory, "state.json")), "A downgrade must not create a replacement file.");
    })),
    ("Saving cannot overwrite a newer backup beside an older main", () => WithDirectory(directory =>
    {
        var store = new LocalStateStore(directory);
        store.Save(new AppState { Privacy = true });
        var backupFile = Path.Combine(directory, "state.json.bak");
        var newerBackup = "{\"schemaVersion\":2,\"privacy\":false}";
        File.WriteAllText(backupFile, newerBackup);
        Throws<UnsupportedStateVersionException>(() => store.Save(new AppState()));
        Equal(newerBackup, File.ReadAllText(backupFile));
        Assert(store.Load().Privacy, "The existing older main should remain untouched.");
    })),
    ("Break records retain the opaque front-end day key", () => WithDirectory(directory =>
    {
        var store = new LocalStateStore(directory);
        store.Save(new AppState { BreakSessions = [new BreakRecord { Date = "2026-10-9", At = "16:38", Source = "real", Seconds = 61.5, Amount = 1.16m }] });
        var record = store.Load().BreakSessions.Single();
        Equal("2026-10-9", record.Date); Equal(61.5d, record.Seconds); Equal(1.16m, record.Amount);
        Throws<InvalidDataException>(() => store.Save(new AppState { BreakSessions = [record with { Seconds = double.NaN }] }));
    })),
    ("Malformed records are skipped on read and produce a warning", () => WithDirectory(directory =>
    {
        File.WriteAllText(Path.Combine(directory, "state.json"), "{\"schemaVersion\":1,\"breakSessions\":[{\"seconds\":-1,\"date\":\"2026-10-9\",\"at\":\"16:38\",\"source\":\"real\"}]}");
        var store = new LocalStateStore(directory);
        Equal(0, store.Load().BreakSessions.Count);
        Assert(store.LastWarning is not null, "Dropped malformed records should not be silent.");
    }))
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL {name}: {exception}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

SalarySnapshot At(int hour, int minute) => SalaryCalculator.Calculate(defaults, new DateTime(2026, 11, 9, hour, minute, 0));
static bool[] PanelSequence(bool initiallyVisible, bool configured, params (PanelInteraction Event, bool PointerOverEntry)[] interactions)
{
    var visible = initiallyVisible;
    return interactions.Select(interaction =>
    {
        visible = PanelVisibilityPolicy.GetNextVisibility(visible, interaction.Event, configured, interaction.PointerOverEntry);
        return visible;
    }).ToArray();
}
static void AssertInside(Rectangle workingArea, Rectangle panel)
{
    Assert(panel.Width > 0 && panel.Height > 0, "A panel must retain at least one visible pixel on each axis.");
    Assert(workingArea.Contains(panel), "The full panel must remain inside the selected monitor's working area.");
}
void Invalid(SalarySettings settings, string field) => Assert(SalaryCalculator.Validate(settings).ContainsKey(field), $"Expected validation error for {field}.");
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
static void WithDirectory(Action<string> action)
{
    var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NiumaBufangqi.Core.Tests"));
    var directory = Path.GetFullPath(Path.Combine(testRoot, Guid.NewGuid().ToString("N")));
    if (!directory.StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Test directory escaped its dedicated temporary root.");
    Directory.CreateDirectory(directory);
    try { action(directory); }
    finally { Directory.Delete(directory, recursive: true); }
}
