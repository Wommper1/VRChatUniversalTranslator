using System.Threading.Channels;
using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Speech;

/// <summary>One finished stretch of speech waiting for the recogniser (16 kHz mono samples).</summary>
public sealed record QueuedSegment(float[] Samples, DateTimeOffset QueuedAt, DateTimeOffset SpeechEndedAt, long Generation);

/// <summary>
/// Hands finished speech segments to the recogniser one at a time. When the recogniser falls behind it keeps the newest speech:
/// a full queue drops its oldest segment, and <see cref="SegmentQueuePolicy"/> skips a segment that is old while a newer one waits.
/// Every skip is reported through <c>skipped</c>, so nothing is lost silently.
/// </summary>
public sealed class SegmentWorkQueue : IAsyncDisposable
{
    private readonly Channel<QueuedSegment> _channel;
    private readonly Func<QueuedSegment, CancellationToken, Task> _process;
    private readonly Action<SkipReason, QueuedSegment, int> _skipped;
    private readonly Action<Exception> _faulted;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _worker;

    public SegmentWorkQueue(
        int capacity,
        Func<QueuedSegment, CancellationToken, Task> process,
        Action<SkipReason, QueuedSegment, int> skipped,
        Action<Exception> faulted)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _skipped = skipped ?? throw new ArgumentNullException(nameof(skipped));
        _faulted = faulted ?? throw new ArgumentNullException(nameof(faulted));
        Capacity = capacity;
        _channel = Channel.CreateBounded<QueuedSegment>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        }, dropped => _skipped(SkipReason.QueueFull, dropped, capacity));
        _worker = Task.Run(() => RunAsync(_cancellation.Token));
    }

    public int Capacity { get; }

    /// <summary>False once the worker has stopped, for example after a failure.</summary>
    public bool IsRunning => !_worker.IsCompleted;

    /// <summary>Segments waiting, not counting the one being recognised.</summary>
    public int Count => _channel.Reader.CanCount ? _channel.Reader.Count : 0;

    /// <summary>Adds a segment. When the queue is full the oldest waiting segment is dropped and reported.</summary>
    public bool Enqueue(QueuedSegment segment) => _channel.Writer.TryWrite(segment);

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        _cancellation.Cancel();
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _cancellation.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var reader = _channel.Reader;
            await foreach (var segment in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var waited = DateTimeOffset.UtcNow - segment.QueuedAt;
                var newerWaiting = reader.CanCount && reader.Count > 0;
                if (SegmentQueuePolicy.ShouldSkip(waited, newerWaiting))
                {
                    _skipped(SkipReason.TooOld, segment, Count);
                    continue;
                }

                await _process(segment, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _faulted(exception);
        }
    }
}
