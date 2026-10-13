namespace VRChatTranslator.Core.Models;

public sealed record SpeechPipelineSnapshot
{
    public SpeechPipelineState State { get; init; } = SpeechPipelineState.Stopped;
    public string ModelName { get; init; } = "Not installed";
    public string Backend { get; init; } = "CPU";
    /// <summary>Which detector decides what counts as speech (for the log and diagnostics).</summary>
    public string VoiceDetection { get; init; } = "—";
    public string DetectedLanguage { get; init; } = "—";
    public TimeSpan? TranscriptionLatency { get; init; }
    public TimeSpan? QueueDelay { get; init; }
    public TimeSpan? EndToCaptionDelay { get; init; }
    public long SuppressedCaptions { get; init; }
    public int QueueLength { get; init; }
    public long DroppedSegments { get; init; }
    public bool IsSpeechDetected { get; init; }
    public string? LastError { get; init; }
}
