using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Core.Services;

/// <summary>Plain-language text and timing rules for telling the user that some speech was skipped to stay live.</summary>
public static class SkippedSpeechNotice
{
    /// <summary>Short text for the in-game overlay.</summary>
    public const string OverlayText = "Skipped some speech · catching up";

    /// <summary>The overlay note is shown at most this often, so a long busy stretch does not flicker.</summary>
    public static readonly TimeSpan OverlayInterval = TimeSpan.FromSeconds(8);

    /// <summary>One line for the status bar and the log. Lengths only, never caption text.</summary>
    public static string Describe(SkippedSpeech skipped, double totalSecondsThisSession)
    {
        ArgumentNullException.ThrowIfNull(skipped);
        var why = skipped.Reason == SkipReason.QueueFull
            ? "the queue was full"
            : "newer speech was already waiting";
        return $"Catching up: skipped {skipped.Audio.TotalSeconds:0.0} s of speech because {why}, so the newest captions stay live "
            + $"(it waited {skipped.Waited.TotalSeconds:0.0} s). Skipped this session: {totalSecondsThisSession:0.0} s.";
    }

    /// <summary>True when enough time has passed since the last overlay note.</summary>
    public static bool ShouldShowOverlay(DateTimeOffset lastShown, DateTimeOffset now) => now - lastShown >= OverlayInterval;
}
