<#
.SYNOPSIS
  Checks the portable package that build-release.ps1 made, and writes its checksum file.

.DESCRIPTION
  Used by the Release workflow and safe to run by hand. It fails (non-zero exit) if the package is missing a file that must
  ship, holds something that looks like a setting, model, log or key, or contains the build machine's folder name, the
  maintainer's user name or an e-mail address. On success it writes SHA256SUMS.txt next to the ZIP.
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$ArtifactsDirectory = 'artifacts',
    [string]$WorkspacePath = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
$name = "VRChatUniversalTranslator-$Version-win-x64"
$folder = Join-Path $ArtifactsDirectory $name
$zip = Join-Path $ArtifactsDirectory "$name-portable.zip"

foreach ($path in @($folder, $zip)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing $path. Run scripts/build-release.ps1 first." }
}

# The app and the documents that ship with it.
$required = 'VRChatUniversalTranslator.exe', 'README.md', 'QUICK_START.md', 'PRIVACY.md', 'SECURITY.md', 'RELEASE_NOTES.md',
    'THIRD_PARTY_NOTICES.md', 'VERIFICATION.md', 'LICENSE'
foreach ($file in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $folder $file))) { throw "The package is missing $file." }
}

# No settings, models, logs or keys.
$suspect = Get-ChildItem -LiteralPath $folder -Recurse -File |
    Where-Object { $_.Name -match '(?i)^(settings|config)\.json$|\.(gguf|ggml|bin|log|pem|key|pfx|env)$' }
if ($suspect) { throw "The package contains files that look like settings, models, logs or keys: $($suspect.FullName -join ', ')" }

# No build-machine folder, user name or e-mail address, as text or as UTF-16 (how .NET stores some strings).
$needles = @($WorkspacePath, $WorkspacePath.Replace('\', '/'), 'MrWom', 'wommper1@', 'gmail.com') | Where-Object { $_ } | Select-Object -Unique
$patterns = foreach ($needle in $needles) {
    [pscustomobject]@{ Needle = $needle; Ascii = $needle; Utf16 = ($needle.ToCharArray() -join [string][char]0) }
}
$found = New-Object System.Collections.Generic.List[string]
foreach ($file in Get-ChildItem -LiteralPath $folder -Recurse -File) {
    $text = [System.Text.Encoding]::GetEncoding(28591).GetString([System.IO.File]::ReadAllBytes($file.FullName))
    foreach ($pattern in $patterns) {
        if ($text.IndexOf($pattern.Ascii, [StringComparison]::Ordinal) -ge 0 -or $text.IndexOf($pattern.Utf16, [StringComparison]::Ordinal) -ge 0) {
            $found.Add("$($file.FullName.Substring($folder.Length + 1)) contains '$($pattern.Needle)'")
        }
    }
}
if ($found.Count -gt 0) { throw "The package holds personal or build-machine data:`n$($found -join "`n")" }

$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText((Join-Path $ArtifactsDirectory 'SHA256SUMS.txt'), "$hash  $(Split-Path $zip -Leaf)`n")
$count = (Get-ChildItem -LiteralPath $folder -Recurse -File).Count
Write-Host "Package OK: $count files, no personal or build-machine data."
Write-Host "SHA-256 $hash  $(Split-Path $zip -Leaf)"
