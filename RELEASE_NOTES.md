# Unreleased — fewer invented captions, preset saved on Apply

- **Captions the recogniser invents from non-speech sound are dropped.** Whisper often "hears" the stock endings of online videos in noise ("Thanks for watching! Please subscribe and hit that like button!", "Welcome to my channel") or repeats one sentence in a loop. A caption made only of those stock phrases, or one sentence repeated three or more times, is no longer shown. A real sentence that merely contains such words is untouched. In a test with steady noise this removed all five invented captions; real speech results were identical across ten test crowds.
- **Apply now saves the chosen performance mode.** Before, "Apply" and "Apply & save settings" saved everything except the selected mode (Balanced, Accurate...), which was only saved when the app closed, so a crash or a killed process lost it.
- Development only, not in the app package: tools that measure caption accuracy and delay against fake crowds (see [VERIFICATION.md](VERIFICATION.md)).

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
