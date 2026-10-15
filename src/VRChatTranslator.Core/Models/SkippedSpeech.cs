namespace VRChatTranslator.Core.Models;

/// <summary>Why a stretch of speech was not sent to the recogniser.</summary>
public enum SkipReason
{
    /// <summary>The queue was full, so the oldest waiting speech made room for newer speech.</summary>
    QueueFull,

    /// <summary>The speech had waited so long that a newer segment was ready, or it was too old to be worth showing.</summary>
    TooOld
}

/// <summary>One stretch of speech the recogniser did not get to. It carries lengths only, never audio or text.</summary>
public sealed record SkippedSpeech(SkipReason Reason, TimeSpan Audio, TimeSpan Waited, int QueueLength);
