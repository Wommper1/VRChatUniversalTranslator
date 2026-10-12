using System.Runtime.Versioning;

namespace VRChatTranslator.Core.Services;

/// <summary>
/// Keeps one copy of the app per Windows user session. A second launch calls <see cref="NotifyPrimary"/> and exits;
/// the first copy is told to show its window. Without this, several copies each capture and transcribe the same VRChat audio.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly string _mutexName;
    private readonly string _signalName;
    private Mutex? _mutex;
    private EventWaitHandle? _signal;
    private RegisteredWaitHandle? _watch;
    private volatile bool _disposed;

    public SingleInstanceGuard(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.AsSpan().IndexOfAny('\\', '/') >= 0)
        {
            throw new ArgumentException("The instance name must be a plain name without slashes.", nameof(name));
        }

        // "Local\" keeps the names per logon session, so another Windows user running the app does not block this one.
        _mutexName = @"Local\" + name;
        _signalName = @"Local\" + name + ".show";
    }

    /// <summary>True when no other copy owns the name, so this copy is the one that should keep running.</summary>
    public bool TryBecomePrimary()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var mutex = new Mutex(initiallyOwned: false, _mutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;
        _signal = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, _signalName);
        return true;
    }

    /// <summary>Asks the running copy to show itself. False when there is no running copy to ask (for example, it is still starting).</summary>
    public bool NotifyPrimary()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!EventWaitHandle.TryOpenExisting(_signalName, out var existing))
        {
            return false;
        }

        using (existing)
        {
            return existing.Set();
        }
    }

    /// <summary>Runs <paramref name="onSecondLaunch"/> on a pool thread each time another copy is started. Call on the primary copy only.</summary>
    public void WatchForSecondLaunch(Action onSecondLaunch)
    {
        ArgumentNullException.ThrowIfNull(onSecondLaunch);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var signal = _signal ?? throw new InvalidOperationException("Only the primary copy can watch for a second launch.");
        _watch = ThreadPool.RegisterWaitForSingleObject(signal, (_, timedOut) =>
        {
            if (!timedOut && !_disposed)
            {
                onSecondLaunch();
            }
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watch?.Unregister(null);
        _signal?.Dispose();
        _mutex?.Dispose();
    }
}
