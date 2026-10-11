using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Core.Services;

public sealed class VrchatProcessDetector : IAsyncDisposable
{
    public const string ProcessName = "VRChat";

    private readonly IProcessSnapshotProvider _processProvider;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _pollInterval;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private CancellationTokenSource? _pollCancellation;
    private Task? _pollTask;
    private VrchatDetectionSnapshot _current;
    private bool _disposed;

    public VrchatProcessDetector(
        IProcessSnapshotProvider? processProvider = null,
        TimeSpan? pollInterval = null,
        TimeProvider? timeProvider = null)
    {
        _processProvider = processProvider ?? new SystemProcessSnapshotProvider();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);

        if (_pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "The polling interval must be positive.");
        }

        _current = VrchatDetectionSnapshot.Waiting(_timeProvider.GetUtcNow());
    }

    public event EventHandler<VrchatDetectionChangedEventArgs>? DetectionChanged;

    public VrchatDetectionSnapshot Current
    {
        get
        {
            lock (_stateGate)
            {
                return _current;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_pollTask is { IsCompleted: false })
            {
                return;
            }

            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            _pollCancellation = new CancellationTokenSource();
            _pollTask = PollAsync(_pollCancellation.Token);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        Task? pollTask;
        CancellationTokenSource? cancellation;

        try
        {
            pollTask = _pollTask;
            cancellation = _pollCancellation;
            _pollTask = null;
            _pollCancellation = null;
            cancellation?.Cancel();
        }
        finally
        {
            _lifecycleGate.Release();
        }

        if (pollTask is not null)
        {
            try
            {
                await pollTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        cancellation?.Dispose();
    }

    public async Task<VrchatDetectionSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var observedAt = _timeProvider.GetUtcNow();
            VrchatDetectionSnapshot next;

            try
            {
                var selected = _processProvider
                    .GetProcessesByName(ProcessName)
                    .Where(IsVrchatProcess)
                    .OrderByDescending(process => process.StartedAt ?? DateTimeOffset.MinValue)
                    .ThenByDescending(process => process.ProcessId)
                    .FirstOrDefault();

                next = selected is null
                    ? VrchatDetectionSnapshot.Waiting(observedAt)
                    : new VrchatDetectionSnapshot(true, selected.ProcessId, selected.StartedAt, observedAt, null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                next = new VrchatDetectionSnapshot(
                    false,
                    null,
                    null,
                    observedAt,
                    $"Process detection failed: {exception.Message}");
            }

            Publish(next);
            return next;
        }
        finally
        {
            _scanGate.Release();
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
        _scanGate.Dispose();
        _lifecycleGate.Dispose();
    }

    private static bool IsVrchatProcess(ProcessSnapshot process) =>
        process.ProcessId > 0 && string.Equals(process.ProcessName, ProcessName, StringComparison.OrdinalIgnoreCase);

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_pollInterval, _timeProvider);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void Publish(VrchatDetectionSnapshot next)
    {
        bool changed;
        lock (_stateGate)
        {
            changed = HasMeaningfulChange(_current, next);
            _current = next;
        }

        if (changed)
        {
            DetectionChanged?.Invoke(this, new VrchatDetectionChangedEventArgs(next));
        }
    }

    private static bool HasMeaningfulChange(VrchatDetectionSnapshot previous, VrchatDetectionSnapshot next) =>
        previous.IsDetected != next.IsDetected ||
        previous.ProcessId != next.ProcessId ||
        previous.ProcessStartedAt != next.ProcessStartedAt ||
        !string.Equals(previous.LastError, next.LastError, StringComparison.Ordinal);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

