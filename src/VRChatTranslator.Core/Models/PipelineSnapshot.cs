namespace VRChatTranslator.Core.Models;

public sealed record PipelineSnapshot
{
    public bool VrchatDetected { get; init; }
    public int? VrchatProcessId { get; init; }
    public CaptureMode CaptureMode { get; init; } = CaptureMode.Unavailable;
    public bool IsCapturing { get; init; }
    public bool IsSpeechDetected { get; init; }
    public bool IsModelLoaded { get; init; }
    public string InferenceBackend { get; init; } = "Not loaded";
    public string DetectedLanguage { get; init; } = "—";
    public TimeSpan? CaptionLatency { get; init; }
    public int AudioQueueLength { get; init; }
    public long DroppedAudioChunks { get; init; }
    public string? LastError { get; init; }
}

