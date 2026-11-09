using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using NiumaBufangqi.Core;
using NiumaBufangqi.Desktop.Taskbar;
using Forms = System.Windows.Forms;

namespace NiumaBufangqi.Desktop;

public partial class App : Application
{
    public static string Version { get; } = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 12 };
    private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValue = "NiumaBufangqi";
    private Mutex? instance;
    private EventWaitHandle? activation;
    private RegisteredWaitHandle? activationWait;
    private LocalStateStore store = null!;
    private DispatcherTimer clock = null!;
    private Forms.NotifyIcon tray = null!;
    private Forms.ContextMenuStrip menu = null!;
    private PanelWindow panel = null!;
    private TaskbarEntry entry = null!;
    private Forms.Screen screen = null!;
    private EventHandler? displayChanged;
    private Forms.ToolStripMenuItem privacyItem = null!;
    private Forms.ToolStripMenuItem widgetItem = null!;
    private Forms.ToolStripMenuItem startupItem = null!;

    internal AppState State { get; private set; } = new();
    internal SalarySnapshot? Snapshot { get; private set; }
    internal string DataDirectory { get; private set; } = "";
    internal bool Exiting { get; private set; }

    protected override void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
        try
        {
            DataDirectory = ResolveDataDirectory(args.Args);
            // Session-local names prevent two applications from writing the same settings at once.
            var mutexSuffix = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(DataDirectory.ToUpperInvariant())))[..16];
            instance = new Mutex(true, @"Local\NiumaBufangqi." + mutexSuffix, out var created);
            var eventName = @"Local\NiumaBufangqi.Activate." + mutexSuffix;
            if (!created)
            {
                if (HasDifferentExecutableRunning())
                    MessageBox.Show("另一个版本的牛马不放器仍在运行。请右键当前挂件或托盘中的图标选择“退出”，再打开此版本。当前不会新增第二个入口。", "牛马不放器 · 请先退出旧版本", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    try { using var existing = EventWaitHandle.OpenExisting(eventName); existing.Set(); } catch (WaitHandleCannotBeOpenedException) { }
                Shutdown(); return;
            }
            if (HasLegacySalaryClockRunning())
            {
                MessageBox.Show("旧版 SalaryClock 仍在运行。请先右键任务栏中的旧薪资入口，选择“退出”，再启动牛马不放器。当前不会再生成第二个入口。", "牛马不放器 · 请先退出旧工具", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(); return;
            }
            activation = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
            store = new LocalStateStore(DataDirectory);
            State = store.Load();
            Log("application-start-version-" + Version);
            State = State with { StartWithWindows = IsCurrentStartupConfigured() };
            panel = new PanelWindow(this);
            entry = new TaskbarEntry(TogglePanel, ShowMenu);
            entry.Diagnostic += state => Log("taskbar-" + state);
            entry.AttachmentChanged += (_, _) =>
            {
                if (!Exiting && !Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(() => { if (panel.IsVisible) Reposition(); });
            };
            MainWindow = panel;
            screen = Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
            CreateTray();
            entry.Start();
            Reposition();
            clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher);
            Tick();
            activationWait = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => Dispatcher.BeginInvoke(() => ShowPanel()), null, Timeout.Infinite, false);
            displayChanged = (_, _) => Dispatcher.BeginInvoke(() =>
            {
                screen = Forms.Screen.AllScreens.FirstOrDefault(item => item.DeviceName == screen.DeviceName) ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
                Reposition();
            });
            SystemEvents.DisplaySettingsChanged += displayChanged;
            DispatcherUnhandledException += (_, eventArgs) =>
            {
                Log("ui-dispatch", eventArgs.Exception); eventArgs.Handled = true;
                panel.SendError("这次操作没有完成，已保存的配置仍在本机。请重试。");
            };
            if (State.Settings is null || args.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase)) ShowPanel("settings");
            else if (args.Args.Contains("--open", StringComparer.OrdinalIgnoreCase)) ShowPanel("today");
            if (!string.IsNullOrEmpty(store.LastWarning)) tray.ShowBalloonTip(6000, "牛马不放器", store.LastWarning, Forms.ToolTipIcon.Info);
        }
        catch (UnsupportedStateVersionException exception)
        {
            MessageBox.Show(exception.Message, "牛马不放器 · 配置版本较新", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(2);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            Log("startup", exception);
            MessageBox.Show("软件没有成功启动。请确认已完整解压，并且本地数据目录可以写入。", "牛马不放器", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static bool HasDifferentExecutableRunning()
    {
        foreach (var process in Process.GetProcessesByName("NiumaBufangqi"))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    if (!string.Equals(process.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException) { return true; }
            }
        }
        return false;
    }

    private static bool HasLegacySalaryClockRunning()
    {
        var found = false;
        foreach (var process in Process.GetProcessesByName("SalaryClock")) { using (process) { if (!process.HasExited) found = true; } }
        return found;
    }

    private static bool IsCurrentStartupConfigured()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupKey, false);
            return string.Equals(key?.GetValue(StartupValue) as string, "\"" + Environment.ProcessPath + "\"", StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Security.SecurityException) { return false; }
    }

    private static string ResolveDataDirectory(string[] args)
    {
        var position = Array.IndexOf(args, "--data-dir");
        if (position >= 0)
        {
            if (position + 1 >= args.Length || !Path.IsPathFullyQualified(args[position + 1])) throw new ArgumentException("--data-dir requires an absolute directory");
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[position + 1]));
        }
        return Path.TrimEndingDirectorySeparator(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NiumaBufangqi"));
    }

    private void CreateTray()
    {
        menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开牛马不放器", null, (_, _) => ShowPanel());
        menu.Items.Add("薪资与工作安排", null, (_, _) => ShowPanel("settings"));
        menu.Items.Add(new Forms.ToolStripSeparator());
        privacyItem = new Forms.ToolStripMenuItem("隐藏收益金额", null, (_, _) => TryChange(() => Commit(State with { Privacy = !State.Privacy })));
        widgetItem = new Forms.ToolStripMenuItem("显示任务栏入口", null, (_, _) => TryChange(() => Commit(State with { TaskbarEntryVisible = !State.TaskbarEntryVisible })));
        startupItem = new Forms.ToolStripMenuItem("随 Windows 启动", null, (_, _) => TryChange(() => SetStartup(!State.StartWithWindows)));
        menu.Items.AddRange([privacyItem, widgetItem, startupItem]);
        menu.Items.Add("打开本地数据目录", null, (_, _) => TryChange(OpenDataFolder));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());
        var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/app-icon.ico"))?.Stream ?? throw new FileNotFoundException("App icon not found");
        using (stream)
        {
            using var resourceIcon = new System.Drawing.Icon(stream);
            tray = new Forms.NotifyIcon { Icon = (System.Drawing.Icon)resourceIcon.Clone(), Text = "牛马不放器", Visible = true, ContextMenuStrip = menu };
        }
        tray.MouseClick += (_, eventArgs) => { if (eventArgs.Button == Forms.MouseButtons.Left) ShowPanel(); };
        tray.MouseDoubleClick += (_, _) => ShowPanel();
    }

    internal void ShowMenu() { UpdateMenu(); menu.Show(Forms.Cursor.Position); }
    private void UpdateMenu()
    {
        privacyItem.Checked = State.Privacy; widgetItem.Checked = State.TaskbarEntryVisible; startupItem.Checked = State.StartWithWindows;
    }

    internal void TogglePanel()
    {
        var visible = PanelVisibilityPolicy.GetNextVisibility(panel.IsVisible, PanelInteraction.EntryClick, State.Settings is not null);
        if (visible) ShowPanel(); else HidePanel("entry-toggle");
    }
    internal void DismissPanelOnDeactivation()
    {
        var visible = PanelVisibilityPolicy.GetNextVisibility(panel.IsVisible, PanelInteraction.LostFocus, State.Settings is not null, entry.IsPointerOverEntry);
        if (!visible) HidePanel("deactivated");
    }
    internal void ShowPanel(string? page = null)
    {
        if (Exiting) return;
        if (!panel.IsVisible) panel.Show();
        Reposition(); panel.Activate();
        entry.SetPanelVisible(true);
        if (page is not null) panel.NavigateTo(page);
        panel.SendState(); panel.SendSnapshot();
    }
    internal void HidePanel(string reason = "request")
    {
        if (!panel.IsVisible) return;
        panel.ClearTransientPaper(); panel.Hide(); entry.SetPanelVisible(false);
    }
    private void Reposition()
    {
        entry.RefreshPlacement();
        if (!panel.IsVisible) return;
        if (entry.TryGetScreenBounds(out var anchor)) NativePositioning.PlacePanel(panel, Forms.Screen.FromRectangle(anchor), anchor);
        else NativePositioning.PlacePanel(panel, screen, null);
    }
    private void Tick()
    {
        Snapshot = State.Settings is null ? null : SalaryCalculator.Calculate(State.Settings, DateTime.Now);
        entry.Update(State, Snapshot);
        panel.SendSnapshot();
        UpdateMenu();
        tray.Text = State.Settings is null ? "牛马不放器 · 先设置薪资" : State.Privacy ? "牛马不放器 · 金额已隐藏" : $"牛马不放器 · 今日估算 ¥{Snapshot!.Earned:0.00}";
    }

    internal void HandleCommand(string type, JsonElement payload)
    {
        switch (type)
        {
            case "hide": HidePanel("bridge"); break;
            case "saveSettings":
                var settings = payload.Deserialize<SalarySettings>(Json) ?? throw new ArgumentException("设置不能为空");
                var errors = SalaryCalculator.Validate(settings);
                if (errors.Count != 0) { panel.SendError(string.Join("；", errors.Values.Take(2))); break; }
                TryChange(() => Commit(State with { Settings = settings })); break;
            case "resetSettings": TryChange(() => Commit(State with { Settings = null })); break;
            case "setPrivacy": TryChange(() => Commit(State with { Privacy = ReadBoolean(payload) })); break;
            case "setStartup": TryChange(() => SetStartup(ReadBoolean(payload))); break;
            case "saveBreakSessions":
                if (!payload.TryGetProperty("sessions", out var sessions) || sessions.ValueKind != JsonValueKind.Array || sessions.GetArrayLength() > 100) throw new ArgumentException("休息记录格式不正确");
                var records = sessions.Deserialize<List<BreakRecord>>(Json) ?? [];
                if (records.Any(record => !ValidRecord(record))) throw new ArgumentException("休息记录格式不正确");
                TryChange(() => Commit(State with { BreakSessions = records })); break;
            case "openDataFolder": TryChange(OpenDataFolder); break;
            case "openExternal":
                if (!payload.TryGetProperty("url", out var value) || value.ValueKind != JsonValueKind.String) throw new ArgumentException("链接格式不正确");
                OpenExternal(value.GetString()!); break;
            default: panel.SendError("这个操作不受当前软件版本支持。"); break;
        }
    }

    private static bool ReadBoolean(JsonElement payload) => payload.TryGetProperty("value", out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        ? value.GetBoolean() : throw new ArgumentException("请选择开关状态");
    private static bool ValidRecord(BreakRecord? record) => record is not null && double.IsFinite(record.Seconds) && record.Seconds >= 0 && record.Seconds <= 366 * 24 * 3600
        && record.Amount >= 0 && record.Amount <= 10_000_000_000m && record.Source == "real"
        && record.Date is not null && Regex.IsMatch(record.Date, @"^\d{4}-\d{1,2}-\d{1,2}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))
        && TimeOnly.TryParseExact(record.At, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private void Commit(AppState next)
    {
        store.Save(next);
        State = next;
        panel.SendState(); Tick();
    }

    private void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupKey, true) ?? throw new IOException("启动设置不可写");
        var previous = key.GetValue(StartupValue) as string;
        var command = "\"" + (Environment.ProcessPath ?? throw new IOException("程序路径不可用")) + "\"";
        if (enabled) key.SetValue(StartupValue, command, RegistryValueKind.String); else key.DeleteValue(StartupValue, false);
        try { Commit(State with { StartWithWindows = enabled }); }
        catch
        {
            if (previous is null) key.DeleteValue(StartupValue, false); else key.SetValue(StartupValue, previous, RegistryValueKind.String);
            throw;
        }
    }

    private void TryChange(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or InvalidOperationException)
        {
            Log("state-change", exception);
            panel.SendError("没有保存成功。请确认本地目录可写，并检查输入后重试。");
        }
    }

    private void OpenDataFolder()
    {
        Directory.CreateDirectory(DataDirectory);
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.ArgumentList.Add(DataDirectory); Process.Start(start);
    }

    internal void OpenExternal(string value)
    {
        if (value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.Host is not ("github.com" or "learn.microsoft.com" or "developer.microsoft.com" or "dotnet.microsoft.com"))
        { panel.SendError("此链接无法从软件内打开。"); return; }
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException) { Log("external-link", exception); panel.SendError("系统浏览器没有打开，请稍后重试。"); }
    }

    internal void Log(string operation, Exception? exception = null)
    {
        try
        {
            if (string.IsNullOrEmpty(DataDirectory)) return;
            var logs = Path.Combine(DataDirectory, "logs"); Directory.CreateDirectory(logs);
            var file = Path.Combine(logs, "app.log");
            if (File.Exists(file) && new FileInfo(file).Length > 256 * 1024) File.Move(file, file + ".previous", true);
            // Deliberately exclude exception messages, payloads, salary and written thoughts.
            File.AppendAllText(file, $"{DateTimeOffset.Now:O} {operation} {exception?.GetType().Name ?? "State"}{Environment.NewLine}");
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
    }


    internal void ExitApplication()
    {
        if (Exiting) return;
        Exiting = true; clock?.Stop();
        panel?.Close(); entry?.Dispose(); Shutdown();
    }
    protected override void OnExit(ExitEventArgs args)
    {
        Exiting = true;
        if (displayChanged is not null) SystemEvents.DisplaySettingsChanged -= displayChanged;
        activationWait?.Unregister(null); activation?.Dispose(); instance?.Dispose();
        entry?.Dispose();
        if (tray is not null) { tray.Visible = false; tray.Icon?.Dispose(); tray.Dispose(); }
        menu?.Dispose(); base.OnExit(args);
    }
}
