# Real-world acceptance checklist — version 1.1.2

The automated checks in [VERIFICATION.md](VERIFICATION.md) cannot cover anything that needs VRChat running, a headset, or a human listening. This checklist is for those. Work top to bottom in one session of about 60–90 minutes. Mark each item **Pass**, **Fail**, or **Skip** and write anything odd in Notes; a failure with a note is more useful than a quiet pass.

This document sets no latency or accuracy targets. Record what you measure and decide for yourself whether it is good enough.

**Tester:** ______  **Date:** ______  **Headset / runtime:** ______  **PC (CPU/RAM):** ______  **Preset used:** ______

## 0. Setup (do first)

- [ ] **Pick the build first.** Use the 1.1.2 portable ZIP from the Releases page. (1.1.1 and 1.1.0 behave the same apart from the first-run notice for a missing speech model, the one-copy guard and the extra log lines added in 1.1.2.) Earlier local builds from before 2026-10-09 lack the phone-display hardening and the (auto)/(locked) marker, so their section 4, 8.7–8.14 and section 11 results do not count. To test the current source instead, run `scripts\run.ps1`. Note that `scripts\build-release.ps1` overwrites the ZIP of the same version in `artifacts\`. Build used: ______
- [ ] To test only the packaged release instead, extract the portable ZIP to a fresh folder (not the project folder) and launch `VRChatUniversalTranslator.exe`. Either way, the window opens with six tabs: Live, History, Replies, Settings, Headset, Advanced.
- [ ] Settings → choose **Balanced**. On a first run there is no speech model, so click **DOWNLOAD SELECTED MODEL** and wait until it says installed; no captions can appear before that. Then **Apply & save settings**. Record whether the app made the missing model obvious. Result: ______
- [ ] Pick a **private or friends-only instance** with at least one other person willing to talk, or a world with spoken audio you can replay. Do the OSC reply test (section 11) only with someone who knows it is coming.
- [ ] Optional but recommended: the process-isolation proof (plays two quiet tones through Windows). From the project folder after a Debug build:
  `dotnet run --project .\tests\VRChatTranslator.Core.Tests\VRChatTranslator.Core.Tests.csproj -- --capture-proof (Resolve-Path .\tests\VRChatTranslator.AudioProofSource\bin\Debug\net10.0-windows\VRChatTranslator.AudioProofSource.exe)`
  Expected: silent-target case reports silence; tone-target case reports a non-zero signal. Result: ______

## 1. Basic live captions (VRChat running)

| # | Action | Expected | Result | Notes |
| --- | --- | --- | --- | --- |
| 1.1 | Start VRChat, then click **Start Listening** on Live | Status shows VRChat detected and listening | | |
| 1.2 | Have someone speak English | Caption appears on the desktop overlay and in History | | |
| 1.3 | Play non-VRChat audio (music or a video in a browser) while VRChat is silent | **No** captions from that audio (VRChat-only capture) | | |
| 1.4 | Click **Start Listening** again to stop | Listening stops; no further captions | | |
| 1.5 | Close VRChat while listening, then restart it | App notices the exit and picks up the new process without being restarted | | |
| 1.6 | Switch the Windows output device mid-session | Captions resume or a visible error explains why not | | |

## 2. Latency (measure, don't guess)

Procedure: in Advanced, note the **inference**, **queue** and **end-to-caption** timings. Have someone say the same short sentence (about 5 words) 5 times, then a longer one (about 15 words) 5 times. Write the Advanced readings below. Repeat once with a busy world (many players) if you can.

| Preset | Short sentence (ms, typical) | Long sentence (ms, typical) | CPU % in VRChat | VRChat FPS before → during |
| --- | --- | --- | --- | --- |
| Balanced | | | | |
| Responsive CPU | | | | |
| Ultra Light | | | | |

- [ ] Captions feel usable in conversation at your preferred preset. Notes: ______
- [ ] VRChat frame rate is acceptable with the translator running. Notes: ______

## 3. False captions

| # | Scenario | Expected | Result | Notes |
| --- | --- | --- | --- | --- |
| 3.1 | 60 seconds of silence in a quiet world | No invented captions | | |
| 3.2 | Music playing in the world, nobody talking | No (or very few) captions; never confident fake sentences | | |
| 3.3 | Coughing, laughing, keyboard noise | No placeholder text such as "speaking foreign language" in History | | |
| 3.4 | A quiet speaker | Still captioned (suppression must not eat real speech) | | |
| 3.5 | Two people talking at once | Result is uncertain/garbled at worst, not confidently wrong; note what you saw | | |

## 4. Language handling

- [ ] **Auto-detect** with someone speaking a non-English language: the live caption, History and the desktop overlay show the language route marked as detected (for example Japanese (auto) → English). Language: ______
- [ ] Lock the source language to that language, **Apply**: captions continue without restarting the app, and the route now says (locked) instead of (auto) in the live caption, History and the overlay.
- [ ] Switch back to **Auto-detect**, **Apply**, while listening: no crash, captions continue, and the route says (auto) again.
- [ ] A speaker changes language mid-conversation: the label follows after a short delay and does not flip on every noisy chunk.
- [ ] **Show original text beside translation** on: the original shown is the real recognized text, not an English reconstruction. English speech does not show a duplicate line.

## 5. Desktop overlay and caption comfort

- [ ] Settings → Caption comfort: change size, background opacity, duration. **Preview** reflects each change immediately.
- [ ] **Unlock / lock overlay** (Ctrl+Alt+O): unlocked = drag and resize; locked = clicks pass through to VRChat.
- [ ] A very long caption grows to fit or can be scrolled while unlocked; text stays readable.
- [ ] **Reset placement** brings an off-screen overlay back. Move the overlay to a second monitor, close the app, unplug or disable that monitor, relaunch: overlay recovers to a visible spot.
- [ ] Non-Latin text (Japanese, Cyrillic, Arabic) renders correctly.

## 6. Shortcuts and tray (do these with VRChat focused)

| # | Action | Expected | Result |
| --- | --- | --- | --- |
| 6.1 | Ctrl+Alt+L | Start/stop listening | |
| 6.2 | Ctrl+Alt+P | Pause/resume an active session | |
| 6.3 | Ctrl+Alt+H | Show/hide captions; listening unchanged | |
| 6.4 | Ctrl+Alt+O | Lock/unlock overlay | |
| 6.5 | Rebind one of L/P/H in Settings to a combo another app already uses, Apply | A conflict message appears; tray still works | |
| 6.6 | Minimize | Hides to tray; double-click the tray icon reopens | |
| 6.7 | Tray menu: status, pause/resume, settings | Each works | |
| 6.8 | Enable "Close button hides to tray", close the window | App stays in tray, with listening status visible | |
| 6.9 | **Tray → Quit** | App exits and capture stops (no lingering process in Task Manager) | |

## 7. Corrections

- [ ] Add a correction for a name that is often misheard, **Preview corrections**, then **Apply & save settings**: later captions use the corrected text.
- [ ] A correction does not alter the same letters inside a longer, unrelated word.
- [ ] Restart the app: the correction is still there.

## 8. SteamVR headset captions (experimental)

Start SteamVR, put the headset on, then **Headset → Connect / reconnect**.

| # | Check | Expected | Result | Notes |
| --- | --- | --- | --- | --- |
| 8.1 | **Preview captions** | Readable caption in the headset at a comfortable distance | | |
| 8.2 | Adjust distance, height, width, **Apply placement** | Caption moves as expected | | |
| 8.3 | Open the SteamVR dashboard, select **Translator** | Panel appears with Start/stop, Pause/resume, Show/hide, Nearer, Farther, Reset position, Move up, Move down, **Wider captions** | | |
| 8.4 | Press each dashboard button with a controller | Each does what it says; **Wider captions** widens the caption | | |
| 8.5 | Live speech while in VRChat | Captions appear in the headset and match the desktop ones | | |
| 8.6 | Caption size/opacity/duration/original-text options | Headset follows Caption comfort settings | | |
| 8.7 | Quit SteamVR while the app is running | Desktop app keeps working; no crash or hang. The Headset tab says SteamVR closed and that it will reconnect automatically (next try in 5 s). SteamVR shuts down fully (`vrserver` and `vrmonitor` leave Task Manager) and does not start again by itself | | |
| 8.8 | Start SteamVR again, put the headset on, and leave the app alone | Within about a minute the status says "Reconnected to SteamVR", and captions, the Translator dashboard panel and your placement return without pressing anything. Record how long it took. **Connect / reconnect** still works as a manual retry | | |
| 8.9 | **Disconnect headset**, then quit the app | Clean exit, no orphaned overlay | | |
| 8.10 | While SteamVR is down and the app is waiting to reconnect, drag the window and click around | The app stays responsive; a stutter every few seconds means a reconnect attempt is blocking the window | | |
| 8.11 | Quit SteamVR, wait for the "will reconnect automatically" message, click **Disconnect headset**, then start SteamVR | Status says "Stopped waiting for SteamVR" and nothing reconnects. Quitting the app while it is waiting leaves no `VRChatUniversalTranslator` process | | |
| 8.12 | Headset tab: tick **Connect to SteamVR automatically when the app starts**, quit the app, start SteamVR with the headset on, then start the app and touch nothing | Status says "Connected to SteamVR automatically" and captions and the Translator dashboard panel work. The app window opens without a noticeable delay | | |
| 8.13 | Quit the app and SteamVR, then start the app first (SteamVR closed) | Window opens normally with no freeze. Status says "Waiting for SteamVR (connect at start is on)" with the next try time. Start SteamVR: it connects within about a minute and says "Connected to SteamVR." (not "Reconnected"). Record how long it took | | |
| 8.14 | Untick the box, quit and restart the app with SteamVR running | Nothing connects until you press **Connect / reconnect**. A fresh install or old config has the box unticked | | |

Runtime/headset limits you observed (Index, Vive, Quest Link, etc.): ______

## 9. Speaker labels (experimental, off by default)

- [ ] With labels **off**: no speaker names appear. (Watch CPU in Task Manager as your baseline.)
- [ ] Download the optional speaker model, enable labels. Two people take turns: labels show **Speaker 1 / Speaker 2** and stay mostly consistent.
- [ ] Short, mixed or overlapping speech shows **Unknown speaker** rather than a confident guess.
- [ ] History → select a speaker → **Name selected speaker**: the name applies to that session's entries.
- [ ] Turn labels off again: extra CPU use drops back (analysis actually stops, not just the text).
- [ ] Restart the app: names and voice data are **not** remembered.

## 10. Output language packs (skip if you only need English)

- [ ] Replies → **Set up local language tools** (needs internet once), install an output-language pack for a language you can read, Apply on Live.
- [ ] Incoming captions appear in that language. If you cancel a download mid-way, the installed packs still work.
- [ ] Optional: **Preload installed pack now** noticeably reduces the delay of the first translated caption. Delay before / after: ______

## 11. Typed reply to VRChat chatbox (only in a private instance, with warning given)

Enable OSC in VRChat: Action Menu → Options → OSC.

- [ ] Replies → type a message, **Translate to preview**, edit the preview.
- [ ] **Copy preview** works without OSC enabled.
- [ ] Without ticking the explicit-send checkbox, the Send button does not send.
- [ ] Tick the checkbox, **Send reviewed reply to VRChat**: the *previewed* text appears in your chatbox (others may see it). Appears as shown: ______
- [ ] Send two replies quickly: the 1.5-second interval is respected, and a message over 144 characters or 9 lines is refused with a visible message.
- [ ] Incoming captions are never sent to the chatbox by themselves.
- [ ] With OSC off in VRChat: the app says so or the send fails visibly, and your typed reply is kept.
- [ ] Read the status line under the consent box at each step and record its text:
  - VRChat closed: it says VRChat is not running.
  - VRChat open with OSC off: it says nothing is listening on UDP port 9000.
  - OSC turned on in VRChat: within about 3 seconds it says VRChat is running and something is listening.
  - Ticking and clearing the consent box adds and removes "Sending is off until you tick the box" immediately.
  - After a send it shows "Last send: HH:mm:ss" and states that delivery cannot be confirmed.
- [ ] Look for the two failures that matter: the line says something is listening but the message never arrives, and the line says nothing is listening while OSC is on and messages do arrive (VRChat may be using a port other than 9000, which the app does not support). Result: ______

## 12. Privacy and data (end of session)

- [ ] `%LOCALAPPDATA%\TheLevelEverythingCompany\VRChatUniversalTranslator` contains settings and models, but **no transcripts and no audio files**.
- [ ] The Advanced phone display was off unless you deliberately enabled it, and its port was not forwarded.
- [ ] After **Tray → Quit**, no `VRChatUniversalTranslator` process remains.

## 13. Phone display (existing add-on; only if you use it)

- [ ] Enable it in Advanced; the link (it ends in `?pin=` and six digits) opens on a phone on the same Wi-Fi and shows captions live. Disable it again afterward.
- [ ] Edit the six digits in the phone's address bar to something else: the page must say it is disconnected / PIN required and show no captions.
- [ ] Turn it off and on again: the PIN in the link is different, and the old link no longer works.

## Results summary

| Section | Pass | Fail | Skip | Blocking issues |
| --- | --- | --- | --- | --- |
| 0 Setup | | | | |
| 1 Basic captions | | | | |
| 2 Latency | | | | |
| 3 False captions | | | | |
| 4 Language handling | | | | |
| 5 Overlay and comfort | | | | |
| 6 Shortcuts and tray | | | | |
| 7 Corrections | | | | |
| 8 SteamVR headset | | | | |
| 9 Speaker labels | | | | |
| 10 Language packs | | | | |
| 11 Typed replies | | | | |
| 12 Privacy and data | | | | |
| 13 Phone display | | | | |

**Failures to fix before calling 1.1.2 accepted:** ______

**Decision:** ☐ Accepted  ☐ Accepted with listed limitations  ☐ Not accepted

## After the session

1. Paste the filled-in results (or the failures) into a new chat or file and the matching items can be fixed and covered with regression tests.
2. Update the "Not verified" list in [VERIFICATION.md](VERIFICATION.md) with the date and what you actually tested.
3. Only then tick the matching boxes in [TODO.md](TODO.md) (sections 3–13) and remove the "experimental" wording for anything that passed on real hardware.
