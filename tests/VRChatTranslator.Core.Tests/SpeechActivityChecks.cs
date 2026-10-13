using System.Runtime.InteropServices;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Speech;

namespace VRChatTranslator.Core.Tests;

internal static partial class Program
{
    private sealed class ScriptedSpeechActivity(Func<int, bool> decide) : ISpeechActivity
    {
        public int Calls { get; private set; }
        public bool Disposed { get; private set; }
        public string Name => "Scripted";
        public bool IsSpeech(ReadOnlySpan<float> samples) => decide(Calls++);
        public void Dispose() => Disposed = true;
    }

    /// <summary>50 ms of constant-level 48 kHz stereo float audio, the format VRChat's process capture delivers.</summary>
    private static AudioChunk ConstantChunk(float level)
    {
        var samples = Enumerable.Repeat(level, 2_400 * 2).ToArray();
        return new AudioChunk(DateTimeOffset.UtcNow, MemoryMarshal.AsBytes(samples.AsSpan()).ToArray(), new AudioFormatInfo(48_000, 2, 32, "IEEE float"), 0, 0);
    }

    private static Task TheSegmenterFollowsTheDetectorNotTheLoudness()
    {
        // Loud audio that the detector says is not speech (music, a game effect) must never open a segment.
        var notSpeech = new ScriptedSpeechActivity(_ => false);
        using (var segmenter = new PcmSpeechSegmenter(notSpeech))
        {
            for (var i = 0; i < 80; i++)
            {
                var result = segmenter.Accept(ConstantChunk(0.5f));
                AssertFalse(result.IsSpeechActive || result.CompletedSegment is not null, "Loud sound the detector rejects is not speech.");
            }
        }

        AssertTrue(notSpeech.Disposed, "The segmenter disposes the detector it was given.");

        // Silent audio that the detector says is speech (the detector is the authority) opens and then closes a segment.
        var speech = new ScriptedSpeechActivity(call => call is >= 5 and < 25);
        using var scripted = new PcmSpeechSegmenter(speech);
        var activeAt = -1;
        float[]? completed = null;
        for (var i = 0; i < 60; i++)
        {
            var result = scripted.Accept(ConstantChunk(0f));
            if (result.IsSpeechActive && activeAt < 0) activeAt = i;
            if (result.CompletedSegment is not null) { completed = result.CompletedSegment; break; }
        }

        AssertEqual(6, activeAt, "Two speech windows in a row open a segment.");
        AssertTrue(completed is not null && completed.Length > 16_000, "After about 350 ms without speech the segment is handed over with the speech in it.");
        AssertEqual("Scripted", scripted.VoiceDetection, "The segmenter reports which detector it uses.");
        return Task.CompletedTask;
    }

    private static Task VoiceDetectionFallsBackToTheEnergyGateWhenTheModelIsMissingOrDamaged()
    {
        var folder = Path.Combine(Path.GetTempPath(), "vrt-vad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var missing = Path.Combine(folder, "absent.onnx");
            using (var detector = SpeechActivityFactory.Create(VoiceDetectionMode.Auto, 0.008, missing)) AssertEqual("Energy gate", detector.Name, "A missing model falls back to the energy gate.");

            var damaged = Path.Combine(folder, "damaged.onnx");
            File.WriteAllBytes(damaged, new byte[SpeechActivityFactory.ExpectedBytes]);
            AssertFalse(SpeechActivityFactory.ModelIsIntact(damaged), "A file of the right size but the wrong content is not trusted.");
            using (var detector = SpeechActivityFactory.Create(VoiceDetectionMode.Auto, 0.008, damaged)) AssertEqual("Energy gate", detector.Name, "A damaged model falls back to the energy gate.");

            var truncated = Path.Combine(folder, "truncated.onnx");
            File.WriteAllBytes(truncated, new byte[1_000]);
            AssertFalse(SpeechActivityFactory.ModelIsIntact(truncated), "A truncated file is not trusted.");

            var refused = false;
            try { SpeechActivityFactory.Create(VoiceDetectionMode.Silero, 0.008, damaged).Dispose(); } catch (FileNotFoundException) { refused = true; }
            AssertTrue(refused, "Requiring Silero with a damaged model fails loudly instead of silently using something else.");

            using (var detector = SpeechActivityFactory.Create(VoiceDetectionMode.Energy, 0.008, missing)) AssertEqual("Energy gate", detector.Name, "Energy mode always uses the energy gate.");

            using var gate = new EnergySpeechActivity(0.008);
            AssertTrue(gate.IsSpeech(Enumerable.Repeat(0.05f, 800).ToArray()), "A loud stretch passes the energy gate.");
            AssertFalse(gate.IsSpeech(new float[800]), "Silence does not.");
            AssertFalse(gate.IsSpeech([]), "An empty stretch does not.");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task TheSileroDetectorKeptForExperimentsLoadsAndRejectsSilenceAndNoise()
    {
        AssertEqual(VoiceDetectionMode.Energy, new LocalWhisperSpeechService().VoiceDetection, "The app uses the energy gate; Silero is only for experiments.");
        AssertTrue(SpeechActivityFactory.ModelIsIntact(SpeechActivityFactory.ModelPath),
            $"The test folder's voice-detection model must be exactly the published file ({SpeechActivityFactory.ModelPath}).");
        using var automatic = SpeechActivityFactory.Create(VoiceDetectionMode.Auto, 0.008);
        AssertEqual("Silero VAD", automatic.Name, "With the model present, Auto uses the Silero detector.");

        int SpeechChunks(float[] samples)
        {
            using var detector = new SileroSpeechActivity(SpeechActivityFactory.ModelPath);
            var count = 0;
            for (var offset = 0; offset + 800 <= samples.Length; offset += 800) if (detector.IsSpeech(samples.AsSpan(offset, 800))) count++;
            return count;
        }

        AssertEqual(0, SpeechChunks(new float[16_000 * 3]), "Silence is never speech.");
        var pink = NoiseScenes.Make("pink", 5, 3);
        var pinkChunks = SpeechChunks(pink);
        AssertTrue(pinkChunks <= 2, $"Steady noise louder than the old energy gate must not count as speech ({pinkChunks} of 60 chunks did).");
        var tones = NoiseScenes.Make("tones", 5, 3);
        var toneChunks = SpeechChunks(tones);
        AssertTrue(toneChunks <= 2, $"Music-like tones must not count as speech ({toneChunks} of 60 chunks did).");
        var clicks = NoiseScenes.Make("clicks", 5, 3);
        var clickChunks = SpeechChunks(clicks);
        AssertTrue(clickChunks <= 2, $"Keyboard-like clicks must not count as speech ({clickChunks} of 60 chunks did).");

        var detectorToDispose = new SileroSpeechActivity(SpeechActivityFactory.ModelPath);
        detectorToDispose.Dispose();
        detectorToDispose.Dispose();
        AssertFalse(detectorToDispose.IsSpeech(new float[800]), "A disposed detector reports no speech instead of crashing.");
        return Task.CompletedTask;
    }
}

internal static partial class Program
{
    private static Task TheFilterDropsStockVideoPhrasesAndLoopsButNotRealSentences()
    {
        // Captions the recogniser actually invented from noise and clicks in the 2026-10-13 evaluation.
        string[] invented =
        [
            "Thank you for watching. Please subscribe and like.",
            "Thanks for watching! Please subscribe and hit that like button!",
            "Thank you for watching! Please subscribe and hit that like button!",
            "Welcome to my channel.",
            "I'm not sure if I can get a new one. I'm not sure if I can get a new one. I'm not sure if I can get a new one.",
            "Please subscribe.",
            "Subtitles by the Amara.org community"
        ];
        foreach (var text in invented)
        {
            AssertTrue(CaptionQualityGate.ShouldSuppress(text, 0.9), $"An invented stock caption must be dropped, even with high confidence: {text}");
        }

        // Real speech that merely contains the same words must survive.
        string[] real =
        [
            "Thanks for watching my stream, I'll be right back.",
            "Welcome to my world, come on in and sit down.",
            "I'm not sure if I can get a new one.",
            "I don't know. I don't know.",
            "Can you subscribe me to the list?",
            "Thank you.",
            "Please like the picture I sent you.",
            "See you next time we meet at the Friday event."
        ];
        foreach (var text in real)
        {
            AssertFalse(CaptionQualityGate.ShouldSuppress(text, 0.9), $"A real sentence must survive: {text}");
        }

        AssertTrue(CaptionQualityGate.IsStockVideoPhrase("THANKS FOR WATCHING!!!"), "Case and punctuation do not hide a stock phrase.");
        AssertFalse(CaptionQualityGate.IsStockVideoPhrase(""), "An empty caption is not a stock phrase.");
        AssertTrue(CaptionQualityGate.HasLoopedSentence("No way no way. No way no way. No way no way. Anyway."), "Three repeats of a sentence is a loop.");
        AssertFalse(CaptionQualityGate.HasLoopedSentence("Yes. Yes. Yes. Yes."), "One-word repeats are left to the existing rule, not this one.");
        return Task.CompletedTask;
    }
}
