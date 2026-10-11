namespace VRChatTranslator.Core.Models;

public sealed record PerformancePreset(
    PerformanceMode Mode,
    string DisplayName,
    string Description,
    string ResourcePolicy)
{
    public override string ToString() => DisplayName;

    public static IReadOnlyList<PerformancePreset> BuiltIn { get; } =
    [
        new(PerformanceMode.UltraLight, "🪶 Ultra Light", "Lowest gaming impact", "CPU preferred; smallest multilingual model"),
        new(PerformanceMode.Balanced, "⚖ Balanced", "Best everyday mode", "Bounded local inference; benchmark-selected backend"),
        new(PerformanceMode.GpuFast, "⚡ Responsive CPU", "Shorter chunks with the base model", "CPU runtime in this build; GPU acceleration is not enabled"),
        new(PerformanceMode.Accurate, "🧠 Accurate", "Better understanding, heavier processing", "Larger multilingual model with an explicit load warning"),
        new(PerformanceMode.Custom, "🔧 Custom", "Advanced safe tuning", "Validated model, backend, queue, VAD, and thread limits")
    ];
}
