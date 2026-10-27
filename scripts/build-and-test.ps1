# BrowseLens Build and Test Script
Param(
    [switch]$ForceNativeCsc
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir

Write-Host '===========================================================' -ForegroundColor Cyan
Write-Host '  BrowseLens — Build and Test Automation                   ' -ForegroundColor Cyan
Write-Host '===========================================================' -ForegroundColor Cyan
Write-Host "Workspace Root: $rootDir"

# Ensure standard dotnet installation path is in PATH
if (Test-Path 'C:\Program Files\dotnet\dotnet.exe') {
    if ($env:PATH -notlike '*C:\Program Files\dotnet*') {
        $env:PATH = "C:\Program Files\dotnet;$env:PATH"
    }
}

$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue

if ($dotnetCmd -and -not $ForceNativeCsc) {
    Write-Host "[BUILD] Using .NET SDK CLI ($($dotnetCmd.Source))..." -ForegroundColor Green
    Set-Location $rootDir
    
    & dotnet build BrowseLens.sln --no-restore /nr:false /m:1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[WARN] dotnet build with --no-restore exited with $LASTEXITCODE; attempting dotnet restore..." -ForegroundColor Yellow
        & dotnet restore BrowseLens.sln
        & dotnet build BrowseLens.sln /nr:false /m:1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "dotnet build failed with code $LASTEXITCODE"
            exit $LASTEXITCODE
        }
    }

    Write-Host ''
    Write-Host '[TEST 1/2] Running automated unit tests...' -ForegroundColor Cyan
    $unitTestDll = Join-Path $rootDir "tests\BrowseLens.UnitTests\bin\Debug\net10.0-windows\BrowseLens.UnitTests.dll"
    if (-not (Test-Path $unitTestDll)) {
        $unitTestDll = Join-Path $rootDir "tests\BrowseLens.UnitTests\bin\Debug\net10.0\BrowseLens.UnitTests.dll"
    }
    & dotnet $unitTestDll
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host ''
    Write-Host '[TEST 2/2] Running automated integration tests (SQLite Persistence)...' -ForegroundColor Cyan
    $integrationTestDll = Join-Path $rootDir "tests\BrowseLens.IntegrationTests\bin\Debug\net10.0-windows\BrowseLens.IntegrationTests.dll"
    if (-not (Test-Path $integrationTestDll)) {
        $integrationTestDll = Join-Path $rootDir "tests\BrowseLens.IntegrationTests\bin\Debug\net10.0\BrowseLens.IntegrationTests.dll"
    }
    & dotnet $integrationTestDll
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host ''
    Write-Host '===========================================================' -ForegroundColor Green
    Write-Host '  ALL UNIT & INTEGRATION TESTS PASSED SUCCESSFULLY!        ' -ForegroundColor Green
    Write-Host '===========================================================' -ForegroundColor Green
    exit 0
} else {
    Write-Host '[BUILD] .NET SDK CLI not detected or native mode requested.' -ForegroundColor Yellow
    Write-Host '[BUILD] Using Windows native C# compiler (csc.exe v4.8)...' -ForegroundColor Yellow

    $cscPath = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path $cscPath)) {
        Write-Error "Neither dotnet CLI nor native csc.exe found at $cscPath"
        exit 1
    }

    $binDir = Join-Path $rootDir 'bin'
    if (-not (Test-Path $binDir)) {
        New-Item -ItemType Directory -Path $binDir -Force | Out-Null
    }

    $coreSources = Get-ChildItem -Path (Join-Path $rootDir 'src\BrowseLens.Core') -Filter '*.cs' -Recurse | Select-Object -ExpandProperty FullName
    $testSources = Get-ChildItem -Path (Join-Path $rootDir 'tests\BrowseLens.UnitTests') -Filter '*.cs' -Recurse | Select-Object -ExpandProperty FullName

    $coreDll = Join-Path $binDir 'BrowseLens.Core.dll'
    $testExe = Join-Path $binDir 'BrowseLens.UnitTests.exe'

    Write-Host 'Compiling BrowseLens.Core.dll...'
    & $cscPath /nologo /target:library /out:$coreDll $coreSources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host 'Compiling BrowseLens.UnitTests.exe...'
    & $cscPath /nologo /target:exe /r:$coreDll /out:$testExe $testSources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host '[TEST EXECUTION] Running BrowseLens.UnitTests.exe...' -ForegroundColor Cyan
    & $testExe
    exit $LASTEXITCODE
}
