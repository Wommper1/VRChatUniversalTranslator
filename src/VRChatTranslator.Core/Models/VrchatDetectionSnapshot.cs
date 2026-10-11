namespace VRChatTranslator.Core.Models;

public sealed record VrchatDetectionSnapshot(
    bool IsDetected,
    int? ProcessId,
    DateTimeOffset? ProcessStartedAt,
    DateTimeOffset ObservedAt,
    string? LastError)
{
    public static VrchatDetectionSnapshot Waiting(DateTimeOffset observedAt) =>
        new(false, null, null, observedAt, null);
}

