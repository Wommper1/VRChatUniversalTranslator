using System.Runtime.Versioning;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;

namespace VRChatTranslator.Audio;

[SupportedOSPlatform("windows10.0.20348")]
public sealed class WasapiProcessAudioCaptureService : IProcessAudioCaptureService
{
    public const int RequestedSampleRate = 48_000;
    public const int RequestedChannels = 2;
    public const int QueueCapacity = 64;

    private static readonly TimeSpan MeterPublishInterval = TimeSpan.FromMilliseconds(33);
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly Channel<AudioChunk> _audioChannel = Channel.CreateBounded<AudioChunk>(
        new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });
    private WasapiRecorder? _recorder;
    private AudioCaptureSnapshot _current = new();
    private AudioFormatInfo? _activeFormat;
    private DateTimeOffset _lastMeterPublish = DateTimeOffset.MinValue;
    private long _totalBytes;
    private long _droppedChunks;
    private bool _intentionalStop;
    private bool _disposed;

    public event EventHandler<AudioCaptureChangedEventArgs>? CaptureChanged;
    public event EventHandler<AudioChunkAvailableEventArgs>? AudioChunkAvailable;

    public ChannelReader<AudioChunk> AudioChunks => _audioChannel.Reader;

    public AudioCaptureSnapshot Current
    {
        get
        {
            lock (_stateGate)
            {
                return _current;
            }
        }
    }

    public async Task StartAsync(int processId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
        {
            throw new PlatformNotSupportedException(
                "VRCHAT ONLY capture requires Windows 10 build 20348 or later. This application targets Windows 11.");
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_current.IsActive && _current.TargetProcessId == processId)
            {
                return;
            }

            await StopRecorderCoreAsync().ConfigureAwait(false);
            ResetCounters();
            Publish(new AudioCaptureSnapshot
            {
                State = AudioCaptureState.Starting,
                Mode = CaptureMode.VrchatProcessOnly,
                TargetProcessId = processId
            });

            try
            {
                var requestedFormat = WaveFormat.CreateIeeeFloatWaveFormat(RequestedSampleRate, RequestedChannels);
                var recorder = await new WasapiRecorderBuilder()
                    .WithProcessLoopback((uint)processId, ProcessLoopbackMode.IncludeTargetProcessTree)
                    .WithFormat(requestedFormat)
                    .WithBufferLength(50)
                    .WithMmcssThreadPriority("Audio")
                    .BuildAsync()
                    .ConfigureAwait(false);

                _activeFormat = new AudioFormatInfo(
                    recorder.WaveFormat.SampleRate,
                    recorder.WaveFormat.Channels,
                    recorder.WaveFormat.BitsPerSample,
                    "IEEE float");
                _intentionalStop = false;
                recorder.DataAvailable += OnDataAvailable;
                recorder.RecordingStopped += OnRecordingStopped;
                _recorder = recorder;
                recorder.StartRecording();

                Publish(new AudioCaptureSnapshot
                {
                    State = AudioCaptureState.Capturing,
                    Mode = CaptureMode.VrchatProcessOnly,
                    TargetProcessId = processId,
                    Format = _activeFormat,
                    StartedAt = DateTimeOffset.UtcNow
                });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await StopRecorderCoreAsync().ConfigureAwait(false);
                Publish(new AudioCaptureSnapshot
                {
                    State = AudioCaptureState.Faulted,
                    Mode = CaptureMode.VrchatProcessOnly,
                    TargetProcessId = processId,
                    LastError = $"VRChat-only audio capture failed: {exception.Message}"
                });
                throw;
            }
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
            if (_recorder is null && _current.State == AudioCaptureState.Stopped)
            {
                return;
            }

            var previous = Current;
            Publish(previous with { State = AudioCaptureState.Stopping, PeakLevel = 0, RmsLevel = 0 });
            await StopRecorderCoreAsync().ConfigureAwait(false);
            Publish(new AudioCaptureSnapshot
            {
                State = AudioCaptureState.Stopped,
                Mode = CaptureMode.Unavailable,
                TotalBytesCaptured = Interlocked.Read(ref _totalBytes),
                DroppedChunks = Interlocked.Read(ref _droppedChunks)
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
        _disposed = true;
        _audioChannel.Writer.TryComplete();
        _lifecycleGate.Dispose();
    }

    private void OnDataAvailable(
        ReadOnlySpan<byte> buffer,
        AudioClientBufferFlags flags,
        long devicePosition,
        long qpcPosition)
    {
        var format = _activeFormat;
        if (format is null || buffer.IsEmpty)
        {
            return;
        }

        var capturedAt = DateTimeOffset.UtcNow;
        var data = buffer.ToArray();
        Interlocked.Add(ref _totalBytes, data.Length);

        if (_audioChannel.Reader.CanCount && _audioChannel.Reader.Count >= QueueCapacity)
        {
            Interlocked.Increment(ref _droppedChunks);
        }

        var chunk = new AudioChunk(capturedAt, data, format, devicePosition, qpcPosition);
        _audioChannel.Writer.TryWrite(chunk);
        AudioChunkAvailable?.Invoke(this, new AudioChunkAvailableEventArgs(chunk));

        if (capturedAt - _lastMeterPublish < MeterPublishInterval)
        {
            return;
        }

        _lastMeterPublish = capturedAt;
        var (peak, rms) = AudioLevelCalculator.MeasureIeeeFloat32(data);
        var previous = Current;
        Publish(previous with
        {
            State = AudioCaptureState.Capturing,
            PeakLevel = peak,
            RmsLevel = rms,
            TotalBytesCaptured = Interlocked.Read(ref _totalBytes),
            DroppedChunks = Interlocked.Read(ref _droppedChunks)
        });
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (_intentionalStop)
        {
            return;
        }

        var previous = Current;
        Publish(previous with
        {
            State = eventArgs.Exception is null ? AudioCaptureState.Stopped : AudioCaptureState.Faulted,
            PeakLevel = 0,
            RmsLevel = 0,
            LastError = eventArgs.Exception is null
                ? "The process audio stream stopped unexpectedly."
                : $"The process audio stream stopped: {eventArgs.Exception.Message}"
        });
    }

    private async Task StopRecorderCoreAsync()
    {
        var recorder = _recorder;
        _recorder = null;
        _activeFormat = null;
        if (recorder is null)
        {
            return;
        }

        _intentionalStop = true;
        recorder.DataAvailable -= OnDataAvailable;
        recorder.RecordingStopped -= OnRecordingStopped;
        await recorder.DisposeAsync().ConfigureAwait(false);
    }

    private void ResetCounters()
    {
        Interlocked.Exchange(ref _totalBytes, 0);
        Interlocked.Exchange(ref _droppedChunks, 0);
        _lastMeterPublish = DateTimeOffset.MinValue;
    }

    private void Publish(AudioCaptureSnapshot snapshot)
    {
        lock (_stateGate)
        {
            _current = snapshot;
        }

        CaptureChanged?.Invoke(this, new AudioCaptureChangedEventArgs(snapshot));
    }
}
