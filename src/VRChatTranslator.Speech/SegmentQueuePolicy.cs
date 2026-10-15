namespace VRChatTranslator.Speech;

/// <summary>
/// When the recogniser is behind, keep the newest speech. A waiting segment is skipped only when it is old and a newer
/// one is already queued; the last segment is always shown, late if need be, because nothing newer would replace it.
/// </summary>
public static class SegmentQueuePolicy
{
    /// <summary>A segment older than this is skipped if a newer one is already waiting.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(6);

    /// <summary>A segment older than this is skipped even if it is the last one: a caption this late would only confuse.</summary>
    public static readonly TimeSpan TooLate = TimeSpan.FromSeconds(20);

    public static bool ShouldSkip(TimeSpan waited, bool newerSegmentWaiting) =>
        waited > TooLate || (waited > StaleAfter && newerSegmentWaiting);
}
