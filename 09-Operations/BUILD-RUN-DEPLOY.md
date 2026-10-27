# BrowseLens — Build, Run, and Deployment Guide

## 1. Prerequisites

- **Operating System:** Windows 11 x64 (verified on Build 26300.9711).
- **Installed SDK:** .NET 10 SDK (`v10.0.403`, runtime `v10.0.14`) installed at `C:\Program Files\dotnet\dotnet.exe`.
- **Auxiliary Compiler:** Windows C# Compiler (`csc.exe` v4.8) — preinstalled with Windows 11.
- **Browser:** Google Chrome (v156+) and/or Microsoft Edge (v156+).

---

## 2. Verified Build and Test Commands

All commands are tested in Windows PowerShell from the repository root.

### A. Build and Run Automated Tests
```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-and-test.ps1
```
- Automatically detects `.NET SDK` if present and runs `dotnet build` + `dotnet test`.
- If `.NET SDK` is not installed, seamlessly falls back to Windows native `csc.exe` v4.8 to build `BrowseLens.Core.dll`, `BrowseLens.Infrastructure.dll`, `BrowseLens.Desktop.exe`, and `BrowseLens.UnitTests.exe` in the `bin/` directory, and runs the automated test runner.

### B. Launch the Desktop Companion Proof of Concept
```powershell
powershell -ExecutionPolicy Bypass -File scripts\run-poc.ps1
```
Or directly:
```powershell
.\bin\BrowseLens.Desktop.exe
```

### C. Build and Package Production Release
```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1
```
- Executes all 7 unit test suites and 11 integration test suites to verify system integrity before packaging.
- Publishes optimized Release configuration binaries to `dist/BrowseLens/`.
- Packages the unpacked Manifest V3 extension to `dist/BrowseLens/extension/`.
- Launch the release executable directly:
```powershell
.\dist\BrowseLens\BrowseLens.Desktop.exe
```

---

## 3. Browser Extension Installation

1. Open Google Chrome (or Microsoft Edge).
2. Navigate to `chrome://extensions` (or `edge://extensions`).
3. Toggle on **Developer mode** in the top-right corner.
4. Click **Load unpacked**.
5. Select the extension directory:
   `src\BrowseLens.BrowserExtension` (or `dist\BrowseLens\extension`)
6. Launch `BrowseLens.Desktop.exe` to start the companion.
7. Note the printed **Authentication Token** in the console.
8. Click the BrowseLens Companion icon in the browser toolbar, choose **Configure Connection Token**, paste the token, and click **Save Settings**.
9. Click **Test Connection** to confirm live local communication.

---

## 4. Verification and Live Telemetry Absence

- **Network Scope:** Confirmed strictly bound to `127.0.0.1:58432`. No external network sockets are opened.
- **Zero Cloud Uploads:** No external HTTP endpoints, analytics SDKs, or cloud synchronization endpoints are defined.
- **Privacy Enforcement:** Private/incognito tabs and internal browser URLs (`chrome://`, `edge://`, `about:`) are rejected and dropped by both the extension and desktop companion.
