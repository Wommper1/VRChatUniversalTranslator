using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;
using VRChatTranslator.Speech;

namespace VRChatTranslator.Core.Tests;

internal static partial class Program
{
    /// <summary>A segment whose length in whole seconds identifies it in the checks below.</summary>
    private static QueuedSegment SecondsOfSpeech(int seconds, TimeSpan? queuedAgo = null)
    {
        var queuedAt = DateTimeOffset.UtcNow - (queuedAgo ?? TimeSpan.Zero);
        return new QueuedSegment(new float[16_000 * seconds], queuedAt, queuedAt, 0);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new InvalidOperationException(message);
            await Task.Delay(10);
        }
    }

    private static Task TheQueuePolicySkipsOldSpeechOnlyWhenNewerSpeechWaits()
    {
        AssertFalse(SegmentQueuePolicy.ShouldSkip(TimeSpan.FromSeconds(2), newerSegmentWaiting: false), "Fresh speech is recognised.");
        AssertFalse(SegmentQueuePolicy.ShouldSkip(TimeSpan.FromSeconds(2), newerSegmentWaiting: true), "Fresh speech is recognised even with more behind it.");
        AssertFalse(SegmentQueuePolicy.ShouldSkip(TimeSpan.FromSeconds(7), newerSegmentWaiting: false), "The last waiting segment is shown late rather than lost.");
        AssertTrue(SegmentQueuePolicy.ShouldSkip(TimeSpan.FromSeconds(7), newerSegmentWaiting: true), "Old speech is skipped when newer speech is waiting.");
        AssertFalse(SegmentQueuePolicy.ShouldSkip(TimeSpan.FromSeconds(5), newerSegmentWaiting: true), "Speech that is not yet old is kept.");
        AssertTrue(SegmentQueuePolicy.ShouldSkip(TimeSpan.FromSeconds(25), newerSegmentWaiting: false), "Speech far too old is skipped even when it is the last.");
        return Task.CompletedTask;
    }

    private static async Task AFullQueueDropsTheOldestSpeechAndSaysSo()
    {
        var skipped = new List<(SkipReason Reason, double Seconds)>();
        var processed = new List<int>();
        var firstStarted = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        await using var queue = new SegmentWorkQueue(
            capacity: 2,
            process: async (segment, token) =>
            {
                lock (processed) processed.Add(segment.Samples.Length / 16_000);
                firstStarted.TrySetResult();
                await release.Task.WaitAsync(token); // cancellable, so a failing check cannot hang the queue's shutdown
            },
            skipped: (reason, segment, _) => { lock (skipped) skipped.Add((reason, segment.Samples.Length / 16_000d)); },
            faulted: exception => throw exception);

        queue.Enqueue(SecondsOfSpeech(1)); // A, being recognised
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        queue.Enqueue(SecondsOfSpeech(2)); // B, waiting
        queue.Enqueue(SecondsOfSpeech(3)); // C, waiting (queue now full)
        AssertEqual(2, queue.Count, "Two segments wait behind the one being recognised.");
        queue.Enqueue(SecondsOfSpeech(4)); // D arrives: B, the oldest, makes room
        lock (skipped)
        {
            AssertEqual(1, skipped.Count, "Exactly one segment was skipped.");
            AssertEqual(SkipReason.QueueFull, skipped[0].Reason, "A full queue is reported as such.");
            AssertNear(2, skipped[0].Seconds, 0.001, "The oldest waiting segment (B, 2 s) was the one skipped.");
        }

        release.SetResult();
        await WaitUntilAsync(() => { lock (processed) return processed.Count == 3; }, "The remaining segments were never recognised.");
        lock (processed) AssertSequence(new[] { 1, 3, 4 }, processed, "The newest speech is kept and handled in order.");
    }

    private static async Task OldSpeechIsSkippedOnlyWhenNewerSpeechIsWaiting()
    {
        // Old speech with newer speech behind it is skipped; the newer speech is recognised.
        var skipped = new List<(SkipReason Reason, double Seconds)>();
        var processed = new List<int>();
        var firstStarted = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        await using (var queue = new SegmentWorkQueue(
            capacity: 4,
            process: async (segment, token) =>
            {
                lock (processed) processed.Add(segment.Samples.Length / 16_000);
                firstStarted.TrySetResult();
                await release.Task.WaitAsync(token);
            },
            skipped: (reason, segment, _) => { lock (skipped) skipped.Add((reason, segment.Samples.Length / 16_000d)); },
            faulted: exception => throw exception))
        {
            queue.Enqueue(SecondsOfSpeech(1));
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queue.Enqueue(SecondsOfSpeech(2, queuedAgo: TimeSpan.FromSeconds(10))); // old, with a newer one behind it
            queue.Enqueue(SecondsOfSpeech(3));
            release.SetResult();
            await WaitUntilAsync(() => { lock (processed) return processed.Count == 2; }, "The newest speech was never recognised.");
            lock (processed) AssertSequence(new[] { 1, 3 }, processed, "Old speech is passed over in favour of the newer speech.");
            lock (skipped)
            {
                AssertEqual(1, skipped.Count, "The old segment was reported as skipped.");
                AssertEqual(SkipReason.TooOld, skipped[0].Reason, "It is reported as too old.");
                AssertNear(2, skipped[0].Seconds, 0.001, "The old 2 s segment was the one skipped.");
            }
        }

        // The same old speech, with nothing newer waiting, is recognised late instead of being lost.
        var lateProcessed = new List<int>();
        var lateSkipped = 0;
        await using (var queue = new SegmentWorkQueue(
            capacity: 4,
            process: (segment, _) => { lock (lateProcessed) lateProcessed.Add(segment.Samples.Length / 16_000); return Task.CompletedTask; },
            skipped: (_, _, _) => Interlocked.Increment(ref lateSkipped),
            faulted: exception => throw exception))
        {
            queue.Enqueue(SecondsOfSpeech(5, queuedAgo: TimeSpan.FromSeconds(10)));
            await WaitUntilAsync(() => { lock (lateProcessed) return lateProcessed.Count == 1; }, "Late speech with nothing newer behind it was lost.");
            AssertEqual(0, lateSkipped, "Nothing is reported as skipped when the late segment is the last one.");
        }
    }

    private static Task SkippedSpeechNoticeIsPlainAndRateLimited()
    {
        var full = new SkippedSpeech(SkipReason.QueueFull, TimeSpan.FromSeconds(3.2), TimeSpan.FromSeconds(4.5), 3);
        var text = SkippedSpeechNotice.Describe(full, 8.1);
        AssertTrue(text.Contains("3.2 s", StringComparison.Ordinal) && text.Contains("queue was full", StringComparison.Ordinal),
            "The note says how much speech and why: " + text);
        AssertTrue(text.Contains("8.1 s", StringComparison.Ordinal), "The note gives the session total: " + text);
        var old = new SkippedSpeech(SkipReason.TooOld, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(7), 1);
        AssertTrue(SkippedSpeechNotice.Describe(old, 1).Contains("newer speech", StringComparison.Ordinal), "Old speech is explained by newer speech waiting.");

        var now = DateTimeOffset.UtcNow;
        AssertTrue(SkippedSpeechNotice.ShouldShowOverlay(DateTimeOffset.MinValue, now), "The first overlay note is shown.");
        AssertFalse(SkippedSpeechNotice.ShouldShowOverlay(now - TimeSpan.FromSeconds(3), now), "A second note within 8 s is held back.");
        AssertTrue(SkippedSpeechNotice.ShouldShowOverlay(now - TimeSpan.FromSeconds(9), now), "After 8 s the next note is shown.");
        return Task.CompletedTask;
    }
}

internal static partial class Program
{
    private static Task OnlyTheMeasuredBaseModelPresetsUseTheFasterAudioContext()
    {
        AssertEqual(PresetRuntimeProfile.BaseModelAudioContext, PresetRuntimeProfile.For(PerformanceMode.Balanced).AudioContextSize, "Balanced uses the faster audio context.");
        AssertEqual(PresetRuntimeProfile.BaseModelAudioContext, PresetRuntimeProfile.For(PerformanceMode.GpuFast).AudioContextSize, "Responsive CPU uses the faster audio context.");
        AssertEqual(0, PresetRuntimeProfile.For(PerformanceMode.Accurate).AudioContextSize, "Accurate (small model) keeps Whisper's default: it was not measured.");
        AssertEqual(0, PresetRuntimeProfile.For(PerformanceMode.UltraLight).AudioContextSize, "Ultra Light (tiny model) keeps Whisper's default: it was not measured.");
        AssertEqual(0, PresetRuntimeProfile.For(PerformanceMode.Custom).AudioContextSize, "Custom keeps Whisper's default.");
        // 768 covers 15 s; every preset's longest segment (4 to 8 s) fits with room to spare.
        foreach (var mode in new[] { PerformanceMode.Balanced, PerformanceMode.GpuFast })
        {
            var profile = PresetRuntimeProfile.For(mode);
            AssertTrue(profile.MaximumSegmentSeconds * 50 + 100 <= profile.AudioContextSize, $"{mode}: the audio context must cover the longest segment plus the pre-roll and silence.");
        }

        return Task.CompletedTask;
    }
}
