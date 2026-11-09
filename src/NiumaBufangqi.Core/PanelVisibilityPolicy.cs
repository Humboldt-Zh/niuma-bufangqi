namespace NiumaBufangqi.Core;

/// <summary>Input intent, independent of a particular window or taskbar implementation.</summary>
public enum PanelInteraction
{
    EntryClick,
    LostFocus,
    OpenRequested,
    CloseRequested
}

/// <summary>
/// Decides the next visibility from the host's actual window state. This policy deliberately
/// owns no visibility state, so external closes and native focus changes cannot leave a second
/// toggle flag out of sync with the window.
/// </summary>
public static class PanelVisibilityPolicy
{
    public static bool GetNextVisibility(
        bool isVisible,
        PanelInteraction interaction,
        bool configured,
        bool pointerOverTaskbarEntry = false) => interaction switch
        {
            PanelInteraction.EntryClick => !isVisible,
            // Clicking the same entry must reach its toggle before light-dismiss hides the panel.
            PanelInteraction.LostFocus => isVisible && (!configured || pointerOverTaskbarEntry),
            PanelInteraction.OpenRequested => true,
            PanelInteraction.CloseRequested => false,
            _ => throw new ArgumentOutOfRangeException(nameof(interaction), interaction, "Unknown panel interaction.")
        };
}
