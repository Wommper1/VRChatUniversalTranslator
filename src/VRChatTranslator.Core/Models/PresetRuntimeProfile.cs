namespace VRChatTranslator.Core.Models;

public sealed record PresetRuntimeProfile(
    PerformanceMode Mode,
    string ModelFileName,
    int CpuThreads,
    double VadThreshold,
    int MaximumSegmentSeconds,
    int MaximumQueueLength,
    string BackendPolicy)
{
    public static PresetRuntimeProfile For(PerformanceMode mode) => mode switch
    {
        PerformanceMode.UltraLight => new(mode, "ggml-tiny.bin", 2, 0.010, 5, 2, "CPU only · lowest latency"),
        PerformanceMode.GpuFast => new(mode, "ggml-base.bin", 4, 0.008, 4, 2, "CPU · shorter utterances · no GPU runtime enabled"),
        PerformanceMode.Accurate => new(mode, "ggml-small.bin", 6, 0.006, 8, 3, "CPU; higher accuracy"),
        PerformanceMode.Custom => new(mode, "ggml-base.bin", 4, 0.008, 6, 3, "Safe custom defaults"),
        _ => new(PerformanceMode.Balanced, "ggml-base.bin", 4, 0.008, 6, 2, "CPU tuned for Ryzen 7 5800X · live queue")
    };
}
