using System.Net;
using System.Net.Sockets;
using VRChatTranslator.Core.Services;

namespace VRChatTranslator.Core.Tests;

internal static partial class Program
{
    private static Task OscStatusIsHonestAboutReadiness()
    {
        var at = new DateTimeOffset(2026, 10, 7, 14, 2, 3, TimeSpan.Zero);
        var sent = new OscSendOutcome(at, null);
        var failed = new OscSendOutcome(at, "boom");

        var notRunning = OscStatusEvaluator.Evaluate(true, false, null, null);
        AssertEqual(OscReadiness.VrchatNotRunning, notRunning.Readiness, "Without VRChat a reply goes nowhere.");
        AssertTrue(notRunning.IsWarning && notRunning.Text.Contains("not running", StringComparison.Ordinal), "A missing VRChat must be a visible warning.");
        AssertEqual(OscReadiness.VrchatNotRunning, OscStatusEvaluator.Evaluate(true, false, true, null).Readiness,
            "Another program on port 9000 must not make a missing VRChat look ready.");

        var unknown = OscStatusEvaluator.Evaluate(true, true, null, null);
        AssertEqual(OscReadiness.ListenerUnknown, unknown.Readiness, "A failed port check is unknown, not ready and not closed.");
        AssertTrue(unknown.IsWarning && unknown.Text.Contains("could not check", StringComparison.Ordinal), "An unknown port state must say so.");

        var closed = OscStatusEvaluator.Evaluate(true, true, false, null);
        AssertEqual(OscReadiness.NoListener, closed.Readiness, "Nothing listening on the port means OSC is off or elsewhere.");
        AssertTrue(closed.IsWarning && closed.Text.Contains("UDP port 9000", StringComparison.Ordinal) && closed.Text.Contains("Action Menu", StringComparison.Ordinal),
            "A closed port must name the port and say how to turn OSC on.");

        var ready = OscStatusEvaluator.Evaluate(true, true, true, null);
        AssertEqual(OscReadiness.Ready, ready.Readiness, "Running with a listener is ready.");
        AssertFalse(ready.IsWarning, "A ready state with no failures is not a warning.");
        AssertTrue(ready.Text.Contains("Nothing sent yet", StringComparison.Ordinal), "Before any send the status says nothing was sent.");
        AssertFalse(ready.Text.Contains("Sending is off", StringComparison.Ordinal), "Consent given must not show the consent hint.");
        AssertTrue(OscStatusEvaluator.Evaluate(false, true, true, null).Text.Contains("Sending is off until you tick the box", StringComparison.Ordinal),
            "Missing consent must be explained.");

        var afterSend = OscStatusEvaluator.Evaluate(true, true, true, sent);
        AssertTrue(afterSend.Text.Contains("Last send: 14:02:03", StringComparison.Ordinal), "The last send time must be shown.");
        AssertTrue(afterSend.Text.Contains("cannot confirm", StringComparison.Ordinal), "A successful send must not claim delivery.");
        AssertFalse(afterSend.IsWarning, "A successful send is not a warning.");

        var afterFailure = OscStatusEvaluator.Evaluate(true, true, true, failed);
        AssertTrue(afterFailure.IsWarning, "A failed send must be a warning even when the port looks open.");
        AssertTrue(afterFailure.Text.Contains("failed at 14:02:03: boom", StringComparison.Ordinal), "A failed send must show when and why.");
        return Task.CompletedTask;
    }

    private static Task OscListenerCheckCountsOnlyReachableEndpoints()
    {
        IPEndPoint At(IPAddress address, int port) => new(address, port);
        AssertTrue(OscStatusEvaluator.IsReachableOnLoopback([At(IPAddress.Loopback, 9000)], 9000), "IPv4 loopback receives a loopback reply.");
        AssertTrue(OscStatusEvaluator.IsReachableOnLoopback([At(IPAddress.Any, 9000)], 9000), "A wildcard IPv4 socket receives a loopback reply.");
        AssertTrue(OscStatusEvaluator.IsReachableOnLoopback([At(IPAddress.IPv6Any, 9000)], 9000), "A dual-stack wildcard socket receives a loopback reply.");
        AssertFalse(OscStatusEvaluator.IsReachableOnLoopback([At(IPAddress.Parse("192.168.1.20"), 9000)], 9000), "A socket bound only to a LAN address does not receive loopback traffic.");
        AssertFalse(OscStatusEvaluator.IsReachableOnLoopback([At(IPAddress.IPv6Loopback, 9000)], 9000), "IPv6 loopback does not receive the IPv4 loopback reply.");
        AssertFalse(OscStatusEvaluator.IsReachableOnLoopback([At(IPAddress.Loopback, 9001)], 9000), "A different port does not count.");
        AssertFalse(OscStatusEvaluator.IsReachableOnLoopback([], 9000), "No listeners means the port is closed.");

        int port;
        using (var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            port = ((IPEndPoint)listener.Client.LocalEndPoint!).Port;
            AssertEqual<bool?>(true, OscStatusEvaluator.ProbeLocalListener(port), "The real listener table must show a socket we just opened.");
        }
        AssertEqual<bool?>(false, OscStatusEvaluator.ProbeLocalListener(port), "The real listener table must show the port closed once the socket is gone.");
        return Task.CompletedTask;
    }

    private static async Task OscSenderRecordsTransportOutcomeOnly()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var target = (IPEndPoint)listener.Client.LocalEndPoint!;
        using var sender = new OscReplySender(target);
        AssertEqual<OscSendOutcome?>(null, sender.LastOutcome, "Nothing is recorded before the first send.");

        var rejected = false;
        try { await sender.SendAsync(""); } catch (ArgumentException) { rejected = true; }
        AssertTrue(rejected, "An invalid reply is rejected.");
        AssertEqual<OscSendOutcome?>(null, sender.LastOutcome, "A rejected reply never reached the network and is not a send.");

        await sender.SendAsync("hello");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var received = await listener.ReceiveAsync(timeout.Token);
        AssertSequence(OscReplySender.BuildPacket("hello"), received.Buffer, "The test socket receives exactly the OSC packet, not VRChat.");
        var first = sender.LastOutcome;
        AssertTrue(first is { Succeeded: true }, "A completed send is recorded as handed off.");

        var limited = false;
        try { await sender.SendAsync("again"); } catch (InvalidOperationException) { limited = true; }
        AssertTrue(limited, "A second send inside the rate limit is refused.");
        AssertTrue(ReferenceEquals(first, sender.LastOutcome), "A rate-limit refusal must not overwrite the last real send.");

        var broken = new OscReplySender(target);
        broken.Dispose();
        var failed = false;
        try { await broken.SendAsync("late"); } catch (ObjectDisposedException) { failed = true; }
        AssertTrue(failed, "Sending through a closed socket fails.");
        AssertTrue(broken.LastOutcome is { Succeeded: false, Error: not null }, "A transport failure is recorded with its reason.");
    }
}
