[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\LocalAsrClient.App\LocalAsrClient.App.csproj'
$outputDirectory = Join-Path $repositoryRoot 'docs\assets\screenshots\product'

Push-Location $repositoryRoot
try {
    & dotnet run --project $projectPath -- --demo-mode --export-demo-screenshots $outputDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "LessASR 文档截图更新失败，退出码：$LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

$expectedScreenshots = @{
    'home.png' = @(1040, 760)
    'history.png' = @(1040, 760)
    'settings.png' = @(1040, 760)
    'continuous-dictation.png' = @(560, 680)
}

Add-Type -AssemblyName System.Drawing
foreach ($entry in $expectedScreenshots.GetEnumerator()) {
    $path = Join-Path $outputDirectory $entry.Key
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "缺少文档截图：$path"
    }

    $image = [System.Drawing.Image]::FromFile($path)
    try {
        if ($image.Width -ne $entry.Value[0] -or $image.Height -ne $entry.Value[1]) {
            throw "截图尺寸不正确：$($entry.Key) 实际为 $($image.Width)×$($image.Height)，期望为 $($entry.Value[0])×$($entry.Value[1])"
        }
    }
    finally {
        $image.Dispose()
    }
}

Write-Host "文档截图已更新：$outputDirectory"
