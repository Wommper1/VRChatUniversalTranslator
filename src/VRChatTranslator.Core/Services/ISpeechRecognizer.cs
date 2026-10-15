namespace VRChatTranslator.Core.Services;

/// <summary>What a recogniser heard in one segment. <see cref="Probability"/> is 0 to 1; use 1 when the engine reports no confidence.</summary>
public sealed record RecognitionResult(string Text, string Language, double Probability);

/// <summary>
/// An engine that turns one segment of 16 kHz mono speech into text. Whisper is built in; this seam lets a faster English-only
/// engine run beside it. Such an engine does not translate: it returns the text as heard.
/// </summary>
public interface ISpeechRecognizer : IDisposable
{
    string Name { get; }

    Task<RecognitionResult> RecognizeAsync(ReadOnlyMemory<float> samples, CancellationToken cancellationToken);
}

/// <summary>Says how likely a segment is spoken English, so the fast English engine is never used on other languages.</summary>
public interface IEnglishGate : IAsyncDisposable
{
    /// <summary>Probability from 0 to 1 that the segment is English speech. Zero when another language was detected.</summary>
    Task<double> EnglishProbabilityAsync(ReadOnlyMemory<float> samples, CancellationToken cancellationToken);
}
