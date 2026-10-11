# Privacy

VRChat Universal Translator is local-first.

## Defaults

- Cloud processing: off and not required
- Telemetry: off and not implemented
- Raw audio recording: off
- Transcript persistence: off
- Phone server: off
- LAN exposure: off until the user explicitly starts Phone Display
- Speaker analysis: off; when enabled, temporary voice embeddings and manual names remain in memory for this app session only
- Headset overlay: disconnected until you choose Connect, unless you turn on the off-by-default option to connect when the app starts; the app never starts SteamVR itself. After a connection you made is lost it retries on its own until you choose Disconnect headset
- OSC replies: only the reviewed preview is sent, on an explicit click with sending enabled; no microphone capture or automatic incoming-caption broadcast
- Reply status: while VRChat is running, the app checks every few seconds whether any program on this PC is listening on UDP port 9000 (it reads the PC's own listener table) so the Replies tab can say whether a reply could arrive. Nothing is sent and the result is not logged

Audio buffers exist only long enough to perform local voice detection and speech inference. Temporary caption history is memory-only and erased when the application closes. Diagnostic logs must not include raw audio or conversation text unless a future explicit diagnostic option clearly says otherwise.

When Phone Display is enabled, the PC sends sanitized caption and diagnostic events to connected local-network browsers. It does not send audio, filesystem paths (any path in an error message is replaced with [path] before it leaves the PC), secrets, or executable commands. The server answers only clients on this PC or a private network, only under a host name this PC would answer to (so a web page cannot reach it through DNS rebinding), and only with the six-digit PIN that is generated each time Phone Display is turned on and carried in the link shown in Advanced. Five wrong PINs from one device block it for five minutes, and at most eight displays can be connected at once. The link is plain HTTP: use it on a network you trust, and never on public Wi-Fi. The application will not configure router port forwarding, UPnP, public tunnels, or broad firewall exceptions.

## What the app downloads, and from where

Downloads happen only when you click a download or setup button, never in the background. Normal captioning and translation work without internet access, accounts, API keys, or paid services once setup is done. These are the only internet destinations the app contacts, and nothing about you, your audio or your captions is sent to any of them:

| When | Destination | What | How it is checked |
| --- | --- | --- | --- |
| Downloading a speech model (Settings) | Hugging Face, `ggerganov/whisper.cpp`, through the Whisper.net library | `ggml-tiny.bin`, `ggml-base.bin` or `ggml-small.bin` | Exact size and SHA-256 built into the app; a mismatch is deleted and refused |
| Turning on speaker labels (optional) | GitHub, `k2-fsa/sherpa-onnx` release `speaker-recongition-models` | One ONNX model file | Exact size and SHA-256 built into the app |
| Setting up language tools (Replies tab, optional) | `api.nuget.org` | An isolated CPython 3.13.15 runtime | SHA-512 built into the setup script |
| Same setup | `pypi.org` / `files.pythonhosted.org` | 14 Python packages (the translation libraries and their dependencies) | Every package is pinned by SHA-256 in `requirements-translation.txt`; `pip --require-hashes` refuses anything else |
| Downloading a language pack (Replies tab, optional) | `raw.githubusercontent.com` (the Argos package index), then `argos-net.com` | A language-pair model | HTTPS to that host only, no redirects, size and file-count limits. The index publishes no checksums, so these packs are trusted as far as that index and host are trusted |

Everything is downloaded into `%LOCALAPPDATA%\TheLevelEverythingCompany\VRChatUniversalTranslator` and nothing is installed system-wide. No system Python is used or changed.

## Files kept on this PC

- `config.json`: your settings (language, caption look, shortcuts, word corrections, recognition hints). No captions, no audio.
- `models\`: the downloaded speech and speaker models.
- `translation\`: the isolated Python runtime and language packs, if you set them up.
- `logs\`: a small daily diagnostic log (start/stop, errors, performance numbers). It never contains caption text or audio. Logs older than 14 days are deleted automatically and one day's log stops growing at 5 MB.

Deleting that folder removes everything the app has stored.

The optional translation worker communicates over private standard input/output, not a network listener. It keeps at most two local model instances and does not log conversation text. The explicit OSC reply destination is loopback UDP port 9000; VRChat may expose that reply to other players. Headset textures are delivered to the local SteamVR compositor. Closing the app terminates workers and discards conversation history and speaker embeddings.
