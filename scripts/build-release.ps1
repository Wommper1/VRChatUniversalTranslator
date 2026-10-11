$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $projectRoot 'artifacts\VRChatUniversalTranslator-1.1.0-win-x64'
$archivePath = Join-Path $projectRoot 'artifacts\VRChatUniversalTranslator-1.1.0-win-x64-portable.zip'
Push-Location $projectRoot
try {
    dotnet restore .\src\VRChatTranslator.App\VRChatTranslator.App.csproj --runtime win-x64 --configfile .\NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    # Map the build folder to /_/ so the DLLs and symbol files do not embed the local path of whoever built them.
    $pathMap = "-p:PathMap=$($projectRoot.TrimEnd('\'))\=/_/"
    dotnet publish .\src\VRChatTranslator.App\VRChatTranslator.App.csproj --configuration Release --runtime win-x64 --self-contained true --no-restore --output $outputPath $pathMap
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
