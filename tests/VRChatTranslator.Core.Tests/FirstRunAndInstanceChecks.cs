using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;
using VRChatTranslator.Speech;

namespace VRChatTranslator.Core.Tests;

internal static partial class Program
{
    private static Task SecondLaunchIsRefusedAndTheFirstCopyIsToldToShow()
    {
        var name = "vrt-test-" + Guid.NewGuid().ToString("N");
        using var shown = new ManualResetEventSlim(false);
        using (var primary = new SingleInstanceGuard(name))
        {
            AssertTrue(primary.TryBecomePrimary(), "The first copy becomes the primary copy.");
            primary.WatchForSecondLaunch(shown.Set);

            using var second = new SingleInstanceGuard(name);
            AssertFalse(second.TryBecomePrimary(), "A second copy must not become primary.");
            AssertTrue(second.NotifyPrimary(), "A second copy can reach the first one.");
            AssertTrue(shown.Wait(TimeSpan.FromSeconds(5)), "The first copy must be told to show itself.");

            shown.Reset();
            using var third = new SingleInstanceGuard(name);
            AssertFalse(third.TryBecomePrimary(), "A third copy is refused too.");
            AssertTrue(third.NotifyPrimary() && shown.Wait(TimeSpan.FromSeconds(5)), "Every later launch reaches the first copy, not just the first one.");
        }

        using var afterQuit = new SingleInstanceGuard(name);
        AssertTrue(afterQuit.TryBecomePrimary(), "Once the first copy has quit, the next launch becomes primary.");

        using var nobodyHome = new SingleInstanceGuard("vrt-test-" + Guid.NewGuid().ToString("N"));
        AssertFalse(nobodyHome.NotifyPrimary(), "Notifying when no copy is running reports that nobody was reached.");

        foreach (var bad in new[] { "", " ", @"a\b", "a/b" })
        {
            var rejected = false;
            try { _ = new SingleInstanceGuard(bad); } catch (ArgumentException) { rejected = true; }
            AssertTrue(rejected, $"The name '{bad}' must be refused so it cannot leave the per-session namespace.");
        }

        var onlyPrimaryMayWatch = false;
        using (var notPrimary = new SingleInstanceGuard(name))
        {
            try { notPrimary.WatchForSecondLaunch(() => { }); } catch (InvalidOperationException) { onlyPrimaryMayWatch = true; }
        }
        AssertTrue(onlyPrimaryMayWatch, "Only the primary copy may watch for second launches.");
        return Task.CompletedTask;
    }

    private static Task FirstRunNoticeSaysWhereToClickAndHowBig()
    {
        var detail = SpeechModelNotice.NeededDetail("ggml-base.bin", 147_951_465);
        AssertTrue(detail.Contains("ggml-base.bin", StringComparison.Ordinal), "The notice names the model file.");
        AssertTrue(detail.Contains("about 148 MB", StringComparison.Ordinal), "The notice says how big the one-time download is.");
        AssertTrue(detail.Contains("Settings", StringComparison.Ordinal) && detail.Contains("DOWNLOAD SELECTED MODEL", StringComparison.Ordinal),
            "The notice says which tab and which button, in the wording the button uses.");
        AssertFalse(SpeechModelNotice.NeededDetail("ggml-base.bin", 0).Contains("about", StringComparison.Ordinal), "An unknown size is left out, not shown as 0 MB.");
        AssertTrue(SpeechModelNotice.ReadyHeadline.Contains("speech model", StringComparison.Ordinal) && SpeechModelNotice.ListeningHeadline.Contains("speech model", StringComparison.Ordinal),
            "Both headlines mention the speech model.");

        var manager = new LocalWhisperModelManager();
        foreach (var mode in Enum.GetValues<PerformanceMode>())
        {
            AssertTrue(manager.GetExpectedBytes(mode) > 50_000_000, $"{mode} must report the published size of its model.");
        }

        return Task.CompletedTask;
    }
}
