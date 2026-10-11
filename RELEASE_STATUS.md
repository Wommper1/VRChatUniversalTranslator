# Release status — VRChat Universal Translator 1.1.0

Updated: 2026-10-11

## Ready for public upload

- Source tree is versioned, MIT-licensed (Copyright 2026 The Level Everything Company), and has README, SECURITY, CONTRIBUTING, issue and pull-request templates, a CI workflow and Dependabot settings.
- A pattern scan of the tracked files and the whole git history (common API-key, token and private-key formats, plus file names such as `.env`, `*.pem`, `*.key`, logs) found nothing; no model files are tracked. This is a scan, not an audit.
- A security and release review was done on 2026-10-11; see "Security and release review" in [VERIFICATION.md](VERIFICATION.md) for what was checked, what was fixed, and what was not done.
- Release and Debug builds succeed with 0 warnings and 0 errors (warnings are errors). The automated suite passes 51/51. The WPF startup smoke test and phone server self-test pass, both from source and from the extracted ZIP.
- Self-contained portable ZIP built from the current source with `scripts\build-release.ps1`: `artifacts/VRChatUniversalTranslator-1.1.0-win-x64-portable.zip`. Its SHA-256 is published in `SHA256SUMS.txt` beside it on the release page, not recorded here, because this file changes with every rebuild.
- The ZIP contains the app, README, QUICK_START, PRIVACY, SECURITY, RELEASE_NOTES, THIRD_PARTY_NOTICES, VERIFICATION, LICENSE and the third-party `licenses\` folder. It contains no settings, models, logs or keys, and the DLLs and symbol files do not embed the local path of whoever built them.

`artifacts/` is git-ignored on purpose: the ZIP is attached to a GitHub Release, not committed.

## Not yet done (so say so on the release)

- **Real-world acceptance.** Live VRChat captioning, the SteamVR headset overlay, OSC replies, shortcuts while VRChat has focus, and a physical phone through the router and firewall have not been run. Use `ACCEPTANCE_CHECKLIST.md` in one 60-90 minute session. Until then, publish the release as a **pre-release**.
- **Code signing.** The executable is unsigned, so Windows SmartScreen may warn. The hash in `SHA256SUMS.txt` is the user's check.
- **Independent security testing.** None has been done.
- **CI.** The workflow in `.github/workflows/ci.yml` has not run yet; the first push is its first run.

The application remains truthfully CPU-only. GPU acceleration is a future engineering goal, not a blocker for 1.1.0.

## What changed since the first local 1.1.0 ZIP (2026-10-03)

That earlier local ZIP (SHA-256 `36DC42CD...6F2F82`) was never meant for release: it lacks the changes below.

- Phone display: PIN always required and new each time, local/private networks only, Host-name check, wrong-PIN limiter, display cap, paths redacted, hash-based Content-Security-Policy and other security headers.
- Verified downloads (exact size and SHA-256) for speech models and the speaker model; hash-locked Python tools; restricted language-pack downloads.
- Full-path process starts and System32-only native library search.
- Log retention (14 days, 5 MB a day); one shared data folder under `TheLevelEverythingCompany`.
- The (auto) / (locked) marker beside the source language on captions.
- An OSC status line on the Replies tab.
- Automatic SteamVR reconnect, and an off-by-default "connect when the app starts" option.
- Neutral defaults, company/product/copyright metadata, and an application icon.

None of the SteamVR or OSC changes has run against a real headset or VRChat.

## Publishing checklist

1. Push the repository, then in **Settings → Code security** turn on **Private vulnerability reporting** (SECURITY.md relies on it), and Dependabot alerts.
2. Add a short repository description and topics (for example `vrchat`, `whisper`, `captions`, `translation`, `windows`, `wpf`).
3. Create a release tagged `v1.1.0`, tick **Set as a pre-release**, attach the ZIP and `SHA256SUMS.txt`, and use `RELEASE_NOTES.md` as the description.
4. Open the Actions tab once after the push and confirm the CI run is green.
