# Code signing policy

> **Draft: not in effect yet.** The program is not code-signed today. This is the text the project will publish once
> SignPath Foundation has accepted it. Do not copy it to the README or the release pages, and do not claim the credit line
> below, until that has happened. Maintainer steps are in [SIGNPATH_SETUP.md](SIGNPATH_SETUP.md).

## Policy text (to publish after approval)

Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

### Who does what

| Role | What it means | Who |
| --- | --- | --- |
| Authors | Commit to the repository without a second review | [Wommper1](https://github.com/Wommper1) |
| Reviewers | Review every change from anyone who is not an author | [Wommper1](https://github.com/Wommper1) |
| Approvers | Approve each signing request, one request per release | [Wommper1](https://github.com/Wommper1) |

The project currently has a single maintainer who holds all three roles. Changes from anyone else arrive as pull requests,
and the `main` branch only accepts a change after its build check has passed. Everyone with a role uses multi-factor
authentication on GitHub and on SignPath. If more people join, this table lists them and links to their profiles.

### What is signed, and what is not

Signed: the program's own files, built from this repository's source: `VRChatUniversalTranslator.exe`,
`VRChatUniversalTranslator.dll`, `VRChatTranslator.Core.dll`, `VRChatTranslator.Audio.dll` and `VRChatTranslator.Speech.dll`.

Not signed by this project: the third-party files in the portable ZIP (the .NET runtime, NAudio, Whisper.net and
whisper.cpp, ONNX Runtime, sherpa-onnx, SharpZipLib, OpenVR, and the rest listed in
[THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md)). They keep exactly the bytes, and any signature, they were published with.
The speech and language models the program downloads later are data files, not programs, and are not signed.

### How a release is built and signed

1. The change is merged into `main` by pull request, with its build check passing, and the release is tagged (`vX.Y.Z`).
2. The [Release workflow](../.github/workflows/release.yml) builds the portable package from that tag on a GitHub-hosted
   runner, runs the automated checks and a vulnerable-package check, and checks the package for personal data and for
   files that should not ship.
3. The files to be signed are sent to SignPath, which refuses any file whose product name or product version is not
   exactly the project's (`VRChat Universal Translator` and the release version).
4. A signing request needs a manual approval by an approver. Nothing is signed automatically.
5. The workflow checks that every one of the five files has a valid, timestamped signature and that every other file in the
   package is byte-for-byte what was built. It then writes the checksum file and prepares a **draft** release. The
   maintainer reads the draft and publishes it by hand.

### Privacy

See [PRIVACY.md](../PRIVACY.md). In short: this program will not transfer any information to other networked systems
unless specifically requested by the user or the person installing or operating it. The only network use is downloads the
user starts (speech models, optional language packs and language tools; every address is listed in PRIVACY.md) and the optional
phone display, which the user switches on and which only answers on the local network. There are no accounts, no telemetry
and no cloud processing. The signing service receives the program files being signed; it never receives users' data.

### Checking a signature

In PowerShell, from the folder you extracted the ZIP to:

```powershell
Get-AuthenticodeSignature .\VRChatUniversalTranslator.exe | Format-List Status, SignerCertificate, TimeStamperCertificate
```

`Status` must be `Valid` and the signer must be SignPath Foundation. Windows also shows this under the file's
Properties, Digital Signatures tab. Check the ZIP against `SHA256SUMS.txt` first, as the release page says.

### Problems

Report a security problem privately through the repository's Security tab (see [SECURITY.md](../SECURITY.md)). If you
believe a signed file is malicious or that this policy is being broken, also tell SignPath at support@signpath.io.
