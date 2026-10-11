param([Parameter(Mandatory=$true)][string]$DataRoot)
$ErrorActionPreference = 'Stop'
# Older Windows PowerShell may default to outdated TLS versions; downloads here must use TLS 1.2 or newer.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$translationRoot = Join-Path ([IO.Path]::GetFullPath($DataRoot)) 'translation'
$packageDir = Join-Path $translationRoot 'python-package'
$pythonExe = Join-Path $packageDir 'tools\python.exe'
$requirements = Join-Path $PSScriptRoot 'requirements-translation.txt'
if (-not (Test-Path -LiteralPath $requirements)) { throw 'requirements-translation.txt is missing next to this script.' }
New-Item -ItemType Directory -Path $translationRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath $pythonExe)) {
    $archivePath = Join-Path $translationRoot 'python-3.13.15.zip'
    $uri = 'https://api.nuget.org/v3-flatcontainer/python/3.13.15/python.3.13.15.nupkg'
    Write-Output 'Downloading isolated Python runtime...'
    Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $archivePath
    # Pinned from the official CPython NuGet package retrieved over HTTPS.
    $expected = '0AD3164E412912412D89EE9E8A9D8292893427812A67B9E43D8EF6766871FAA7F10DC15899E3691C14E0336FD79DA3D39EAA843EAC1E3E056A9151AD336BAC04'
    $hex = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA512).Hash
    if ($hex -ne $expected) { Remove-Item -LiteralPath $archivePath -Force; throw 'Python package checksum mismatch.' }
    Expand-Archive -LiteralPath $archivePath -DestinationPath $packageDir -Force
    Remove-Item -LiteralPath $archivePath -Force
}
Write-Output 'Installing CPU-only translation libraries (hash-verified)...'
# Every wheel, including indirect dependencies, is pinned by SHA-256; pip refuses anything that does not match.
& $pythonExe -I -m pip install --disable-pip-version-check --require-hashes --no-deps --only-binary=:all: --no-warn-script-location -r $requirements
if ($LASTEXITCODE -ne 0) { throw 'Translation dependency installation failed.' }
& $pythonExe -I -c 'import ctranslate2, sentencepiece, sacremoses, subword_nmt; print(ctranslate2.__version__)'
if ($LASTEXITCODE -ne 0) { throw 'Translation runtime import check failed.' }
Set-Content -LiteralPath (Join-Path $translationRoot 'ready-v2.txt') -Value 'ctranslate2=4.8.1 sentencepiece=0.2.2 sacremoses=0.2.0 subword-nmt=0.3.8 python=3.13.15 hash-locked' -Encoding UTF8
