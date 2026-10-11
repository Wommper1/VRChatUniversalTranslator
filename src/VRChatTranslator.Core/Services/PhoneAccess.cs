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

    public static string NewPin() => RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, PinLength)).ToString("D" + PinLength);

    /// <summary>Compares in constant time so the answer does not leak how many digits were right.</summary>
    public static bool PinMatches(string? supplied, string expected)
    {
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(supplied ?? string.Empty));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}

/// <summary>Counts wrong PINs per client so a six-digit PIN cannot simply be tried one after another.</summary>
public sealed class PinAttemptLimiter(int maxFailures = 5, TimeSpan? window = null)
{
    private readonly TimeSpan _window = window ?? TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, Queue<DateTimeOffset>> _failures = [];
    private readonly object _gate = new();

    /// <summary>How long the client must wait, or zero when it may try.</summary>
    public TimeSpan RetryAfter(string client, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_failures.TryGetValue(client, out var queue))
            {
                return TimeSpan.Zero;
            }

            Prune(queue, now);
            if (queue.Count == 0)
            {
                _failures.Remove(client);
                return TimeSpan.Zero;
            }

            return queue.Count >= maxFailures ? queue.Peek() + _window - now : TimeSpan.Zero;
        }
    }

    public void RecordFailure(string client, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_failures.TryGetValue(client, out var queue))
            {
                if (_failures.Count >= 1000)
                {
                    _failures.Clear(); // a flood of addresses must not grow this without bound
                }

                _failures[client] = queue = new Queue<DateTimeOffset>();
            }

            Prune(queue, now);
            queue.Enqueue(now);
        }
    }

    public void RecordSuccess(string client)
    {
        lock (_gate)
        {
            _failures.Remove(client);
        }
    }

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
