using System;
using System.Drawing;

namespace NiumaBufangqi.Desktop.Taskbar;

internal readonly record struct EntrySlot(Rectangle Bounds, int Dpi, bool Compact);

internal interface IEmbeddedEntry
{
    bool TryGetAllocatedSlot(out EntrySlot slot);
    void SetSuppressed(bool suppressed);
}

internal interface IElevatedEntry
{
    bool TryShow(EntrySlot slot);
    void Hide();
}

internal enum EntryElevationMode { Unavailable, Embedded, Raised }

/// <summary>
/// Switches the visible entry without transferring Explorer's child HWND. The
/// allocated slot remains owned by the embedded entry even while it is hidden.
/// </summary>
internal sealed class EntryElevationCoordinator : IDisposable
{
    private readonly IEmbeddedEntry embedded;
    private readonly IElevatedEntry popup;
    private bool requestedPanelVisible;
    private bool pending;
    private bool synchronizing;
    private bool disposed;

    internal EntryElevationCoordinator(IEmbeddedEntry embedded, IElevatedEntry popup)
    {
        this.embedded = embedded ?? throw new ArgumentNullException(nameof(embedded));
        this.popup = popup ?? throw new ArgumentNullException(nameof(popup));
    }

    internal EntryElevationMode Mode { get; private set; } = EntryElevationMode.Unavailable;

    internal void Synchronize(bool panelVisible)
    {
        if (disposed) return;
        requestedPanelVisible = panelVisible;
        pending = true;
        if (synchronizing) return;
        synchronizing = true;
        Request? previous = null;
        try
        {
            while (pending && !disposed)
            {
                pending = false;
                var hasSlot = embedded.TryGetAllocatedSlot(out var slot)
                    && !EntryElevationPlacement.Calculate(slot).IsEmpty;
                if (disposed) return;
                var request = new Request(requestedPanelVisible, hasSlot, hasSlot ? slot : default);
                // Native show/hide notifications may synchronously request the
                // same state again. Drain changed intent/layout, not duplicates.
                if (request == previous) break;
                previous = request;
                Apply(request);
            }
        }
        catch (Exception error)
        {
            try { Restore(previous is { HasSlot: true }); }
            catch (Exception cleanupError) { throw new AggregateException(error, cleanupError); }
            throw;
        }
        finally { synchronizing = false; }
    }

    private void Apply(Request request)
    {
        if (disposed) return;
        if (request.PanelVisible && request.HasSlot && popup.TryShow(request.Slot))
        {
            if (disposed) { Restore(false); return; }
            // Showing first preserves an entry if popup creation/rendering fails.
            embedded.SetSuppressed(true);
            if (!disposed) Mode = EntryElevationMode.Raised;
            return;
        }
        Restore(request.HasSlot);
    }

    private void Restore(bool hasSlot)
    {
        Mode = disposed || !hasSlot ? EntryElevationMode.Unavailable : EntryElevationMode.Embedded;
        // Always attempt both halves, even if one native surface rejects a call.
        try { embedded.SetSuppressed(false); }
        finally { popup.Hide(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        pending = false;
        Restore(false);
    }

    private readonly record struct Request(bool PanelVisible, bool HasSlot, EntrySlot Slot);
}

/// <summary>Physical-pixel bounds confined to the already allocated native slot.</summary>
internal static class EntryElevationPlacement
{
    internal static Rectangle Calculate(EntrySlot slot)
    {
        var bounds = slot.Bounds;
        var right = (long)bounds.Left + bounds.Width;
        var bottom = (long)bounds.Top + bounds.Height;
        if (slot.Dpi <= 0 || bounds.Width <= 0 || bounds.Height <= 0
            || right > int.MaxValue || bottom > int.MaxValue) return Rectangle.Empty;

        long Scale(int dip) => ((long)dip * slot.Dpi + 48) / 96;
        var insetX = (int)Math.Min(Scale(2), (bounds.Width - 1L) / 2);
        // A short/compact bar must keep its height for readable text.
        var insetY = (int)Math.Min(Scale(3), Math.Max(0, (bounds.Height - Scale(36)) / 2));
        return new Rectangle(bounds.Left + insetX, bounds.Top + insetY,
            bounds.Width - 2 * insetX, bounds.Height - 2 * insetY);
    }
}
