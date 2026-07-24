[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\LocalAsrClient.App\LocalAsrClient.App.csproj'

Push-Location $repositoryRoot
try {
    & dotnet run --project $projectPath -- --demo-mode
    if ($LASTEXITCODE -ne 0) {
        throw "LessASR 演示模式启动失败，退出码：$LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
