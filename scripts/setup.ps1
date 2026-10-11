param(
    [switch]$DownloadBalancedModel
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    dotnet restore .\VRChatUniversalTranslator.slnx --configfile .\NuGet.Config
    dotnet build .\VRChatUniversalTranslator.slnx --configuration Debug --no-restore
    dotnet run --project .\tests\VRChatTranslator.Core.Tests\VRChatTranslator.Core.Tests.csproj --no-build
    if ($DownloadBalancedModel) {
        dotnet run --project .\tests\VRChatTranslator.Core.Tests\VRChatTranslator.Core.Tests.csproj --no-build -- --download-balanced-model
    }
}
finally {
    Pop-Location
}
