using System.Text.Json;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;
using VRChatTranslator.Core.Settings;

namespace VRChatTranslator.Core.Tests;

internal static partial class Program
{
    private static Task WordErrorRateIsExactAndForgivingAboutCaseAndPunctuation()
    {
        AssertEqual((0, 3), WordErrorRate.Compute("the cat sat", "the cat sat"), "Identical text has no errors.");
        AssertEqual((1, 3), WordErrorRate.Compute("the cat sat", "the dog sat"), "One substituted word is one error.");
        AssertEqual((1, 3), WordErrorRate.Compute("the cat sat", "the cat"), "One missing word is one error.");
        AssertEqual((1, 2), WordErrorRate.Compute("the cat", "the big cat"), "One extra word is one error.");
        AssertEqual(0.0, WordErrorRate.Rate("The cat, SAT.", "the cat sat"), "Case and punctuation do not count.");
        AssertEqual(0.5, WordErrorRate.Rate("it's fine", "its fine"), "An apostrophe inside a word is part of the word.");
        AssertEqual(1.0, WordErrorRate.Rate("a b c d", "d c b a"), "A fully reordered sentence is all errors.");
        AssertEqual(0.0, WordErrorRate.Rate("", ""), "Nothing said and nothing heard is perfect.");
        AssertEqual(1.0, WordErrorRate.Rate("", "something"), "Words heard where nothing was said is a full error.");
        AssertEqual(1.0, WordErrorRate.Rate("hello there", ""), "Hearing nothing is a full error.");
        return Task.CompletedTask;
    }

    private static Task EvaluationMathIsCorrect()
    {
        var values = Enumerable.Range(1, 10).Select(i => (double)i).ToArray();
        AssertEqual(5.0, EvalMath.Percentile(values, 50), "Median of 1..10 by nearest rank is 5.");
        AssertEqual(9.0, EvalMath.Percentile(values, 90), "90th percentile of 1..10 is 9.");
        AssertEqual(10.0, EvalMath.Percentile(values, 100), "100th percentile is the maximum.");
        AssertEqual(0.0, EvalMath.Percentile([], 90), "No values give 0.");
        AssertEqual(1.0, EvalMath.WordOverlap("quick brown fox", "the quick brown fox jumps"), "Every caption word is in the reference.");
        AssertEqual(0.0, EvalMath.WordOverlap("", "anything"), "An empty caption overlaps nothing.");
        AssertFalse(EvalMath.IsInvented("the quick brown fox", ["the quick brown fox jumps over"]), "A caption matching a talker is not invented.");
        AssertTrue(EvalMath.IsInvented("thanks for watching everyone", ["the quick brown fox jumps over"]), "A caption matching no talker is invented.");
        AssertFalse(EvalMath.IsInvented("okay", ["something else entirely"]), "Very short captions are not judged.");
        AssertFalse(EvalMath.IsInvented("the quick unknown words here", ["a b c", "the quick brown fox here"]), "Matching any one talker is enough.");
        return Task.CompletedTask;
    }

    private static Task CrowdMixerPlacesVoicesLikeVrchatCapture()
    {
        float[] Constant(float value, int samples) => Enumerable.Repeat(value, samples).ToArray();
        var mix = CrowdMixer.Mix(
        [
            new MixTalker(Constant(0.5f, 16_000), GainDb: 0, Pan: -1, StartSeconds: 0),
            new MixTalker(Constant(0.5f, 16_000), GainDb: -6.0206, Pan: 1, StartSeconds: 0)
        ], totalSeconds: 1.0);
        AssertEqual(CrowdMixer.OutputRate * 2, mix.Length, "One second of 48 kHz stereo is 96,000 values.");
        AssertTrue(Math.Abs(mix[200] - 0.5f) < 0.001f && Math.Abs(mix[201] - 0.25f) < 0.001f,
            $"A hard-left voice at 0 dB and a hard-right voice at -6 dB land in their own channels (left {mix[200]:0.000}, right {mix[201]:0.000}).");

        var centre = CrowdMixer.Mix([new MixTalker(Constant(0.5f, 16_000), 0, 0, 0)], 1.0);
        AssertTrue(Math.Abs(centre[200] - centre[201]) < 1e-6f && Math.Abs(centre[200] - 0.5f * MathF.Sqrt(0.5f)) < 0.001f,
            "A centred voice has equal-power left and right (about -3 dB each).");

        var late = CrowdMixer.Mix([new MixTalker(Constant(0.5f, 16_000), 0, 0, StartSeconds: 0.5)], 1.0);
        AssertEqual(0f, late[2 * 24_000 - 2], "Nothing sounds before the start time.");
        AssertTrue(late[2 * 24_000 + 2] > 0.3f, "The voice starts at the start time.");

        var loud = CrowdMixer.Mix(
        [
            new MixTalker(Constant(0.9f, 16_000), 0, -1, 0),
            new MixTalker(Constant(0.9f, 16_000), 0, -1, 0),
            new MixTalker(Constant(0.45f, 16_000), 0, 1, 0)
        ], 1.0);
        var peak = loud.Max(sample => Math.Abs(sample));
        AssertTrue(peak <= 0.9901f && peak > 0.98f, $"A sum above full scale is scaled to just under it (peak {peak:0.0000}).");
        AssertTrue(Math.Abs(loud[200] / loud[201] - 1.8 / 0.45) < 0.01, "Scaling down keeps the balance between left and right.");
        return Task.CompletedTask;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<Utterance>> SyntheticSpeakers()
    {
        var speakers = new Dictionary<string, IReadOnlyList<Utterance>>();
        for (var s = 0; s < 8; s++)
        {
            var list = new List<Utterance>();
            for (var u = 0; u < 5; u++)
            {
                var seconds = 3 + (s * 5 + u) % 7;
                list.Add(new Utterance($"{100 + s}-{u:00}", (100 + s).ToString(), new float[16_000 * seconds], $"speaker{s} sentence{u} word word"));
            }

            speakers[(100 + s).ToString()] = list;
        }

        return speakers;
    }

    private static Task CrowdScenariosAreSeededAndPlanned()
    {
        var speakers = SyntheticSpeakers();
        var (first, firstAudio) = CrowdScenarioBuilder.Build("a", 4, 7, speakers);
        var (second, secondAudio) = CrowdScenarioBuilder.Build("a", 4, 7, speakers);
        AssertEqual(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second), "The same seed gives the same crowd plan.");
        AssertTrue(firstAudio.AsSpan().SequenceEqual(secondAudio), "The same seed gives the same audio.");
        var (other, _) = CrowdScenarioBuilder.Build("a", 4, 8, speakers);
        AssertTrue(JsonSerializer.Serialize(first) != JsonSerializer.Serialize(other), "A different seed gives a different crowd.");

        AssertEqual(4, first.Plan.Count, "Four talkers are planned.");
        AssertEqual(4, first.Plan.Select(t => t.SpeakerId).Distinct().Count(), "Every talker is a different person.");
        AssertTrue(first.Plan[0].GainDb is <= -3 and >= -6, "The dominant talker is only a little below full level.");
        AssertTrue(first.Plan.Skip(1).All(t => t.GainDb is <= -10 and >= -20), "The other talkers are 10 to 20 dB down.");
        AssertTrue(first.Plan.All(t => t.Pan is >= -0.9 and <= 0.9), "Voices are panned inside the stereo field.");
        foreach (var talker in first.Plan)
        {
            AssertTrue(talker.Placed.Count >= 1, "Every talker says at least one utterance.");
            AssertTrue(talker.Placed.Skip(1).All(p => p.EndSeconds <= 30.0), "Later utterances fit the target length.");
            for (var i = 1; i < talker.Placed.Count; i++) AssertTrue(talker.Placed[i].StartSeconds >= talker.Placed[i - 1].EndSeconds, "A talker does not overlap themself.");
        }

        var expected = first.Plan.SelectMany(t => t.Placed).Select(p => p.Id).ToArray();
        AssertEqual(expected.Length, expected.Distinct().Count(), "No utterance is used twice.");
        AssertEqual(first.Plan.Max(t => t.Placed[^1].EndSeconds) + 1.0, first.DurationSeconds, "The clip ends one second after the last word.");
        AssertEqual((int)Math.Ceiling(first.DurationSeconds * CrowdMixer.OutputRate) * 2, firstAudio.Length, "The audio is as long as the plan says.");
        var dominant = speakers[first.Plan[0].SpeakerId].ToDictionary(u => u.Id);
        AssertEqual(string.Join(' ', first.Plan[0].Placed.Select(p => dominant[p.Id].Text)), first.Plan[0].Reference, "The answer is exactly what the dominant talker said.");

        var tooMany = false;
        try { CrowdScenarioBuilder.Build("a", 9, 7, speakers); } catch (ArgumentOutOfRangeException) { tooMany = true; }
        AssertTrue(tooMany, "More talkers than speakers is refused.");
        return Task.CompletedTask;
    }

    private static async Task ApplySavesThePerformancePreset()
    {
        var folder = Path.Combine(Path.GetTempPath(), "vrt-preset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var original = new AppSettings { ShowOriginalText = true };
            var applied = original.WithRuntimeSelection(PerformanceMode.Accurate, new CustomModeSettings { CpuThreads = 6 });
            AssertEqual(PerformanceMode.Balanced, original.SelectedMode, "The original settings record is not changed.");

            var store = new JsonSettingsStore(Path.Combine(folder, "config.json"));
            await store.SaveAsync(applied);
            var loaded = await store.LoadAsync();
            AssertEqual(PerformanceMode.Accurate, loaded.SelectedMode, "The chosen preset must survive a save and load, not only closing the app.");
            AssertEqual(6, loaded.CustomMode.CpuThreads, "The custom-mode fields are saved with it.");
            AssertTrue(loaded.ShowOriginalText, "Other settings are left alone.");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
