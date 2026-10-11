namespace VRChatTranslator.Core.Services;

/// <summary>
/// Decides when the optional SteamVR overlay should be reconnected after SteamVR goes away, and when it must not.
/// It owns no OpenVR calls: the caller supplies them as delegates, so the policy can be tested without a headset.
/// <para>
/// Waiting for SteamVR only ever follows something the user asked for: a connection they established and have not ended,
/// or the "connect when the app starts" option they turned on. A first manual connect that fails, an explicit disconnect,
/// and disposal never start or keep a retry loop.
/// Retries wait 5, 10, 20, then 30 seconds so a SteamVR that is still shutting down is not poked immediately. A
/// connection that dies within 30 seconds of being established does not reset that backoff, so a link that flaps
/// cannot cause a rapid reconnect loop.
/// </para>
/// </summary>
public sealed class HeadsetLink
{
    public static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan StableAfter = TimeSpan.FromSeconds(30);

    private readonly Func<string?> _tryConnect;
    private readonly Action _tearDown;
    private readonly Action<bool> _setRetryTimer;
    private readonly Action<string> _status;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action _onReconnected;
    private bool _wanted, _connected, _hasConnected;
    private DateTimeOffset _connectedAt, _nextAttempt;
    private int _waits;

    /// <param name="tryConnect">Connects and returns null on success or a human-readable reason on failure.</param>
    /// <param name="tearDown">Releases any live connection. Must be safe when nothing is connected.</param>
    /// <param name="setRetryTimer">Starts or stops the timer that calls <see cref="RetryTick"/> about once a second.</param>
    /// <param name="status">Receives user-facing status text.</param>
    /// <param name="clock">The current time; replaced in tests.</param>
    /// <param name="onReconnected">Called after an automatic reconnect so the caller can restore what was on screen.</param>
    public HeadsetLink(Func<string?> tryConnect, Action tearDown, Action<bool> setRetryTimer, Action<string> status,
        Func<DateTimeOffset> clock, Action onReconnected)
    {
        _tryConnect = tryConnect;
        _tearDown = tearDown;
        _setRetryTimer = setRetryTimer;
        _status = status;
        _clock = clock;
        _onReconnected = onReconnected;
    }

    public bool IsConnected => _connected;

    /// <summary>True while the connection was lost and automatic reconnecting is still in progress.</summary>
    public bool IsWaiting => _wanted && !_connected;

    public static TimeSpan DelayFor(int wait) =>
        TimeSpan.FromSeconds(Math.Min(MaxDelay.TotalSeconds, InitialDelay.TotalSeconds * Math.Pow(2, Math.Max(0, wait - 1))));

    /// <summary>The user pressed Connect / reconnect.</summary>
    public void UserConnect()
    {
        var wasWaiting = IsWaiting;
        _tearDown();
        if (!wasWaiting) Reset();
        var failure = Attempt();
        if (failure is null)
        {
            MarkConnected();
            _setRetryTimer(false);
            _status("Connected to SteamVR. Open the SteamVR dashboard and choose Translator for controls.");
        }
        else if (wasWaiting)
        {
            Schedule();
            _status($"SteamVR is still unavailable: {failure} Retrying automatically; next try in {NextSeconds()} s.");
        }
        else
        {
            _setRetryTimer(false);
            _status("Headset unavailable: " + failure);
        }
    }

    /// <summary>
    /// The user opted in to connecting when the app starts. Unlike <see cref="UserConnect"/>, a failure here keeps waiting
    /// for SteamVR (the usual case is that the app was started first), because the user already asked for that.
    /// Does nothing if a connection or a wait is already in progress.
    /// </summary>
    public void AutoConnect()
    {
        if (_connected || IsWaiting) return;
        Reset();
        var failure = Attempt();
        if (failure is null)
        {
            MarkConnected();
            _setRetryTimer(false);
            _status("Connected to SteamVR automatically. Open the SteamVR dashboard and choose Translator for controls.");
            return;
        }
        _wanted = true;
        Schedule();
        _setRetryTimer(true);
        _status($"Waiting for SteamVR (connect at start is on): {failure} Next try in {NextSeconds()} s. Use Disconnect headset to stop.");
    }

    /// <summary>The user pressed Disconnect, or the app is closing. Ends any retrying.</summary>
    public void UserDisconnect()
    {
        Reset();
        _setRetryTimer(false);
        _tearDown();
    }

    /// <summary>The live connection broke (SteamVR quit or an OpenVR call failed).</summary>
    public void ConnectionLost(string reason)
    {
        _tearDown();
        if (IsWaiting) return;
        if (!_wanted || !_connected)
        {
            _status(reason);
            return;
        }
        _connected = false;
        if (_clock() - _connectedAt >= StableAfter) _waits = 0;
        Schedule();
        _setRetryTimer(true);
        _status($"{reason} The headset will reconnect automatically when SteamVR is back (next try in {NextSeconds()} s). Use Disconnect headset to stop.");
    }

    /// <summary>Called about once a second while <see cref="IsWaiting"/>.</summary>
    public void RetryTick()
    {
        if (!IsWaiting || _clock() < _nextAttempt) return;
        var failure = Attempt();
        if (failure is null)
        {
            var first = !_hasConnected;
            MarkConnected();
            _setRetryTimer(false);
            _status(first
                ? "Connected to SteamVR. Open the SteamVR dashboard and choose Translator for controls."
                : "Reconnected to SteamVR. Captions and dashboard controls are restored.");
            _onReconnected();
            return;
        }
        Schedule();
        _status($"Waiting for SteamVR: {failure} Next try in {NextSeconds()} s. Use Disconnect headset to stop.");
    }

    private string? Attempt()
    {
        try { return _tryConnect(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return ex.Message; }
    }

    private void MarkConnected()
    {
        _wanted = true;
        _connected = true;
        _hasConnected = true;
        _connectedAt = _clock();
    }

    private void Schedule()
    {
        _waits++;
        _nextAttempt = _clock() + DelayFor(_waits);
    }

    private void Reset()
    {
        _wanted = false;
        _connected = false;
        _waits = 0;
    }

    private int NextSeconds() => (int)DelayFor(_waits).TotalSeconds;
}
