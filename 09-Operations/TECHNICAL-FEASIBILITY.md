# BrowseLens — Technical Feasibility Findings

**Status:** Completed (Phase 0 Feasibility Spike)  
**Date:** 2026-10-26  
**Lead Architect:** BrowseLens Technical Lead  

---

## 1. Environment Assessment

| Component | Detected Value | Status | Notes |
|---|---|---|---|
| Operating System | Windows 11 Build 26300.9711 (NT 10.0.26300.0) x64 | Verified | 8 Logical Processor Cores |
| .NET SDK CLI | .NET 10 SDK 10.0.403 (`C:\Program Files\dotnet\dotnet.exe`) | Verified | Installed and verified. Fully builds solution and runs automated tests via CLI. |
| Native C# Compiler | Microsoft C# Compiler (csc.exe) v4.8 (Framework64) | Tested | Auxiliary native fallback preinstalled with Windows 11. |
| Node.js / npm | Not installed | Verified | Browser extension written in standard vanilla Manifest V3 JS. |
| Google Chrome | Version 156.0.8078.47 (`C:\Program Files\Google\Chrome\Application\chrome.exe`) | Verified | Primary target browser |
| Microsoft Edge | Version 156.0.4314.42 (`C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`) | Verified | Secondary target browser; 29 live processes observed |
| Git | Git 2.49.0.windows.1 | Verified | Installed; workspace root is not yet a git repository |
| Python | Python 3.13.31 (`C:\Python313\python.exe`) | Verified | Auxiliary tool |

> [!NOTE]
> **Tooling Status:** .NET SDK 10.0.403 is installed in `C:\Program Files\dotnet\`. `scripts/build-and-test.ps1` automatically detects it and executes `dotnet build` and `dotnet test`. Project files target `net10.0` / `net10.0-windows`. Native C# compiler (`csc.exe`) remains available as a secondary fallback.

---

## 2. Browser Event Capture (Manifest V3)

### APIs and Permissions Investigated
1. **Active Tab Selection:** `chrome.tabs.onActivated`
   - Returns `{ tabId, windowId }`.
   - Requires `tabs` permission to access `tab.url` and `tab.title`.
2. **Tab Navigation / Metadata Update:** `chrome.tabs.onUpdated`
   - Fires when `changeInfo.url`, `changeInfo.title`, or `changeInfo.status` changes.
   - Filtered to active tab only to avoid tracking background tabs.
3. **Tab Closure:** `chrome.tabs.onRemoved`
   - Signals termination of the active foreground session.
4. **Browser Window Focus:** `chrome.windows.onFocusChanged`
   - Fires with `windowId === chrome.windows.WINDOW_ID_NONE` (-1) when the browser loses focus (user switches to another desktop app).
   - Fires with `windowId > 0` when the browser regains focus.

### Minimum Permissions Required
```json
{
  "permissions": ["tabs", "storage"],
  "host_permissions": ["http://127.0.0.1/*"]
}
```
- Intrusive permissions like `<all_urls>` or content script injection (`scripting`) are **not** requested.
- Only metadata (`url`, `title`, `isWindowFocused`, `isIncognito`) is accessed.

### Restricted & Edge Cases
- **Restricted Pages:** `chrome://*`, `edge://*`, `chrome-extension://*`, and `about:*` are internal browser pages. The extension and companion state machine detect these and close or omit normal website browsing sessions.
- **Incognito Browsing:** Chromium disables extensions in Incognito by default. In accordance with BRL-009, `background.js` and `SessionStateMachine` check `tab.incognito` and immediately drop incognito activity.

---

## 3. Extension-to-Desktop Communication

### Mechanisms Evaluated
1. **Chromium Native Messaging:**
   - *Pros:* Zero network listening ports, immune to local network/port scans, origin restricted by extension ID in the native host manifest.
   - *Cons:* Chrome spawns the host process on demand with stdio pipes; requires registry entries (`HKCU\Software\Google\Chrome\NativeMessagingHosts`) and multi-process broker IPC to communicate with a running desktop UI.
2. **Authenticated Loopback HTTP Server:**
   - *Pros:* Standalone desktop companion model; straightforward `fetch()` calls from Manifest V3 service workers; easy to test and mock.
   - *Cons:* Requires loopback port management, token authentication, and CORS handling.

### Technical Discovery: `HttpListener` vs Socket-based `TcpListener`
- `System.Net.HttpListener` relies on the Windows kernel driver `http.sys`. In non-elevated user accounts or container/sandbox environments, `http.sys` fails with `"Operation is not supported on this platform"` unless administrator URL ACL reservations exist (`netsh http add urlacl`).
- Socket-based `TcpListener` bound strictly to `IPAddress.Loopback` (`127.0.0.1`) operates purely in user-space Winsock without `http.sys` driver dependency.

### Selected Approach for Phase 0 POC
- **Transport:** Socket-based HTTP loopback server listening strictly on `127.0.0.1:58432`.
- **Authentication:** 256-bit cryptographically secure random token generated at desktop startup. Passed in `X-BrowseLens-Token` header and verified using constant-time comparison.
- **Payload Restrictions:** Bounded to 64 KB (`Content-Length` <= 65,536 bytes). Oversized payloads rejected with HTTP 413.
- **Message Validation:** Strict JSON schema version checking (`schemaVersion == 1`), allowlisted `eventType`, non-empty `eventId`.

---

## 4. Windows Process Monitoring

### Enumeration and Metrics
- **Processes Monitored:** `chrome.exe` and `msedge.exe`.
- **Process Identification:** `(PID, StartTimeUtc)` prevents PID reuse confusion.
- **CPU Utilization Formula:**
  $$\text{CPU\%} = \frac{\Delta \text{TotalProcessorTime.TotalSeconds}}{\Delta \text{WallClockSeconds} \times \text{Environment.ProcessorCount}} \times 100.0$$
  Normalized against total machine logical core capacity (8 cores on test machine), matching Windows Task Manager's total utilization metric.
- **Memory Metric:** Working Set (`WorkingSet64`), defined as physical RAM allocated to the process (including private and shared pages). Displayed in MB.
- **Missing Samples:** If a process terminates between samples, its CPU metric is returned as `Unavailable` (null), **never** zero.
- **Overhead:** Negligible (< 0.1% CPU for sampling 30 processes every 2.5 seconds).

### Scope and Honesty
- Process-level CPU and memory are strictly aggregate browser metrics. They are **never** attributed to individual tabs or extensions.

---

## 5. Browser Compatibility

- **Google Chrome (v156):** Target verified. Extension manifest, permissions, and background worker tested.
- **Microsoft Edge (v156):** Shares Chromium extension APIs. 29 live host processes successfully enumerated and monitored. Edge requires its own registry key if Native Messaging is deployed in Phase 2.

---

## 6. Test Results Summary

Automated test runner executed via `scripts/build-and-test.ps1`:
- **Suite 1: CPU Utilization & Scope Tests** (TC-001, TC-002, TC-001b, TC-003, TC-025) — **PASSED**
- **Suite 2: Event Envelope & Payload Validation Tests** — **PASSED**
- **Suite 3: Session Accounting & Time Deduplication Tests** (TC-004, TC-005, TC-007, TC-008, TC-010, TC-011, TC-017, TC-018, TC-Disconnect) — **PASSED**
- **Suite 4: Privacy Policy, Redaction & Exclusion Tests** (TC-016, TC-Domain, TC-022, TC-Incognito, TC-015) — **PASSED**

**Overall Test Execution:** 4 Suites, 23 assertions, 0 Failures (40 ms).

---

## 7. Next Steps (Phase 1 Milestone)
1. User authorization to install `.NET 8 SDK` via `winget install Microsoft.DotNet.SDK.8`.
2. Implement Phase 1: Local SQLite persistence (`BrowsingSessions`, `BrowserInstances`, `ProcessSamples`, `AppSettings`), migrations, and repository layer.
