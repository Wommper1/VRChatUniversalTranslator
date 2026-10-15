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
    /// <summary>
    /// Whisper normally reads 30 seconds of audio context even for a 6 second segment. 768 (15 seconds) made the base model about
    /// 30% faster in the 2026-10-14 measurements for about one extra point of word errors; see VERIFICATION.md. 0 keeps Whisper's default.
    /// </summary>
    public const int BaseModelAudioContext = 768;

    /// <summary>Audio context override for Whisper, or 0 for the default. Only set for presets that were measured.</summary>
    public int AudioContextSize { get; init; }

    public static PresetRuntimeProfile For(PerformanceMode mode) => mode switch
    {
        PerformanceMode.UltraLight => new(mode, "ggml-tiny.bin", 2, 0.010, 5, 2, "CPU only · lowest latency"),
        PerformanceMode.GpuFast => new(mode, "ggml-base.bin", 4, 0.008, 4, 2, "CPU · shorter utterances · no GPU runtime enabled") { AudioContextSize = BaseModelAudioContext },
        PerformanceMode.Accurate => new(mode, "ggml-small.bin", 6, 0.006, 8, 3, "CPU; higher accuracy"),
        PerformanceMode.Custom => new(mode, "ggml-base.bin", 4, 0.008, 6, 3, "Safe custom defaults"),
        _ => new(PerformanceMode.Balanced, "ggml-base.bin", 4, 0.008, 6, 2, "CPU tuned for Ryzen 7 5800X · live queue") { AudioContextSize = BaseModelAudioContext }
    };
}
