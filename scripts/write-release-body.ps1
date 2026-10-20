<#
.SYNOPSIS
  Writes the text of a GitHub release from RELEASE_NOTES.md and the package's checksum.

.DESCRIPTION
  Used by the Release workflow to fill in the DRAFT release. The newest section of RELEASE_NOTES.md becomes "What's new";
  the install steps carry the real SHA-256. The owner reads and edits the draft before publishing it.
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$ZipName,
    [Parameter(Mandatory)][string]$Sha256,
    [Parameter(Mandatory)][string]$OutputPath,
    [switch]$Signed,
    [string]$NotesPath = 'RELEASE_NOTES.md'
)

$ErrorActionPreference = 'Stop'
$lines = @(Get-Content -LiteralPath $NotesPath -Encoding UTF8)
$headings = @(for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -like '# *') { $i } })
if ($headings.Count -eq 0 -or $lines[$headings[0]] -notlike "# $Version *") {
    throw "The first heading in $NotesPath must start with '# $Version '."
}
$end = if ($headings.Count -gt 1) { $headings[1] - 1 } else { $lines.Count - 1 }
$section = if ($end -gt $headings[0]) { ($lines[($headings[0] + 1)..$end] -join "`n").Trim() } else { '' }
$title = ($lines[$headings[0]] -replace '^#\s+', '') -replace "^$([regex]::Escape($Version))\s*[\u2014-]\s*", ''

$smartScreen = if ($Signed) {
    'The program files are digitally signed (publisher: SignPath Foundation, which signs open-source projects for free; see `docs/CODE_SIGNING.md`). Windows SmartScreen may still warn until the signed file has built a reputation, so check the hash first.'
} else {
    'The app is not code-signed, so Windows SmartScreen may warn the first time (use **More info**, then **Run anyway**, only after the hash matches), and Microsoft Defender may check the unfamiliar program in the cloud for a moment. That is expected for new software.'
}

$body = @"
<!-- Draft written by the Release workflow. Read it, fix anything that is not true for this release, then publish. -->
## VRChat Universal Translator $Version (pre-release)

Live captions and English translation for VRChat, running entirely on your own Windows PC. It listens to the audio VRChat plays (never your microphone), turns speech into text with a local Whisper model, translates it to English, and shows captions in a click-through desktop overlay, on a SteamVR dashboard overlay, and optionally on a phone on your home network.

**No account, no telemetry, no cloud.** Audio and captions stay on your PC.

### What's new in ${Version}: $title

$section

### Install
1. Download ``$ZipName`` and ``SHA256SUMS.txt`` below.
2. Check the download in PowerShell:
   ``Get-FileHash .\$ZipName -Algorithm SHA256``
   The result must be:
   ``$Sha256``
3. Extract the whole ZIP to a folder and run ``VRChatUniversalTranslator.exe``. $smartScreen
4. **Download the speech model before expecting captions.** In **Settings**, choose **Balanced** and click **DOWNLOAD SELECTED MODEL** once (about 148 MB). After that, captioning works offline.

Updating from an earlier version: extract the new ZIP to a new folder and run it. Your settings and downloaded models live in ``%LOCALAPPDATA%\TheLevelEverythingCompany\VRChatUniversalTranslator`` and carry over.

Requires Windows 11 x64. See ``QUICK_START.md`` inside the ZIP.

### Known limitations
This is an early release. The status line at the top of the README and ``VERIFICATION.md`` (inside the ZIP) say exactly what was and was not tested; live VRChat sessions, a physical SteamVR headset and a physical phone have not all been through a full acceptance run, so treat the SteamVR overlay, speaker labels and phone display as experimental. CPU only. The phone display uses plain HTTP, so use it only on a network you trust.

Security reports: use the repository's Security tab (see ``SECURITY.md``).

Not affiliated with, endorsed by, or sponsored by VRChat Inc.
"@

[System.IO.File]::WriteAllText($OutputPath, $body + "`n")
Write-Host "Wrote the release text for $Version to $OutputPath"
