<#
.SYNOPSIS
    Downloads the latest public/ folder from Lieyuan621/Endaxis to Resources/public.
.DESCRIPTION
    Fetches the main branch ZIP archive from GitHub, extracts only the public/
    subdirectory, and syncs it to Resources/public in the project root.
.PARAMETER TargetDir
    Override the destination directory (default: Resources/public relative to project root).
.EXAMPLE
    .\scripts\download-endaxis-public.ps1
    .\scripts\download-endaxis-public.ps1 -TargetDir "C:\custom\path"
#>
[CmdletBinding()]
param(
    [string]$TargetDir
)

$ErrorActionPreference = 'Stop'

$repoZipUrl = 'https://github.com/Lieyuan621/Endaxis/archive/refs/heads/main.zip'
$zipEntryPrefix = 'Endaxis-main/public'

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
if (-not $TargetDir) {
    $TargetDir = Join-Path (Join-Path $projectRoot 'Resources') 'public'
}

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "endaxis-dl-$([guid]::NewGuid().ToString('N').Substring(0,8))"
$zipPath = Join-Path $tempDir 'repo.zip'

function Format-FileSize([long]$bytes) {
    if ($bytes -ge 1MB) { return '{0:N2} MB' -f ($bytes / 1MB) }
    if ($bytes -ge 1KB) { return '{0:N2} KB' -f ($bytes / 1KB) }
    return "$bytes B"
}

try {
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

    Write-Host "[1/5] Downloading archive from GitHub..." -ForegroundColor Cyan
    $progressPreference_backup = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $repoZipUrl -OutFile $zipPath -UseBasicParsing
    $ProgressPreference = $progressPreference_backup

    $zipSize = (Get-Item $zipPath).Length
    Write-Host "      Downloaded $(Format-FileSize $zipSize)" -ForegroundColor Gray

    Write-Host "[2/5] Extracting archive..." -ForegroundColor Cyan
    $extractDir = Join-Path $tempDir 'extracted'
    Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force

    $sourcePath = Get-ChildItem -Path $extractDir -Directory | ForEach-Object {
        Join-Path $_.FullName 'public'
    } | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $sourcePath) {
        throw "Could not find public/ directory in the downloaded archive."
    }

    Write-Host "[3/5] Preparing target directory: $TargetDir" -ForegroundColor Cyan
    if (Test-Path $TargetDir) {
        Remove-Item -Path $TargetDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null

    Write-Host "[4/5] Copying files..." -ForegroundColor Cyan
    Copy-Item -Path (Join-Path $sourcePath '*') -Destination $TargetDir -Recurse -Force

    $stats = Get-ChildItem -Path $TargetDir -Recurse -File
    $totalFiles = $stats.Count
    $totalSize = ($stats | Measure-Object -Property Length -Sum).Sum

    Write-Host "[5/5] Cleaning up temporary files..." -ForegroundColor Cyan
    Remove-Item -Path $tempDir -Recurse -Force

    Write-Host ""
    Write-Host "Done! Synced $totalFiles files ($(Format-FileSize $totalSize)) to:" -ForegroundColor Green
    Write-Host "  $TargetDir" -ForegroundColor Green
}
catch {
    Write-Host "Error: $_" -ForegroundColor Red
    if (Test-Path $tempDir) {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    exit 1
}
