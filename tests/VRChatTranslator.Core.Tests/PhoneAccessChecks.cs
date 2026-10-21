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

    private static Task PhonePinGuessingCannotBeSpreadAcrossAddresses()
    {
        // An IPv6 device can use any address in its network, so it is counted by the network (the first 64 bits).
        var a = System.Net.IPAddress.Parse("fe80::1111:2222:3333:4444");
        var b = System.Net.IPAddress.Parse("fe80::aaaa:bbbb:cccc:dddd");
        var other = System.Net.IPAddress.Parse("fd00:1:2:3::9");
        AssertEqual(PhoneAccessPolicy.ClientKey(a), PhoneAccessPolicy.ClientKey(b), "Two addresses in one IPv6 network count as one client.");
        AssertTrue(PhoneAccessPolicy.ClientKey(a) != PhoneAccessPolicy.ClientKey(other), "Different IPv6 networks count as different clients.");
        AssertEqual("192.168.1.50", PhoneAccessPolicy.ClientKey(System.Net.IPAddress.Parse("::ffff:192.168.1.50")), "An IPv4 client reached over IPv6 is counted by its IPv4 address.");
        AssertTrue(PhoneAccessPolicy.ClientKey(System.Net.IPAddress.Parse("192.168.1.50")) != PhoneAccessPolicy.ClientKey(System.Net.IPAddress.Parse("192.168.1.51")), "Two IPv4 phones stay separate.");
        AssertEqual("unknown", PhoneAccessPolicy.ClientKey(null), "A missing address is still counted.");

        // All clients together get a limit too, so trying one PIN from many different addresses is slowed as well.
        var start = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var shared = new PinAttemptLimiter(maxFailures: 5, window: TimeSpan.FromMinutes(5), maxTotalFailures: 30);
        for (var i = 0; i < 29; i++)
        {
            shared.RecordFailure($"10.0.0.{i + 1}", start.AddSeconds(i));
        }

        AssertEqual(TimeSpan.Zero, shared.RetryAfter("10.0.0.200", start.AddSeconds(30)), "Twenty-nine wrong PINs across many phones do not yet pause everyone.");
        shared.RecordFailure("10.0.0.30", start.AddSeconds(29));
        var pause = shared.RetryAfter("10.0.0.200", start.AddSeconds(31));
        AssertTrue(pause > TimeSpan.FromMinutes(4) && pause <= TimeSpan.FromMinutes(5), "Thirty wrong PINs from different addresses pause every address for about five minutes.");
        AssertEqual(TimeSpan.Zero, shared.RetryAfter("10.0.0.200", start.AddMinutes(6)), "The overall pause ends once the failures age out.");

        // A flood of fresh addresses must not wipe out another client's block.
        var flooded = new PinAttemptLimiter(maxFailures: 5, window: TimeSpan.FromMinutes(5), maxTotalFailures: 1_000_000);
        for (var i = 0; i < 5; i++)
        {
            flooded.RecordFailure("victim", start.AddSeconds(i));
        }

        for (var i = 0; i < 3000; i++)
        {
            flooded.RecordFailure($"flood-{i}", start.AddSeconds(10));
        }

        AssertTrue(flooded.RetryAfter("victim", start.AddSeconds(20)) > TimeSpan.Zero, "A block survives a flood of other addresses.");
        AssertTrue(flooded.RetryAfter("flood-0", start.AddSeconds(20)) == TimeSpan.Zero, "A client with one wrong PIN may still try.");
        AssertEqual(TimeSpan.Zero, flooded.RetryAfter("victim", start.AddMinutes(6)), "The block still ends once its failures age out.");

        // A correct PIN clears that client's failures but not the overall count.
        var mixed = new PinAttemptLimiter(maxFailures: 5, window: TimeSpan.FromMinutes(5), maxTotalFailures: 3);
        mixed.RecordFailure("phone", start);
        mixed.RecordFailure("attacker-a", start);
        mixed.RecordSuccess("phone");
        mixed.RecordFailure("attacker-b", start);
        AssertTrue(mixed.RetryAfter("phone", start.AddSeconds(1)) > TimeSpan.Zero, "A correct PIN does not erase the overall count.");
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
