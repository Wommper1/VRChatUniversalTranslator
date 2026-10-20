# Release status — VRChat Universal Translator 1.1.4

Updated: 2026-10-20 (after publishing 1.1.4)

## Where things stand

- The repository is public, MIT-licensed (Copyright 2026 The Level Everything Company), with README, SECURITY, CONTRIBUTING, a code of conduct, issue forms, a pull-request template, a CI workflow and Dependabot.
- Releases **1.1.0**, **1.1.1**, **1.1.2**, **1.1.3** and **1.1.4** are published as **pre-releases**. **1.1.4** (2026-10-20, tag `v1.1.4` on commit `8f52539`) only updates the audio-capture library NAudio.Wasapi to 3.2.0; see [RELEASE_NOTES.md](RELEASE_NOTES.md). Its package was downloaded anonymously from the release page and checked against the published checksum. Before it, **1.1.3** (2026-10-15, tag `v1.1.3` on commit `d3fb6a0`) **1.1.3** (2026-10-15, tag `v1.1.3` on commit `d3fb6a0`) makes captions faster (an optional fast English engine, a faster Whisper setting for Balanced and Responsive CPU) and stops dropped speech being silent; see [RELEASE_NOTES.md](RELEASE_NOTES.md). Its package was downloaded anonymously from the release page and checked against the published checksum.
- What was tested, and what was not, is in [VERIFICATION.md](VERIFICATION.md). The new fast English engine was measured on test recordings only and has not yet been run in a live VRChat lobby.
- The portable ZIP contains the app, README, QUICK_START, PRIVACY, SECURITY, RELEASE_NOTES, THIRD_PARTY_NOTICES, VERIFICATION, LICENSE and the third-party `licenses\` folder. It contains no settings, models, logs or keys, and the DLLs and symbol files do not embed the local path of whoever built them.
- `artifacts/` is git-ignored on purpose: the ZIP is attached to a GitHub Release, never committed. Its SHA-256 is published in `SHA256SUMS.txt` beside it, not recorded in this file.

## Not yet done (say so on every release)

- **Real-world acceptance, partly done.** On 2026-10-12 live VRChat captioning, the model download, the four global shortcuts, saved settings and the app's load were checked on a real PC (see [VERIFICATION.md](VERIFICATION.md)). Still not run: the SteamVR headset overlay, OSC replies to a live chatbox, a physical phone through the router and firewall, caption accuracy and false captions, other languages, language packs and speaker labels. Use `ACCEPTANCE_CHECKLIST.md` for those. Until they are done, publish releases as **pre-releases**.
- **Fast English captions in a real lobby.** Measured on read-aloud test recordings and a small multilingual set only; its accuracy on real lobby chatter, accented English and very short utterances is not known, and nobody has yet clicked the Settings download button in the finished app (the app's own installer was run for real in a test: 12.4 s, checksums matched). It is off by default and labelled experimental.
- **Code signing.** The executable is unsigned, so Windows SmartScreen may warn. The hash in `SHA256SUMS.txt` is the user's check. A free signing route through SignPath Foundation is prepared (the Release workflow, `.signpath/` and [docs/SIGNPATH_SETUP.md](docs/SIGNPATH_SETUP.md)) but has not been applied for, so nothing is signed yet.
- **Independent security testing.** None has been done.

The application remains truthfully CPU-only. GPU acceleration is a future engineering goal, not a release blocker.

## How to make a release

1. Change `<Version>` in `src/VRChatTranslator.App/VRChatTranslator.App.csproj`. The build script reads the version from there.
2. Update the version in the current-release lines of `README.md`, `SECURITY.md` and the bug-report form, add a section at the top of `RELEASE_NOTES.md`, and record what you tested in `VERIFICATION.md`.
3. Run the checks listed in `CONTRIBUTING.md`, plus `--capture-proof` if audio code changed.
4. Run `scripts\build-release.ps1`. Extract the ZIP to a clean folder, run the executable with `--smoke-test` and `--phone-self-test`, and scan it for personal data.
5. Write `SHA256SUMS.txt` (`<hash>  <zip file name>`), commit, push, and wait for CI to be green.
6. Create the release: tag `vX.Y.Z` on that commit, tick **Set as a pre-release** until acceptance is done, attach the ZIP and `SHA256SUMS.txt`.
7. Download the ZIP from the release page as a stranger would and check that its hash matches.

## Making a release with the Release workflow (alternative to the steps above)

Steps 1 to 5 are the same (version, notes, checks, merged by pull request). Then tag the merged commit and push the tag (`git tag vX.Y.Z`, `git push origin vX.Y.Z`). The [Release workflow](.github/workflows/release.yml) builds the package on GitHub, runs the checks, checks the package for personal data, signs it if SignPath is switched on, and creates a **draft pre-release** with the ZIP and `SHA256SUMS.txt`. You still read the draft, download it and check it as in step 7, then publish it by hand. A package built this way is not byte-identical to one built on your PC (file times differ), so the hash that counts is the one in the draft.

## What changed in 1.1.4 compared with 1.1.3

- NAudio.Wasapi 3.1.0 to 3.2.0. Nothing else in the app. 72 automated checks, the real audio-capture test gave the same result as 1.1.1.
- The repository now ignores secret-looking files and requires its build check to pass before changes reach `main`, so a release is prepared on a branch, merged by pull request, and the package is built from the merged `main`.

## What changed since 1.1.2

- Optional fast English captions (Parakeet 110M, off by default) with a language check so other languages stay with Whisper; Balanced and Responsive CPU use a shorter Whisper audio context (about 30% faster); skipped speech is shown instead of vanishing and the last waiting segment is recognised late rather than dropped; invented stock-phrase captions are filtered; Apply saves the performance mode.
- New dependency SharpZipLib 1.4.2 (MIT). 72 automated checks (53 in 1.1.2).

## What changed in 1.1.2 compared with 1.1.1

- With no speech model installed, the app says so on every tab and opens Settings at the download button once per listening session (before, it said "Listening" while nothing could be captioned).
- A second launch shows the running copy instead of starting another.
- The diagnostic log records which shortcut or tray action ran, the mode and language codes applied, and model events (never caption text, audio, vocabulary or corrections).
- Two new automated checks (53 in total), more WPF smoke-test checks, and documentation of the first real-world session.

## What changed in 1.1.1 compared with 1.1.0

- NAudio.Wasapi 3.0.1 to 3.1.0 and sherpa-onnx 1.13.5 to 1.13.8. Nothing else in the app.

## What changed in 1.1.0 compared with the first local build (2026-10-03)

That first local ZIP (SHA-256 `36DC42CD...6F2F82`) was never released. 1.1.0 added:

- Phone display: PIN always required and new each time, local/private networks only, Host-name check, wrong-PIN limiter, display cap, paths redacted, hash-based Content-Security-Policy and other security headers.
- Verified downloads (exact size and SHA-256) for speech models and the speaker model; hash-locked Python tools; restricted language-pack downloads.
- Full-path process starts and System32-only native library search.
- Log retention (14 days, 5 MB a day); one shared data folder under `TheLevelEverythingCompany`.
- The (auto) / (locked) marker beside the source language on captions, an OSC status line on the Replies tab, automatic SteamVR reconnect, and an off-by-default "connect when the app starts" option.
- Neutral defaults, company/product/copyright metadata, and an application icon.