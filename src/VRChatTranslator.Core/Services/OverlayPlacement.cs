namespace VRChatTranslator.Core.Services;

public static class OverlayPlacement
{
    /// <summary>
    /// Keeps one axis of a window inside the visible screen area. A saved position from a monitor that is no
    /// longer connected is pulled back on-screen, a non-finite saved value falls back to the screen start, and a
    /// window larger than the screen is anchored at the screen start instead of throwing.
    /// </summary>
    public static double ClampAxis(double position, double screenStart, double screenExtent, double windowSize) =>
        Math.Clamp(double.IsFinite(position) ? position : 0, screenStart,
            Math.Max(screenStart, screenStart + screenExtent - windowSize));
}
