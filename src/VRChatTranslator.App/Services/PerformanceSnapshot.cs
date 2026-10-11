namespace VRChatTranslator.App.Services;

public sealed record PerformanceSnapshot(
    double SystemCpuPercent,
    double TranslatorCpuPercent,
    double? VrchatCpuPercent,
    long TranslatorMemoryBytes,
    long SystemUsedMemoryBytes,
    long SystemTotalMemoryBytes,
    double? GpuPercent,
    long? GpuUsedMemoryMegabytes,
    long? GpuTotalMemoryMegabytes,
    string? LastError);
