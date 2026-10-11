$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    dotnet run --project .\src\VRChatTranslator.App\VRChatTranslator.App.csproj
}
finally {
    Pop-Location
}
