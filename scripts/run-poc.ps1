# Launch BrowseLens Desktop Proof of Concept
Param(
    [int]$Port = 58432
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir

# Ensure dotnet in PATH
if (Test-Path 'C:\Program Files\dotnet\dotnet.exe') {
    if ($env:PATH -notlike '*C:\Program Files\dotnet*') {
        $env:PATH = "C:\Program Files\dotnet;$env:PATH"
    }
}

$net10Dll = Join-Path $rootDir "src\BrowseLens.Desktop\bin\Debug\net10.0-windows\BrowseLens.Desktop.dll"
$binExe = Join-Path $rootDir "bin\BrowseLens.Desktop.exe"

if (Test-Path $net10Dll) {
    Write-Host "Launching BrowseLens Desktop Companion (.NET 10) on port $Port..." -ForegroundColor Green
    & dotnet $net10Dll $Port
} elseif (Test-Path $binExe) {
    Write-Host "Launching BrowseLens Desktop Companion (Native) on port $Port..." -ForegroundColor Green
    & $binExe $Port
} else {
    Write-Host "BrowseLens.Desktop not found. Building first..." -ForegroundColor Yellow
    & (Join-Path $scriptDir "build-and-test.ps1")
    if (Test-Path $net10Dll) {
        & dotnet $net10Dll $Port
    } else {
        & $binExe $Port
    }
}
