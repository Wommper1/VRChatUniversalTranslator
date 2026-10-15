using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Core.Services;

public interface ILocalSpeechService : IAsyncDisposable
{
    event EventHandler<SpeechPipelineChangedEventArgs>? StateChanged;
    event EventHandler<CaptionProducedEventArgs>? CaptionProduced;

    /// <summary>Raised when speech is skipped to stay live. May be raised on the audio thread, so handlers must return quickly.</summary>
    event EventHandler<SpeechSkippedEventArgs>? SpeechSkipped;

    SpeechPipelineSnapshot Current { get; }
    Func<CaptionEvent, CancellationToken, Task<CaptionEvent>>? TransformCaptionAsync { get; set; }
    void DiscardPendingCaptions();
    bool SpeakerLabelsEnabled { get; set; }

    /// <summary>
    /// An optional fast English-only engine used beside Whisper. Set before StartAsync; the caller owns and disposes it.
    /// English speech goes to it, other languages and all translation stay with Whisper.
    /// </summary>
    ISpeechRecognizer? FastEnglishRecognizer { get; set; }

    /// <summary>A small Whisper model used only to check whether speech is English; null uses the main speech model, which is slower.</summary>
    string? EnglishGateModelPath { get; set; }

    /// <summary>Whisper audio context override (0 keeps the default). Set before StartAsync; smaller is faster for short segments.</summary>
    int AudioContextSize { get; set; }

    Task StartAsync(
        IProcessAudioCaptureService audioCapture,
        string modelPath,
        int threads,
        bool showOriginalText,
        double vadThreshold = 0.008,
        int maximumSegmentSeconds = 12,
        int maximumQueueLength = 3,
        bool translateToEnglish = true,
        string sourceLanguage = "auto",
        string? vocabulary = null,
        CancellationToken cancellationToken = default);

    Task StopAsync();
}
