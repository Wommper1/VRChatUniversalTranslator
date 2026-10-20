# 1.1.4 — library update

- Updated NAudio.Wasapi (audio capture) from 3.1.0 to 3.2.0. NAudio 3.2.0 is mostly new features for other platforms and fixes to its playback classes (`WasapiPlayer`, `WasapiOut`, `DirectSoundOut`), which this app does not use. The app uses only `WasapiRecorderBuilder` (per-process capture of `VRChat.exe`), `WaveFormat`, and, in its own test tools, `WaveFileReader`. None of the APIs NAudio deprecated in 3.2.0 are used (the build treats warnings as errors and has none). The real audio-capture test gave the same result as before; see [VERIFICATION.md](VERIFICATION.md).
- Nothing else in the app changed: no new features, no behavior changes. Settings, downloaded models and language packs carry over unchanged.
- The repository also now ignores secret-looking files (`.env`, key and credential files) and requires its build check to pass before changes reach the main branch. Neither affects the app or the package.
- The same real-VRChat, SteamVR and phone checks are still outstanding (see [ACCEPTANCE_CHECKLIST.md](ACCEPTANCE_CHECKLIST.md)), so this is still a pre-release.

# 1.1.3 — faster captions: optional fast English engine, faster Whisper presets, skipped speech is shown

This release is about the delay between someone speaking and the caption appearing, and about not losing speech silently. Settings, downloaded models and language packs carry over unchanged. It is a **pre-release**: the new fast English engine has been measured on test recordings (see [VERIFICATION.md](VERIFICATION.md)) but has **not yet been run in a live VRChat lobby**, and everything that was still outstanding in 1.1.2 (SteamVR headset captions, typed replies to the VRChat chatbox, the phone display on a physical phone, language packs, speaker labels, caption accuracy on real lobby speech) still is.

- **Fast English captions (optional, experimental, off by default).** In Settings you can download NVIDIA's Parakeet 110M English model (104 MB, plus the small Whisper tiny model for the language check if you do not have it, 182 MB at most). English speech is then captioned about 0.4 s (spoken language locked to English) or about 0.8 s (Auto-detect) after the speaker stops, instead of about 2 s, and made fewer mistakes in our tests. Every other language, and all translation, still use Whisper, and a quick language check runs first so another language is never captioned as English: in a test of 61 segments in eight languages (Mandarin, German, Spanish, French, Japanese, Korean, Portuguese, Russian) none went to the English engine. The cost is that other-language captions are about 0.4 s slower while it is switched on. Switch it off at any time. The download is verified by exact size and SHA-256, only two files are unpacked from it, and the model is credited under its CC-BY-4.0 licence in THIRD_PARTY_NOTICES. Limits: the test speech was read audiobook text, which probably flatters Parakeet; accented English, English mixed with foreign words and very short utterances were not tested.
- **Faster Whisper in Balanced and Responsive CPU.** Whisper's audio context is trimmed from 30 s to 15 s for these two presets (both use the base model). Recognition was about 30 to 35% faster in our tests with no loss of accuracy over five single-voice clips and no false captions on noise. Accurate, Ultra Light and Custom are unchanged because they were not measured.
- **Skipped speech is no longer silent.** When the recogniser falls behind (a busy lobby, a slow or busy PC), the app keeps the newest speech and skips the oldest. Before, that happened with no sign anywhere, not even in the log. Now each skip shows a line in the status bar, adds to a "Skipped speech" total on the Live tab, puts a short amber note on the caption overlay (at most once every 8 seconds), and writes a log line with lengths only, never audio or text.
- **Late is better than lost.** Waiting speech used to be thrown away after 6 seconds even when nothing newer was behind it. Now the last waiting segment is always recognised, late if need be. A segment is skipped only when it is older than 6 seconds *and* newer speech is waiting, or when it is older than 20 seconds.
- **Captions the recogniser invents from non-speech sound are dropped.** Whisper often "hears" the stock endings of online videos in noise ("Thanks for watching! Please subscribe and hit that like button!", "Welcome to my channel") or repeats one sentence in a loop. A caption made only of those stock phrases, or one sentence repeated three or more times, is no longer shown. A real sentence that merely contains such words is untouched. In a test with steady noise this removed all five invented captions; real speech results were identical across ten test crowds.
- **Apply now saves the chosen performance mode.** Before, "Apply" and "Apply & save settings" saved everything except the selected mode (Balanced, Accurate...), which was only saved when the app closed, so a crash or a killed process lost it.
- New dependency: SharpZipLib 1.4.2 (MIT), used only to open the one downloaded Parakeet archive. New third-party credits are in THIRD_PARTY_NOTICES and the packaged `licenses\` folder; the new optional download is listed in PRIVACY.
- 72 automated checks (53 in 1.1.2), extra WPF smoke-test checks, and a new acceptance-checklist section for the new features.
- Development only, not in the app package: tools that measure caption accuracy and delay against fake crowds and multilingual recordings (see [VERIFICATION.md](VERIFICATION.md)).

# 1.1.2 — first-run help, one copy at a time, clearer logs

Found in the first real-world test of 1.1.1 on a Windows PC with VRChat (see [VERIFICATION.md](VERIFICATION.md)). Settings, downloaded models and language packs carry over unchanged. Everything that was not run in that test (SteamVR headset captions, typed replies to the VRChat chatbox, the phone display on a physical phone, language packs, speaker labels, caption accuracy) is still outstanding, so this is still a pre-release.

- **First run no longer looks broken.** A fresh install has no speech model, so listening used to say "Listening to VRChat only" while nothing could be captioned. Now, with no model installed, the header on every tab says so and says where to click, the first listening session opens Settings at **DOWNLOAD SELECTED MODEL**, and captions start by themselves when the download finishes.
- **One copy at a time.** Starting the app a second time now shows the copy that is already running instead of starting another. In the test, three copies ran at once and two of them transcribed the same VRChat audio, which pushed the PC to 91% CPU.
- **Clearer diagnostic log**, still with no caption text or audio: which shortcut or tray action ran, the mode and language codes applied (never vocabulary or corrections), and when the speech model is missing or has been downloaded.
- Two new automated checks (53 in total) and extra checks in the WPF smoke test. The README and QUICK_START now say plainly that the speech model must be downloaded first.

# 1.1.1 — library updates

- Updated NAudio.Wasapi (audio capture) from 3.0.1 to 3.1.0 and sherpa-onnx (optional speaker labels) from 1.13.5 to 1.13.8. These are maintenance updates. The parts of each library the app uses behaved identically in every test, including the real audio-capture test; see [VERIFICATION.md](VERIFICATION.md).
- Nothing else in the app changed. Settings, downloaded models and language packs carry over unchanged.
- The same real-VRChat, SteamVR and phone checks are still outstanding (see [ACCEPTANCE_CHECKLIST.md](ACCEPTANCE_CHECKLIST.md)), so this is still a pre-release.

# 1.1.0 — desktop conversation upgrade

- Six focused tabs: Live, History, Replies, Settings, Headset, Advanced. Readable dark dropdowns, dark secondary controls, larger body text and keyboard-focus outlines.
- Adjustable caption size/background/duration with previews, original text, grow-to-fit long captions, overlay positioning and lock controls.
- Bounded in-memory history with timestamps, language routes, follow-latest, copy, clear, and optional session speaker names.
- Auto-detect or source lock, independent output-language choice, local CTranslate2 language packs, typed reply preview/edit/copy, explicit rate-limited OSC send.
- Editable whole-word corrections with scopes and separate recognition hints.
- Global shortcuts, conflict reporting, system tray controls and explicit Quit.
- Native SteamVR captions and dashboard controls; missing-headset recovery. Experimental until physical headset acceptance.
- Optional local speaker embeddings and conservative session clustering, disabled by default. Mixed/short/uncertain voices are not forced into known identities.
- Queue/end-of-speech timings, stale-work dropping, conservative non-speech-marker filtering, enabled recognition confidence values, cancellation across stop/clear, and redundant English-original pass avoidance.
- Safe setting normalization and unique atomic save files. Shutdown and model-reload cleanup fixes.
- New regression tests and reproducible public-fixture translation/speech/speaker checks. See VERIFICATION.md for evidence.

CPU only: the old GPU Fast preset is now honestly named Responsive CPU. The persisted enum stays compatible with old settings. Incremental drafts and accelerated GPU inference are not enabled; final captions remain the only history entries.

Not a certified bug-free or fully hardware-accepted release. The earlier v1.0 real-VRChat test is not evidence that these new integrations have passed in-headset testing.

## Also in this build (added after the first 1.1.0 package, included in the public ZIP)

- **Phone display hardening.** The optional phone display now requires a six-digit PIN that is new each time it is turned on and is carried in the link; it refuses clients outside this PC or a private network, refuses a Host name this PC would not answer to (DNS rebinding), blocks a device for five minutes after five wrong PINs, allows at most eight open displays, and keeps file paths out of the status line. It is still off by default and read-only.
- **Verified downloads.** Speech models are checked against their exact published size and SHA-256 before they are used, and a partial file no longer counts as "installed". The optional speaker model is checked the same way. The built-in size for the Ultra Light model was wrong (the progress bar could pass 100%); it is fixed.
- **Hash-locked language tools.** The optional Python tools install 14 packages pinned by SHA-256 with `pip --require-hashes --no-deps`. Language-pack downloads are limited to `argos-net.com`, follow no redirects, and have size and file-count limits.
- **Phone page protections.** A hash-based Content-Security-Policy (only the page's own script and style may run), `nosniff`, `no-referrer` (the address carries the PIN), no-store and frame-denial headers, no server banner, and request limits. Checked in a real browser: an injected inline script and a script from another site are both blocked.
- **Safer process and library loading.** Windows PowerShell and `nvidia-smi` are started by full path from trusted locations, and native Windows calls use a System32-only search path.
- **Logs stay small.** A day's diagnostic log stops at 5 MB and logs older than 14 days are deleted. They never contained caption text.
- **One data folder.** Settings, models, language tools and logs now live in `%LOCALAPPDATA%\TheLevelEverythingCompany\VRChatUniversalTranslator`, set in one place. Earlier local test builds used a different folder; move it there to keep downloaded models, or let the app download them again.
- **Clean defaults and branding.** The default recognition hints are just "VRChat" (earlier test builds carried a personal list), the executable has the company, product and copyright details and a proper icon, and the tray uses that icon.
- Captions mark the source language as (auto) when detected or (locked) when you chose it.
- The Replies tab shows an OSC status line: whether VRChat is running, whether anything is listening on UDP port 9000, whether sending is enabled, and the last send. It cannot confirm that VRChat received a reply.
- If SteamVR closes or restarts, a headset connection you made is re-established automatically (5, 10, 20, then every 30 seconds) until you choose Disconnect headset.
- New off-by-default option to connect the headset when the app starts, waiting for SteamVR if it is not running yet.

Still unverified on real VRChat and SteamVR; see ACCEPTANCE_CHECKLIST.md. The version number is still 1.1.0: the first 1.1.0 ZIP (2026-10-03) was a local build without these changes, and the public ZIP was rebuilt from the current source on 2026-10-11.
