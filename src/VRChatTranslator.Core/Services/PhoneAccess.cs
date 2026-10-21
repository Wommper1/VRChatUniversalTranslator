using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace VRChatTranslator.Core.Services;

/// <summary>
/// The rules behind the phone display, kept free of any server code so they can be checked on their own.
/// The page is meant for a phone on the same home network and nothing else: a client must be on this PC or a private
/// network, the Host header must be a name this PC would answer to (which stops a web page from reaching the server through
/// DNS rebinding), and every caption stream needs the PIN.
/// </summary>
public static class PhoneAccessPolicy
{
    public const int PinLength = 6;

    public static bool IsLocalNetworkClient(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC                  // fc00::/7 unique local
                || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80); // fe80::/10 link local
        }

        return false;
    }

    /// <summary>True for localhost, a private or loopback IP address, or this PC's own name; anything else is refused.</summary>
    public static bool IsExpectedHost(string? hostHeader, string machineName)
    {
        if (string.IsNullOrWhiteSpace(hostHeader))
        {
            return false;
        }

        var host = StripPort(hostHeader.Trim().ToLowerInvariant());
        if (host.Length == 0)
        {
            return false;
        }

        if (IPAddress.TryParse(host, out var address))
        {
            return IsLocalNetworkClient(address);
        }

        if (host == "localhost")
        {
            return true;
        }

        var machine = machineName.Trim().ToLowerInvariant();
        return machine.Length > 0 && (host == machine || host == machine + ".local" || host == machine + ".lan");
    }

    private static string StripPort(string header)
    {
        if (header.StartsWith('['))
        {
            var end = header.IndexOf(']');
            return end < 0 ? header : header[1..end];
        }

        return header.Count(c => c == ':') == 1 ? header[..header.IndexOf(':')] : header;
    }

    /// <summary>
    /// The name the PIN limiter counts a client under. An IPv6 client is counted by its /64 network, because a device can pick
    /// any other address in that network at will; without this, trying each PIN from a fresh address would never be limited.
    /// </summary>
    public static string ClientKey(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6
            ? "v6:" + Convert.ToHexString(address.GetAddressBytes(), 0, 8)
            : address.ToString();
    }

    public static string NewPin() => RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, PinLength)).ToString("D" + PinLength);

    /// <summary>Compares in constant time so the answer does not leak how many digits were right.</summary>
    public static bool PinMatches(string? supplied, string expected)
    {
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(supplied ?? string.Empty));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}

/// <summary>
/// Counts wrong PINs so a six-digit PIN cannot simply be tried one after another. Each client gets <c>maxFailures</c> wrong
/// tries per window, and all clients together get <c>maxTotalFailures</c>, so guessing from many addresses at once is slowed
/// as well. While the overall limit is reached everyone waits, even with the right PIN; that is deliberate.
/// </summary>
public sealed class PinAttemptLimiter(int maxFailures = 5, TimeSpan? window = null, int maxTotalFailures = 30)
{
    private const int MaximumTrackedClients = 1000;
    private const string OverflowClient = "~overflow";
    private readonly TimeSpan _window = window ?? TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, Queue<DateTimeOffset>> _failures = [];
    private readonly Queue<DateTimeOffset> _allFailures = new();
    private readonly object _gate = new();

    /// <summary>How long the client must wait, or zero when it may try.</summary>
    public TimeSpan RetryAfter(string client, DateTimeOffset now)
    {
        lock (_gate)
        {
            Prune(_allFailures, now);
            var wait = _allFailures.Count >= maxTotalFailures ? _allFailures.Peek() + _window - now : TimeSpan.Zero;

            if (_failures.TryGetValue(KeyFor(client), out var queue))
            {
                Prune(queue, now);
                if (queue.Count == 0)
                {
                    _failures.Remove(KeyFor(client));
                }
                else if (queue.Count >= maxFailures)
                {
                    var clientWait = queue.Peek() + _window - now;
                    if (clientWait > wait)
                    {
                        wait = clientWait;
                    }
                }
            }

            return wait;
        }
    }

    public void RecordFailure(string client, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_failures.Count >= MaximumTrackedClients && !_failures.ContainsKey(client))
            {
                // Make room by dropping clients whose failures have all aged out. Never clear everyone: that would let a flood of
                // fresh addresses wipe out another client's block.
                foreach (var stale in _failures.Where(pair => IsStale(pair.Value, now)).Select(pair => pair.Key).ToArray())
                {
                    _failures.Remove(stale);
                }
            }

            var key = KeyFor(client);
            if (!_failures.TryGetValue(key, out var queue))
            {
                _failures[key] = queue = new Queue<DateTimeOffset>();
            }

            Prune(queue, now);
            queue.Enqueue(now);
            Prune(_allFailures, now);
            _allFailures.Enqueue(now);
        }
    }

    public void RecordSuccess(string client)
    {
        lock (_gate)
        {
            _failures.Remove(KeyFor(client));
        }
    }

    // Clients beyond the tracking limit share one bucket, so memory stays bounded without letting anyone reset anyone.
    private string KeyFor(string client) =>
        _failures.ContainsKey(client) || _failures.Count < MaximumTrackedClients ? client : OverflowClient;

    private bool IsStale(Queue<DateTimeOffset> queue, DateTimeOffset now) => queue.Count == 0 || now - queue.Last() >= _window;

    private void Prune(Queue<DateTimeOffset> queue, DateTimeOffset now)
    {
        while (queue.Count > 0 && now - queue.Peek() >= _window)
        {
            queue.Dequeue();
        }
    }
}

/// <summary>The phone page shows the app's last error; file locations in it stay on the PC.</summary>
public static partial class PhoneStatusRedactor
{
    private const int MaximumLength = 160;

    [GeneratedRegex(@"(?:[A-Za-z]:[\\/]|\\\\)[^\s'""<>|]*")]
    private static partial Regex PathPattern();

    public static string? Redact(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var redacted = PathPattern().Replace(text, "[path]");
        return redacted.Length <= MaximumLength ? redacted : redacted[..MaximumLength] + "…";
    }
}
