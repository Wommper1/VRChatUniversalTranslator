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
    /// <summary>Total length of speech skipped to stay live since listening started.</summary>
    public double SkippedSpeechSeconds { get; init; }
    /// <summary>Segments captioned by the fast English engine since listening started.</summary>
    public long FastEnglishSegments { get; init; }
    /// <summary>Segments captioned by Whisper since listening started.</summary>
    public long WhisperSegments { get; init; }
    public bool IsSpeechDetected { get; init; }
    public string? LastError { get; init; }
}
