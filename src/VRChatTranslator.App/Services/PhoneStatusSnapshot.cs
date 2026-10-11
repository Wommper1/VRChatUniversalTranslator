namespace VRChatTranslator.App.Services;

public sealed record PhoneStatusSnapshot(
    bool VrchatDetected,
    bool Capturing,
    string SpeechState,
    string DetectedLanguage,
    double? LatencyMilliseconds,
    int QueueLength,
    double AudioLevelPercent = 0,
    string Model = "—",
    string Mode = "Balanced",
    double? TranslatorCpuPercent = null,
    double? SystemCpuPercent = null,
    double? GpuPercent = null,
    string? LastError = null);
