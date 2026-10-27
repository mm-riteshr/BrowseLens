# BrowseLens Release Publishing Script
# Builds, tests, and publishes a standalone release package to dist/

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir

Write-Host '===========================================================' -ForegroundColor Cyan
Write-Host '  BrowseLens — Release Packaging & Deployment              ' -ForegroundColor Cyan
Write-Host '===========================================================' -ForegroundColor Cyan

# 1. Run full test suite first
Write-Host '[1/3] Running full test verification...' -ForegroundColor Yellow
& (Join-Path $scriptDir 'build-and-test.ps1')
if ($LASTEXITCODE -ne 0) {
    Write-Error "Tests failed. Release packaging aborted."
    exit $LASTEXITCODE
}

# 2. Publish Desktop Application
$distDir = Join-Path $rootDir 'dist\BrowseLens'
if (Test-Path $distDir) {
    try {
        Remove-Item -Path $distDir -Recurse -Force -ErrorAction Stop | Out-Null
    }
    catch {
        Start-Sleep -Milliseconds 500
        Remove-Item -Path $distDir -Recurse -Force -ErrorAction SilentlyContinue | Out-Null
    }
}
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

Write-Host ''
Write-Host '[2/3] Publishing Desktop Application to dist/BrowseLens...' -ForegroundColor Green
$desktopProj = Join-Path $rootDir 'src\BrowseLens.Desktop\BrowseLens.Desktop.csproj'

& dotnet publish $desktopProj -c Release -o $distDir /nr:false /m:1
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 3. Copy Browser Extension
Write-Host ''
Write-Host '[3/3] Packaging Browser Extension...' -ForegroundColor Green
$distExtDir = Join-Path $distDir 'extension'
Copy-Item -Path (Join-Path $rootDir 'src\BrowseLens.BrowserExtension') -Destination $distExtDir -Recurse -Force

Write-Host ''
Write-Host '===========================================================' -ForegroundColor Green
Write-Host '  RELEASE PACKAGE SUCCESSFULLY CREATED!                    ' -ForegroundColor Green
Write-Host '===========================================================' -ForegroundColor Green
Write-Host "Output Directory: $distDir"
Write-Host "Desktop Executable: $(Join-Path $distDir 'BrowseLens.Desktop.exe')"
Write-Host "Extension Directory: $distExtDir"
Write-Host ''
Write-Host 'To run release desktop app: & "' + (Join-Path $distDir 'BrowseLens.Desktop.exe') + '"'

