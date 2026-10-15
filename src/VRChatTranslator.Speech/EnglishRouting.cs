using VRChatTranslator.Core.Services;
using Whisper.net;

namespace VRChatTranslator.Speech;

/// <summary>
/// Decides which engine hears a segment when the fast English engine is installed and switched on. A wrong "English" would
/// caption another language as nonsense, so the rule is cautious: unless the user locked English, the segment must be
/// detected as English with high confidence. Everything else goes to Whisper, which also translates.
/// </summary>
public static class EnglishRouting
{
    /// <summary>How sure the language check must be (0 to 1) before the fast English engine is used.</summary>
    public const double DetectionThreshold = 0.85;

    /// <summary>True when the language check is needed before choosing: only when the spoken language is left on auto-detect.</summary>
    public static bool NeedsLanguageCheck(string sourceLanguage) =>
        string.Equals(sourceLanguage, "auto", StringComparison.OrdinalIgnoreCase);

    public static bool UseFastEngine(string sourceLanguage, double englishProbability)
    {
        if (string.Equals(sourceLanguage, "en", StringComparison.OrdinalIgnoreCase)) return true;
        return NeedsLanguageCheck(sourceLanguage) && englishProbability >= DetectionThreshold;
    }
}

/// <summary>Uses Whisper's own language detection on a small audio context (fast: only the encoder and one decoder step run).</summary>
public sealed class WhisperEnglishGate(WhisperProcessor processor) : IEnglishGate
{
    private readonly object _gate = new();

    public Task<double> EnglishProbabilityAsync(ReadOnlyMemory<float> samples, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var (language, probability) = processor.DetectLanguageWithProbability(samples.ToArray());
                return string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? Math.Clamp((double)probability, 0, 1) : 0d;
            }
        }, cancellationToken);

    public ValueTask DisposeAsync() => processor.DisposeAsync();
}
