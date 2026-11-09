using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace NiumaBufangqi.Desktop;

internal sealed class PanelWindow : Window
{
    private const string Origin = "https://niuma.app/";
    private readonly App owner;
    private readonly WebView2 browser;
    private readonly TextBlock loading;
    private bool ready;
    private bool initializing;
    private string? pendingPage;

    public PanelWindow(App owner)
    {
        this.owner = owner;
        Title = "牛马不放器";
        Width = 560; Height = 760;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(255, 254, 249));
        var grid = new Grid();
        loading = new TextBlock
        {
            Text = "正在给工位充点电…", FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(111, 134, 87)),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(28)
        };
        browser = new WebView2 { Visibility = Visibility.Hidden, DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 255, 254, 249) };
        grid.Children.Add(browser); grid.Children.Add(loading); Content = grid;
        Loaded += async (_, _) => await InitializeBrowserAsync();
        Closing += OnClosing;
        // First-run configuration stays visible until the user closes it explicitly.
        Deactivated += (_, _) => { if (!owner.Exiting && owner.State.Settings is not null) owner.HidePanel("deactivated"); };
    }

    public async Task InitializeBrowserAsync()
    {
        if (initializing || ready) return;
        initializing = true;
        try
        {
            var assets = Path.Combine(AppContext.BaseDirectory, "WebAssets");
            if (!File.Exists(Path.Combine(assets, "index.html"))) throw new FileNotFoundException("发布目录缺少界面文件，请完整解压发布包。");
            var profile = Path.Combine(owner.DataDirectory, "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, profile);
            await browser.EnsureCoreWebView2Async(environment);
            var web = browser.CoreWebView2;
            web.SetVirtualHostNameToFolderMapping("niuma.app", assets, CoreWebView2HostResourceAccessKind.Deny);
            web.Settings.AreHostObjectsAllowed = false;
            web.Settings.AreDefaultContextMenusEnabled = false;
            web.Settings.IsStatusBarEnabled = false;
            web.Settings.IsGeneralAutofillEnabled = false;
            web.Settings.IsPasswordAutosaveEnabled = false;
            web.Settings.AreBrowserAcceleratorKeysEnabled = false;
            web.Settings.AreDefaultScriptDialogsEnabled = false;
#if DEBUG
            web.Settings.AreDevToolsEnabled = true;
#else
            web.Settings.AreDevToolsEnabled = false;
#endif
            web.PermissionRequested += (_, args) => { args.State = CoreWebView2PermissionState.Deny; args.Handled = true; };
            web.NewWindowRequested += (_, args) => { args.Handled = true; if (args.IsUserInitiated) owner.OpenExternal(args.Uri); };
            web.NavigationStarting += (_, args) => { if (!IsApplicationUri(args.Uri)) args.Cancel = true; };
            web.FrameNavigationStarting += (_, args) => { if (!IsApplicationUri(args.Uri)) args.Cancel = true; };
            web.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            web.WebResourceRequested += (_, args) =>
            {
                if (IsApplicationUri(args.Request.Uri)) return;
                args.Response = environment.CreateWebResourceResponse(new MemoryStream(), 403, "Blocked", "Content-Type: text/plain");
            };
            web.WebMessageReceived += OnMessage;
            web.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess) { browser.Visibility = Visibility.Visible; loading.Visibility = Visibility.Collapsed; }
                else ShowFailure("界面没有加载成功。请确认发布包已经完整解压，再重新打开。", null);
            };
            web.ProcessFailed += (_, _) => ShowFailure("界面进程暂时不可用。请从托盘退出后重新打开，已保存的配置仍在本机。", null);
            var bootstrap = JsonSerializer.Serialize(new { state = owner.State, version = App.Version }, App.Json);
            await web.AddScriptToExecuteOnDocumentCreatedAsync("window.__NIUMA_BOOTSTRAP__=" + bootstrap + ";");
            web.Navigate(Origin + "index.html");
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            ShowFailure("需要 Microsoft Edge WebView2 Runtime 才能显示界面。请安装微软官方运行时后重新打开。", exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            ShowFailure("界面暂时无法启动。请完整解压软件，并确认本地数据目录可以写入。", exception);
        }
        finally { initializing = false; }
    }

    private void ShowFailure(string text, Exception? exception)
    {
        ready = false; loading.Text = text; loading.Visibility = Visibility.Visible; browser.Visibility = Visibility.Hidden;
        if (exception is not null) owner.Log("webview-start", exception);
        var panel = new StackPanel { Margin = new Thickness(30), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = Brushes.DarkOliveGreen, LineHeight = 25 });
        var button = new Button { Content = "查看微软官方 WebView2 安装页", Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 22, 0, 0) };
        button.Click += (_, _) => owner.OpenExternal("https://developer.microsoft.com/microsoft-edge/webview2/");
        panel.Children.Add(button);
        var exit = new Button { Content = "退出软件", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 12, 0, 0) };
        exit.Click += (_, _) => owner.ExitApplication(); panel.Children.Add(exit);
        ((Grid)Content).Children.Add(panel);
    }

    private static bool IsApplicationUri(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("niuma.app", StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo);

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!IsApplicationUri(args.Source)) return;
        try
        {
            if (args.WebMessageAsJson.Length > 64 * 1024) throw new InvalidOperationException("消息过长");
            using var document = JsonDocument.Parse(args.WebMessageAsJson, new JsonDocumentOptions { MaxDepth = 12 });
            var envelope = document.RootElement;
            if (envelope.ValueKind != JsonValueKind.Object || !envelope.TryGetProperty("version", out var version) || version.GetInt32() != 1
                || !envelope.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !envelope.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("消息格式不受支持");
            if (type.GetString() == "ready")
            {
                ready = true;
                SendState(); SendSnapshot();
                if (pendingPage is not null) { NavigateTo(pendingPage); pendingPage = null; }
                return;
            }
            owner.HandleCommand(type.GetString()!, payload);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            owner.Log("bridge-message", exception);
            SendError("这次操作没有保存。请检查输入后再试一次。");
        }
    }

    public void SendState() => Post(new { type = "state", state = owner.State });
    public void SendSnapshot() => Post(new { type = "snapshot", snapshot = owner.Snapshot, configured = owner.State.Settings is not null });
    public void SendError(string message) => Post(new { type = "error", message });
    private void Post(object value)
    {
        if (!ready || browser.CoreWebView2 is null) return;
        try { browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value, App.Json)); }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.COMException) { owner.Log("bridge-post", exception); }
    }

    public void NavigateTo(string page)
    {
        if (page is not ("today" or "settings" or "break" or "feel")) return;
        if (!ready) { pendingPage = page; return; }
        _ = ExecuteSafelyAsync("document.querySelector('[data-page=\"" + page + "\"]')?.click();");
    }

    public void ClearTransientPaper() => _ = ExecuteSafelyAsync("if(document.getElementById('paperOverlay')?.open){window.PaperPlay?.close();}");
    private async Task ExecuteSafelyAsync(string script)
    {
        if (!ready || browser.CoreWebView2 is null) return;
        try { await browser.CoreWebView2.ExecuteScriptAsync(script); }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.COMException) { owner.Log("ui-command", exception); }
    }

    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (owner.Exiting) { browser.Dispose(); return; }
        args.Cancel = true; owner.HidePanel("closing");
    }
}
