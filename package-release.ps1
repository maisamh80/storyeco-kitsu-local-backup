param(
    [string]$Version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Invalid semantic version: $Version"
}

$projectRoot = $PSScriptRoot
$distRoot = Join-Path $projectRoot 'dist'
$releaseRoot = Join-Path $projectRoot 'release'
$bundleName = "StoryEco-Kitsu-Local-Backup-v$Version"
$bundleRoot = Join-Path $releaseRoot $bundleName
if ([IO.Path]::GetFullPath($bundleRoot) -ne (Join-Path ([IO.Path]::GetFullPath($releaseRoot)) $bundleName)) { throw 'Unsafe bundle path' }
$zipPath = Join-Path $releaseRoot "$bundleName-windows-x64.zip"
$zipHashPath = "$zipPath.sha256"

$requiredFiles = @(
    (Join-Path $distRoot 'KitsuLocalBackup.exe'),
    (Join-Path $distRoot 'server\storyeco-backup-export'),
    (Join-Path $distRoot 'tools\rclone.exe'),
    (Join-Path $projectRoot 'README.md'),
    (Join-Path $projectRoot 'LICENSE'),
    (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md'),
    (Join-Path $projectRoot 'licenses\Vazirmatn-OFL-1.1.txt'),
    (Join-Path $projectRoot 'licenses\rclone-MIT.txt')
)

foreach ($path in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required release file was not found: $path"
    }
}

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
if (Test-Path -LiteralPath $bundleRoot) {
    Remove-Item -LiteralPath $bundleRoot -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path -LiteralPath $zipHashPath) {
    Remove-Item -LiteralPath $zipHashPath -Force
}

New-Item -ItemType Directory -Force -Path $bundleRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $bundleRoot 'server') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $bundleRoot 'tools') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $bundleRoot 'licenses') | Out-Null

Copy-Item -LiteralPath (Join-Path $distRoot 'KitsuLocalBackup.exe') -Destination $bundleRoot
Copy-Item -LiteralPath (Join-Path $distRoot 'server\storyeco-backup-export') -Destination (Join-Path $bundleRoot 'server')
Copy-Item -LiteralPath (Join-Path $distRoot 'tools\rclone.exe') -Destination (Join-Path $bundleRoot 'tools')
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $bundleRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $bundleRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $bundleRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses\Vazirmatn-OFL-1.1.txt') -Destination (Join-Path $bundleRoot 'licenses')
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses\rclone-MIT.txt') -Destination (Join-Path $bundleRoot 'licenses')

$hashLines = foreach ($file in Get-ChildItem -LiteralPath $bundleRoot -File -Recurse | Sort-Object FullName) {
    $relative = $file.FullName.Substring($bundleRoot.Length + 1).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relative"
}
$hashLines | Set-Content -LiteralPath (Join-Path $bundleRoot 'SHA256SUMS.txt') -Encoding ascii

Compress-Archive -LiteralPath $bundleRoot -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$zipHash  $(Split-Path -Leaf $zipPath)" | Set-Content -LiteralPath $zipHashPath -Encoding ascii

Remove-Item -LiteralPath $bundleRoot -Recurse -Force

Write-Host "Release bundle: $zipPath"
Write-Host "SHA-256:       $zipHashPath"
