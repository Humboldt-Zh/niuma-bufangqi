using System;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NiumaBufangqi.Core;
using Drawing = System.Drawing;
using Wpf = System.Windows;
using Media = System.Windows.Media;
using Imaging = System.Windows.Media.Imaging;

namespace NiumaBufangqi.Desktop.Taskbar;

internal readonly record struct TaskbarPresentation(string Amount, string MaximumAmount, string Subtitle)
{
    internal static TaskbarPresentation Create(AppState state, SalarySnapshot? snapshot)
    {
        if (state.Settings is null || snapshot is null) return new("牛马不放器", "牛马不放器", "点击设置薪资");
        var amount = state.Privacy ? "¥****" : "¥" + snapshot.Earned.ToString("0.00", CultureInfo.InvariantCulture);
        var maximum = state.Privacy ? "¥****" : "¥" + Math.Max(snapshot.DailySalary, snapshot.Earned).ToString("0.00", CultureInfo.InvariantCulture);
        var minutes = double.IsFinite(snapshot.RemainingWallSeconds)
            ? (int)Math.Ceiling(Math.Clamp(snapshot.RemainingWallSeconds / 60, 0, 1440)) : 0;
        var subtitle = snapshot.Status switch
        {
            "after" => "今日收工 · 下班万岁",
            "rest" => "休息日 · 快乐归你",
            "lunch" => $"午休 · {minutes / 60}时{minutes % 60:00}分下班",
            "before" => "未到上班时间",
            _ => $"今日已赚 · {minutes / 60}时{minutes % 60:00}分下班"
        };
        return new(amount, maximum, subtitle);
    }
}

internal readonly record struct TaskbarPalette(bool Light, Media.Color Primary, Media.Color Secondary, Media.Color Accent)
{
    internal static TaskbarPalette Read()
    {
        if (System.Windows.Forms.SystemInformation.HighContrast)
        {
            var color = Drawing.SystemColors.WindowText;
            var ink = Media.Color.FromRgb(color.R, color.G, color.B);
            return new(color.GetBrightness() < .5f, ink, ink, ink);
        }
        var light = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = key?.GetValue("SystemUsesLightTheme") is int mode && mode == 1;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException) { }
        return light
            ? new(true, Media.Color.FromRgb(44, 52, 37), Media.Color.FromRgb(96, 110, 82), Media.Color.FromRgb(102, 130, 73))
            : new(false, Media.Color.FromRgb(247, 249, 241), Media.Color.FromRgb(207, 219, 193), Media.Color.FromRgb(191, 212, 160));
    }
}

internal static class TaskbarRenderer
{
    private static readonly Media.FontFamily Family = new("Segoe UI Variable, Segoe UI, Microsoft YaHei UI");
    private static readonly Media.Typeface AmountFace = new(Family, Wpf.FontStyles.Normal, Wpf.FontWeights.SemiBold, Wpf.FontStretches.Normal);
    private static readonly Media.Typeface CaptionFace = new(Family, Wpf.FontStyles.Normal, Wpf.FontWeights.Normal, Wpf.FontStretches.Normal);

    internal static (int Normal, int Compact) Measure(TaskbarPresentation presentation, int dpi)
    {
        var scale = dpi / 96d;
        var amountWidth = MeasureAmount(presentation.MaximumAmount, scale);
        var captionWidth = Text(presentation.Subtitle, 11, CaptionFace, Media.Brushes.White, scale).WidthIncludingTrailingWhitespace;
        return ((int)Math.Ceiling(Math.Max(104, 26 + Math.Max(amountWidth, captionWidth)) * scale),
            (int)Math.Ceiling((26 + amountWidth) * scale));
    }

    internal static Drawing.Bitmap Render(TaskbarPresentation presentation, Drawing.Size size, int dpi,
        bool compact, bool highlighted, TaskbarPalette palette)
    {
        var scale = dpi / 96d;
        var width = size.Width / scale;
        var height = size.Height / scale;
        var visual = new Media.DrawingVisual();
        Media.TextOptions.SetTextRenderingMode(visual, Media.TextRenderingMode.Grayscale);
        Media.TextOptions.SetTextFormattingMode(visual, Media.TextFormattingMode.Display);
        using (var drawing = visual.RenderOpen())
        {
            // Alpha=1 keeps the whole information slot clickable without a visible card.
            drawing.DrawRectangle(new Media.SolidColorBrush(Media.Color.FromArgb(1, 0, 0, 0)), null, new Wpf.Rect(0, 0, width, height));
            if (highlighted)
            {
                var tint = palette.Light ? Media.Colors.Black : Media.Colors.White;
                drawing.DrawRoundedRectangle(new Media.SolidColorBrush(Media.Color.FromArgb(14, tint.R, tint.G, tint.B)), null,
                    new Wpf.Rect(1, 3, Math.Max(0, width - 2), Math.Max(0, height - 6)), 5, 5);
            }
            var primary = new Media.SolidColorBrush(palette.Primary);
            var secondary = new Media.SolidColorBrush(palette.Secondary);
            var accent = new Media.SolidColorBrush(palette.Accent);
            DrawClock(drawing, 8, (height - 18) / 2, accent);
            if (compact) DrawAmount(drawing, presentation.Amount, 34, (height - 20) / 2, primary, scale);
            else
            {
                var top = (height - 35) / 2;
                DrawAmount(drawing, presentation.Amount, 34, top, primary, scale);
                var caption = Text(presentation.Subtitle, 11, CaptionFace, secondary, scale);
                drawing.DrawText(caption, new Wpf.Point(34, top + 20 + (15 - caption.Height) / 2));
            }
        }
        var target = new Imaging.RenderTargetBitmap(size.Width, size.Height, dpi, dpi, Media.PixelFormats.Pbgra32);
        target.Render(visual);
        var pixels = new byte[checked(size.Width * size.Height * 4)];
        target.CopyPixels(pixels, size.Width * 4, 0);
        var bitmap = new Drawing.Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        bitmap.SetResolution(dpi, dpi);
        var data = bitmap.LockBits(new Drawing.Rectangle(Drawing.Point.Empty, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }

    private static Media.FormattedText Text(string value, double size, Media.Typeface face, Media.Brush brush, double scale) =>
        new(value, CultureInfo.InvariantCulture, Wpf.FlowDirection.LeftToRight, face, size, brush, scale);

    private static double DigitWidth(double scale) => Enumerable.Range(0, 10)
        .Max(digit => Text(digit.ToString(CultureInfo.InvariantCulture), 14, AmountFace, Media.Brushes.White, scale).WidthIncludingTrailingWhitespace);

    private static double MeasureAmount(string amount, double scale)
    {
        var digitWidth = DigitWidth(scale);
        return amount.Sum(character => char.IsAsciiDigit(character) ? digitWidth
            : Text(character.ToString(), character == '¥' ? 12 : 14, AmountFace, Media.Brushes.White, scale).WidthIncludingTrailingWhitespace);
    }

    private static void DrawAmount(Media.DrawingContext drawing, string amount, double x, double y, Media.Brush ink, double scale)
    {
        var digitWidth = DigitWidth(scale);
        var sample = Text("0", 14, AmountFace, ink, scale);
        var baseline = y + (20 - sample.Height) / 2 + sample.Baseline;
        foreach (var character in amount)
        {
            var glyph = Text(character.ToString(), character == '¥' ? 12 : 14, AmountFace, ink, scale);
            var advance = char.IsAsciiDigit(character) ? digitWidth : glyph.WidthIncludingTrailingWhitespace;
            drawing.DrawText(glyph, new Wpf.Point(x + (advance - glyph.WidthIncludingTrailingWhitespace) / 2, baseline - glyph.Baseline));
            x += advance;
        }
    }

    private static void DrawClock(Media.DrawingContext drawing, double x, double y, Media.Brush ink)
    {
        var pen = new Media.Pen(ink, 1.5) { StartLineCap = Media.PenLineCap.Round, EndLineCap = Media.PenLineCap.Round };
        drawing.DrawEllipse(null, pen, new Wpf.Point(x + 9, y + 9), 7.2, 7.2);
        drawing.DrawLine(pen, new Wpf.Point(x + 9, y + 5), new Wpf.Point(x + 9, y + 9));
        drawing.DrawLine(pen, new Wpf.Point(x + 9, y + 9), new Wpf.Point(x + 12.1, y + 10.8));
    }
}
