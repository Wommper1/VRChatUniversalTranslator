# Start here — VRChat Universal Translator 1.1

1. Extract the entire portable ZIP to a folder. Double-click `VRChatUniversalTranslator.exe`.
2. Start VRChat. On **Live**, leave spoken language on **Auto-detect**, or lock it to the language being spoken. Choose **English** as output for the simplest setup.
3. **First run only: download the speech model.** A fresh install has no speech model, so no captions can appear until you get one. In **Settings**, choose **Balanced** and click **DOWNLOAD SELECTED MODEL** (about 148 MB, once). Wait until the line above the button says the model is installed. The app checks the file before using it. Then return to **Live**, click **Apply**, then **Start Listening**. If you start listening first, nothing is wrong: the model status in Settings just says "required" until you download it.
4. Use **Settings → Caption comfort** for text size, background, duration, and a preview. Unlock the overlay to drag/resize it; lock again to click through it. Very long captions can be scrolled while unlocked. History keeps the last 100 captions, in memory only.

## Faster and clearer captions

Translated-only is the fastest default. Showing original foreign speech requires a second recognition pass. **Responsive CPU** uses shorter chunks; **Ultra Light** uses a smaller model and can trade accuracy for speed. There is no enabled GPU backend in this build. For repeated wrong words, add a correction, preview it, and click **Apply & save settings**. Recognition hints influence the recognizer; corrections replace exact words afterward.

Auto-detect works without choosing a language in advance. In noisy worlds, locking the source language can help. Each caption shows whether its source language was detected, "(auto)", or chosen by you, "(locked)". Mixed/overlapping voices and music are still challenging; captions and speaker labels are estimates, not a reliable transcript.

## Other output languages and typed replies

Open **Replies → Set up local language tools** (one-time internet download; no account or API key). It installs an isolated Python runtime inside the app's data folder, not your system Python.

- For incoming captions: choose the desired output language on Live; in Replies click **Install Live output-language pack**; return to Live and **Apply**. Auto-detected speech uses English as the intermediate translation when necessary.
- For a typed reply: choose **I write in** and **Reply in**, install that pair, type your message, and **Translate to preview**. Review/edit the preview. **Copy preview** works without VRChat OSC.
- To send: enable OSC yourself in VRChat's Action Menu → Options → OSC. Tick the app's explicit-send checkbox, then click **Send reviewed reply to VRChat**. This sends only the displayed preview to local port 9000. Other players may see it. There is no delivery acknowledgement; check your chatbox. A status line under the checkbox shows whether VRChat is running, whether anything is listening on port 9000, and the last send, so you can tell before sending whether a reply has a chance of arriving. The limit is 144 characters / 9 lines, with a 1.5-second send interval. Incoming captions are never auto-sent. The app never captures your microphone.

If tools or a pair are missing, a visible message explains the issue and the typed reply is retained. Incoming fallback text retains its actual language label. Downloads can be retried. Translation runs locally after setup; installed model folders show their licenses/metadata.

## Controls while playing

- Ctrl+Alt+L: start/stop listening.
- Ctrl+Alt+P: pause/resume an active session.
- Ctrl+Alt+H: show/hide desktop and headset captions without changing listening.
- Ctrl+Alt+O: unlock/lock the desktop overlay.

Change the first three in Settings, then Apply. Conflicts show a message; tray controls remain usable. Minimize hides to the system tray by default. Double-click its icon to reopen. Closing exits by default; optional close-to-tray is in Settings. **Tray → Quit** always exits and stops capture.

## SteamVR headset captions

For a PC-connected headset using **SteamVR**, start SteamVR and connect the headset, then choose **Headset → Connect / reconnect**. Preview captions and adjust distance, height, and width. Open the SteamVR dashboard and select **Translator** for controller-operated listening, pause, visibility, and placement buttons. Font/opacity/duration/original text follow Caption comfort. Closing SteamVR leaves the desktop app available. If SteamVR closes or restarts while the headset is connected, the app reconnects on its own (first retry after 5 seconds, then every 10 to 30 seconds) until you choose **Disconnect headset**. To connect when the app starts, tick **Connect to SteamVR automatically when the app starts** (off by default); if SteamVR is not running yet, the app waits for it. The app never starts SteamVR itself. Placement is saved.

This is an experimental native OpenVR overlay, not a VRChat mod. Standalone Quest and non-SteamVR runtimes are not supported. Physical headset positioning/controller interaction still needs acceptance testing.

## Optional speaker labels

Download the optional speaker model in Settings, then enable **Optional speaker labels**. Off is the default and stops new speaker analysis. Labels such as **Speaker 1** are temporary voice-cluster estimates, not VRChat usernames. Short/ambiguous/mixed voices can show **Unknown speaker**. In History, select a numbered speaker, enter a session name, and choose **Name selected speaker**. Names and voice embeddings are not saved across app sessions. Turn labels off in crowded worlds or when minimizing extra processing.

## Data and troubleshooting

Preferences and models: `%LOCALAPPDATA%\TheLevelEverythingCompany\VRChatUniversalTranslator`. No transcript or audio file is automatically saved. **Advanced → Save 10 second audio test** saves audio only after you explicitly choose a destination. The existing optional phone display is in Advanced; leave it off if unused. Open the link it shows (it includes a PIN that is new every time) on a phone on the same Wi-Fi. Never forward its port, and do not use it on public Wi-Fi.

If listening shows no captions, check the model status in Settings first. Run only one copy of the app at a time: two copies would each process the same VRChat audio and slow everything down. For missing models use the download buttons. For off-screen captions use **Reset placement**. For a conflicting shortcut choose a different key. If a setting file is damaged the app falls back to safe defaults. For slow captions inspect Advanced's inference, queue, and end-to-caption timings. See `VERIFICATION.md` for tested limits and remaining real-VR checks.

## Optional: fast English captions (experimental)

If most of what you hear is English, **Settings → Fast English captions** can cut the delay from about two seconds to about half a second to a second. It needs a one-time download (about 180 MB: NVIDIA's Parakeet English model, plus the small Whisper model used to check the language). Click **DOWNLOAD FAST ENGLISH MODEL**; it switches itself on when the download is checked and finished.

- English speech is captioned by the fast engine. Every other language, and all translation, is still handled by Whisper. A quick language check runs first, so a Japanese or Spanish speaker is never captioned as if they spoke English.
- Locking the spoken language to **English** skips that check and is fastest. Locking it to any other language means Whisper is used.
- It is off by default. Untick the box to go back to Whisper only; nothing else changes.
