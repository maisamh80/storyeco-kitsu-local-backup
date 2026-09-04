$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourcePath = Join-Path $projectRoot 'src\KitsuLocalBackup.cs'
$assetsDir = Join-Path $projectRoot 'assets'
$fontPath = Join-Path $assetsDir 'Vazirmatn-VariableFont_wght.ttf'
$kitsuLogoPath = Join-Path $assetsDir 'kitsu.png'
$storyEcoLogoPath = Join-Path $assetsDir 'storyeco-dark.png'
$outputDir = Join-Path $projectRoot 'dist'
$outputPath = Join-Path $outputDir 'KitsuLocalBackup.exe'
$compiler = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
}

if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'C# compiler was not found.'
}

foreach ($asset in @($fontPath, $kitsuLogoPath, $storyEcoLogoPath)) {
    if (-not (Test-Path -LiteralPath $asset)) {
        throw "Embedded asset was not found: $asset"
    }
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $outputDir 'server') | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'server\storyeco-backup-export') `
    -Destination (Join-Path $outputDir 'server\storyeco-backup-export') -Force

& $compiler `
    /nologo `
    /target:winexe `
    /optimize+ `
    /platform:x64 `
    /out:$outputPath `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Runtime.Serialization.dll `
    /reference:System.Security.dll `
    /reference:System.Windows.Forms.dll `
    "/resource:$fontPath,StoryEco.Assets.Vazirmatn.ttf" `
    "/resource:$kitsuLogoPath,StoryEco.Assets.Kitsu.png" `
    "/resource:$storyEcoLogoPath,StoryEco.Assets.StoryEco.png" `
    $sourcePath

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

Write-Host "Built: $outputPath"
