using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace VRChatTranslator.Speech;

public sealed class LocalWhisperSpeechService : ILocalSpeechService
{
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private SpeechPipelineSnapshot _current = new();
    private IProcessAudioCaptureService? _audioCapture;
    private PcmSpeechSegmenter? _segmenter;
    private SegmentWorkQueue? _queue;
    private WhisperFactory? _factory;
    private WhisperProcessor? _translationProcessor;
    private WhisperProcessor? _originalProcessor;
    private IEnglishGate? _ownedEnglishGate;
    private WhisperFactory? _gateFactory;
    private long _fastSegments;
    private long _whisperSegments;
    private bool _fastEngineFailed;
    private string _modelName = "Not installed";
    private long _droppedSegments;
    private long _skippedAudioMilliseconds;
    private bool _lastSpeechState;
    private string _lastCaptionText = string.Empty;
    private DateTimeOffset _lastCaptionAt = DateTimeOffset.MinValue;
    private bool _disposed;
    private readonly LanguageStabilizer _languageStabilizer = new();
    private string _sourceLanguage = "auto";
    private bool _translateToEnglish;
    private long _suppressedCaptions;
    private long _captionGeneration;
    private readonly SessionSpeakerLabeler _speakers = new();
    private volatile bool _speakerLabelsEnabled;
    public bool SpeakerLabelsEnabled { get => _speakerLabelsEnabled; set => _speakerLabelsEnabled = value; }
    public Func<CaptionEvent, CancellationToken, Task<CaptionEvent>>? TransformCaptionAsync { get; set; }

    /// <summary>
    /// Which detector decides what is speech. The app uses the energy gate: in the 2026-10-13 crowd evaluation Silero gave no
    /// accuracy gain with several voices and was about 0.1 s slower, and the stock-phrase filter already removes the false
    /// captions it was tried for (see VERIFICATION.md). Auto and Silero stay available for experiments when the model file is present.
    /// </summary>
    public VoiceDetectionMode VoiceDetection { get; set; } = VoiceDetectionMode.Energy;

    /// <summary>How sure the Silero detector must be (0 to 1) before sound counts as speech. Higher rejects more non-speech.</summary>
    public float SileroThreshold { get; set; } = 0.5f;

    /// <summary>How long speech must stop before a segment is handed to the recogniser.</summary>
    public int EndSilenceMilliseconds { get; set; } = 350;

    /// <summary>
    /// Whisper normally reads 30 seconds of audio context (1500 positions) even for a 3 second segment. A smaller value (50 per
    /// second of speech) makes short segments much faster but can cost accuracy. 0 keeps Whisper's default. Experimental.
    /// </summary>
    public int AudioContextSize { get; set; }

    /// <summary>
    /// A fast English-only engine used beside Whisper (set before StartAsync; the caller owns and disposes it). Segments that are
    /// English go to it; every other language, and all translation, stays with Whisper. See <see cref="EnglishRouting"/>.
    /// </summary>
    public ISpeechRecognizer? FastEnglishRecognizer { get; set; }

    /// <summary>
    /// Measurement only: send every segment to the fast engine and never load Whisper. The app never sets this.
    /// </summary>
    public bool FastEnglishOnly { get; set; }

    /// <summary>The language check used when the spoken language is on auto-detect. Null builds one from the Whisper model.</summary>
    public IEnglishGate? EnglishGate { get; set; }

    /// <summary>Audio context for the built-in language check: small is much faster, and detection needs far less than recognition.</summary>
    public int EnglishGateAudioContext { get; set; } = 384;

    /// <summary>A smaller Whisper model (for example tiny) used only for the language check, which is much faster than using the main model. Optional.</summary>
    public string? EnglishGateModelPath { get; set; }
    public void DiscardPendingCaptions() => Interlocked.Increment(ref _captionGeneration);

    public event EventHandler<SpeechPipelineChangedEventArgs>? StateChanged;
    public event EventHandler<CaptionProducedEventArgs>? CaptionProduced;
    public event EventHandler<SpeechSkippedEventArgs>? SpeechSkipped;

    public SpeechPipelineSnapshot Current
    {
        get
        {
            lock (_stateGate)
            {
                return _current;
            }
        }
    }

    public async Task StartAsync(
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
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(audioCapture);
        if (!File.Exists(modelPath))
        {
            Publish(new SpeechPipelineSnapshot { State = SpeechPipelineState.ModelMissing });
            return;
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_queue is { IsRunning: true })
            {
                return;
            }
            if (_factory is not null) await StopCoreAsync().ConfigureAwait(false);

            Publish(new SpeechPipelineSnapshot
            {
                State = SpeechPipelineState.LoadingModel,
                ModelName = Path.GetFileName(modelPath),
                Backend = "CPU"
            });

            _modelName = Path.GetFileName(modelPath);
            _sourceLanguage = LanguageCatalog.SourceLanguages.Any(l => l.Code == sourceLanguage) ? sourceLanguage : "auto";
            _translateToEnglish = translateToEnglish;
            RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
            if (!FastEnglishOnly) await Task.Run(() =>
            {
                _factory = WhisperFactory.FromPath(modelPath);
                var translationBuilder = _factory.CreateBuilder()
                    .WithLanguage(_sourceLanguage)
                    .WithThreads(Math.Clamp(threads, 1, 8))
                    .WithProbabilities()
                    .WithNoSpeechThreshold(0.6f)
                    .WithNoContext()
                    .WithSingleSegment();
                if (!string.IsNullOrWhiteSpace(vocabulary)) translationBuilder.WithPrompt(vocabulary[..Math.Min(vocabulary.Length, 1000)]);
                if (AudioContextSize > 0) translationBuilder.WithAudioContextSize(AudioContextSize);
                if (translateToEnglish)
                {
                    translationBuilder.WithTranslate();
                }

                _translationProcessor = translationBuilder.Build();

                if (showOriginalText && translateToEnglish)
                {
                    var originalBuilder = _factory.CreateBuilder()
                        .WithLanguage(_sourceLanguage)
                        .WithThreads(Math.Clamp(threads, 1, 8))
                        .WithNoContext()
                        .WithSingleSegment();
                    if (AudioContextSize > 0) originalBuilder.WithAudioContextSize(AudioContextSize);
                    _originalProcessor = originalBuilder.Build();
                }

                if (FastEnglishRecognizer is not null && EnglishGate is null && EnglishRouting.NeedsLanguageCheck(_sourceLanguage))
                {
                    var gateModel = EnglishGateModelPath;
                    if (!string.IsNullOrWhiteSpace(gateModel) && File.Exists(gateModel)) _gateFactory = WhisperFactory.FromPath(gateModel);
                    _ownedEnglishGate = new WhisperEnglishGate((_gateFactory ?? _factory).CreateBuilder()
                        .WithLanguage("auto")
                        .WithThreads(Math.Clamp(threads, 1, 8))
                        .WithAudioContextSize(Math.Max(1, EnglishGateAudioContext))
                        .Build());
                }
            }, cancellationToken).ConfigureAwait(false);

            _segmenter = new PcmSpeechSegmenter(
                SpeechActivityFactory.Create(VoiceDetection, vadThreshold, sileroThreshold: SileroThreshold), maximumSegmentSeconds, EndSilenceMilliseconds);
            Interlocked.Exchange(ref _droppedSegments, 0);
            Interlocked.Exchange(ref _skippedAudioMilliseconds, 0);
            Interlocked.Exchange(ref _fastSegments, 0);
            Interlocked.Exchange(ref _whisperSegments, 0);
            _queue =new SegmentWorkQueue(Math.Clamp(maximumQueueLength, 1, 5), ProcessSegmentAsync, RecordSkipped, OnWorkerFaulted);
            _audioCapture = audioCapture;
            _audioCapture.AudioChunkAvailable += OnAudioChunkAvailable;
            Publish(new SpeechPipelineSnapshot
            {
                State = SpeechPipelineState.Listening,
                ModelName = _modelName,
                Backend = "CPU",
                VoiceDetection = _segmenter.VoiceDetection
            });
        }
        catch (OperationCanceledException)
        {
            await StopCoreAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await StopCoreAsync().ConfigureAwait(false);
            Publish(new SpeechPipelineSnapshot
            {
                State = SpeechPipelineState.Faulted,
                ModelName = _modelName,
                Backend = "CPU",
                LastError = $"Local Whisper failed to start: {exception.Message}"
            });
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
            Publish(new SpeechPipelineSnapshot
            {
                State = SpeechPipelineState.Stopped,
                ModelName = _modelName,
                Backend = "CPU",
                DroppedSegments = Interlocked.Read(ref _droppedSegments),
                SkippedSpeechSeconds = SkippedSeconds
            });
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _speakers.Dispose();
        _disposed = true;
        _lifecycleGate.Dispose();
    }

    private void OnAudioChunkAvailable(object? sender, AudioChunkAvailableEventArgs eventArgs)
    {
        var segmenter = _segmenter;
        var queue = _queue;
        if (segmenter is null || queue is null)
        {
            return;
        }

        var result = segmenter.Accept(eventArgs.Chunk);
        if (_lastSpeechState != result.IsSpeechActive)
        {
            _lastSpeechState = result.IsSpeechActive;
            Publish(Current with { IsSpeechDetected = result.IsSpeechActive });
        }

        if (result.CompletedSegment is null)
        {
            return;
        }

        // When the queue is full it drops the oldest segment and calls RecordSkipped, so the newest speech is kept.
        queue.Enqueue(new QueuedSegment(result.CompletedSegment, DateTimeOffset.UtcNow,
            eventArgs.Chunk.CapturedAt - TimeSpan.FromSeconds(result.TrailingSilenceSamples / 16000d), Interlocked.Read(ref _captionGeneration)));
        Publish(Current with
        {
            QueueLength = queue.Count,
            DroppedSegments = Interlocked.Read(ref _droppedSegments),
            SkippedSpeechSeconds = SkippedSeconds
        });
    }

    private double SkippedSeconds => Interlocked.Read(ref _skippedAudioMilliseconds) / 1000d;

    private void RecordSkipped(SkipReason reason, QueuedSegment segment, int queueLength)
    {
        Interlocked.Increment(ref _droppedSegments);
        var audio = TimeSpan.FromSeconds(segment.Samples.Length / 16000d);
        Interlocked.Add(ref _skippedAudioMilliseconds, (long)audio.TotalMilliseconds);
        try
        {
            SpeechSkipped?.Invoke(this, new SpeechSkippedEventArgs(
                new SkippedSpeech(reason, audio, DateTimeOffset.UtcNow - segment.QueuedAt, queueLength)));
        }
        catch
        {
            // A faulty listener must never break audio capture or recognition.
        }
    }

    private void OnWorkerFaulted(Exception exception) =>
        Publish(Current with
        {
            State = SpeechPipelineState.Faulted,
            LastError = $"Local transcription failed: {exception.Message}",
            IsSpeechDetected = false
        });

    private async Task ProcessSegmentAsync(QueuedSegment segment, CancellationToken cancellationToken)
    {
        var samples = segment.Samples;
        if (segment.Generation != Interlocked.Read(ref _captionGeneration)) return;
        var queueDelay = DateTimeOffset.UtcNow - segment.QueuedAt;
        var stopwatch = Stopwatch.StartNew();
        Publish(Current with { State = SpeechPipelineState.Transcribing, IsSpeechDetected = false });

        WhisperTextResult? fastResult = null;
        var fast = FastEnglishRecognizer;
        if (fast is not null && !_fastEngineFailed && await ChooseFastEnglishAsync(samples, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                var heard = await fast.RecognizeAsync(samples, cancellationToken).ConfigureAwait(false);
                fastResult = new WhisperTextResult(heard.Text, heard.Language, heard.Probability);
            }
            catch (Exception exception) when (exception is not OperationCanceledException && !FastEnglishOnly)
            {
                // A broken fast engine must never silence captions: Whisper takes over for the rest of the session.
                _fastEngineFailed = true;
                Publish(Current with { LastError = $"The fast English engine stopped ({exception.Message}); Whisper is handling all speech." });
            }
        }

        WhisperTextResult translatedResult;
        if (fastResult is not null)
        {
            translatedResult = fastResult;
            Interlocked.Increment(ref _fastSegments);
        }
        else
        {
            var translationProcessor = _translationProcessor
                ?? throw new InvalidOperationException("The translation processor is not loaded.");
            translatedResult = await ReadTextAsync(translationProcessor, samples, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _whisperSegments);
        }

        var languageCode = _sourceLanguage != "auto" ? _sourceLanguage : _languageStabilizer.Resolve(
            translatedResult.Language,
            translatedResult.Probability,
            DateTimeOffset.UtcNow);
        var translated = translatedResult.Text;
        var original = _originalProcessor is null || translatedResult.Language == "en"
            ? (!_translateToEnglish || string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase) ? translated : string.Empty)
            : (await ReadTextAsync(_originalProcessor, samples, cancellationToken).ConfigureAwait(false)).Text;

        stopwatch.Stop();
        translated = NormalizeCaption(translated);
        original = NormalizeCaption(original);
        if (CaptionQualityGate.ShouldSuppress(translated, translatedResult.Probability) || IsDuplicate(translated))
        {
            Interlocked.Increment(ref _suppressedCaptions);
            PublishListening(languageCode, stopwatch.Elapsed);
            return;
        }

        var caption = new CaptionEvent(
            DateTimeOffset.UtcNow,
            languageCode,
            LanguageCatalog.GetName(languageCode),
            original,
            translated,
            translatedResult.Probability,
            stopwatch.Elapsed)
        {
            TargetLanguage = _translateToEnglish ? "en" : languageCode,
            LanguageLocked = _sourceLanguage != "auto",
            QueueDelay = queueDelay,
            EndToCaptionDelay = DateTimeOffset.UtcNow - segment.SpeechEndedAt
        };
        if (_speakerLabelsEnabled)
        {
            try { caption = caption with { SpeakerId = _speakers.Label(samples) }; }
            catch { caption = caption with { SpeakerId = "Unknown speaker", Notice = "Speaker analysis unavailable. Captions continue without attribution." }; }
            if (!_speakerLabelsEnabled) caption = caption with { SpeakerId = null };
        }
        else _speakers.Reset();
        if (TransformCaptionAsync is { } transform) caption = await transform(caption, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (segment.Generation != Interlocked.Read(ref _captionGeneration)) { PublishListening(languageCode, stopwatch.Elapsed); return; }
        caption = caption with { EndToCaptionDelay = DateTimeOffset.UtcNow - segment.SpeechEndedAt };
        _lastCaptionText = translated;
        _lastCaptionAt = caption.Timestamp;
        CaptionProduced?.Invoke(this, new CaptionProducedEventArgs(caption));
        Publish(Current with { QueueDelay = queueDelay, EndToCaptionDelay = caption.EndToCaptionDelay });
        PublishListening(languageCode, stopwatch.Elapsed);
    }

    /// <summary>Decides whether the fast English engine hears this segment. Never true for a language other than English.</summary>
    private async Task<bool> ChooseFastEnglishAsync(ReadOnlyMemory<float> samples, CancellationToken cancellationToken)
    {
        if (FastEnglishOnly) return true;
        if (!EnglishRouting.NeedsLanguageCheck(_sourceLanguage)) return EnglishRouting.UseFastEngine(_sourceLanguage, 0);
        var gate = EnglishGate ?? _ownedEnglishGate;
        if (gate is null) return false;
        try
        {
            var probability = await gate.EnglishProbabilityAsync(samples, cancellationToken).ConfigureAwait(false);
            return EnglishRouting.UseFastEngine(_sourceLanguage, probability);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false; // when the language is uncertain, Whisper decides
        }
    }

    private static async Task<WhisperTextResult> ReadTextAsync(
        WhisperProcessor processor,
        ReadOnlyMemory<float> samples,
        CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var language = "und";
        double probabilityTotal = 0;
        var segmentCount = 0;
        await foreach (var segment in processor.ProcessAsync(samples, cancellationToken))
        {
            text.Append(segment.Text);
            if (!string.IsNullOrWhiteSpace(segment.Language))
            {
                language = segment.Language;
            }

            probabilityTotal += Math.Clamp(segment.Probability, 0, 1);
            segmentCount++;
        }

        return new WhisperTextResult(
            text.ToString(),
            language,
            segmentCount == 0 ? 0 : probabilityTotal / segmentCount);
    }

    private void PublishListening(string languageCode, TimeSpan latency)
    {
        Publish(Current with
        {
            State = SpeechPipelineState.Listening,
            DetectedLanguage = LanguageCatalog.GetName(languageCode),
            TranscriptionLatency = latency,
            QueueLength = _queue?.Count ?? 0,
            DroppedSegments = Interlocked.Read(ref _droppedSegments),
            SkippedSpeechSeconds = SkippedSeconds,
            FastEnglishSegments = Interlocked.Read(ref _fastSegments),
            WhisperSegments = Interlocked.Read(ref _whisperSegments),
            SuppressedCaptions = Interlocked.Read(ref _suppressedCaptions),
            IsSpeechDetected = false,
            LastError = null
        });
    }

    private bool IsDuplicate(string text) =>
        string.Equals(text, _lastCaptionText, StringComparison.OrdinalIgnoreCase) &&
        DateTimeOffset.UtcNow - _lastCaptionAt < TimeSpan.FromSeconds(4);

    private static string NormalizeCaption(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();

    private async Task StopCoreAsync()
    {
        DiscardPendingCaptions();
        if (_audioCapture is not null)
        {
            _audioCapture.AudioChunkAvailable -= OnAudioChunkAvailable;
            _audioCapture = null;
        }

        if (_queue is not null)
        {
            await _queue.DisposeAsync().ConfigureAwait(false);
            _queue = null;
        }

        _segmenter?.Dispose();
        _segmenter = null;
        if (_translationProcessor is not null)
        {
            await _translationProcessor.DisposeAsync().ConfigureAwait(false);
            _translationProcessor = null;
        }

        if (_originalProcessor is not null)
        {
            await _originalProcessor.DisposeAsync().ConfigureAwait(false);
            _originalProcessor = null;
        }

        if (_ownedEnglishGate is not null)
        {
            await _ownedEnglishGate.DisposeAsync().ConfigureAwait(false);
            _ownedEnglishGate = null;
        }

        _gateFactory?.Dispose();
        _gateFactory = null;

        _fastEngineFailed = false;

        _factory?.Dispose();
        _factory = null;
        _lastSpeechState = false;
        _languageStabilizer.Reset();
        _lastCaptionText = "";
    }

    private void Publish(SpeechPipelineSnapshot snapshot)
    {
        lock (_stateGate)
        {
            _current = snapshot;
        }

        StateChanged?.Invoke(this, new SpeechPipelineChangedEventArgs(snapshot));
    }

    private sealed record WhisperTextResult(string Text, string Language, double Probability);
}
