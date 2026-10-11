using System.Net;
using VRChatTranslator.Core.Services;

namespace VRChatTranslator.Core.Tests;

internal static partial class Program
{
    private static Task PhoneClientsMustBeOnTheLocalNetwork()
    {
        foreach (var local in new[] { "127.0.0.1", "::1", "192.168.1.20", "10.0.0.5", "172.16.4.4", "172.31.255.1", "169.254.3.3",
                     "::ffff:192.168.1.20", "fe80::1", "fd00::5" })
        {
            AssertTrue(PhoneAccessPolicy.IsLocalNetworkClient(IPAddress.Parse(local)), $"{local} is on this PC or a private network.");
        }

        foreach (var outside in new[] { "8.8.8.8", "172.32.0.1", "172.15.0.1", "203.0.113.9", "::ffff:8.8.8.8", "2001:4860:4860::8888", "100.64.0.1" })
        {
            AssertFalse(PhoneAccessPolicy.IsLocalNetworkClient(IPAddress.Parse(outside)), $"{outside} is not a local-network client.");
        }

        AssertFalse(PhoneAccessPolicy.IsLocalNetworkClient(null), "An unknown client address is refused.");
        return Task.CompletedTask;
    }

    private static Task PhoneHostHeaderMustBeANameThisPcAnswersTo()
    {
        foreach (var good in new[] { "localhost", "localhost:8765", "127.0.0.1:8765", "[::1]:8765", "192.168.1.20:8765", "10.1.2.3", "MY-PC", "my-pc:8765", "my-pc.local", "my-pc.lan:8765" })
        {
            AssertTrue(PhoneAccessPolicy.IsExpectedHost(good, "My-PC"), $"{good} is a name this PC answers to.");
        }

        foreach (var bad in new[] { "rebound.example", "rebound.example:8765", "8.8.8.8:8765", "192.168.1.20.evil.example", "my-pc.evil.example", "", "   ", null })
        {
            AssertFalse(PhoneAccessPolicy.IsExpectedHost(bad, "My-PC"), $"{bad ?? "(none)"} must be refused.");
        }

        AssertFalse(PhoneAccessPolicy.IsExpectedHost("anything", ""), "An empty machine name must not match every host.");
        return Task.CompletedTask;
    }

    private static Task PhonePinsAreSixDigitsAndMatchExactly()
    {
        var pins = Enumerable.Range(0, 200).Select(_ => PhoneAccessPolicy.NewPin()).ToArray();
        AssertTrue(pins.All(pin => pin.Length == PhoneAccessPolicy.PinLength && pin.All(char.IsAsciiDigit)), "A PIN is exactly six digits, leading zeros kept.");
        AssertTrue(pins.Distinct().Count() > 150, "New PINs must not repeat.");

        AssertTrue(PhoneAccessPolicy.PinMatches("012345", "012345"), "The right PIN matches.");
        AssertFalse(PhoneAccessPolicy.PinMatches("12345", "012345"), "A PIN without its leading zero does not match.");
        AssertFalse(PhoneAccessPolicy.PinMatches("0123456", "012345"), "A longer PIN does not match.");
        AssertFalse(PhoneAccessPolicy.PinMatches("", "012345"), "An empty PIN does not match.");
        AssertFalse(PhoneAccessPolicy.PinMatches(null, "012345"), "A missing PIN does not match.");
        return Task.CompletedTask;
    }

    private static Task PhonePinAttemptsAreLimitedPerClient()
    {
        var limiter = new PinAttemptLimiter(maxFailures: 5, window: TimeSpan.FromMinutes(5));
        var start = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < 4; i++)
        {
            limiter.RecordFailure("192.168.1.50", start.AddSeconds(i));
        }

        AssertEqual(TimeSpan.Zero, limiter.RetryAfter("192.168.1.50", start.AddSeconds(5)), "Four wrong PINs are still allowed another try.");
        limiter.RecordFailure("192.168.1.50", start.AddSeconds(4));
        var wait = limiter.RetryAfter("192.168.1.50", start.AddSeconds(10));
        AssertTrue(wait > TimeSpan.FromMinutes(4) && wait <= TimeSpan.FromMinutes(5), "Five wrong PINs block the client for about five minutes.");
        AssertEqual(TimeSpan.Zero, limiter.RetryAfter("192.168.1.60", start.AddSeconds(10)), "One phone's failures must not block another phone.");

        AssertEqual(TimeSpan.Zero, limiter.RetryAfter("192.168.1.50", start.AddMinutes(6)), "The block ends once the failures age out.");

        limiter.RecordFailure("192.168.1.70", start);
        limiter.RecordSuccess("192.168.1.70");
        for (var i = 0; i < 4; i++)
        {
            limiter.RecordFailure("192.168.1.70", start.AddSeconds(i));
        }

        AssertEqual(TimeSpan.Zero, limiter.RetryAfter("192.168.1.70", start.AddSeconds(5)), "A correct PIN clears the earlier failures.");
        return Task.CompletedTask;
    }

    private static Task PhoneStatusKeepsFilePathsOffThePhone()
    {
        var redacted = PhoneStatusRedactor.Redact(@"Local Whisper failed to start: Could not find file 'C:\Users\Someone\AppData\Local\Models\ggml.bin'.");
        AssertTrue(redacted is not null && !redacted.Contains("Someone", StringComparison.Ordinal) && !redacted.Contains(@"C:\", StringComparison.Ordinal),
            "A Windows path in an error message must not reach the phone.");
        AssertTrue(redacted!.Contains("Could not find file", StringComparison.Ordinal) && redacted.Contains("[path]", StringComparison.Ordinal),
            "The rest of the message stays readable and shows that a path was hidden.");
        AssertFalse(PhoneStatusRedactor.Redact(@"Cannot open \\server\share\model.bin now")!.Contains("server", StringComparison.Ordinal), "A network path is hidden too.");
        AssertEqual("VRChat-only audio capture failed: access denied", PhoneStatusRedactor.Redact("VRChat-only audio capture failed: access denied"), "Messages without a path pass through unchanged.");
        AssertEqual<string?>(null, PhoneStatusRedactor.Redact(null), "No error stays no error.");
        AssertTrue(PhoneStatusRedactor.Redact(new string('x', 500))!.Length <= 161, "A very long error is cut short.");
        return Task.CompletedTask;
    }
}
