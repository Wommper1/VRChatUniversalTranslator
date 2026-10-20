# Verification — versions 1.1.0 to 1.1.4

This records what was actually run and what was not. It is not a claim of bug-free or fully hardware-accepted software; see "Not verified" below.

## Release 1.1.4, 2026-10-20

1.1.4 changes one library and nothing else in the app: NAudio.Wasapi 3.1.0 to 3.2.0 (audio capture). NAudio 3.2.0's release notes list macOS support, VB.NET helpers, deprecations of old playback and WAV helper APIs, and fixes to `WasapiPlayer`, `WasapiOut` and `DirectSoundOut`. This app uses none of those: its packaged code uses only `WasapiRecorderBuilder` (per-process capture), `WaveFormat` and `ToSampleProvider`, and only its own test tools use `WaveFileReader` (not the deprecated `ReadNextSampleFrame`). What was run on the build PC (Windows 11, .NET SDK 10.0.401):

| Check | Result |
| --- | --- |
| Debug build and Release package build, warnings as errors | 0 warnings, 0 errors |
| Automated checks | 72/72 (same as 1.1.3) |
| WPF `--smoke-test` and `--phone-self-test` | pass, both from source and from the extracted ZIP; the extracted executable reports file version 1.1.4.0 and NAudio.Wasapi 3.2.0 |
| Real WASAPI process capture (`--capture-proof`, two short test tones) | selected process 14.999%, unrelated process 0.000% (identical to 1.1.1 with NAudio 3.1.0) |
| Known-vulnerable packages | none in any project |
| Package contents | 659 files, the same file list as the published 1.1.3 package: none added, none removed, 21 differ. Twelve are the NAudio libraries (`NAudio.Core.dll`, `NAudio.Wasapi.dll`) and the app's own assemblies, symbol files and manifest, which carry the version number. Nine are documents: four with real text changes (README, SECURITY, RELEASE_NOTES, VERIFICATION) and five whose text is identical to 1.1.3 and differ only in line endings (PRIVACY, QUICK_START, THIRD_PARTY_NOTICES and two licence files; the build PC's Git checks files out with Windows line endings). No other third-party file changed |
| Scan of every file in the package for a user name, machine path, repository path, e-mail address or source-link data (ASCII and UTF-16) | none in any file the app owns. The word "sourcelink" appears inside `Microsoft.DiaSymReader.Native.amd64.dll`, a Microsoft native library that is byte-identical to the one in the published 1.1.3 package |
| Files in the package that look like settings, models, logs or keys | none (only `VRChatUniversalTranslator.runtimeconfig.json`, the .NET runtime configuration) |

Not run for this release: a live VRChat session, the SteamVR headset, typed replies to the VRChat chatbox, a physical phone, language packs, speaker labels, and the one-copy check with the extracted executable (the code that does it did not change). Everything that was not verified for 1.1.3 still is not.

## Release 1.1.3, 2026-10-15

1.1.3 makes captions faster and stops dropped speech being silent (see [RELEASE_NOTES.md](RELEASE_NOTES.md)). What was run on the build PC:

| Check | Result |
| --- | --- |
| Debug and Release build, warnings as errors | 0 warnings, 0 errors |
| Automated checks | 72/72 (53 in 1.1.2, plus 19 new: the speech queue and skipped-speech note, the audio-context presets, the fast English installer and its safety rules, the language routing, the fast engine captioning without Whisper). The safety rules (unsafe archive paths, wrong size or checksum, missing file, a locked non-English language, the queue keeping the newest speech, the stale-segment rule) were each deliberately broken once to confirm a check fails |
| WPF `--smoke-test` and `--phone-self-test` | pass, both from source and from the extracted ZIP; the smoke test now also checks the skipped-speech overlay note and the fast English Settings text and states |
| One-copy check with the extracted executable | the first copy started; two further launches each exited with code 0 in 264 ms in total; exactly one copy stayed running and closed when asked |
| The app's own fast English installer, run for real | downloaded the Parakeet archive from GitHub into an empty folder in 12.4 s; the archive and both extracted files matched their pinned size and SHA-256; only `model.int8.onnx` and `tokens.txt` were written; the recogniser started from them; a second request did nothing. Run with the development command `--check-fast-english-download`; a person clicking the Settings button in the finished app has not yet been done |
| Known-vulnerable packages | none (including the new SharpZipLib 1.4.2) |
| Package contents | 659 files. Compared file by file with the 1.1.2 package: 3 added (the SharpZipLib library and its licence, the NVIDIA attribution notice), none removed, 17 changed: seven documents and the app's own assemblies and manifest, which carry the version number. No other third-party file changed |
| Scan of every file in the package for a user name, machine path, repository path or source-link data | none |
| Files in the package that look like settings, models, logs or keys | none |

Not run for this release: a live VRChat session with the fast English engine (it was measured on test recordings only), the SteamVR headset, typed replies to the VRChat chatbox, a physical phone, language packs, and speaker labels. The audio capture code is unchanged since 1.1.1, so `--capture-proof` was not re-run.

## Crowd accuracy evaluation, 2026-10-13 (development measurements on the 1.1.2 code)

Purpose: measure how well captions hold up when several people talk at once, so that changes can be judged by numbers. Method: the public LibriSpeech test-clean speech set (CC BY 4.0, not shipped, kept outside the repository) is mixed into fake crowds of 1, 2, 3, 4 and 6 talkers: one dominant (loudest, 3 to 6 dB below full level) and the others 10 to 20 dB quieter, each placed at a random stereo position and delivered as 48 kHz stereo float like VRChat's process capture. Two clips per crowd size (10 clips of about 30 s), plus three 30 s non-speech clips (steady noise, music-like tones, keyboard-like clicks). Each clip is played in real time through the real speech pipeline on CPU. Word error rate (WER) is measured against the dominant talker only. Tools: `--build-crowd` and `--eval` in `tests/VRChatTranslator.Core.Tests` (not part of the app).

| Preset | 1 voice | 2 voices | 3 voices | 4 voices | 6 voices | Typical delay |
| --- | --- | --- | --- | --- | --- | --- |
| Balanced | 7.4% | 28.6% | 67.5% | 60.4% | 59.1% | about 1.9 s |
| Responsive CPU | 16.4% | 30.3% | 76.3% | 66.0% | 59.2% | about 1.8 s |
| Accurate | 8.5% | 21.1% | 73.0% | 54.9% | 62.7% | 4.6 to 7.0 s |

Findings:

- A single voice is captioned well. With three or more voices the loudest talker's words are mostly present but garbled by the others, and the larger Accurate model does not fix that and is much slower. Whisper's own confidence is not a usable overlap detector (one garbled 3-voice caption scored 0.91).
- Steady noise made the recogniser invent "Thank you for watching. Please subscribe and like." five times. The stock-phrase filter added after this removed all of them with no change in any crowd result.
- Silero VAD (a neural voice detector, MIT) was tried in place of the loudness gate: no accuracy gain with several voices, a small delay cost (mean of three alternating runs on one voice: 2,080 ms against 1,970 ms; the ranges overlap), and it fed fewer non-speech sounds to the recogniser (5 against 14 passes in 90 s of noise). It is not used by the app; the model and its licence are kept in the test folder for later experiments.
- A shorter wait before closing a segment (250 ms instead of 350 ms) made delay and accuracy worse, not better.

Limits: fake crowds built from clean read speech, two clips per size, English only, one PC, delays vary by about 0.3 s from run to run with whatever else the PC is doing, and the stereo positions are an assumption about VRChat's spatial audio that has not been checked against a real lobby recording.

## Speed and fast-English evaluation, 2026-10-14 (development measurements)

Purpose: find out whether captions can be made clearly faster without losing accuracy. Method: a second, different fake-crowd set (seed 777; the same recipe as above, different voices), played in real time through the real speech pipeline, Balanced preset (base model, 4 threads, loudness gate). Three single-voice clips (about 28 s each), three 3-voice clips (about 29 s each) and the three non-speech clips. "Recognition" is the time the engine spent on one segment; "end to caption" is from the speaker stopping to the caption appearing (it includes the 0.35 s the app waits to be sure the speaker has stopped). One PC (Ryzen 7 5800X), delays vary by about 0.3 s from run to run.

| Engine / setting | Recognition | End to caption | Word errors, 1 voice | Word errors, 3 voices | False captions on noise |
| --- | --- | --- | --- | --- | --- |
| Whisper base (today's Balanced) | about 1.73 s | about 2.07 s | 5.9% | 57.4% | 0 |
| Whisper base, audio context 768 | about 1.20 s | about 1.55 s | 6.8% | 57.3% | 0 |
| Whisper base, audio context 512 | about 1.08 s | about 1.41 s | 8.4% | 54.0% | not run |
| Whisper base, audio context 384 | about 1.06 s | about 1.39 s | 8.4% | 47.8% | not run |
| Whisper base compressed (q5_1) | about 2.0 s (slower) | about 2.4 s | 6.8% | 51.2% | not run |
| Whisper base compressed (q5_1), audio context 768 | about 1.4 s | about 1.75 s | 8.0% | 55.0% | not run |
| Parakeet 110M int8 (English only) | about 0.06 s | about 0.40 s | 3.8% | 34.4% | 0 |
| Hybrid: Parakeet for English, language check by Whisper base, audio context 384 | about 0.9 s | about 1.26 s | 3.9% (1 voice only) | not run | not run |
| Hybrid: Parakeet for English, language check by Whisper tiny | about 0.46 s | about 0.80 s | 3.9% (1 voice only) | not run | not run |

Findings:

- The compressed Whisper model (q5_1) is smaller to download but not faster on this CPU: about 15% slower. It is not used.
- Shrinking Whisper's audio context to 768 made recognition about 30% faster and produced no false captions on the noise clips. On the first three single-voice clips it cost about one point of word errors (5.9% to 6.8%). A confirmation on two more single-voice clips from the first crowd set, with the preset default, showed it about 35% faster with no extra errors (0.0% and 6.2% against 8.6% and 6.2%); over all five clips the mean was 5.3% with 768 and 6.5% without. Below 768 accuracy fell without much more speed. Five single-voice clips is still a small sample. Balanced and Responsive CPU (base model) now use 768; the others keep Whisper's default because they were not measured.
- Parakeet is far faster (the recogniser itself takes about 60 ms) and made fewer errors on these clips. The caption appears about 0.4 s after speech ends, almost all of which is the app's own wait to see whether the speaker has stopped. On the noise clips it produced no false captions (the quality filter dropped every empty or stock result).
- Parakeet is English only. It is used only for English: either the spoken language is locked to English, or a language check on the segment says English with at least 85% confidence. In all 25 English segments of the three single-voice clips the check said English. The check itself costs a fixed 0.4 s with Whisper tiny (about 0.86 s with Whisper base) whatever the audio length, so on Auto-detect English captions arrive about 0.8 s after speech ends, against about 2.1 s for Whisper alone.

Limits, stated plainly:

- The speech is read-aloud audiobook text. NVIDIA's training data for Parakeet includes LibriSpeech-style speech, so these clips favour Parakeet more than real VRChat chatter would. The speed advantage is real; the size of the accuracy advantage on real lobby speech is not known until a real lobby recording is measured.
- Language check on other languages (measured 2026-10-15): three real read-speech clips each (public FLEURS set, CC-BY-4.0, first few megabytes of each language's development archive) in Mandarin, German, Spanish (Latin American), French, Japanese, Korean, Portuguese (Brazil) and Russian, 24 clips and 61 segments in all, played through the hybrid with the spoken language on Auto-detect and the Whisper tiny language check. **None of the 61 segments went to the English-only engine; Whisper captioned all of them** with sensible English translations. This is a small sample: it does not show what happens with accented English, English with foreign words, or very short utterances, and the 85% confidence threshold was not tuned on it. Non-English captions are about 0.4 s slower in the hybrid than with Whisper alone, because the language check runs first.
- Parakeet has no confidence score, so the confidence-based part of the caption filter cannot apply to it; the text-based filters still do.
- The hybrid rows were run on the single-voice clips only.

## Skipped speech made visible, 2026-10-14

When the recogniser falls behind, speech is now skipped visibly instead of silently, and the last waiting segment is recognised late instead of being dropped after 6 seconds. Checked by four automated checks (queue policy, full queue drops the oldest and reports it, old speech is skipped only when newer speech waits, notice text and rate limit), each shown to fail when its rule is deliberately broken, and by the window self-test (the overlay note appears). The Balanced preset keeps only two waiting segments; in the real lobby session of 2026-10-12 the queue stood at 1 or 2 for most samples, so speech was very likely being dropped without any sign. The amount that was dropped then cannot be known: the old build did not record it.

## Release 1.1.2, 2026-10-12

1.1.2 contains the fixes for the problems found in the real-world session below. It has no library updates. What was run on the build PC:

| Check | Result |
| --- | --- |
| Debug and Release build, warnings as errors | 0 warnings, 0 errors |
| Automated checks | 53/53 (51 in 1.1.1, plus 2 new) |
| WPF `--smoke-test` and `--phone-self-test` | pass, both from source and from the extracted ZIP |
| One-copy check with the extracted executable | the first copy started; two further launches each exited with code 0 in well under a second; exactly one copy stayed running and closed when asked |
| Known-vulnerable packages | none |
| Package contents | 656 files, as in 1.1.1. Compared file by file with the 1.1.1 package, only 16 differ: six documents and the app's own assemblies (which carry the version number). No third-party file changed |
| Scan of every file in the package for a user name, machine path, repository path or source-link data | none in the app's own files. The only matches elsewhere are Microsoft's own build paths inside its libraries and the documented speaker-model download address, both present in 1.1.1 too |

The 2026-10-12 real-world session below ran 1.1.1. The 1.1.2 fixes were checked by the automated checks, the smoke test and the one-copy check above, but not yet in a live VRChat session. The audio, speaker and speech code is unchanged since 1.1.1, so the real capture, speaker and Whisper checks recorded for 1.1.1 were not repeated.

## Real-world session, 2026-10-12

First run of the released 1.1.1 ZIP on a real Windows 11 PC with VRChat (CPU only, SteamVR not running, so no headset captions). The tester downloaded the ZIP from the Releases page in a web browser, as any user would. Results the author checked in the app's own diagnostic log or on disk are marked "log"; results the tester reported are marked "reported".

| Check | Result |
| --- | --- |
| Download | Size 97,688,878 bytes; SHA-256 equal to the line in the published `SHA256SUMS.txt` (checked on disk) |
| First launch | Windows showed its unfamiliar-program prompt and Microsoft Defender ran a cloud check, sending the executable for analysis. Defender recorded no detection (checked in its history). Reported by the tester as the first-launch experience |
| Speech model download | The in-app download of the Balanced model (about 148 MB) and, later, the Accurate model (about 488 MB) completed; both files match the pinned size and SHA-256 (checked on disk) |
| Live captions from VRChat's audio | Worked in a lobby with many people; the speech engine loaded the model and transcribed continuously (log) |
| Delay from speech to caption, as the app's own reading (log) | Balanced: about 2.1 to 2.3 s typical, with one 8.2 s reading in the first minute while the model warmed up. Accurate: median 6.1 s, range 4.8 to 9.9 s, too slow for conversation on this PC |
| Load while captioning (log) | Balanced: translator CPU about 24 to 27% (0.3% idle), memory 340 to 570 MB. Accurate: CPU average 25%, memory up to 976 MB |
| Settings saved and reloaded | A mode saved with **Apply & save settings** was loaded again after a restart (log: the next start reported the saved mode) |
| Global shortcuts with VRChat in front | Ctrl+Alt+L, Ctrl+Alt+P, Ctrl+Alt+H and Ctrl+Alt+O worked (reported). The log showed four start/stop changes with clean restarts of capture and speech, and no errors |
| Errors | No error, fault or crash line in any session that evening (log) |

Problems found, fixed in 1.1.2 (see [RELEASE_NOTES.md](RELEASE_NOTES.md)): with no speech model installed, the app said it was listening while no captions could appear and did not say why; and starting the app several times ran several copies at once (three copies ran, two of them transcribing the same audio, and system CPU use reached 91%).

The fixes were first checked on the source tree by the build (0 warnings, 0 errors, Release and Debug), the 53 automated checks, the WPF smoke test (whose desktop checks now cover the first-run header and a real hotkey press written to the log) and the phone self-test, all passing. Each new check was also broken on purpose and confirmed to fail. A real run of the new build started three times left exactly one copy running, and the second and third launches exited within a fraction of a second.

Not covered by this session: caption accuracy and false captions were not scored, language labels and other languages were not tested, and the tray menu, the shortcut-conflict message and close-to-tray were only reported as working. SteamVR headset captions, typed replies to the VRChat chatbox, the phone display on a physical phone, output language packs and speaker labels were not run.

## Release 1.1.1, 2026-10-11

1.1.1 changes two libraries and nothing else in the app: NAudio.Wasapi 3.0.1 to 3.1.0 (audio capture) and sherpa-onnx 1.13.5 to 1.13.8 (optional speaker labels). NAudio 3.1.0's release notes list two breaking API changes (`WaveFormat` marshalling and the `IsFormatSupported` return type); the app uses neither. Each update was tested alone and then together, against the same tests, on the same PC:

| Check | 1.1.0 libraries | 1.1.1 libraries |
| --- | --- | --- |
| Debug and Release build, warnings as errors | 0 warnings, 0 errors | 0 warnings, 0 errors |
| Automated checks | 51/51 | 51/51 |
| WPF `--smoke-test`, `--phone-self-test` | pass | pass |
| Real WASAPI process capture (`--capture-proof`, two short test tones) | selected process 14.999%, unrelated process 0.000% | 14.999%, 0.000% (identical) |
| Real speaker embeddings (`--speaker-test`, three public recordings) | Speaker 1, Speaker 1, Unknown speaker | the same three labels |
| Real Whisper pipeline (`--pipeline-test`, base model, jfk.wav) | about 1.65 to 2.0 s per caption | 1.68 to 2.4 s across runs (run-to-run noise on a busy PC; Whisper uses neither updated library) |
| Package contents | 656 files | 656 files; only two native DLLs differ in size (+0.4 MB in total); licenses unchanged |
| Known-vulnerable packages | none | none |

Still not verified, as for 1.1.0: anything against real VRChat, SteamVR, a headset, or a physical phone.

## Phone display hardening, 2026-10-09

The optional phone display used to need no PIN (the PIN code existed but the app never supplied one), listened to any caller under any Host name, and its
`/health` answer claimed `localOnly` while the port was open on every adapter. It now requires a six-digit PIN that is new each time it is turned on and is carried in the
link, refuses clients outside this PC and private networks, refuses a Host name this PC would not answer to (DNS rebinding), blocks a device for five minutes after five wrong
PINs, allows at most eight open displays, and hides file paths in the status line.

| Check | Result |
| --- | --- |
| Debug solution build with `--no-restore` | Succeeded, 0 warnings, 0 errors |
| Deterministic core suite | **45/45 passed** (5 new phone-access checks: local-network clients, Host names, PINs, wrong-PIN limiter, path redaction) |
| `--phone-self-test` | Passed. It now also checks: no PIN and a wrong PIN get 401, a foreign Host gets 403, five wrong PINs then the right one gets 429, a ninth display gets 503, links carry the PIN, `/health` no longer claims `localOnly` |
| WPF `--smoke-test` | Exit code 0 |
| Mutation check | 13 deliberate breaks of the new code (PIN never checked, foreign Host accepted, failures not counted, no cap, PIN dropped from links, 172.32.x treated as private, IPv4-mapped handling removed, any Host accepted, PIN length ignored, limiter never blocks, success not clearing failures, paths not redacted, health claim restored): every one made a check fail. Three of them first failed only at compile time, so they were rewritten to compile and rerun; those also failed a check |

The self-test now listens on this PC only (`loopbackOnly`), so running it no longer raises a Windows Firewall prompt. That means the all-adapters bind (`ListenAnyIP`) is **not** exercised by it. The
2026-10-03 portable ZIP listed in the 2026-10-06 section below was built before this change; the public ZIP was rebuilt on 2026-10-11 and does contain it (see the next section). Still not verified: a physical phone through the router and firewall.

## Security and release review, 2026-10-11

A read-through of every place the app uses the network, starts a program, unpacks an archive, serves a page, or writes a file, followed by the checks below. Fixes from it are listed in [RELEASE_NOTES.md](RELEASE_NOTES.md).

| Check | Result |
| --- | --- |
| Known-vulnerable NuGet packages (`dotnet list package --vulnerable --include-transitive`) | None in any project, and no deprecated packages. Newer minor versions of NAudio.Wasapi and sherpa-onnx exist and were not adopted; there is no security reason to, and each would need its own test pass |
| Compiler analyzers, all rules at the latest level, over the app | 614 warnings, almost all style (ConfigureAwait, naming, culture). The one security rule, CA5392 (native-library search path), applied to 8 imports of Windows system DLLs; all now use a System32-only search path |
| Pinned model sizes and hashes | The three Whisper models' sizes and SHA-256 values in the app equal the values published by Hugging Face and the files already downloaded and used in earlier testing. GitHub publishes no digest for the speaker model, so its size and SHA-256 were recorded from one download of the official release |
| Language-tools setup, clean run | The real `setup-translation.ps1` run into an empty folder downloaded the pinned Python, installed all 14 hash-locked packages, and passed its import check (38 s). A copy of the requirements with one wrong hash was refused by pip with a hash-mismatch error and nothing was kept |
| Language-pack index | 100 packs listed; all hosted directly on `argos-net.com` with no redirects; the largest is 272 MB. The index publishes no checksums |
| ZIP path traversal | A test builds a ZIP whose entry climbs out of the target folder; extraction is refused and nothing is written outside it |
| Phone page in a real browser | The real server class was served from a throwaway harness and opened in a Chromium-based browser. The page connected and rendered; a hostile caption (script and image tags) showed as plain text; an injected inline script and a script from another site were both blocked by the Content-Security-Policy; no console errors came from the page's own code |
| Automated checks | **51/51 passed**, 6 of them new. Five deliberate breaks of the new logic (skipped hash comparison, missing `nosniff`, `unsafe-inline` allowed, reversed log retention, wrong data folder) each made a check fail |
| Solution build | Release and Debug, 0 warnings and 0 errors (warnings are errors) |
| WPF `--smoke-test`, `--phone-self-test` (now also checks the security headers on a live server) | Exit code 0 / passed, from source and from the extracted portable ZIP |

Not done, and not claimed: an independent penetration test; code signing; TLS for the phone display (it is plain HTTP by design, see [SECURITY.md](SECURITY.md)); testing the phone page on an iPhone or in other browsers; the all-adapters bind through a real router and firewall; anything against real VRChat, SteamVR, a headset, or a physical phone.

## Public package, 2026-10-11

Built with `scripts\build-release.ps1` from the committed source. The SHA-256 of the ZIP is published in `SHA256SUMS.txt` beside it on the release page, not here, because this file is inside the ZIP. The ZIP was extracted into a clean folder and the packaged executable passed `--smoke-test` and `--phone-self-test`.

## Revalidated on 2026-10-06

The following offline checks were rerun while deliberately avoiding VRChat, SteamVR, audio playback/capture, physical-phone traffic, and LM Studio:

| Check | Result |
| --- | --- |
| Debug solution build with `--no-restore` | Succeeded, 0 warnings, 0 errors |
| Deterministic core suite | **24/24 passed** |
| WPF `--smoke-test` | Exit code 0 |
| `--phone-self-test` | Passed |
| Existing 1.1.0 portable ZIP | 97,433,844 bytes; SHA-256 `36DC42CD39F76F18EEFC8C34BA8BF4E81295B763B842999699E09456716F2F82` |

The real-world items in "Not verified" remain open and must be recorded in `ACCEPTANCE_CHECKLIST.md`; this revalidation does not substitute for them.

## Run on 2026-10-03 (this PC, Windows 11, .NET SDK 10.0.401)

| Check | Command | Result |
| --- | --- | --- |
| Full solution build (Debug) | `dotnet build VRChatUniversalTranslator.slnx -c Debug` | Succeeded, 0 warnings, 0 errors |
| Deterministic core tests | `dotnet run --project tests\VRChatTranslator.Core.Tests\VRChatTranslator.Core.Tests.csproj` | **24/24 passed** |
| WPF startup smoke test | `dotnet run --project src\VRChatTranslator.App\VRChatTranslator.App.csproj -- --smoke-test` | Exit code 0 |
| Phone server self-test | `... VRChatTranslator.App.csproj -- --phone-self-test` | Passed (startup, LAN listening, SSE, reconnect/history replay, escaping, port conflict, shutdown) |

| Portable package | `scripts\build-release.ps1` | Built `VRChatUniversalTranslator-1.1.0-win-x64-portable.zip` (about 97 MB, self-contained) |
| Packaged app, extracted to a clean folder | `VRChatUniversalTranslator.exe --smoke-test` and `--phone-self-test` | Both exit 0 / pass. Zip contains README, QUICK_START, PRIVACY, RELEASE_NOTES, THIRD_PARTY_NOTICES, VERIFICATION, `licenses\`, and `openvr_api.dll` |

The 24 core tests cover process selection and restart/exit handling, event deduplication, audio metering, diagnostic WAV output, language catalog and noisy-label resistance, speech segmentation, bounded history, performance-profile bounds, atomic settings round trip and malformed-settings recovery, word-correction boundaries/scope, non-speech placeholder suppression, history clear, OSC packet padding and unsafe-text rejection, speaker-cluster uncertainty, and language-pack status.

## Carried forward from 2026-08-27 (not re-run for 1.1.0)

`LANGUAGE_PACKS.md` records real English↔Spanish and French language-pack download, cancel/retry, translation and preload checks (cold 584 ms, preload 461 ms, post-preload 73 ms for a short fixture — single-run text-translation timings, not an end-to-caption latency figure). Those checks need internet access and several hundred MB of model downloads, and the test-data folder from that run is no longer on this PC, so they were not repeated for this release.

## Changes since that run

- Version 1.1.0; release scripts now fail loudly if restore/publish fails and bundle the documentation.
- The "GPU preferred (CPU fallback)" backend choice was removed from the custom-performance list. Nothing used a GPU, so the label was misleading; the app is CPU-only and says so.
- SteamVR caption service: rendering calls are guarded so a compositor error cannot crash the desktop app, and shutdown tolerates a compositor that has already exited. The unused "Reconnect on PC" dashboard button was replaced with "Wider captions".
- When Whisper's own detected output language is English, the redundant second recognition pass for the original text is skipped.
- Third-party license texts are bundled with the app.

These changes were covered by the build, the 24 tests and the smoke test above; none has its own new regression test, and the SteamVR changes could not be exercised without a headset.

## Not verified

The items below are covered step by step in [ACCEPTANCE_CHECKLIST.md](ACCEPTANCE_CHECKLIST.md), which is meant to be filled in during one real VRChat/headset session.

- **Native SteamVR captions and dashboard controls** — never run with a physical headset. Experimental.
- **Speaker labels** — off by default; the speaker pipeline checks (`--speaker-test`) and the real-audio pipeline check (`--pipeline-test`) were not re-run because the model and fixtures are not on this PC.
- **Process-only capture proof** (`--capture-proof`) — plays audible tones through Windows; run for 1.1.1 and again for 1.1.4 (same result both times), not re-run for 1.1.2 or 1.1.3, whose capture code is unchanged.
- **False-caption behavior and caption accuracy** in real VRChat — latency was measured on 2026-10-12 (see above) but accuracy and false captions were not scored.
- **Tray menu behavior while VRChat has focus** — the four global shortcuts were exercised on 2026-10-12; the tray menu, the shortcut-conflict message and close-to-tray were only reported as working.
- **Explicit OSC reply sending** to a live VRChat session.
- Phone display on a physical phone through the router/firewall.

## Reproducing

```powershell
dotnet build VRChatUniversalTranslator.slnx -c Debug --configfile NuGet.Config
dotnet run --project tests\VRChatTranslator.Core.Tests\VRChatTranslator.Core.Tests.csproj
dotnet run --project src\VRChatTranslator.App\VRChatTranslator.App.csproj -- --smoke-test
dotnet run --project src\VRChatTranslator.App\VRChatTranslator.App.csproj -- --phone-self-test
```
