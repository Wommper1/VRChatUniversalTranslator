# Security policy

## Reporting a vulnerability

Please report security problems **privately**, not in a public issue.

Use GitHub's private reporting: open the repository's **Security** tab and choose **Report a vulnerability**. Include what you found, the version (in the executable's Properties → Details), and steps to reproduce. You will get an answer as soon as the maintainer can read it; this is a small project, so please allow a few days.

Only the latest release is supported with fixes.

## What the app is, for threat-modelling

VRChat Universal Translator is a local-first Windows desktop app. It listens to the audio that `VRChat.exe` plays, turns it into captions on the PC, and shows them in an overlay, in SteamVR, and, only if you switch it on, on a phone on your own network. It has no account system, no cloud service of its own and no telemetry. See [PRIVACY.md](PRIVACY.md) for every network destination it can contact.

The parts that could matter for security are:

| Area | What it does | Safeguards |
| --- | --- | --- |
| Phone display (off by default) | A small HTTP server that streams captions to a browser | New six-digit PIN each time it is turned on, required for the caption stream; only this PC and private networks may connect; the `Host` header must be a name this PC answers to (blocks DNS rebinding); five wrong PINs block a device for five minutes; at most eight displays; paths are removed from the status line; the page renders captions with `textContent` only and carries a hash-based Content-Security-Policy, `nosniff`, `no-referrer`, no-store and frame-denial headers |
| Model downloads | Speech and speaker models | Exact size and SHA-256 built into the app; a mismatch is deleted and refused |
| Language tools setup (optional) | Downloads an isolated CPython and 14 Python packages | Python runtime pinned by SHA-512; every package pinned by SHA-256 and installed with `pip --require-hashes --no-deps`; nothing installed system-wide |
| Language packs (optional) | Downloads translation models from `argos-net.com` | HTTPS to that host only, no redirects, size and file-count limits, ZIP path traversal rejected |
| Started programs | Windows PowerShell for setup, an isolated Python worker, `nvidia-smi` for GPU numbers | Started by full path from trusted locations, never by name; the worker runs in Python isolated mode and receives text only as JSON on standard input |
| Native calls | Window and hotkey functions from `user32`/`kernel32` | Imported with a System32-only DLL search path |
| Files | Settings, models, logs under `%LOCALAPPDATA%\TheLevelEverythingCompany\VRChatUniversalTranslator` | Settings are written atomically; logs hold no caption text, are capped at 5 MB a day and are deleted after 14 days |

## Known limitations

These are deliberate or unavoidable, so please do not report them as vulnerabilities, but do tell us if you see a way to make them better:

- **The phone display uses plain HTTP.** Anyone who can watch traffic on the same network could read captions and the PIN. Use it only on a network you trust, never on public Wi-Fi, and never forward its port.
- **The executable is not code-signed**, so Windows SmartScreen may warn the first time. Check the download against `SHA256SUMS.txt` on the release page.
- **Language packs have no upstream checksum.** The Argos package index publishes none, so those packs are trusted as far as that index and host are trusted. Their contents are model data, not programs.
- **A person who can already run code as you, or edit the app folder, can change what the app does.** The portable app trusts its own folder.
- **VRChat can show an OSC reply to other players.** Replies are sent only on an explicit click.

## Verifying a download

```powershell
Get-FileHash .\VRChatUniversalTranslator-1.1.3-win-x64-portable.zip -Algorithm SHA256
```

The result must equal the line for that file in `SHA256SUMS.txt` on the same release page.

## Dependencies

NuGet packages are checked with `dotnet list package --vulnerable --include-transitive`; the last check found none. Dependabot is configured to propose updates to NuGet packages and GitHub Actions.
