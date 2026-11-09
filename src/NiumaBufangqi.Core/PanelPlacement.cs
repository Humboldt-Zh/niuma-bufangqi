using System.Drawing;

namespace NiumaBufangqi.Core;

/// <summary>Positions the panel beside an existing taskbar entry without reserving any floating-card space.</summary>
public static class PanelPlacement
{
    public static Rectangle Calculate(Rectangle workArea, Rectangle? entryBounds, double scale)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0 || !double.IsFinite(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(workArea), "A valid working area and positive display scale are required.");
        var right = (long)workArea.Left + workArea.Width;
        var bottom = (long)workArea.Top + workArea.Height;
        if (right > int.MaxValue || bottom > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(workArea));
        int Scaled(int value) => (int)Math.Min(int.MaxValue, Math.Max(1, Math.Round(value * scale)));
        var margin = Math.Min(Scaled(8), Math.Min((workArea.Width - 1) / 2, (workArea.Height - 1) / 2));
        var width = Math.Min(Scaled(560), workArea.Width - margin * 2);
        var desiredLeft = entryBounds?.Left ?? workArea.Left + margin;
        var left = (int)Math.Clamp((long)desiredLeft, (long)workArea.Left + margin, right - margin - width);
        var topTaskbar = entryBounds is { } entry && (long)entry.Top + entry.Height <= (long)workArea.Top + margin;
        if (topTaskbar)
        {
            var height = Math.Min(Scaled(760), workArea.Height - margin * 2);
            return new Rectangle(left, workArea.Top + margin, width, height);
        }
        var anchorTop = Math.Clamp((long)(entryBounds?.Top ?? (int)bottom), (long)workArea.Top + margin + 1, bottom);
        var panelBottom = Math.Max((long)workArea.Top + margin + 1, anchorTop - margin);
        var panelHeight = (int)Math.Min(Scaled(760), panelBottom - workArea.Top - margin);
        return new Rectangle(left, (int)(panelBottom - panelHeight), width, Math.Max(1, panelHeight));
    }
}
