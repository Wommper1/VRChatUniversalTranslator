using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VRChatTranslator.Audio;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;
using VRChatTranslator.Core.Settings;
using VRChatTranslator.Speech;

namespace VRChatTranslator.Core.Tests;

internal static partial class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args is ["--build-crowd", var crowdArchive, var crowdFolder, .. var buildOptions]) return await CrowdEval.BuildAsync(crowdArchive, crowdFolder, buildOptions);
        if (args is ["--eval", var corpusFolder, .. var evalOptions]) return await CrowdEval.RunAsync(corpusFolder, evalOptions);
        if (args is ["--check-fast-english-archive", var fastArchive]) return await CheckFastEnglishArchiveAsync(fastArchive);
        if (args is ["--build-foreign", var foreignRoot, var foreignOut]) return await CrowdEval.BuildForeignAsync(foreignRoot, foreignOut);
        if (args is ["--check-fast-english-download", var downloadFolder]) return await CheckFastEnglishDownloadAsync(downloadFolder);
        if (args is ["--speaker-test", var sampleFolder]) return await RunSpeakerTest(sampleFolder);
        if (args is ["--pipeline-test", var fixtureFolder]) return await RunPipelineTest(fixtureFolder);
        if (args is ["--pack-download-test", var packFolder]) return await RunPackDownloadTest(packFolder);
        if (args is ["--translation-test", var dataRoot])
        {
            await using var translator = new LocalTextTranslator(dataRoot);
            await translator.InstallPairAsync("en", "es", new Progress<string>(Console.WriteLine));
            var coldTimer = Stopwatch.StartNew();
            var result = await translator.TranslateAsync("Hello, how are you?", "en", "es");
            Console.WriteLine($"Cold translation: {coldTimer.Elapsed.TotalMilliseconds:0} ms");
            Console.WriteLine("English → Spanish: " + result);
            AssertTrue(result.Contains("Hola", StringComparison.OrdinalIgnoreCase), "Real local translation should produce Spanish.");
            AssertEqual("unchanged", await translator.TranslateAsync("unchanged", "en", "en"), "Same-language bypass must preserve text.");
            await translator.InstallPairAsync("es", "en", new Progress<string>(Console.WriteLine));
            var reverse = await translator.TranslateAsync("Hola, ¿cómo estás?", "es", "en");
            Console.WriteLine("Spanish → English: " + reverse);
            AssertTrue(reverse.Contains("how", StringComparison.OrdinalIgnoreCase), "Reverse pair should produce English.");
            AssertTrue(translator.InstalledPairs.Contains("English → Spanish"), "Installed packs must have readable direction labels.");
            AssertTrue(translator.OutputPackStatus("es").Contains("ready offline", StringComparison.Ordinal), "Status must identify an installed usable pack.");
            await using var preloaded = new LocalTextTranslator(dataRoot);
            var warmupTimer = Stopwatch.StartNew();
            await preloaded.PreloadAsync("en", "es");
            Console.WriteLine($"Preload: {warmupTimer.Elapsed.TotalMilliseconds:0} ms");
            var warmTimer = Stopwatch.StartNew();
            var warmResult = await preloaded.TranslateAsync("Hello, how are you?", "en", "es");
            Console.WriteLine($"Translation after preload: {warmTimer.Elapsed.TotalMilliseconds:0} ms");
            AssertEqual(result, warmResult, "Preloading must not change translation output.");
            await preloaded.PreloadAsync("es", "en");
            AssertEqual(reverse, await preloaded.TranslateAsync("Hola, ¿cómo estás?", "es", "en"), "BPE preload must preserve output too.");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var wasCanceled = false;
            try { await preloaded.PreloadAsync("en", "es", canceled.Token); } catch (OperationCanceledException) { wasCanceled = true; }
            AssertTrue(wasCanceled, "Canceled preload must stop without damaging installed packs.");
            AssertEqual(result, await preloaded.TranslateAsync("Hello, how are you?", "en", "es"), "Translation must recover after canceled preload.");
            return 0;
        }
        if (args is ["--capture-proof", var proofSourcePath])
        {
            return await RunCaptureProof(proofSourcePath);
        }

        if (args is ["--download-balanced-model"])
        {
            return await DownloadBalancedModel();
        }

        if (args is ["--whisper-file", var modelPath, var wavePath])
        {
            var text = await WhisperFileVerifier.TranslateToEnglishAsync(modelPath, wavePath);
            Console.WriteLine(text);
            return string.IsNullOrWhiteSpace(text) ? 1 : 0;
        }

        (string Name, Func<Task> Test)[] tests =
        [
            ("no process reports waiting", NoProcessReportsWaiting),
            ("newest valid VRChat process is selected", NewestValidProcessIsSelected),
            ("unchanged scans do not duplicate events", UnchangedScansDoNotDuplicateEvents),
            ("restart publishes the replacement PID", RestartPublishesReplacementPid),
            ("exit clears the detected process", ExitClearsDetectedProcess),
            ("provider failures are visible", ProviderFailuresAreVisible),
            ("audio meter reports silence", AudioMeterReportsSilence),
            ("audio meter measures float samples", AudioMeterMeasuresFloatSamples),
            ("audio meter ignores invalid floats", AudioMeterIgnoresInvalidFloats),
            ("diagnostic WAV has exact duration", DiagnosticWaveHasExactDuration),
            ("language catalog maps common codes", LanguageCatalogMapsCommonCodes),
            ("speech segmenter emits bounded utterance", SpeechSegmenterEmitsBoundedUtterance),
            ("caption hub keeps bounded ordered history", CaptionHubKeepsBoundedOrderedHistory),
            ("performance profiles stay within safe bounds", PerformanceProfilesStayWithinSafeBounds),
            ("settings round trip atomically", SettingsRoundTripAtomically),
            ("language labels resist one noisy chunk", LanguageLabelsResistOneNoisyChunk),
            ("word corrections respect boundaries scope and literal replacements", WordCorrectionsAreSafe),
            ("non-speech placeholders are suppressed without censoring sentences", CaptionFilterIsConservative),
            ("history clear discards session content", HistoryClear),
            ("invalid audio does not poison the segmenter", InvalidSegmentSamples),
            ("OSC packet is correctly padded and rejects unsafe text", OscPackets),
            ("speaker clusters preserve uncertainty and session boundaries", SpeakerClustering),
            ("malformed optional settings recover safely", SettingsNormalization),
            ("language pack status distinguishes missing partial and installed packs", LanguagePackStatus),
            ("caption route marks locked and auto-detected source languages", CaptionRouteMarksLockedAndAutoLanguages),
            ("output translation failure keeps the caption and explains why", OutputTranslationKeepsCaptionWhenItFails),
            ("overlay placement pulls a disconnected-monitor position back on screen", OverlayPlacementKeepsWindowOnScreen),
            ("OSC status is honest about VRChat, the port, consent and the last send", OscStatusIsHonestAboutReadiness),
            ("OSC listener check counts only endpoints a loopback reply can reach", OscListenerCheckCountsOnlyReachableEndpoints),
            ("OSC sender records transport outcomes but not rejections", OscSenderRecordsTransportOutcomeOnly),
            ("headset link retries back off 5 10 20 30 seconds", HeadsetLinkBacksOffGently),
            ("headset link reconnects after SteamVR restarts", HeadsetLinkReconnectsAfterSteamVrRestarts),
            ("headset link stops when the user disconnects", HeadsetLinkStopsWhenTheUserDisconnects),
            ("headset link does not retry a connect that never worked", HeadsetLinkDoesNotRetryAConnectThatNeverWorked),
            ("headset link keeps backing off when the link flaps", HeadsetLinkKeepsBackingOffWhenTheLinkFlaps),
            ("headset link manual connect while waiting keeps waiting", HeadsetLinkManualConnectWhileWaitingKeepsWaiting),
            ("headset connect at start works when SteamVR is already running", HeadsetAutoConnectWorksWhenSteamVrIsAlreadyRunning),
            ("headset connect at start waits for SteamVR to start", HeadsetAutoConnectWaitsForSteamVrToStart),
            ("headset connect at start can be cancelled", HeadsetAutoConnectCanBeCancelled),
            ("headset connect at start is off by default and persists", HeadsetAutoConnectIsOffByDefaultAndPersists),
            ("phone display only serves clients on this PC or a private network", PhoneClientsMustBeOnTheLocalNetwork),
            ("phone display refuses a Host name this PC would not answer to", PhoneHostHeaderMustBeANameThisPcAnswersTo),
            ("phone display PINs are six digits and compared exactly", PhonePinsAreSixDigitsAndMatchExactly),
            ("phone display limits wrong PIN guesses per client", PhonePinAttemptsAreLimitedPerClient),
            ("phone display status keeps file paths off the phone", PhoneStatusKeepsFilePathsOffThePhone),
            ("app data lives in one company folder shared by every component", AppDataLivesInOneCompanyFolder),
            ("downloads are accepted only when size and SHA-256 match exactly", DownloadsAreAcceptedOnlyWhenTheyMatchExactly),
            ("old diagnostic logs are removed and other files are kept", OldDiagnosticLogsAreRemovedAndOthersKept),
            ("phone page allows only its own script and style, with safe headers", PhonePageAllowsOnlyItsOwnScriptAndStyle),
            ("PowerShell is started by its full path", PowerShellIsStartedByFullPath),
            ("language pack extraction refuses entries that climb out of the folder", LanguagePackExtractionRefusesPathTraversal),
            ("a second launch is refused and the first copy is told to show itself", SecondLaunchIsRefusedAndTheFirstCopyIsToldToShow),
            ("the first-run notice says where to click and how big the model is", FirstRunNoticeSaysWhereToClickAndHowBig),
            ("word error rate is exact and forgiving about case and punctuation", WordErrorRateIsExactAndForgivingAboutCaseAndPunctuation),
            ("evaluation math (percentiles, word overlap, invented captions) is correct", EvaluationMathIsCorrect),
            ("the crowd mixer places voices like VRChat's stereo capture", CrowdMixerPlacesVoicesLikeVrchatCapture),
            ("crowd scenarios are seeded and planned correctly", CrowdScenariosAreSeededAndPlanned),
            ("Apply saves the chosen performance preset", ApplySavesThePerformancePreset),
            ("the segmenter follows the voice detector, not the loudness", TheSegmenterFollowsTheDetectorNotTheLoudness),
            ("voice detection falls back to the energy gate when the model is missing or damaged", VoiceDetectionFallsBackToTheEnergyGateWhenTheModelIsMissingOrDamaged),
            ("the Silero detector kept for experiments loads and rejects silence, noise, tones and clicks", TheSileroDetectorKeptForExperimentsLoadsAndRejectsSilenceAndNoise),
            ("the filter drops stock video phrases and loops but not real sentences", TheFilterDropsStockVideoPhrasesAndLoopsButNotRealSentences),
            ("the queue policy skips old speech only when newer speech waits", TheQueuePolicySkipsOldSpeechOnlyWhenNewerSpeechWaits),
            ("a full queue drops the oldest speech, keeps the newest and says so", AFullQueueDropsTheOldestSpeechAndSaysSo),
            ("old speech is skipped only when newer speech is waiting, never silently", OldSpeechIsSkippedOnlyWhenNewerSpeechIsWaiting),
            ("the skipped-speech note is plain and rate limited", SkippedSpeechNoticeIsPlainAndRateLimited),
            ("only the measured base-model presets use the faster audio context", OnlyTheMeasuredBaseModelPresetsUseTheFasterAudioContext),
            ("the fast English model unpacks only the two expected files", TheFastEnglishModelUnpacksOnlyTheTwoExpectedFiles),
            ("the fast English model rejects unsafe, wrong or incomplete archives and leaves nothing behind", TheFastEnglishModelRejectsUnsafeWrongOrIncompleteArchives),
            ("the fast English model download is pinned and optional", TheFastEnglishModelDownloadIsPinnedAndOptional),
            ("English routing never sends another language to the English-only engine", EnglishRoutingIsCautiousAboutOtherLanguages),
            ("the fast English engine captions speech without loading Whisper", TheFastEnglishEngineCaptionsSpeechWithoutLoadingWhisper)
        ];

        var failures = 0;
        foreach (var (name, test) in tests)
        {
            try
            {
                await test();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL  {name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static async Task NoProcessReportsWaiting()
    {
        var provider = new FakeProcessSnapshotProvider();
        await using var detector = CreateDetector(provider);
        var snapshot = await detector.RefreshAsync();

        AssertFalse(snapshot.IsDetected, "No process should be detected.");
        AssertEqual<int?>(null, snapshot.ProcessId, "PID should be empty.");
        AssertEqual<string?>(null, snapshot.LastError, "Waiting is not an error.");
    }

    private static async Task NewestValidProcessIsSelected()
    {
        var provider = new FakeProcessSnapshotProvider();
        var older = DateTimeOffset.Parse("2026-08-26T10:00:00Z");
        var newer = older.AddMinutes(2);
        provider.Set(
            new ProcessSnapshot(11, "NotVRChat", newer.AddMinutes(1)),
            new ProcessSnapshot(20, "vrchat", older),
            new ProcessSnapshot(42, "VRChat", newer));

        await using var detector = CreateDetector(provider);
        var snapshot = await detector.RefreshAsync();

        AssertTrue(snapshot.IsDetected, "VRChat should be detected.");
        AssertEqual<int?>(42, snapshot.ProcessId, "The newest valid VRChat process should win.");
        AssertEqual<DateTimeOffset?>(newer, snapshot.ProcessStartedAt, "Start identity should be retained.");
    }

    private static async Task UnchangedScansDoNotDuplicateEvents()
    {
        var provider = new FakeProcessSnapshotProvider();
        provider.Set(new ProcessSnapshot(77, "VRChat", DateTimeOffset.Parse("2026-08-26T10:00:00Z")));
        await using var detector = CreateDetector(provider);
        var eventCount = 0;
        detector.DetectionChanged += (_, _) => eventCount++;

        await detector.RefreshAsync();
        await detector.RefreshAsync();

        AssertEqual(1, eventCount, "Only the state transition should publish an event.");
    }

    private static async Task RestartPublishesReplacementPid()
    {
        var provider = new FakeProcessSnapshotProvider();
        provider.Set(new ProcessSnapshot(100, "VRChat", DateTimeOffset.Parse("2026-08-26T10:00:00Z")));
        await using var detector = CreateDetector(provider);
        var publishedPids = new List<int?>();
        detector.DetectionChanged += (_, args) => publishedPids.Add(args.Snapshot.ProcessId);

        await detector.RefreshAsync();
        provider.Set(new ProcessSnapshot(200, "VRChat", DateTimeOffset.Parse("2026-08-26T10:05:00Z")));
        await detector.RefreshAsync();

        AssertSequence([100, 200], publishedPids, "A replacement process must publish its new PID.");
    }

    private static async Task ExitClearsDetectedProcess()
    {
        var provider = new FakeProcessSnapshotProvider();
        provider.Set(new ProcessSnapshot(300, "VRChat", DateTimeOffset.Parse("2026-08-26T10:00:00Z")));
        await using var detector = CreateDetector(provider);

        await detector.RefreshAsync();
        provider.Set();
        var snapshot = await detector.RefreshAsync();

        AssertFalse(snapshot.IsDetected, "Exited VRChat should be cleared.");
        AssertEqual<int?>(null, snapshot.ProcessId, "Exited process PID should be cleared.");
    }

    private static async Task ProviderFailuresAreVisible()
    {
        var provider = new FakeProcessSnapshotProvider
        {
            Failure = new InvalidOperationException("synthetic provider failure")
        };
        await using var detector = CreateDetector(provider);
        var snapshot = await detector.RefreshAsync();

        AssertFalse(snapshot.IsDetected, "A failed scan cannot report detection.");
        AssertTrue(snapshot.LastError?.Contains("synthetic provider failure", StringComparison.Ordinal) == true,
            "The provider error should remain visible.");
    }

    private static Task AudioMeterReportsSilence()
    {
        var samples = new float[64];
        var (peak, rms) = AudioLevelCalculator.MeasureIeeeFloat32(MemoryMarshal.AsBytes(samples.AsSpan()));
        AssertEqual(0d, peak, "Silence peak should be zero.");
        AssertEqual(0d, rms, "Silence RMS should be zero.");
        return Task.CompletedTask;
    }

    private static Task AudioMeterMeasuresFloatSamples()
    {
        float[] samples = [0.5f, -0.25f, 0.25f, -0.5f];
        var (peak, rms) = AudioLevelCalculator.MeasureIeeeFloat32(MemoryMarshal.AsBytes(samples.AsSpan()));
        AssertNear(0.5, peak, 0.0001, "Peak should use the greatest absolute sample.");
        AssertNear(Math.Sqrt(0.15625), rms, 0.0001, "RMS should measure sample energy.");
        return Task.CompletedTask;
    }

    private static Task AudioMeterIgnoresInvalidFloats()
    {
        float[] samples = [float.NaN, float.PositiveInfinity, 0.25f];
        var (peak, rms) = AudioLevelCalculator.MeasureIeeeFloat32(MemoryMarshal.AsBytes(samples.AsSpan()));
        AssertNear(0.25, peak, 0.0001, "Invalid samples must not poison peak metering.");
        AssertNear(0.25, rms, 0.0001, "Invalid samples must not poison RMS metering.");
        return Task.CompletedTask;
    }

    private static async Task DiagnosticWaveHasExactDuration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vrchat-translator-wave-test-{Guid.NewGuid():N}.wav");
        var format = new AudioFormatInfo(48_000, 2, 32, "IEEE float");
        var source = new byte[1_000];

        try
        {
            await FloatWaveFile.WriteExactDurationAsync(path, format, source, TimeSpan.FromMilliseconds(100));
            var bytes = await File.ReadAllBytesAsync(path);
            AssertEqual(44 + 48_000 * 2 * 4 / 10, bytes.Length, "WAV length should represent exactly 100 ms.");
            AssertEqual("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4), "WAV should have a RIFF header.");
            AssertEqual("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4), "WAV should have a WAVE signature.");
            AssertEqual(3, BitConverter.ToUInt16(bytes, 20), "WAV should declare IEEE-float samples.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Task LanguageCatalogMapsCommonCodes()
    {
        AssertEqual("Japanese", LanguageCatalog.GetName("ja"), "Japanese should have a readable label.");
        AssertEqual("English", LanguageCatalog.GetName("EN"), "Language codes should be case-insensitive.");
        AssertEqual("ZZ", LanguageCatalog.GetName("zz"), "Unknown codes should remain visible.");
        return Task.CompletedTask;
    }

    private static Task SpeechSegmenterEmitsBoundedUtterance()
    {
        var segmenter = new PcmSpeechSegmenter();
        SpeechSegmenterResult result = default;
        for (var index = 0; index < 8; index++)
        {
            result = segmenter.Accept(CreateStereoChunk(0.08f));
        }

        for (var index = 0; index < 12; index++)
        {
            var next = segmenter.Accept(CreateStereoChunk(0f));
            if (next.CompletedSegment is not null)
            {
                result = next;
                break;
            }
        }

        AssertTrue(result.CompletedSegment is { Length: > 8_000 }, "Speech followed by silence should produce an utterance.");
        AssertFalse(result.IsSpeechActive, "The segmenter should return to silence after completing an utterance.");
        return Task.CompletedTask;
    }

    private static Task CaptionHubKeepsBoundedOrderedHistory()
    {
        var hub = new CaptionHub(2);
        var events = 0;
        hub.CaptionPublished += (_, _) => events++;
        hub.Publish(CreateCaption("one"));
        hub.Publish(CreateCaption("two"));
        hub.Publish(CreateCaption("three"));

        AssertEqual(3, events, "Every caption should be published to live consumers.");
        AssertSequence(["two", "three"], hub.History.Select(caption => caption.TranslatedText).ToArray(),
            "History should retain the newest captions in order.");
        return Task.CompletedTask;
    }

    private static CaptionEvent CreateCaption(string text) => new(
        DateTimeOffset.UtcNow,
        "en",
        "English",
        text,
        text,
        1,
        TimeSpan.Zero);

    private static Task PerformanceProfilesStayWithinSafeBounds()
    {
        foreach (var mode in Enum.GetValues<PerformanceMode>())
        {
            var profile = PresetRuntimeProfile.For(mode);
            AssertTrue(profile.ModelFileName.EndsWith(".bin", StringComparison.Ordinal), "Every preset should select a local model.");
            AssertTrue(!profile.ModelFileName.Contains(".en.", StringComparison.OrdinalIgnoreCase), "English-only Whisper models are forbidden.");
            AssertTrue(profile.CpuThreads is >= 1 and <= 8, "Thread counts must stay safe.");
            AssertTrue(profile.MaximumQueueLength is >= 1 and <= 5, "Queues must remain bounded.");
            AssertTrue(profile.MaximumSegmentSeconds is >= 4 and <= 20, "Segment duration must stay bounded.");
        }

        return Task.CompletedTask;
    }

    private static async Task SettingsRoundTripAtomically()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"vrchat-translator-settings-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "config.json");
        try
        {
            var store = new JsonSettingsStore(path);
            var expected = new AppSettings { SelectedMode = PerformanceMode.UltraLight, PreloadOutputLanguage = true };
            await store.SaveAsync(expected);
            var actual = await store.LoadAsync();
            AssertEqual(PerformanceMode.UltraLight, actual.SelectedMode, "Saved mode should round trip.");
            AssertTrue(actual.PreloadOutputLanguage, "Output-pack preload preference should persist.");
            AssertFalse(File.Exists(path + ".tmp"), "Atomic save should not leave a temporary file.");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                File.Delete(path);
                Directory.Delete(directory);
            }
        }
    }

    private static Task LanguageLabelsResistOneNoisyChunk()
    {
        var stabilizer = new LanguageStabilizer();
        var now = DateTimeOffset.UtcNow;
        AssertEqual("ja", stabilizer.Resolve("ja", 0.55, now), "The first usable language should become active.");
        AssertEqual("ja", stabilizer.Resolve("en", 0.45, now.AddSeconds(1)), "One noisy English result must not relabel foreign speech.");
        AssertEqual("en", stabilizer.Resolve("en", 0.45, now.AddSeconds(2)), "Two agreeing chunks should allow a real speaker change.");
        AssertEqual("es", stabilizer.Resolve("es", 0.80, now.AddSeconds(3)), "A very clear result should switch immediately.");
        return Task.CompletedTask;
    }

    private static Task WordCorrectionsAreSafe()
    {
        WordCorrection[] rules = [new() { Find = "Alex", Replace = "Alex$1" },
            new() { Find = "world", Replace = "room", Scope = CorrectionScope.Translated },
            new() { Find = "hello", Replace = "bye", Enabled = false }];
        AssertEqual("Alex$1 Alexander hello world", WordCorrectionService.Apply("Alex Alexander hello world", rules, CorrectionScope.Original), "Rules must be literal, whole-word and scoped.");
        AssertEqual("Alex$1 room", WordCorrectionService.Apply("alex world", rules, CorrectionScope.Translated), "Matching should ignore case.");
        AssertEqual("café!", WordCorrectionService.Apply("cafe!", [new() { Find = "cafe", Replace = "café" }], CorrectionScope.Original), "Unicode must survive.");
        return Task.CompletedTask;
    }

    private static Task CaptionFilterIsConservative()
    {
        foreach (var marker in new[] { "[BLANK_AUDIO]", "(speaking foreign language)", "[Music]", "♪", "   " })
            AssertTrue(CaptionQualityGate.ShouldSuppress(marker, 0.8), "Non-speech markers should not become captions.");
        AssertFalse(CaptionQualityGate.ShouldSuppress("I enjoy speaking foreign language classes.", 0.8), "Real sentences must survive.");
        AssertFalse(CaptionQualityGate.ShouldSuppress("こんにちは", 0.8), "Japanese text must survive.");
        AssertTrue(CaptionQualityGate.ShouldSuppress("bad guess", 0.05), "Very low confidence should be suppressed.");
        return Task.CompletedTask;
    }

    private static Task HistoryClear()
    {
        var hub = new CaptionHub();
        hub.Publish(CreateCaption("temporary"));
        hub.Clear();
        AssertEqual(0, hub.History.Count, "Clear must discard central replay history too.");
        return Task.CompletedTask;
    }

    private static Task InvalidSegmentSamples()
    {
        var segmenter = new PcmSpeechSegmenter();
        for (var index = 0; index < 10; index++) segmenter.Accept(CreateStereoChunk(float.NaN));
        AssertEqual<float[]?>(null, segmenter.Flush(), "Invalid audio must not turn into speech.");
        return Task.CompletedTask;
    }

    private static Task OscPackets()
    {
        var packet = OscReplySender.BuildPacket("こんにちは");
        AssertEqual(0, packet.Length % 4, "OSC strings must be padded to four bytes.");
        AssertTrue(System.Text.Encoding.UTF8.GetString(packet).Contains(",sTF\0", StringComparison.Ordinal), "OSC must request explicit immediate text without sound.");
        foreach (var invalid in new[] { "", new string('a', 145), "bad\0text", string.Join('\n', Enumerable.Repeat("x", 10)) })
        {
            var rejected = false;
            try { OscReplySender.BuildPacket(invalid); } catch (ArgumentException) { rejected = true; }
            AssertTrue(rejected, "Invalid reply must never be silently truncated or sent.");
        }
        return Task.CompletedTask;
    }

    private static Task SpeakerClustering()
    {
        var clusters = new SpeakerClusters();
        AssertEqual("Unknown speaker", clusters.Resolve([float.NaN, 0]), "Invalid embeddings must be unknown.");
        AssertEqual("Speaker 1", clusters.Resolve([1, 0]), "First voice gets a session label.");
        AssertEqual("Speaker 1", clusters.Resolve([.99f, .01f]), "Close voice remains consistent.");
        AssertEqual("Speaker 2", clusters.Resolve([0, 1]), "Distinct voice gets another label.");
        AssertEqual("Unknown speaker", clusters.Resolve([.7f, .7f]), "Ambiguous blend must not force an attribution.");
        clusters.Clear();
        AssertEqual("Speaker 1", clusters.Resolve([0, 1]), "Clearing discards all identity state.");
        return Task.CompletedTask;
    }

    private static Task SettingsNormalization()
    {
        var safe = JsonSettingsStore.Normalize(new AppSettings { Overlay = new() { FontSize = double.NaN, Left = double.PositiveInfinity },
            CustomMode = null!, Headset = null!, WordCorrections = [null!, new() { Find = "name", Replace = null! }], CustomVocabulary = null! });
        AssertEqual(32d, safe.Overlay.FontSize, "Nonfinite font should recover.");
        AssertEqual<double?>(null, safe.Overlay.Left, "Invalid position must be reset.");
        AssertEqual(4, safe.CustomMode.CpuThreads, "Null settings should restore defaults.");
        AssertEqual(1, safe.WordCorrections.Count, "Null rules should be discarded.");
        return Task.CompletedTask;
    }

    private static AudioChunk CreateStereoChunk(float value)
    {
        var floats = new float[48_000 / 20 * 2];
        Array.Fill(floats, value);
        var bytes = MemoryMarshal.AsBytes(floats.AsSpan()).ToArray();
        return new AudioChunk(
            DateTimeOffset.UtcNow,
            bytes,
            new AudioFormatInfo(48_000, 2, 32, "IEEE float"),
            0,
            0);
    }

    private static async Task<int> DownloadBalancedModel()
    {
        var manager = new LocalWhisperModelManager();
        var lastReported = -1;
        var progress = new Progress<ModelDownloadProgress>(value =>
        {
            var percent = (int)(value.Percentage ?? 0);
            if (percent >= lastReported + 10)
            {
                lastReported = percent;
                Console.WriteLine($"Model download: {percent}%");
            }
        });
        var path = await manager.DownloadBalancedModelAsync(progress);
        Console.WriteLine($"Model ready: {path}");
        return File.Exists(path) ? 0 : 1;
    }

    private static async Task<int> RunCaptureProof(string proofSourcePath)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
        {
            Console.Error.WriteLine("FAIL  capture proof requires Windows 10 build 20348 or later.");
            return 1;
        }

        if (!File.Exists(proofSourcePath))
        {
            Console.Error.WriteLine($"FAIL  proof source was not found: {proofSourcePath}");
            return 1;
        }

        await using var capture = new WasapiProcessAudioCaptureService();

        using var silentTarget = StartProofSource(proofSourcePath, "--silent 3500");
        await capture.StartAsync(silentTarget.Id);
        using var unrelatedTone = StartProofSource(proofSourcePath, "--tone 250 1000 880");
        var exclusionPeak = await MeasurePeakAsync(capture, TimeSpan.FromMilliseconds(1700));
        await capture.StopAsync();
        await WaitForSuccessfulExit(unrelatedTone);
        await WaitForSuccessfulExit(silentTarget);

        using var targetTone = StartProofSource(proofSourcePath, "--tone 750 1000 440");
        await capture.StartAsync(targetTone.Id);
        var targetPeak = await MeasurePeakAsync(capture, TimeSpan.FromMilliseconds(2200));
        await capture.StopAsync();
        await WaitForSuccessfulExit(targetTone);

        Console.WriteLine($"Unrelated-process peak while silent target was selected: {exclusionPeak:P3}");
        Console.WriteLine($"Selected-process tone peak: {targetPeak:P3}");

        if (exclusionPeak > 0.001)
        {
            Console.Error.WriteLine("FAIL  unrelated system audio leaked into process-only capture.");
            return 1;
        }

        if (targetPeak < 0.01)
        {
            Console.Error.WriteLine("FAIL  selected-process audio was not captured.");
            return 1;
        }

        Console.WriteLine("PASS  process-only capture contains the selected process and excludes unrelated audio.");
        return 0;
    }

    private static Process StartProofSource(string executablePath, string arguments)
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("The audio proof source process did not start.");
    }

    [SupportedOSPlatform("windows10.0.20348")]
    private static async Task<double> MeasurePeakAsync(
        WasapiProcessAudioCaptureService capture,
        TimeSpan duration)
    {
        var peak = 0d;
        var gate = new object();

        void OnCaptureChanged(object? sender, AudioCaptureChangedEventArgs eventArgs)
        {
            lock (gate)
            {
                peak = Math.Max(peak, eventArgs.Snapshot.PeakLevel);
            }
        }

        capture.CaptureChanged += OnCaptureChanged;
        try
        {
            await Task.Delay(duration);
        }
        finally
        {
            capture.CaptureChanged -= OnCaptureChanged;
        }

        lock (gate)
        {
            return peak;
        }
    }

    private static async Task WaitForSuccessfulExit(Process process)
    {
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
        AssertEqual(0, process.ExitCode, "The proof audio source should exit cleanly.");
    }

    private static VrchatProcessDetector CreateDetector(IProcessSnapshotProvider provider) =>
        new(provider, TimeSpan.FromHours(1));

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
        }
    }

    private static void AssertNear(double expected, double actual, double tolerance, string message)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
        }
    }

    private static void AssertSequence<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string message)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException($"{message} Expected: [{string.Join(", ", expected)}]; actual: [{string.Join(", ", actual)}].");
        }
    }

    private sealed class FakeProcessSnapshotProvider : IProcessSnapshotProvider
    {
        private IReadOnlyList<ProcessSnapshot> _snapshots = [];

        public Exception? Failure { get; init; }

        public IReadOnlyList<ProcessSnapshot> GetProcessesByName(string executableNameWithoutExtension)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            AssertEqual("VRChat", executableNameWithoutExtension, "Detector should query the executable name without .exe.");
            return _snapshots;
        }

        public void Set(params ProcessSnapshot[] snapshots)
        {
            _snapshots = snapshots;
        }
    }
}
