namespace VRChatTranslator.Core.Models;

public sealed record AudioCaptureSnapshot
{
    public AudioCaptureState State { get; init; } = AudioCaptureState.Stopped;
    public CaptureMode Mode { get; init; } = CaptureMode.Unavailable;
    public int? TargetProcessId { get; init; }
    public AudioFormatInfo? Format { get; init; }
    public double PeakLevel { get; init; }
    public double RmsLevel { get; init; }
    public long TotalBytesCaptured { get; init; }
    public long DroppedChunks { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public string? LastError { get; init; }

    public bool IsActive => State is AudioCaptureState.Starting or AudioCaptureState.Capturing;
}

