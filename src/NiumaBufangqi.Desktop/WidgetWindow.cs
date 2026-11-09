using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NiumaBufangqi.Core;

namespace NiumaBufangqi.Desktop;

internal sealed class WidgetWindow : Window
{
    private readonly TextBlock amount;
    private readonly TextBlock subtitle;

    public WidgetWindow(Action togglePanel, Action openMenu)
    {
        Title = "牛马不放器 · 桌面挂件";
        Width = 224; Height = 51;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        var outer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(245, 246, 248, 236)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(205, 218, 187)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11),
            Padding = new Thickness(13, 7, 10, 7), Cursor = Cursors.Hand
        };
        var row = new DockPanel();
        var face = new TextBlock { Text = "◷", FontSize = 26, Foreground = Brush("#788E5B"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -1, 11, 0) };
        DockPanel.SetDock(face, Dock.Left); row.Children.Add(face);
        var indicator = new TextBlock { Text = "⌃", FontSize = 13, Foreground = Brush("#9CAE83"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
        DockPanel.SetDock(indicator, Dock.Right); row.Children.Add(indicator);
        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        amount = new TextBlock { Text = "先设置薪资", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brush("#4B6040"), TextTrimming = TextTrimming.CharacterEllipsis };
        subtitle = new TextBlock { Text = "牛马不放器 · 点开缓口气", FontSize = 9, Foreground = Brush("#94A180"), Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        lines.Children.Add(amount); lines.Children.Add(subtitle); row.Children.Add(lines);
        outer.Child = row; Content = outer;
        MouseLeftButtonUp += (_, _) => togglePanel();
        MouseRightButtonUp += (_, _) => openMenu();
        ToolTip = "牛马不放器：点击展开，右键打开菜单";
    }

    public void Update(AppState state, SalarySnapshot? snapshot)
    {
        if (snapshot is null || state.Settings is null)
        {
            amount.Text = "先设置薪资"; subtitle.Text = "牛马不放器 · 点开缓口气"; return;
        }
        amount.Text = state.Privacy ? "¥****" : $"¥{snapshot.Earned:0.00}";
        var minutes = (int)Math.Ceiling(snapshot.RemainingWallSeconds / 60);
        subtitle.Text = snapshot.Status switch
        {
            "after" => "今日收工 · 下班万岁",
            "rest" => "休息日 · 快乐归你",
            "lunch" => $"午休充电 · {minutes / 60}时{minutes % 60:00}分下班",
            "before" => "还没到点 · 先照顾自己",
            _ => $"今日已赚 · {minutes / 60}时{minutes % 60:00}分下班"
        };
    }

    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
}
