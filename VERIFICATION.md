# Verification — versions 1.1.0 and 1.1.1

This records what was actually run and what was not. It is not a claim of bug-free or fully hardware-accepted software; see "Not verified" below.

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

Problems found, now fixed in the unreleased changes (see [RELEASE_NOTES.md](RELEASE_NOTES.md)): with no speech model installed, the app said it was listening while no captions could appear and did not say why; and starting the app several times ran several copies at once (three copies ran, two of them transcribing the same audio, and system CPU use reached 91%).

The fixes were checked by the build (0 warnings, 0 errors, Release and Debug), the 53 automated checks, the WPF smoke test (whose desktop checks now cover the first-run header and a real hotkey press written to the log) and the phone self-test, all passing. Each new check was also broken on purpose and confirmed to fail. A real run of the new build started three times left exactly one copy running, and the second and third launches exited within a fraction of a second.

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
- **Process-only capture proof** (`--capture-proof`) — plays audible tones through Windows; not run.
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
