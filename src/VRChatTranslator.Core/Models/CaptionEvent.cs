namespace VRChatTranslator.Core.Models;

public sealed record CaptionEvent(
    DateTimeOffset Timestamp,
    string DetectedLanguage,
    string LanguageName,
    string OriginalText,
    string TranslatedText,
    double? Confidence,
    TimeSpan Latency)
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string TargetLanguage { get; init; } = "en";
    /// <summary>True when the user locked the spoken language; false when it was auto-detected.</summary>
    public bool LanguageLocked { get; init; }
    public string? SpeakerId { get; init; }
    public string? Notice { get; init; }
    public TimeSpan QueueDelay { get; init; }
    public TimeSpan EndToCaptionDelay { get; init; }
}
