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
    private Channel<QueuedSegment>? _segments;
    private CancellationTokenSource? _workerCancellation;
    private Task? _worker;
    private WhisperFactory? _factory;
    private WhisperProcessor? _translationProcessor;
    private WhisperProcessor? _originalProcessor;
    private string _modelName = "Not installed";
    private long _droppedSegments;
    private bool _lastSpeechState;
    private string _lastCaptionText = string.Empty;
    private DateTimeOffset _lastCaptionAt = DateTimeOffset.MinValue;
    private bool _disposed;
    private int _segmentQueueCapacity = 3;
    private readonly LanguageStabilizer _languageStabilizer = new();
    private string _sourceLanguage = "auto";
    private bool _translateToEnglish;
    private long _suppressedCaptions;
    private long _captionGeneration;
    private readonly SessionSpeakerLabeler _speakers = new();
    private volatile bool _speakerLabelsEnabled;
    public bool SpeakerLabelsEnabled { get => _speakerLabelsEnabled; set => _speakerLabelsEnabled = value; }
    public Func<CaptionEvent, CancellationToken, Task<CaptionEvent>>? TransformCaptionAsync { get; set; }
    public void DiscardPendingCaptions() => Interlocked.Increment(ref _captionGeneration);

    public event EventHandler<SpeechPipelineChangedEventArgs>? StateChanged;
    public event EventHandler<CaptionProducedEventArgs>? CaptionProduced;

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
            if (_worker is { IsCompleted: false })
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
            await Task.Run(() =>
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
                if (translateToEnglish)
                {
                    translationBuilder.WithTranslate();
                }

                _translationProcessor = translationBuilder.Build();

                if (showOriginalText && translateToEnglish)
                {
                    _originalProcessor = _factory.CreateBuilder()
                        .WithLanguage(_sourceLanguage)
                        .WithThreads(Math.Clamp(threads, 1, 8))
                        .WithNoContext()
                        .WithSingleSegment()
                        .Build();
                }
            }, cancellationToken).ConfigureAwait(false);

            _segmentQueueCapacity = Math.Clamp(maximumQueueLength, 1, 5);
            _segmenter = new PcmSpeechSegmenter(vadThreshold, maximumSegmentSeconds);
            _segments = Channel.CreateBounded<QueuedSegment>(new BoundedChannelOptions(_segmentQueueCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = false
            });
            _audioCapture = audioCapture;
            _audioCapture.AudioChunkAvailable += OnAudioChunkAvailable;
            _workerCancellation = new CancellationTokenSource();
            _worker = ProcessSegmentsAsync(_segments.Reader, _workerCancellation.Token);
            Publish(new SpeechPipelineSnapshot
            {
                State = SpeechPipelineState.Listening,
                ModelName = _modelName,
                Backend = "CPU"
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
                DroppedSegments = Interlocked.Read(ref _droppedSegments)
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
        var segments = _segments;
        if (segmenter is null || segments is null)
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

        if (segments.Reader.CanCount && segments.Reader.Count >= _segmentQueueCapacity)
        {
            Interlocked.Increment(ref _droppedSegments);
        }

        segments.Writer.TryWrite(new QueuedSegment(result.CompletedSegment, DateTimeOffset.UtcNow,
            eventArgs.Chunk.CapturedAt - TimeSpan.FromSeconds(result.TrailingSilenceSamples / 16000d), Interlocked.Read(ref _captionGeneration)));
        Publish(Current with
        {
            QueueLength = segments.Reader.CanCount ? segments.Reader.Count : 0,
            DroppedSegments = Interlocked.Read(ref _droppedSegments)
        });
    }

    private async Task ProcessSegmentsAsync(ChannelReader<QueuedSegment> segments, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var segment in segments.ReadAllAsync(cancellationToken))
            {
                if (DateTimeOffset.UtcNow - segment.QueuedAt > TimeSpan.FromSeconds(6))
                {
                    Interlocked.Increment(ref _droppedSegments);
                    continue;
                }
                await ProcessSegmentAsync(segment, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Publish(Current with
            {
                State = SpeechPipelineState.Faulted,
                LastError = $"Local transcription failed: {exception.Message}",
                IsSpeechDetected = false
            });
        }
    }

    private async Task ProcessSegmentAsync(QueuedSegment segment, CancellationToken cancellationToken)
    {
        var samples = segment.Samples;
        if (segment.Generation != Interlocked.Read(ref _captionGeneration)) return;
        var queueDelay = DateTimeOffset.UtcNow - segment.QueuedAt;
        var translationProcessor = _translationProcessor
            ?? throw new InvalidOperationException("The translation processor is not loaded.");
        var stopwatch = Stopwatch.StartNew();
        Publish(Current with { State = SpeechPipelineState.Transcribing, IsSpeechDetected = false });

        var translatedResult = await ReadTextAsync(translationProcessor, samples, cancellationToken).ConfigureAwait(false);
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
            QueueLength = _segments?.Reader.CanCount == true ? _segments.Reader.Count : 0,
            DroppedSegments = Interlocked.Read(ref _droppedSegments),
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

        _segments?.Writer.TryComplete();
        _workerCancellation?.Cancel();
        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _worker = null;
        _workerCancellation?.Dispose();
        _workerCancellation = null;
        _segments = null;
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
    private sealed record QueuedSegment(float[] Samples, DateTimeOffset QueuedAt, DateTimeOffset SpeechEndedAt, long Generation);
}
