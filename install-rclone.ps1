$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$targetDirectory = Join-Path $projectRoot 'dist\tools'
$targetPath = Join-Path $targetDirectory 'rclone.exe'
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ("kitsu-rclone-" + [guid]::NewGuid().ToString('N'))

New-Item -ItemType Directory -Force -Path $temporary | Out-Null

try {
    $versionText = (Invoke-RestMethod -Uri 'https://downloads.rclone.org/version.txt').Trim()
    if ($versionText -notmatch '^rclone v(?<version>[0-9]+\.[0-9]+\.[0-9]+)$') {
        throw "Unexpected rclone version response: $versionText"
    }

    $version = $Matches.version
    $archiveName = "rclone-v$version-windows-amd64.zip"
    $baseUrl = "https://downloads.rclone.org/v$version"
    $archivePath = Join-Path $temporary $archiveName
    $checksumsPath = Join-Path $temporary 'SHA256SUMS'

    Write-Host "Downloading rclone v$version..."
    Invoke-WebRequest -Uri "$baseUrl/$archiveName" -OutFile $archivePath
    Invoke-WebRequest -Uri "$baseUrl/SHA256SUMS" -OutFile $checksumsPath

    $checksumLine = Get-Content -LiteralPath $checksumsPath |
        Where-Object { $_ -match "\s+$([regex]::Escape($archiveName))$" } |
        Select-Object -First 1

    if (-not $checksumLine) {
        throw "SHA256 entry was not found for $archiveName"
    }

    $expected = ($checksumLine -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -ne $actual) {
        throw "rclone SHA256 verification failed."
    }

    $expanded = Join-Path $temporary 'expanded'
    Expand-Archive -LiteralPath $archivePath -DestinationPath $expanded
    $binary = Get-ChildItem -LiteralPath $expanded -Filter rclone.exe -Recurse |
        Select-Object -First 1
    if (-not $binary) {
        throw 'rclone.exe was not found inside the verified archive.'
    }

    New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
    Copy-Item -LiteralPath $binary.FullName -Destination $targetPath -Force

    & $targetPath version
    if ($LASTEXITCODE -ne 0) {
        throw "rclone self-check failed with exit code $LASTEXITCODE"
    }

    Write-Host "Installed and verified: $targetPath"
}
finally {
    if (Test-Path -LiteralPath $temporary) {
        Remove-Item -LiteralPath $temporary -Recurse -Force
    }
}
