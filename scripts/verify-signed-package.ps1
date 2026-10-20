<#
.SYNOPSIS
  Checks a code-signed copy of the package against the unsigned one it came from.

.DESCRIPTION
  Used by the Release workflow after SignPath returns the signed files. It fails (non-zero exit) unless:
    * each of the app's own files has a valid Authenticode signature with a timestamp, and
    * every other file is present and byte-identical to the unsigned package (only our own files may be signed;
      third-party libraries must keep exactly the bytes, and the signatures, they came with), and
    * no file was added or removed.
#>
param(
    [Parameter(Mandatory)][string]$UnsignedFolder,
    [Parameter(Mandatory)][string]$SignedFolder,
    [string[]]$SignedFiles = @('VRChatUniversalTranslator.exe', 'VRChatUniversalTranslator.dll', 'VRChatTranslator.Core.dll',
        'VRChatTranslator.Audio.dll', 'VRChatTranslator.Speech.dll')
)

$ErrorActionPreference = 'Stop'
$unsignedRoot = (Resolve-Path -LiteralPath $UnsignedFolder).Path.TrimEnd('\', '/')
$signedRoot = (Resolve-Path -LiteralPath $SignedFolder).Path.TrimEnd('\', '/')

function Get-Map([string]$root) {
    $map = @{}
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
        $relative = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
        $map[$relative] = $file.FullName
    }
    $map
}

$unsigned = Get-Map $unsignedRoot
$signed = Get-Map $signedRoot
$problems = New-Object System.Collections.Generic.List[string]

foreach ($relative in $unsigned.Keys | Where-Object { -not $signed.ContainsKey($_) }) { $problems.Add("Missing from the signed package: $relative") }
foreach ($relative in $signed.Keys | Where-Object { -not $unsigned.ContainsKey($_) }) { $problems.Add("Added to the signed package: $relative") }

foreach ($relative in $unsigned.Keys | Where-Object { $signed.ContainsKey($_) -and $SignedFiles -notcontains $_ }) {
    $before = (Get-FileHash -LiteralPath $unsigned[$relative] -Algorithm SHA256).Hash
    $after = (Get-FileHash -LiteralPath $signed[$relative] -Algorithm SHA256).Hash
    if ($before -ne $after) { $problems.Add("Changed although it is not one of our own files: $relative") }
}

foreach ($relative in $SignedFiles) {
    if (-not $signed.ContainsKey($relative)) { $problems.Add("Our own file is missing: $relative"); continue }
    $signature = Get-AuthenticodeSignature -LiteralPath $signed[$relative]
    if ($signature.Status -ne 'Valid') { $problems.Add("$relative is not validly signed (status: $($signature.Status))"); continue }
    if ($null -eq $signature.TimeStamperCertificate) { $problems.Add("$relative is signed without a timestamp, so it would stop validating when the certificate expires"); continue }
    Write-Host "$relative signed by $($signature.SignerCertificate.Subject)"
}

if ($problems.Count -gt 0) { throw "The signed package is not what was expected:`n$($problems -join "`n")" }
Write-Host "Signed package OK: $($SignedFiles.Count) of our own files signed, $($unsigned.Count - $SignedFiles.Count) other files unchanged."
