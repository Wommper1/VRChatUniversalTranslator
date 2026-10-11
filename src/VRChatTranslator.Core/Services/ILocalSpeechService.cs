using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Core.Services;

public interface ILocalSpeechService : IAsyncDisposable
{
    event EventHandler<SpeechPipelineChangedEventArgs>? StateChanged;
    event EventHandler<CaptionProducedEventArgs>? CaptionProduced;

    SpeechPipelineSnapshot Current { get; }
    Func<CaptionEvent, CancellationToken, Task<CaptionEvent>>? TransformCaptionAsync { get; set; }
    void DiscardPendingCaptions();
    bool SpeakerLabelsEnabled { get; set; }

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
