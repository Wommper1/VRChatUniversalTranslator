# Desktop app upgrade TODO

Updated: 2026-10-06

> **Status note (2026-10-06 audit, version 1.1.0):** every checkbox below has now
> been audited against the source code and the offline checks (Debug build,
> 24/24 deterministic tests, and the WPF `--smoke-test`, whose `RunDesktopChecks`
> asserts the tab set, bounded history, bilingual display, clear, long-caption
> growth and shortcut-conflict reporting).
>
> - `[x]` means the feature is implemented, wired to real behavior, and confirmed
>   by code review plus an automated test or the smoke test wherever one exists.
>   The evidence is in the italic line under the item.
> - `[ ]` means some part is missing, or it needs a physical headset, live
>   VRChat, a real phone, real audio, a human pass over the running app, or a
>   measurement. The italic **Status** line says which part is open.
>
> Nothing here was marked complete from memory, and ticked items are not a claim
> of bug-free software. What was and was not run is in
> [VERIFICATION.md](VERIFICATION.md); real-world checks are in
> [ACCEPTANCE_CHECKLIST.md](ACCEPTANCE_CHECKLIST.md). Section 3's GPU item is not
> done: the app is CPU-only and no longer offers a GPU option.

This combines the desktop features planned for this release. Existing internal
groundwork does not count as a finished, usable feature.

Scope: the Windows desktop app and PC-connected headset experience. No new mobile
or phone-add-on features are included; preserve existing functionality.

## Priority order

1. Finish the cleaner interface and comfortable captions.
2. Improve caption latency and false-caption handling.
3. Add in-game shortcuts, background operation, and native headset controls.
4. Complete the remaining agreed conversation and translation features below.
5. Run the completeness, bug-check, and regression loop until the release criteria
   are met. Document any external blocker instead of claiming completion.

## 1. Cleaner main screen

- [x] Make connection status, Start/Stop, language selection, and live captions the
  main focus.
  *Evidence: the header card (listening summary, level meter, Start/Stop) and the
  Live tab (spoken/output language pickers, live caption) in `MainWindow.xaml`;
  the smoke test asserts the six-tab layout.*
- [x] Move CPU/GPU statistics, diagnostic recordings, and developer tools into
  Advanced or Diagnostics.
  *Evidence: the Advanced tab holds CPU/GPU/VRAM/timing statistics, custom tuning,
  the Developer tools expander (audio test, test caption) and the phone add-on.*
- [x] Give conversation history and settings clear, easy-to-find places.
  *Evidence: dedicated History, Replies, Settings and Headset tabs.*
- [ ] Keep dropdowns, selected items, buttons, and focus states readable in the
  dark theme, including at different Windows display scales.
  *Status: dark dropdown/button styles and a keyboard-focus outline on buttons are
  implemented (`App.xaml`). Contrast was calculated on 2026-10-20: every text colour on its background is at least 4.5:1, and input borders were raised from 2.2:1 to about 4.7:1 (the guideline for borders is 3:1). No one has looked at the window at other Windows display scales.*
- [ ] Verify navigation, keyboard access, resizing, and small-screen layouts in
  the running app.
  *Status: partly done. Every control now has a screen-reader name, and the startup test (`--smoke-test`, `AccessibilityChecks.cs`) fails if a new one is added without a name. Tab order, keyboard use, resizing and a real screen reader still need a human pass on the running app (the window has a 820×600 minimum size).*

## 2. Comfortable captions

- [x] Add controls for font size, background opacity, caption duration, and
  placement, with an immediate preview.
  *Evidence: size/opacity/duration sliders apply live through
  `OnDesktopPreferenceChanged` → `OverlayWindow.ApplyPreferences`, and Preview
  shows a sample caption. Placement is by dragging/resizing plus Reset, not
  numeric fields.*
- [x] Make moving, resizing, locking, hiding, and resetting the overlay simple.
  *Evidence: Ctrl+Alt+O or the Unlock/lock button toggles click-through; dragging
  and a resize grip work while unlocked; Show/hide and Reset placement buttons.*
- [x] Keep text readable when both original and translated text are shown.
  *Evidence: `FitCaption` grows the overlay upward; the smoke test asserts the
  overlay exceeds its default height for a long bilingual caption at 64 pt.*
- [x] Save preferences safely and recover gracefully if a saved monitor position
  is no longer available.
  *Evidence: unique temp file + atomic move (`SettingsRoundTripAtomically` test);
  non-finite positions are reset (`SettingsNormalization` test);
  `EnsureVisiblePlacement` clamps the overlay to the virtual screen at startup.
  The clamp arithmetic now lives in `OverlayPlacement.ClampAxis` and is tested
  (`overlay placement pulls a disconnected-monitor position back on screen`:
  unplugged right monitor, negative-origin virtual screen, NaN/infinity, window
  wider than the screen). The call against a real monitor change has not been
  exercised.*
- [ ] Verify long captions, multilingual fonts, contrast, and line wrapping.
  *Status: long-caption growth is smoke-tested and text contrast was calculated (see section 1). Multilingual fonts and wrapping have not been inspected in the running app.*

## 3. More responsive caption processing

- [ ] Measure end-of-speech-to-caption delay and queue delay separately; record a
  repeatable baseline before changing the pipeline.
  *Status: both are measured separately in code (`QueueDelay` and
  `EndToCaptionDelay`, shown under Advanced). No baseline has been recorded.*
- [ ] Investigate incremental/draft captions and clearly distinguish drafts from
  final text if implemented.
  *Status: not implemented. 1.1.0 release notes record drafts as not enabled;
  only final captions exist.*
- [ ] Tune utterance boundaries, queue limits, stale-work dropping, and reusable
  model loading without losing normal short speech.
  *Status: the mechanisms exist (350 ms end-silence, 300 ms minimum speech,
  bounded queue of 1–5, segments older than 6 s dropped, cancellation on
  stop/clear, models kept loaded while running) but none was tuned against real
  measurements.*
- [ ] Investigate real GPU acceleration with capability checks, safe resource
  limits, and a truthful CPU fallback. A mode name alone is not GPU support.
  *Status: not done. The app is CPU-only and says so (Responsive CPU).*
- [ ] Benchmark the chosen improvements while VRChat is active; record actual
  latency and resource use rather than promise an unmeasured speed.
  *Status: not done. Needs a live VRChat session.*

## 4. False-caption handling

- [ ] Reduce captions invented from silence, music, non-speech sounds, and noisy
  audio without suppressing legitimate quiet speakers.
  *Status: implemented (VAD threshold with a two-chunk onset, plus
  `CaptionQualityGate` for non-speech markers, confidence below 12% and heavy
  repetition). Marker handling is unit-tested; behavior on real silence, music,
  noise and quiet speakers is unmeasured.*
- [x] Address language-label changes and placeholder output such as "speaking
  foreign language" reported during real-world use.
  *Evidence: `LanguageStabilizer` (`LanguageLabelsResistOneNoisyChunk` test) and
  the non-speech list, where "speaking foreign language" is suppressed while real
  sentences containing it survive (`CaptionFilterIsConservative` test).*
- [ ] Handle repeated text, overlapping speech, interrupted utterances, and low
  confidence gracefully; prefer uncertainty over confidently wrong output.
  *Status: duplicate/repetition filtering, low-confidence suppression and
  "Unknown speaker" for ambiguous voices exist. Overlapping and interrupted
  speech have not been tested.*
- [ ] Add regression cases for silence, short speech, multiple languages, noise,
  and rapid language changes.
  *Status: partial. Covered: speech-then-silence segmentation, invalid audio,
  noisy language labels, non-speech markers. Missing: end-to-end audio fixtures
  for silence, music, noise, short speech and multiple languages (the
  `--pipeline-test` fixtures are not on this PC).*

## 5. In-game shortcuts and background operation

- [x] Add configurable global shortcuts for Start/Stop, pause/resume, and caption
  visibility, while preserving the existing overlay lock shortcut.
  *Evidence: `DesktopShell` registers three configurable hotkeys (defaults
  Ctrl+Alt+L / P / H) and Ctrl+Alt+O still locks the overlay.*
- [x] Detect shortcut conflicts and provide understandable recovery instructions.
  *Evidence: `Configure` reports conflicts and invalid keys with a recovery
  message; the smoke test asserts a duplicate shortcut reports a conflict.*
- [x] Add a system-tray menu with listening status, open window, pause/resume,
  settings, and explicit Quit.
  *Evidence: the tray menu offers Open, Settings, Start/stop, Pause/resume,
  Show/hide captions and Quit; the tray text shows listening/paused/stopped.*
- [x] Make minimize/close behavior clear; never leave listening active invisibly
  or make the app difficult to exit.
  *Evidence: minimize-to-tray is on by default, close-to-tray is off by default,
  Quit always exits and runs cleanup (`OnClosing`), and the tray text shows the
  listening state.*
- [ ] Verify operation while VRChat has focus and complete cleanup on Quit.
  *Status: not done. Needs live VRChat; the quit/cleanup path is code-reviewed
  only.*

## 6. Native headset captions and controls

- [ ] Implement a native PC VR overlay, initially targeting SteamVR; do not
  describe the existing Windows overlay as native headset integration.
  *Status: implemented (`SteamVrCaptionService`, an OpenVR overlay application
  that does not hook VRChat) but never run against SteamVR. Experimental.*
- [ ] Provide readable headset caption placement and accessible in-VR controls
  for listening, visibility, and positioning.
  *Status: distance/height/width settings and SteamVR dashboard controls are
  implemented; unverified in a headset.*
- [ ] Evaluate wrist/dashboard controls without forcing users to return to the
  monitor for routine actions.
  *Status: dashboard controls implemented; wrist controls not attempted.*
- [ ] Preserve desktop operation when SteamVR is missing, closed, or restarted.
  *Status: a missing runtime, missing headset or init failure reports a status and
  leaves desktop captions running, and rendering calls are guarded. Since
  2026-10-07 a connection the user established is also re-established on its own
  after SteamVR quits or an OpenVR call fails (`HeadsetLink`): first retry after
  5 s, then 10 s, 20 s, 30 s and every 30 s after that, until the user chooses
  Disconnect headset or the app closes. The wait gives a closing SteamVR time to
  finish, a link that dies within 30 s of connecting does not reset the backoff,
  a first Connect that fails never starts retrying, and a reconnect redraws the
  caption that was on screen and restores the dashboard and placement. The
  status text on the Headset tab says what it is waiting for and how to stop it.
  The decision logic is covered by six tests driven by a fake clock and a fake
  connection (reconnect after a restart, backoff timing, explicit disconnect,
  failed first connect, flapping link, manual connect while waiting). The real
  OpenVR calls on the reconnect path (`IsHmdPresent`, `Init`, creating the
  overlays, the Quit event) have never run against SteamVR, so whether a restarted
  SteamVR accepts the new connection, and whether an `Init` attempt blocks the UI
  for noticeable time while SteamVR starts, are unverified. Connecting at app
  start is an opt-in checkbox on the Headset tab ("Connect to SteamVR
  automatically when the app starts", saved as `AutoConnectHeadset`, off by
  default and absent from older config files, which read as off). When it is on
  the app connects at once if SteamVR is running; otherwise it waits using the
  same 5/10/20/30 s schedule and connects when SteamVR appears, and Disconnect
  headset stops the waiting until the next start. Covered by four tests (already
  running, waits for SteamVR to start, cancellable, off by default and
  persisting). Never run against real SteamVR; in particular whether `Init`
  fails fast when SteamVR is not running, and so does not stall startup, is
  unverified.*
- [ ] Verify with a physical headset and document supported runtimes and any
  limitations. Phone features are outside this work.
  *Status: not done. Needs a physical headset.*

## 7. Conversation history

- [x] Add a bounded, scrollable history with timestamps and language labels.
  *Evidence: each entry's heading is `HH:mm:ss · language route`; the list is
  capped at 100 (smoke test) and the caption hub keeps bounded ordered history
  (`CaptionHubKeepsBoundedOrderedHistory` test).*
- [x] Support pause/resume auto-scroll so incoming captions do not interrupt
  reading older messages.
  *Evidence: the "Follow latest captions" checkbox gates scrolling.*
- [x] Add copy and clear actions with understandable behavior.
  *Evidence: Copy selected / Clear history. Clear also discards the replay
  history, pending captions and the headset caption (`HistoryClear` test and
  smoke test).*
- [x] Keep history in memory by default; do not automatically save transcripts or
  raw audio. Any future export must be explicitly user-triggered.
  *Evidence: `SaveTranscripts` is forced to false by settings normalization,
  history lives in memory only, and the diagnostic log holds state and metrics
  rather than caption text. The only audio file is the user-triggered "Save 10
  second audio test".*
- [ ] Keep draft updates from creating duplicate history entries.
  *Status: not applicable yet. Drafts are not implemented, so only final captions
  reach history.*

## 8. Better language control with auto-detect

- [x] Keep Auto-detect as the default source-language option.
  *Evidence: `AppSettings.SourceLanguage` defaults to `auto`; the smoke test
  asserts it.*
- [x] Allow an explicit source-language lock using languages supported by the
  selected recognizer.
  *Evidence: the spoken-language picker comes from `LanguageCatalog`; a locked
  language bypasses the stabilizer. Every preset uses a multilingual model.*
- [x] Show the difference between a user-selected language and an automatically
  detected language.
  *Evidence (2026-10-07): `CaptionEvent.LanguageLocked` is set by the speech
  service, and `CaptionPresentation.Route` appends "(locked)" or "(auto)" to the
  source language in the live caption, history, desktop overlay and SteamVR
  overlay. Placeholder captions carry no marker. Covered by the
  `caption route marks locked and auto-detected source languages` test and by
  smoke-test assertions on the live caption and history heading. The phone page
  is unchanged. Not yet seen in a running session with real speech.*
- [x] Allow returning to Auto without restarting the app; handle changes during
  listening safely.
  *Evidence: Apply reconfigures speech under a gate (stop, apply, reconcile) while
  listening. Code review only.*
- [ ] Verify multilingual conversations and that language stabilization does not
  prevent genuine speaker/language changes.
  *Status: the stabilizer's switch rules are unit-tested; real multilingual
  conversations have not been tried.*

## 9. Original text beside the translation

- [x] Add a clear translated-only / original-and-translation display option.
  *Evidence: the "Show original text beside translation" setting; the smoke test
  asserts both modes.*
- [x] Display the actual recognized source text, not a reconstruction from the
  English translation.
  *Evidence: the original comes from a separate transcription pass (or the same
  pass for English speech). When neither exists it is left empty rather than
  back-translated.*
- [ ] Apply the choice consistently to desktop captions, history, and the native
  headset overlay.
  *Status: desktop caption, overlay and history honor it. The headset overlay
  receives the setting but has never been exercised.*
- [ ] Label both languages, avoid duplicate presentation when they are the same,
  and measure the additional processing cost.
  *Status: the language route label and same-text de-duplication are done; the
  extra processing cost has not been measured.*

## 10. Choose the output language

Language-pack follow-up (2026-08-27; evidence in [LANGUAGE_PACKS.md](LANGUAGE_PACKS.md)):

- [x] Add a clearly discoverable offline output-language pack download option.
- [x] Show installed directions and missing/partial/runtime-not-ready states.
- [x] Show download progress, support cancel/retry, and keep existing translations available during another pack download.
- [x] Offer optional in-memory preloading to reduce first-caption text-model startup delay without claiming faster speech recognition.

- [x] Add target-language selection separate from source-language detection.
  *Evidence: "Translate into" is a separate picker from "Spoken language".*
- [x] Add a suitable translation backend for non-English targets; the existing
  Whisper-to-English path alone does not provide this feature.
  *Evidence: local CTranslate2 language packs; real English↔Spanish and French
  translation verified 2026-08-27 (LANGUAGE_PACKS.md).*
- [x] Keep the default local/private workflow and make required model downloads,
  supported language pairs, and unavailable options clear.
  *Evidence: the Offline language packs panel states that packs work offline,
  that internet is needed for downloads only, and shows directions and
  missing/partial states (`LanguagePackStatus` test).*
- [x] Handle same-language transcription without unnecessary translation.
  *Evidence: `TranslateIncomingCaptionAsync` returns the original text when the
  detected language is the target. Code review only.*
- [ ] Test multiple supported source/target pairs and preserve the original text
  if translation fails.
  *Status: English↔Spanish and English↔French were tested for real on 2026-08-27;
  the test-data folder from that run is gone, so other pairs remain untested.
  The failure path is now covered (2026-10-07): the fallback moved into
  `CaptionOutputTranslation.ApplyAsync`, and the
  `output translation failure keeps the caption and explains why` test checks
  that text, original, route and caption id are preserved, the "Output language
  unavailable" notice appears, and cancellation is not reported as a failure.*

## 11. Editable word corrections

- [x] Provide an editor for repeatedly misheard names, world names, and slang.
  *Evidence: the Settings → Editable word corrections list and fields.*
- [x] Separate recognition hints from deterministic text replacement so users
  understand what each rule does.
  *Evidence: "Recognition hints" is a separate field labelled "not replacements".*
- [x] Support adding, editing, disabling, and deleting rules with a preview.
  *Evidence: Add/update, an Enabled checkbox, Remove, and Preview corrections
  (applied to a sample text, original and translation).*
- [x] Avoid replacements inside unrelated words and verify Unicode handling.
  *Evidence: `WordCorrectionsAreSafe` test covers whole-word matching, case
  insensitivity, literal replacement text and `cafe` → `café`.*
- [x] Persist rules safely and define whether they affect original text,
  translated text, or both.
  *Evidence: rules are saved with settings (capped at 200, normalized) and each
  has an Original / Translated / Both scope. Rules take effect after "Apply &
  save settings".*

## 12. Translated replies

- [x] Add a typed reply composer with explicit source and target languages.
  *Evidence: Replies tab with "I write in" and "Reply in" pickers.*
- [x] Preview and allow editing the translation before sending.
  *Evidence: the preview box is editable and is what gets sent.*
- [ ] Add deliberate, user-triggered VRChat OSC chatbox sending with visible
  connection status, message-limit handling, and rate limiting.
  *Status: sending is user-triggered behind a consent checkbox, with the
  144-character / 9-line limit enforced (`OscPackets` test) and a 1.5 s rate
  limit. Since 2026-10-07 the Replies tab also shows a status line under the
  consent box (`OscStatusEvaluator`, refreshed every 3 s, on VRChat start/exit,
  on consent changes and after each send). OSC over UDP has no connection, so it
  reports what can be known locally: whether VRChat is running, whether a socket
  reachable from 127.0.0.1 is bound to UDP 9000 (read from the local listener
  table; "something is listening", not proof it is VRChat or that OSC accepts the
  chatbox), whether consent is given, and the last send's time and outcome. It
  states that delivery cannot be confirmed. Tests: three OSC checks (status
  wording, listener filtering against a real socket, sender outcome recording
  against a throwaway local socket) plus smoke-test assertions. VRChat's other
  port when 9000 is busy is not supported. Nothing has been sent to a live VRChat
  session, so the indicator has never been seen against the real game.*
- [x] Never automatically broadcast incoming conversation captions or history.
  *Evidence: the Send button handler is the only caller of `OscReplySender`;
  headset dashboard controls never send replies.*
- [x] Preserve the typed reply if translation or sending fails.
  *Evidence: the input box is never cleared by translate or send, and a failed
  send keeps the preview text. Code review only.*

Optional outbound microphone translation remains a later decision, not a
requirement of this list. Do not begin microphone capture implicitly.

## 13. Optional speaker labels

- [x] Make speaker labels optional and OFF by default, with a simple toggle for
  crowded worlds or performance-sensitive sessions.
  *Evidence: `SpeakerLabelsEnabled` defaults to false; the smoke test asserts it.*
- [x] When disabled, stop speaker-analysis processing, not just label rendering.
  *Evidence: the pipeline only calls the labeler when enabled, and resets its
  session state otherwise.*
- [ ] Investigate local speaker diarization for session labels such as Speaker 1
  and Speaker 2; benchmark reliability and extra latency before release.
  *Status: implemented (sherpa-onnx ERes2Net session clustering) but never
  benchmarked. `--speaker-test` was not rerun because the model and fixtures are
  not on this PC.*
- [x] Allow manual session names and consistent visual labels where feasible.
  *Evidence: "Name selected speaker" on the History tab; names are session-only.*
- [x] Show Unknown/uncertain when attribution is unreliable, especially with
  overlapping speech or voice changers.
  *Evidence: `SpeakerClustering` test; inconsistent halves, short clips and
  ambiguous embeddings return "Unknown speaker".*
- [x] Do not claim automatic VRChat usernames: the current mixed audio capture
  does not supply per-player identity. Avoid persistent voice identity profiles
  or cross-session identification by default.
  *Evidence: the UI says labels are estimates, not usernames; clusters are
  in-memory and cleared on reset.*

## Final completeness and bug-check loop

This is the required end-of-implementation verification loop, not a scheduled
background task. A documentation audit alone does not complete the features.

1. **Audit coverage.** Compare every checkbox with the implementation. Confirm
   that controls are connected to real behavior, defaults are correct, settings
   persist, and no placeholder is presented as a completed feature.
   *(Done once on 2026-10-06 for the code; repeat after any change.)*
2. **Build and test.** Run the build, deterministic tests, rendered-window smoke
   tests, and focused new regression tests. Check packaging and launch behavior.
3. **Exercise the app.** Test normal use, error recovery, missing models,
   start/stop, VRChat exit/restart, audio interruptions, language changes, history,
   overlay controls, tray behavior, and all new optional-feature toggles.
4. **Test integration and performance.** Verify native headset controls on real
   hardware, explicit reply sending in a controlled session, and caption latency
   during VRChat use. Keep established process-only capture and privacy defaults
   intact; do not regress the existing phone add-on.
5. **Record and fix bugs.** Write down failures, fix them, add regression coverage,
   and rerun affected tests plus the main regression suite.
6. **Repeat steps 1–5** until there are no missing agreed features or unresolved
   release-blocking defects. If credentials, permission, hardware, or user input
   is required, document the exact blocker and leave the affected items unchecked.
7. **Finish and hand off.** Update usage instructions, version/release notes,
   create and test the portable package, and summarize measured results and any
   explicitly accepted limitations. Do not claim "bug-free" from passing tests.

- [ ] All agreed features implemented and individually verified.
- [ ] Completeness audit passed with no unexplained unchecked requirements.
- [ ] Build and automated regression checks passed.
- [ ] Desktop usability and accessibility checks passed.
- [ ] Real VRChat/headset acceptance and performance checks completed.
- [ ] No unresolved release-blocking bugs.
- [ ] Privacy defaults and existing behavior preserved.
- [ ] Final portable package and instructions verified.

## Verification evidence

Record dates, commands/scenarios, results, remaining issues, and release artifact
paths here as implementation proceeds. Keep conversation content and raw audio
out of this document.

- 2026-08-27: Created the combined planning checklist. Feature implementation
  checkboxes intentionally remain unchecked.
- 2026-08-27: Checklist coverage audit passed for all 14 sections (13 feature/
  quality sections plus the final verification loop); no new feature was marked
  complete.
- 2026-08-27: Existing baseline build passed with 0 warnings and 0 errors:
  `dotnet build .\VRChatUniversalTranslator.slnx --configuration Release --no-restore`.
- 2026-08-27: Existing deterministic suite passed 16/16 tests:
  `dotnet run --project .\tests\VRChatTranslator.Core.Tests\VRChatTranslator.Core.Tests.csproj --configuration Release --no-build`.
  These baseline checks do not verify the planned features or replace the final
  desktop/headset acceptance and bug-check loop above.
- 2026-10-06: Audited every checkbox against the source, the 24 deterministic
  tests and the smoke-test assertions (read-only; VRChat, SteamVR, audio, phone
  and LM Studio were not touched). Ticked items cite their evidence inline. Still
  open, by cause: needs a headset or live VRChat (sections 3, 5, 6, 9, 12, final
  loop); needs a human pass over the running app (sections 1, 2); needs real
  audio or fixtures (sections 4, 8, 13); not implemented (draft captions, GPU
  acceleration, wrist controls, locked-versus-detected language indicator,
  automatic SteamVR reconnect, OSC connection indicator).
- 2026-10-07: Added the locked/auto language marker and tests for the
  output-translation failure path and the overlay position clamp (logic moved
  into `CaptionPresentation`, `CaptionOutputTranslation` and `OverlayPlacement`
  so the test project can reach it; behavior unchanged apart from the marker).
  Debug build 0 warnings/0 errors, 27/27 deterministic tests, WPF `--smoke-test`
  exit 0 with new marker assertions. Each new test was also confirmed to fail
  when its behavior was deliberately broken. No VRChat, SteamVR, audio, phone or
  LM Studio involved; nothing committed.
- 2026-10-07 (later): Added the OSC status line on the Replies tab
  (`OscStatusEvaluator`, `OscSendOutcome`, injectable target on `OscReplySender`).
  Debug build 0 warnings/0 errors, 30/30 deterministic tests, WPF `--smoke-test`
  exit 0. The three new tests were confirmed to fail when their behavior was
  deliberately broken. Tests send only to a throwaway local socket, never to port
  9000. Not looked at visually in a running window; VRChat, SteamVR, audio, phone
  and LM Studio were not involved; nothing committed.
- 2026-10-07 (evening): Added automatic SteamVR reconnect (`HeadsetLink`; the OpenVR
  calls stay in `SteamVrCaptionService`). Debug build 0 warnings/0 errors,
  36/36 deterministic tests (6 new, fake clock and fake connection), WPF
  `--smoke-test` exit 0 with new idle-state assertions. Four deliberate breakages
  of the retry rules were each caught by the matching test. No SteamVR, headset,
  VRChat, audio, phone or LM Studio involved; the reconnect path has not run
  against real OpenVR; nothing committed.
- 2026-10-07 (night): Added the opt-in "connect to SteamVR when the app starts"
  option (`AutoConnectHeadset`, `HeadsetLink.AutoConnect`). Debug build 0
  warnings/0 errors, 40/40 deterministic tests (4 new), WPF `--smoke-test` exit 0
  with an assertion that the option is off by default. Each new behavior (waits
  for SteamVR, no double start, first connection worded as a connection, off by
  default) was confirmed to fail when deliberately broken; two earlier mutation
  runs did not compile and were discarded and redone. The smoke test does not
  toggle the checkbox because that would write the real config file. No SteamVR,
  headset, VRChat, audio, phone or LM Studio involved; the real OpenVR calls at
  startup have not run; nothing committed.
