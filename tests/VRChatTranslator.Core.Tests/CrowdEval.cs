using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NAudio.MediaFoundation;
using NAudio.Wave;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;
using VRChatTranslator.Core.Settings;
using VRChatTranslator.Speech;

namespace VRChatTranslator.Core.Tests;

// Development-only tools for measuring caption accuracy and speed against fake crowds. Nothing in this file ships in the app.
// Usage (from the repository root):
//   dotnet run -c Release --project tests\VRChatTranslator.Core.Tests -- --build-crowd <test-clean.tar.gz> <outFolder> [--seed 1234] [--repeats 2]
//   dotnet run -c Release --project tests\VRChatTranslator.Core.Tests -- --eval <corpusFolder> [--mode Balanced] [--speed 1] [--max-files 20] [--talkers 1,3,6]

/// <summary>Word error rate with the usual normalisation: case, punctuation and apostrophe edges do not count.</summary>
internal static class WordErrorRate
{
    public static string[] Tokens(string text) =>
        Regex.Replace((text ?? "").ToLowerInvariant(), @"[^\p{L}\p{Nd}']+", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim('\''))
            .Where(word => word.Length > 0)
            .ToArray();

    /// <summary>Edit distance in words (substitutions + insertions + deletions) and the number of reference words.</summary>
    public static (int Errors, int ReferenceWords) Compute(string reference, string hypothesis)
    {
        var r = Tokens(reference);
        var h = Tokens(hypothesis);
        var previous = new int[h.Length + 1];
        var current = new int[h.Length + 1];
        for (var j = 0; j <= h.Length; j++) previous[j] = j;
        for (var i = 1; i <= r.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= h.Length; j++)
            {
                var substitution = previous[j - 1] + (r[i - 1] == h[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j] + 1, current[j - 1] + 1));
            }

            (previous, current) = (current, previous);
        }

        return (previous[h.Length], r.Length);
    }

    public static double Rate(string reference, string hypothesis)
    {
        var (errors, words) = Compute(reference, hypothesis);
        return words == 0 ? (errors == 0 ? 0 : 1) : (double)errors / words;
    }
}

internal static class EvalMath
{
    /// <summary>Nearest-rank percentile of already collected values; 0 for no values.</summary>
    public static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        var rank = (int)Math.Ceiling(percentile / 100d * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }

    /// <summary>Share of the caption's distinct words that appear in the reference text.</summary>
    public static double WordOverlap(string caption, string reference)
    {
        var words = WordErrorRate.Tokens(caption).Distinct().ToArray();
        if (words.Length == 0) return 0;
        var known = WordErrorRate.Tokens(reference).ToHashSet();
        return (double)words.Count(known.Contains) / words.Length;
    }

    /// <summary>A caption is "invented" when it shares almost nothing with what any talker said (at least three words, under 30% overlap).</summary>
    public static bool IsInvented(string caption, IEnumerable<string> references)
    {
        if (WordErrorRate.Tokens(caption).Length < 3) return false;
        return references.All(reference => WordOverlap(caption, reference) < 0.30);
    }
}

internal sealed record MixTalker(float[] Samples16k, double GainDb, double Pan, double StartSeconds);

internal static class CrowdMixer
{
    public const int OutputRate = 48_000;

    /// <summary>
    /// Builds what VRChat's process capture delivers: 48 kHz stereo float. Voices are placed with a constant-power pan and a gain.
    /// The sum is scaled down only if it would exceed full scale, like a real audio device, so relative levels are preserved.
    /// </summary>
    public static float[] Mix(IReadOnlyList<MixTalker> talkers, double totalSeconds)
    {
        var frames = (int)Math.Ceiling(totalSeconds * OutputRate);
        var mix = new float[frames * 2];
        foreach (var talker in talkers)
        {
            var gain = Math.Pow(10, talker.GainDb / 20);
            var angle = (Math.Clamp(talker.Pan, -1, 1) + 1) * Math.PI / 4;
            var left = (float)(Math.Cos(angle) * gain);
            var right = (float)(Math.Sin(angle) * gain);
            var start = (int)Math.Round(talker.StartSeconds * OutputRate);
            for (var i = 0; i < talker.Samples16k.Length; i++)
            {
                for (var k = 0; k < 3; k++)
                {
                    var frame = start + i * 3 + k;
                    if (frame < 0 || frame >= frames) continue;
                    mix[frame * 2] += talker.Samples16k[i] * left;
                    mix[frame * 2 + 1] += talker.Samples16k[i] * right;
                }
            }
        }

        var peak = 0f;
        foreach (var sample in mix) peak = Math.Max(peak, Math.Abs(sample));
        if (peak > 0.99f)
        {
            var scale = 0.99f / peak;
            for (var i = 0; i < mix.Length; i++) mix[i] *= scale;
        }

        return mix;
    }
}

internal sealed record Utterance(string Id, string SpeakerId, float[] Samples, string Text);
internal sealed record PlacedUtterance(string Id, double StartSeconds, double EndSeconds);
internal sealed record PlannedTalker(string SpeakerId, double GainDb, double Pan, IReadOnlyList<PlacedUtterance> Placed, string Reference);
internal sealed record CrowdScenario(string Name, int Talkers, int Seed, double DurationSeconds, IReadOnlyList<PlannedTalker> Plan);

internal static class CrowdScenarioBuilder
{
    /// <summary>
    /// One scenario: talker 0 is the dominant (loudest, nearest) voice, the others are quieter and panned elsewhere. Everything is
    /// seeded, so the same inputs always give the same crowd. Only utterances that fit inside the target length are placed.
    /// </summary>
    public static (CrowdScenario Scenario, float[] Audio) Build(
        string name, int talkerCount, int seed, IReadOnlyDictionary<string, IReadOnlyList<Utterance>> speakers, double targetSeconds = 30)
    {
        if (talkerCount < 1 || talkerCount > speakers.Count) throw new ArgumentOutOfRangeException(nameof(talkerCount));
        var rng = new Random(seed);
        var chosen = speakers.Keys.OrderBy(id => id, StringComparer.Ordinal).OrderBy(_ => rng.Next()).Take(talkerCount).ToArray();
        var plan = new List<PlannedTalker>();
        var mixTalkers = new List<MixTalker>();
        var duration = 0d;
        for (var index = 0; index < chosen.Length; index++)
        {
            var gainDb = index == 0 ? -3 - rng.NextDouble() * 3 : -10 - rng.NextDouble() * 10;
            var pan = -0.9 + rng.NextDouble() * 1.8;
            var utterances = speakers[chosen[index]].OrderBy(u => u.Id, StringComparer.Ordinal).OrderBy(_ => rng.Next()).ToArray();
            var placed = new List<PlacedUtterance>();
            var texts = new List<string>();
            var time = index == 0 ? 0.3 : rng.NextDouble() * 2.0;
            foreach (var utterance in utterances)
            {
                var length = utterance.Samples.Length / 16_000d;
                if (placed.Count > 0 && time + length > targetSeconds) break;
                placed.Add(new PlacedUtterance(utterance.Id, time, time + length));
                texts.Add(utterance.Text);
                mixTalkers.Add(new MixTalker(utterance.Samples, gainDb, pan, time));
                time += length + (index == 0 ? 0.4 + rng.NextDouble() * 0.6 : 0.1 + rng.NextDouble() * 1.0);
            }

            duration = Math.Max(duration, placed.Count == 0 ? 0 : placed[^1].EndSeconds);
            plan.Add(new PlannedTalker(chosen[index], gainDb, pan, placed, string.Join(' ', texts)));
        }

        duration += 1.0;
        var scenario = new CrowdScenario(name, talkerCount, seed, duration, plan);
        return (scenario, CrowdMixer.Mix(mixTalkers, duration));
    }
}

/// <summary>Synthetic non-speech scenes for false-caption checks: steady noise, music-like tones, keyboard-like clicks.</summary>
internal static class NoiseScenes
{
    public static readonly string[] Kinds = ["pink", "tones", "clicks"];

    /// <summary>Mono 16 kHz samples, deterministic for a seed.</summary>
    public static float[] Make(string kind, int seed, double seconds)
    {
        var count = (int)(seconds * 16_000);
        var rng = new Random(seed);
        var samples = new float[count];
        switch (kind)
        {
            case "pink":
            {
                // Voss-McCartney pink noise: eight white-noise rows updated at halving rates.
                var rows = new double[8];
                double sum = 0;
                for (var i = 0; i < count; i++)
                {
                    var row = System.Numerics.BitOperations.TrailingZeroCount(i + 1) % rows.Length;
                    sum -= rows[row];
                    rows[row] = rng.NextDouble() * 2 - 1;
                    sum += rows[row];
                    samples[i] = (float)((sum + rng.NextDouble() * 2 - 1) / 9);
                }

                Normalise(samples, 0.03);
                break;
            }
            case "tones":
            {
                // A sustained three-note chord with harmonics and a slow tremolo: loud, steady, clearly not speech.
                double[] notes = [220.0, 277.18, 329.63];
                for (var i = 0; i < count; i++)
                {
                    var t = i / 16_000.0;
                    double v = 0;
                    foreach (var note in notes)
                    {
                        for (var h = 1; h <= 4; h++) v += Math.Sin(2 * Math.PI * note * h * t) / h;
                    }

                    samples[i] = (float)(v * (0.7 + 0.3 * Math.Sin(2 * Math.PI * 2 * t)));
                }

                Normalise(samples, 0.04);
                break;
            }
            case "clicks":
            {
                // Short noise bursts a few times a second, like keys or effects.
                for (var i = 0; i < count; i++)
                {
                    if (rng.NextDouble() >= 4.0 / 16_000) continue;
                    for (var k = 0; k < 160 && i + k < count; k++) samples[i + k] += (float)((rng.NextDouble() * 2 - 1) * 0.4 * Math.Exp(-k / 30.0));
                }

                break;
            }
            default:
                throw new ArgumentException($"Unknown noise scene '{kind}'.", nameof(kind));
        }

        return samples;
    }

    private static void Normalise(float[] samples, double targetRms)
    {
        double sum = 0;
        foreach (var sample in samples) sum += sample * sample;
        var rms = Math.Sqrt(sum / Math.Max(1, samples.Length));
        if (rms <= 0) return;
        var scale = (float)(targetRms / rms);
        for (var i = 0; i < samples.Length; i++) samples[i] *= scale;
    }
}

internal static class CrowdEval
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly int[] DefaultTalkerCounts = [1, 2, 3, 4, 6];

    /// <summary>Reads a LibriSpeech test-clean archive and writes fake-crowd WAV files plus a JSON answer sheet each.</summary>
    public static async Task<int> BuildAsync(string archivePath, string outputFolder, string[] options)
    {
        var seed = IntOption(options, "--seed", 1234);
        var repeats = IntOption(options, "--repeats", 2);
        Directory.CreateDirectory(outputFolder);
        MediaFoundationApi.Startup();

        Console.WriteLine($"Reading {archivePath} ...");
        var speakers = await Task.Run(() => ReadLibriSpeech(archivePath, maximumSpeakers: 14, maximumUtterancesPerSpeaker: 8));
        Console.WriteLine($"Using {speakers.Count} speakers, {speakers.Values.Sum(v => v.Count)} utterances.");

        var written = 0;
        foreach (var talkers in DefaultTalkerCounts)
        {
            for (var repeat = 0; repeat < repeats; repeat++)
            {
                var name = $"crowd-n{talkers}-{repeat + 1}";
                var (scenario, audio) = CrowdScenarioBuilder.Build(name, talkers, seed + talkers * 1000 + repeat, speakers);
                var wavePath = Path.Combine(outputFolder, name + ".wav");
                using (var writer = new WaveFileWriter(wavePath, WaveFormat.CreateIeeeFloatWaveFormat(CrowdMixer.OutputRate, 2)))
                {
                    writer.WriteSamples(audio, 0, audio.Length);
                }

                await File.WriteAllTextAsync(Path.Combine(outputFolder, name + ".json"), JsonSerializer.Serialize(scenario, Json));
                Console.WriteLine($"  {name}: {scenario.DurationSeconds:0.0} s, dominant speaker {scenario.Plan[0].SpeakerId}, {scenario.Plan[0].Placed.Count} utterances");
                written++;
            }
        }

        // Sounds that are not speech at all. Any caption produced from them is a false caption.
        foreach (var kind in NoiseScenes.Kinds)
        {
            var name = $"crowd-n0-{kind}";
            var rng = new Random(seed + 7 * (Array.IndexOf(NoiseScenes.Kinds, kind) + 1));
            var pan = -0.5 + rng.NextDouble();
            var audio = CrowdMixer.Mix([new MixTalker(NoiseScenes.Make(kind, seed, 30), 0, pan, 0)], 30);
            var wavePath = Path.Combine(outputFolder, name + ".wav");
            using (var writer = new WaveFileWriter(wavePath, WaveFormat.CreateIeeeFloatWaveFormat(CrowdMixer.OutputRate, 2)))
            {
                writer.WriteSamples(audio, 0, audio.Length);
            }

            await File.WriteAllTextAsync(Path.Combine(outputFolder, name + ".json"), JsonSerializer.Serialize(new CrowdScenario(name, 0, seed, 30, []), Json));
            Console.WriteLine($"  {name}: 30.0 s of {kind} (no speech at all)");
            written++;
        }

        Console.WriteLine($"Wrote {written} files to {outputFolder}");
        return 0;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<Utterance>> ReadLibriSpeech(string archivePath, int maximumSpeakers, int maximumUtterancesPerSpeaker)
    {
        var transcripts = new Dictionary<string, string>(StringComparer.Ordinal);
        var audio = new Dictionary<string, (string Speaker, byte[] Bytes)>(StringComparer.Ordinal);
        var kept = new List<string>();
        var perSpeaker = new Dictionary<string, int>(StringComparer.Ordinal);
        using var file = File.OpenRead(archivePath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (tar.GetNextEntry(copyData: false) is { } entry)
        {
            if (entry.EntryType != TarEntryType.RegularFile || entry.DataStream is null) continue;
            var parts = entry.Name.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var speaker = parts.Length >= 3 ? parts[^3] : "";
            var fileName = parts[^1];
            if (speaker.Length == 0) continue;
            if (!kept.Contains(speaker))
            {
                if (kept.Count >= maximumSpeakers) continue;
                kept.Add(speaker);
            }

            if (fileName.EndsWith(".trans.txt", StringComparison.Ordinal))
            {
                using var reader = new StreamReader(entry.DataStream, Encoding.UTF8);
                while (reader.ReadLine() is { } line)
                {
                    var split = line.IndexOf(' ');
                    if (split > 0) transcripts[line[..split]] = line[(split + 1)..].Trim();
                }
            }
            else if (fileName.EndsWith(".flac", StringComparison.Ordinal))
            {
                perSpeaker.TryGetValue(speaker, out var count);
                if (count >= maximumUtterancesPerSpeaker) continue;
                perSpeaker[speaker] = count + 1;
                using var memory = new MemoryStream();
                entry.DataStream.CopyTo(memory);
                audio[Path.GetFileNameWithoutExtension(fileName)] = (speaker, memory.ToArray());
            }
        }

        var result = new Dictionary<string, IReadOnlyList<Utterance>>(StringComparer.Ordinal);
        foreach (var group in audio.Where(pair => transcripts.ContainsKey(pair.Key)).GroupBy(pair => pair.Value.Speaker))
        {
            var utterances = group.Select(pair => new Utterance(pair.Key, group.Key, DecodeTo16kMono(pair.Value.Bytes), transcripts[pair.Key])).ToList();
            result[group.Key] = utterances;
        }

        return result;
    }

    private static float[] DecodeTo16kMono(byte[] flac)
    {
        using var reader = new StreamMediaFoundationReader(new MemoryStream(flac));
        if (reader.WaveFormat.SampleRate != 16_000 || reader.WaveFormat.Channels != 1)
        {
            throw new InvalidDataException($"Expected 16 kHz mono speech, got {reader.WaveFormat}.");
        }

        var provider = reader.ToSampleProvider();
        var samples = new List<float>(16_000 * 10);
        var buffer = new float[16_000];
        int read;
        while ((read = provider.Read(buffer.AsSpan())) > 0) samples.AddRange(buffer.AsSpan(0, read).ToArray());
        return samples.ToArray();
    }

    /// <summary>Feeds each crowd file through the real speech pipeline at real-time pace and reports accuracy and speed.</summary>
    public static async Task<int> RunAsync(string corpusFolder, string[] options)
    {
        var modeName = StringOption(options, "--mode", "Balanced");
        if (!Enum.TryParse<PerformanceMode>(modeName, ignoreCase: true, out var mode)) throw new ArgumentException($"Unknown mode '{modeName}'.");
        var speed = DoubleOption(options, "--speed", 1.0);
        var vadName = StringOption(options, "--vad", "auto");
        if (!Enum.TryParse<VoiceDetectionMode>(vadName, ignoreCase: true, out var vad)) throw new ArgumentException($"Unknown voice detection '{vadName}' (auto, energy, silero).");
        var tuning = new Tuning(DoubleOption(options, "--silero-threshold", 0.5), IntOption(options, "--end-silence", 350));
        var maxFiles = IntOption(options, "--max-files", int.MaxValue);
        var talkerFilter = StringOption(options, "--talkers", "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet();

        var manager = new LocalWhisperModelManager();
        if (!manager.IsModelInstalled(mode)) throw new InvalidOperationException($"The model for {mode} is not installed at {manager.GetModelPath(mode)}.");
        await manager.VerifyInstalledAsync(mode);
        var profile = PresetRuntimeProfile.For(mode);
        Console.WriteLine($"Mode {mode}: {profile.ModelFileName} verified; threads {profile.CpuThreads}, segment max {profile.MaximumSegmentSeconds} s, queue {profile.MaximumQueueLength}, feed speed x{speed}, voice detection {vad} (threshold {tuning.SileroThreshold}, end silence {tuning.EndSilenceMilliseconds} ms)");

        var sheets = Directory.GetFiles(corpusFolder, "crowd-*.json").OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var rows = new List<EvalRow>();
        foreach (var sheet in sheets.Take(maxFiles))
        {
            var scenario = JsonSerializer.Deserialize<CrowdScenario>(await File.ReadAllTextAsync(sheet))
                ?? throw new InvalidDataException($"{sheet} is not a crowd answer sheet.");
            if (talkerFilter.Count > 0 && !talkerFilter.Contains(scenario.Talkers)) continue;
            var row = await EvaluateOneAsync(scenario, Path.ChangeExtension(sheet, ".wav"), manager.GetModelPath(mode), profile, speed, vad, tuning, options.Contains("--show"));
            rows.Add(row);
            var wer = double.IsNaN(row.Wer) ? "   n/a" : $"{row.Wer,6:P1}";
            var invented = row.Talkers == 0 ? "false" : "invented";
            Console.WriteLine($"{row.Name,-16} N={row.Talkers}  WER {wer}  captions {row.Captions,3}  {invented} {row.Invented,2}  end-to-caption median {row.MedianEndToCaptionMs,5:0} ms p90 {row.P90EndToCaptionMs,5:0} ms  dropped {row.Dropped}  suppressed {row.Suppressed}  voice={row.VoiceDetection}");
        }

        var report = BuildReport(mode, speed, vad, rows);
        Console.WriteLine();
        Console.WriteLine(report);
        var reportPath = Path.Combine(corpusFolder, $"results-{mode}-{vad}-x{speed:0.##}-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        await File.WriteAllTextAsync(reportPath, report);
        Console.WriteLine($"Report saved to {reportPath}");
        return rows.Count == 0 ? 1 : 0;
    }

    private sealed record Tuning(double SileroThreshold, int EndSilenceMilliseconds);

    private static async Task<EvalRow> EvaluateOneAsync(CrowdScenario scenario, string wavePath, string modelPath, PresetRuntimeProfile profile, double speed, VoiceDetectionMode vad, Tuning tuning, bool show = false)
    {
        using var wave = new WaveFileReader(wavePath);
        if (wave.WaveFormat.SampleRate != CrowdMixer.OutputRate || wave.WaveFormat.Channels != 2 || wave.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
        {
            throw new InvalidDataException($"{wavePath} must be 48 kHz stereo float, like VRChat's process capture.");
        }

        var interleaved = new float[(int)(wave.Length / sizeof(float))];
        var read = 0;
        var provider = wave.ToSampleProvider();
        while (read < interleaved.Length)
        {
            var got = provider.Read(interleaved.AsSpan(read));
            if (got == 0) break;
            read += got;
        }

        var captions = new List<CaptionEvent>();
        var gate = new object();
        await using var capture = new StereoFixtureCapture();
        await using var speech = new LocalWhisperSpeechService { VoiceDetection = vad, SileroThreshold = (float)tuning.SileroThreshold, EndSilenceMilliseconds = tuning.EndSilenceMilliseconds };
        speech.CaptionProduced += (_, e) => { lock (gate) captions.Add(e.Caption); };
        await speech.StartAsync(capture, modelPath, profile.CpuThreads, showOriginalText: false, vadThreshold: profile.VadThreshold,
            maximumSegmentSeconds: profile.MaximumSegmentSeconds, maximumQueueLength: profile.MaximumQueueLength,
            translateToEnglish: true, sourceLanguage: "auto", vocabulary: string.Join(", ", AppSettings.DefaultVocabulary));
        var loading = Stopwatch.StartNew();
        while (speech.Current.State != SpeechPipelineState.Listening && loading.Elapsed < TimeSpan.FromSeconds(60)) await Task.Delay(100);

        await capture.FeedAsync(interleaved, speed);
        await capture.FeedAsync(new float[CrowdMixer.OutputRate * 2 * 2], speed);

        var drain = Stopwatch.StartNew();
        var quiet = 0;
        while (drain.Elapsed < TimeSpan.FromSeconds(60) && quiet < 5)
        {
            await Task.Delay(200);
            quiet = speech.Current.QueueLength == 0 && speech.Current.State == SpeechPipelineState.Listening ? quiet + 1 : 0;
        }

        var snapshot = speech.Current;
        var voiceDetection = snapshot.VoiceDetection;
        await speech.StopAsync();
        CaptionEvent[] ordered;
        lock (gate) ordered = captions.OrderBy(c => c.Timestamp).ToArray();

        var hypothesis = string.Join(' ', ordered.Select(c => c.TranslatedText));
        var references = scenario.Plan.Select(t => t.Reference).ToArray();
        var noiseOnly = scenario.Plan.Count == 0;
        if (show)
        {
            Console.WriteLine($"  --- {scenario.Name}: what the dominant talker said:");
            Console.WriteLine("  " + (noiseOnly ? "(nobody: this clip has no speech)" : scenario.Plan[0].Reference));
            Console.WriteLine("  --- what the app captioned (language, confidence, text):");
            foreach (var caption in ordered)
            {
                Console.WriteLine($"  [{caption.DetectedLanguage}, {caption.Confidence:0.00}, end-to-caption {caption.EndToCaptionDelay.TotalMilliseconds:0} ms] {caption.TranslatedText}");
            }

            Console.WriteLine($"  --- (suppressed by the quality gate: {snapshot.SuppressedCaptions}, dropped for waiting too long: {snapshot.DroppedSegments})");
        }

        var endToCaption = ordered.Select(c => c.EndToCaptionDelay.TotalMilliseconds).ToArray();
        return new EvalRow(
            scenario.Name,
            scenario.Talkers,
            noiseOnly ? double.NaN : WordErrorRate.Rate(scenario.Plan[0].Reference, hypothesis),
            ordered.Length,
            // With no talker at all, every caption is a false caption.
            noiseOnly ? ordered.Length : ordered.Count(c => EvalMath.IsInvented(c.TranslatedText, references)),
            EvalMath.Percentile(endToCaption, 50),
            EvalMath.Percentile(endToCaption, 90),
            snapshot.DroppedSegments,
            snapshot.SuppressedCaptions,
            voiceDetection);
    }

    private static string BuildReport(PerformanceMode mode, double speed, VoiceDetectionMode vad, IReadOnlyList<EvalRow> rows)
    {
        var text = new StringBuilder();
        text.AppendLine($"# Crowd evaluation, {mode}, voice detection {vad} ({rows.Select(r => r.VoiceDetection).Distinct().FirstOrDefault()}), feed speed x{speed:0.##}, {DateTime.Now:yyyy-MM-dd HH:mm}");
        text.AppendLine();
        text.AppendLine("WER is measured against the dominant (loudest) talker only. Lower is better. Invented = caption sharing under 30% of its words with every talker. For the 0-talker rows (noise, tones, clicks) every caption is a false caption and the delay columns are not meaningful.");
        text.AppendLine();
        text.AppendLine("| Talkers | Files | Mean WER | Captions | Invented / false | Median end-to-caption (ms) | p90 (ms) | Dropped | Suppressed |");
        text.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var group in rows.GroupBy(r => r.Talkers).OrderBy(g => g.Key))
        {
            var wer = group.Key == 0 ? "n/a" : $"{group.Average(r => r.Wer):P1}";
            text.AppendLine($"| {group.Key} | {group.Count()} | {wer} | {group.Sum(r => r.Captions)} | {group.Sum(r => r.Invented)} | {group.Average(r => r.MedianEndToCaptionMs):0} | {group.Average(r => r.P90EndToCaptionMs):0} | {group.Sum(r => r.Dropped)} | {group.Sum(r => r.Suppressed)} |");
        }

        return text.ToString();
    }

    private static string StringOption(string[] options, string name, string fallback)
    {
        var index = Array.IndexOf(options, name);
        return index >= 0 && index + 1 < options.Length ? options[index + 1] : fallback;
    }

    private static int IntOption(string[] options, string name, int fallback) =>
        int.TryParse(StringOption(options, name, ""), out var value) ? value : fallback;

    private static double DoubleOption(string[] options, string name, double fallback) =>
        double.TryParse(StringOption(options, name, ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0 ? value : fallback;

    private sealed record EvalRow(string Name, int Talkers, double Wer, int Captions, int Invented, double MedianEndToCaptionMs, double P90EndToCaptionMs, long Dropped, long Suppressed, string VoiceDetection);

    /// <summary>Delivers 48 kHz stereo float audio to the speech pipeline in 50 ms steps, like WASAPI process capture does.</summary>
    private sealed class StereoFixtureCapture : IProcessAudioCaptureService
    {
        public event EventHandler<AudioCaptureChangedEventArgs>? CaptureChanged { add { } remove { } }
        public event EventHandler<AudioChunkAvailableEventArgs>? AudioChunkAvailable;
        public AudioCaptureSnapshot Current => new() { State = AudioCaptureState.Capturing };
        public System.Threading.Channels.ChannelReader<AudioChunk> AudioChunks => System.Threading.Channels.Channel.CreateUnbounded<AudioChunk>().Reader;
        public Task StartAsync(int processId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async Task FeedAsync(float[] interleavedStereo, double speed)
        {
            const int framesPerStep = 2_400;
            var clock = Stopwatch.StartNew();
            var step = 0;
            for (var offset = 0; offset < interleavedStereo.Length; offset += framesPerStep * 2)
            {
                var count = Math.Min(framesPerStep * 2, interleavedStereo.Length - offset);
                var due = TimeSpan.FromMilliseconds(step * 50 / speed);
                var wait = due - clock.Elapsed;
                if (wait > TimeSpan.Zero) await Task.Delay(wait);
                step++;
                var bytes = MemoryMarshal.AsBytes(interleavedStereo.AsSpan(offset, count)).ToArray();
                AudioChunkAvailable?.Invoke(this, new AudioChunkAvailableEventArgs(new AudioChunk(
                    DateTimeOffset.UtcNow, bytes, new AudioFormatInfo(CrowdMixer.OutputRate, 2, 32, "IEEE float"), 0, 0)));
            }
        }
    }
}
