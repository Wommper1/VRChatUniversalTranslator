$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
# The version lives in one place, the app project; the package names follow it.
$projectFile = Join-Path $projectRoot 'src\VRChatTranslator.App\VRChatTranslator.App.csproj'
$version = ([xml](Get-Content -LiteralPath $projectFile -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Could not read a x.y.z <Version> from $projectFile (got '$version')." }
$outputPath = Join-Path $projectRoot "artifacts\VRChatUniversalTranslator-$version-win-x64"
$archivePath = Join-Path $projectRoot "artifacts\VRChatUniversalTranslator-$version-win-x64-portable.zip"
Push-Location $projectRoot
try {
    dotnet restore .\src\VRChatTranslator.App\VRChatTranslator.App.csproj --runtime win-x64 --configfile .\NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    # A release is always compiled from scratch, so it can never reuse output built with different settings.
    dotnet clean .\src\VRChatTranslator.App\VRChatTranslator.App.csproj --configuration Release --runtime win-x64 --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Clean failed.' }
    # Start from an empty output folder so a stale file from an earlier build can never end up in the package.
    if (Test-Path -LiteralPath $outputPath) { Remove-Item -LiteralPath $outputPath -Recurse -Force }
    # Map the build folder to /_/ so the DLLs and symbol files do not embed the local path of whoever built them.
    # SourceLink is switched off for the same reason: once the repository has a GitHub remote, the SDK would write the
    # local build folder (and the source URL) into the symbol files.
    # The product version in each file's properties is then exactly x.y.z, the same for every build of a release. The commit id is
    # left out on purpose: code-signing rules (SignPath) need the version to stay identical across builds, and the release tag already
    # names the commit.
    $pathMap = "-p:PathMap=$($projectRoot.TrimEnd('\'))\=/_/"
    dotnet publish .\src\VRChatTranslator.App\VRChatTranslator.App.csproj --configuration Release --runtime win-x64 --self-contained true --no-restore --output $outputPath $pathMap -p:EnableSourceLink=false -p:IncludeSourceRevisionInInformationalVersion=false
    if ($LASTEXITCODE -ne 0) { throw 'Portable build failed.' }
    foreach ($name in @('README.md','QUICK_START.md','PRIVACY.md','RELEASE_NOTES.md','THIRD_PARTY_NOTICES.md','VERIFICATION.md','SECURITY.md','LICENSE')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $outputPath
    }
    Compress-Archive -Path (Join-Path $outputPath '*') -DestinationPath $archivePath -CompressionLevel Optimal -Force
    Write-Host "Portable release created at $outputPath"
    Write-Host "Portable ZIP created at $archivePath"
}
finally {
    Pop-Location
}
