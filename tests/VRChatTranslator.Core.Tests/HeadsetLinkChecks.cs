using VRChatTranslator.Core.Services;
using VRChatTranslator.Core.Settings;

namespace VRChatTranslator.Core.Tests;

internal static partial class Program
{
    /// <summary>A fake SteamVR: connect results come from a queue (default: SteamVR is not running) and time is manual.</summary>
    private sealed class HeadsetLinkHarness
    {
        public DateTimeOffset Now = new(2026, 10, 7, 20, 0, 0, TimeSpan.Zero);
        public readonly Queue<string?> Results = new();
        public readonly List<string> Statuses = [];
        public int ConnectCalls, TearDowns, Reconnected;
        public bool RetryTimerOn;
        public Exception? ThrowOnConnect;
        public readonly HeadsetLink Link;

        public HeadsetLinkHarness() => Link = new HeadsetLink(
            () =>
            {
                ConnectCalls++;
                if (ThrowOnConnect is not null) throw ThrowOnConnect;
                return Results.Count > 0 ? Results.Dequeue() : "SteamVR is not running.";
            },
            () => TearDowns++, on => RetryTimerOn = on, Statuses.Add, () => Now, () => Reconnected++);

        public string Last => Statuses[^1];
        public void Advance(double seconds) => Now += TimeSpan.FromSeconds(seconds);

        public static HeadsetLinkHarness Connected()
        {
            var harness = new HeadsetLinkHarness();
            harness.Results.Enqueue(null);
            harness.Link.UserConnect();
            harness.Advance(60); // long enough to count as a stable connection
            return harness;
        }
    }

    private static Task HeadsetLinkBacksOffGently()
    {
        AssertSequence<double>([5, 5, 10, 20, 30, 30, 30], new[] { 0, 1, 2, 3, 4, 5, 6 }.Select(n => HeadsetLink.DelayFor(n).TotalSeconds).ToList(),
            "Retries must wait 5, 10, 20 then 30 seconds so a closing SteamVR is not poked straight away.");
        return Task.CompletedTask;
    }

    private static Task HeadsetLinkReconnectsAfterSteamVrRestarts()
    {
        var h = HeadsetLinkHarness.Connected();
        AssertTrue(h.Link.IsConnected && !h.Link.IsWaiting && !h.RetryTimerOn, "A user connect that works is connected and not retrying.");
        AssertTrue(h.Last.Contains("Connected to SteamVR", StringComparison.Ordinal), "A working connect says so.");

        h.Link.ConnectionLost("SteamVR closed.");
        AssertTrue(h.Link.IsWaiting && h.RetryTimerOn && !h.Link.IsConnected, "Losing a user-established connection starts waiting.");
        AssertTrue(h.Last.Contains("SteamVR closed.", StringComparison.Ordinal) && h.Last.Contains("next try in 5 s", StringComparison.Ordinal) &&
            h.Last.Contains("Disconnect headset", StringComparison.Ordinal), "The loss message must give the reason, the timing and how to stop.");

        h.Advance(4);
        h.Link.RetryTick();
        AssertEqual(1, h.ConnectCalls, "No retry may happen before the first 5 second wait is over.");

        h.Advance(1);
        h.Link.RetryTick(); // SteamVR is still down: the fake reports it
        AssertEqual(2, h.ConnectCalls, "The first retry happens after 5 seconds.");
        AssertTrue(h.Link.IsWaiting && h.RetryTimerOn, "A failed retry keeps waiting.");
        AssertTrue(h.Last.Contains("Waiting for SteamVR", StringComparison.Ordinal) && h.Last.Contains("SteamVR is not running.", StringComparison.Ordinal) &&
            h.Last.Contains("Next try in 10 s", StringComparison.Ordinal), "A failed retry shows why and backs off to 10 seconds.");

        h.Advance(9);
        h.Link.RetryTick();
        AssertEqual(2, h.ConnectCalls, "The second wait must last the full 10 seconds.");

        h.Advance(1);
        h.Results.Enqueue(null); // SteamVR is back
        h.Link.RetryTick();
        AssertEqual(3, h.ConnectCalls, "The next retry reaches SteamVR.");
        AssertTrue(h.Link.IsConnected && !h.Link.IsWaiting && !h.RetryTimerOn, "A successful retry stops the retry timer.");
        AssertEqual(1, h.Reconnected, "The caller is told once so it can redraw the caption that was on screen.");
        AssertTrue(h.Last.Contains("Reconnected to SteamVR", StringComparison.Ordinal), "The user sees that the headset is back.");
        return Task.CompletedTask;
    }

    private static Task HeadsetLinkStopsWhenTheUserDisconnects()
    {
        var h = HeadsetLinkHarness.Connected();
        h.Link.ConnectionLost("SteamVR closed.");
        var tearDownsBefore = h.TearDowns;
        h.Link.UserDisconnect();
        AssertTrue(!h.Link.IsWaiting && !h.Link.IsConnected && !h.RetryTimerOn, "Disconnect must end the waiting and stop the timer.");
        AssertTrue(h.TearDowns > tearDownsBefore, "Disconnect must release the connection.");

        h.Advance(1000);
        h.Link.RetryTick();
        AssertEqual(1, h.ConnectCalls, "After Disconnect nothing may reconnect in the background.");

        h.Link.ConnectionLost("late event");
        AssertTrue(!h.Link.IsWaiting && !h.RetryTimerOn, "A loss after Disconnect must not restart retrying.");
        AssertEqual("late event", h.Last, "A stray loss is reported but not acted on.");

        var live = HeadsetLinkHarness.Connected();
        live.Link.UserDisconnect();
        live.Link.ConnectionLost("closing");
        AssertTrue(!live.Link.IsWaiting && !live.RetryTimerOn, "Disconnecting a live connection (also done on exit) must not leave a retry behind.");
        return Task.CompletedTask;
    }

    private static Task HeadsetLinkDoesNotRetryAConnectThatNeverWorked()
    {
        var h = new HeadsetLinkHarness();
        h.Link.UserConnect();
        AssertEqual("Headset unavailable: SteamVR is not running.", h.Last, "A first connect that fails reports the reason.");
        AssertTrue(!h.Link.IsWaiting && !h.RetryTimerOn, "A first connect that fails must not start background retrying.");
        h.Advance(1000);
        h.Link.RetryTick();
        AssertEqual(1, h.ConnectCalls, "No retries for a connection that was never established.");

        h.Link.ConnectionLost("noise");
        AssertTrue(!h.Link.IsWaiting && !h.RetryTimerOn, "A loss without a prior connection does not start retrying either.");

        var broken = new HeadsetLinkHarness { ThrowOnConnect = new InvalidOperationException("boom") };
        broken.Link.UserConnect();
        AssertEqual("Headset unavailable: boom", broken.Last, "A connect that throws is a failure, not a crash.");
        return Task.CompletedTask;
    }

    private static Task HeadsetLinkKeepsBackingOffWhenTheLinkFlaps()
    {
        var h = HeadsetLinkHarness.Connected();
        h.Link.ConnectionLost("SteamVR closed.");
        AssertTrue(h.Last.Contains("next try in 5 s", StringComparison.Ordinal), "A loss after a stable connection starts at 5 seconds.");

        h.Advance(5);
        h.Results.Enqueue(null);
        h.Link.RetryTick();
        AssertTrue(h.Link.IsConnected, "The first reconnect works.");

        h.Advance(3); // dies again almost at once
        h.Link.ConnectionLost("SteamVR disconnected.");
        AssertTrue(h.Last.Contains("next try in 10 s", StringComparison.Ordinal), "A link that dies within seconds must not reset the backoff.");

        h.Advance(10);
        h.Results.Enqueue(null);
        h.Link.RetryTick();
        AssertTrue(h.Link.IsConnected, "The second reconnect works.");

        h.Advance(60); // this one stays up
        h.Link.ConnectionLost("SteamVR closed.");
        AssertTrue(h.Last.Contains("next try in 5 s", StringComparison.Ordinal), "A connection that stayed up resets the backoff.");
        return Task.CompletedTask;
    }

    private static Task HeadsetAutoConnectWorksWhenSteamVrIsAlreadyRunning()
    {
        var h = new HeadsetLinkHarness();
        h.Results.Enqueue(null);
        h.Link.AutoConnect();
        AssertTrue(h.Link.IsConnected && !h.Link.IsWaiting && !h.RetryTimerOn, "Connect at start connects at once when SteamVR is running.");
        AssertTrue(h.Last.Contains("Connected to SteamVR automatically", StringComparison.Ordinal), "The status says it connected on its own.");

        h.Link.AutoConnect();
        AssertEqual(1, h.ConnectCalls, "Connect at start while already connected must not connect again.");

        var waiting = new HeadsetLinkHarness();
        waiting.Link.AutoConnect(); // fails: SteamVR is not running, so it waits
        waiting.Link.AutoConnect();
        AssertEqual(1, waiting.ConnectCalls, "Connect at start while already waiting must not start a second attempt.");
        AssertTrue(waiting.Link.IsWaiting && waiting.RetryTimerOn, "The original wait is unchanged.");
        return Task.CompletedTask;
    }

    private static Task HeadsetAutoConnectWaitsForSteamVrToStart()
    {
        var h = new HeadsetLinkHarness();
        h.Link.AutoConnect(); // the app was started before SteamVR
        AssertTrue(h.Link.IsWaiting && h.RetryTimerOn && !h.Link.IsConnected, "Connect at start waits for SteamVR when it is not running yet.");
        AssertTrue(h.Last.Contains("Waiting for SteamVR", StringComparison.Ordinal) && h.Last.Contains("connect at start is on", StringComparison.Ordinal) &&
            h.Last.Contains("Next try in 5 s", StringComparison.Ordinal) && h.Last.Contains("Disconnect headset", StringComparison.Ordinal),
            "The wait is announced with its reason, timing and how to stop it.");

        h.Advance(4);
        h.Link.RetryTick();
        AssertEqual(1, h.ConnectCalls, "No attempt before the first wait is over.");
        h.Advance(1);
        h.Link.RetryTick();
        AssertEqual(2, h.ConnectCalls, "The first retry happens after 5 seconds.");
        AssertTrue(h.Last.Contains("Next try in 10 s", StringComparison.Ordinal), "The wait backs off like a reconnect does.");

        h.Advance(10);
        h.Results.Enqueue(null); // SteamVR has started
        h.Link.RetryTick();
        AssertTrue(h.Link.IsConnected && !h.Link.IsWaiting && !h.RetryTimerOn, "It connects as soon as SteamVR is available.");
        AssertTrue(h.Last.StartsWith("Connected to SteamVR.", StringComparison.Ordinal), "The first connection is not described as a reconnect.");
        AssertEqual(1, h.Reconnected, "The caller redraws whatever was on screen.");

        h.Advance(60);
        h.Link.ConnectionLost("SteamVR closed.");
        AssertTrue(h.Link.IsWaiting && h.Last.Contains("next try in 5 s", StringComparison.Ordinal), "Once connected, losing SteamVR is handled like any other loss.");
        h.Advance(5);
        h.Results.Enqueue(null);
        h.Link.RetryTick();
        AssertTrue(h.Last.StartsWith("Reconnected to SteamVR.", StringComparison.Ordinal), "A later connection is described as a reconnect.");
        return Task.CompletedTask;
    }

    private static Task HeadsetAutoConnectCanBeCancelled()
    {
        var h = new HeadsetLinkHarness();
        h.Link.AutoConnect();
        h.Link.UserDisconnect();
        AssertTrue(!h.Link.IsWaiting && !h.RetryTimerOn, "Disconnect ends the wait for SteamVR.");
        h.Advance(1000);
        h.Results.Enqueue(null);
        h.Link.RetryTick();
        AssertEqual(1, h.ConnectCalls, "Nothing connects in the background after Disconnect, even if SteamVR is now running.");
        AssertFalse(h.Link.IsConnected, "The headset stays disconnected until the user connects or the app restarts.");
        return Task.CompletedTask;
    }

    private static async Task HeadsetAutoConnectIsOffByDefaultAndPersists()
    {
        AssertFalse(new AppSettings().AutoConnectHeadset, "Connecting the headset at start must be opt-in.");
        var directory = Path.Combine(Path.GetTempPath(), $"vrchat-translator-autoconnect-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "config.json");
        try
        {
            Directory.CreateDirectory(directory);
            var store = new JsonSettingsStore(path);
            await File.WriteAllTextAsync(path, "{ \"selectedMode\": \"Balanced\" }");
            AssertFalse((await store.LoadAsync()).AutoConnectHeadset, "A config file from before this option existed must load with it off.");

            await store.SaveAsync(new AppSettings { AutoConnectHeadset = true });
            AssertTrue((await store.LoadAsync()).AutoConnectHeadset, "The option must survive a save and load.");
            await store.SaveAsync((await store.LoadAsync()) with { AutoConnectHeadset = false });
            AssertFalse((await store.LoadAsync()).AutoConnectHeadset, "Turning the option off must be saved too.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static Task HeadsetLinkManualConnectWhileWaitingKeepsWaiting()
    {
        var h = HeadsetLinkHarness.Connected();
        h.Link.ConnectionLost("SteamVR closed.");

        h.Link.UserConnect(); // SteamVR is still down
        AssertTrue(h.Link.IsWaiting && h.RetryTimerOn, "A manual attempt that fails while waiting must not give up on reconnecting.");
        AssertTrue(h.Last.Contains("still unavailable", StringComparison.Ordinal) && h.Last.Contains("Retrying automatically", StringComparison.Ordinal),
            "The user is told reconnecting continues.");

        h.Results.Enqueue(null);
        h.Link.UserConnect();
        AssertTrue(h.Link.IsConnected && !h.Link.IsWaiting && !h.RetryTimerOn, "A manual attempt that works ends the waiting.");
        AssertEqual(0, h.Reconnected, "A manual connect is not an automatic reconnect.");
        return Task.CompletedTask;
    }
}
