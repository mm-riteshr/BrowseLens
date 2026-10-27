# BrowseLens — Release Notes

## Version 1.0 (MVP) — General Availability & Phase 4 Hardening (Completed 2026-10-27)

### Highlights
BrowseLens Version 1.0 is officially validated, hardened, and packaged for Windows 11. It provides a complete, local-first browser observability companion and Chromium extension (Chrome and Edge) that accounts for active tab foreground browsing durations, tracks browser process-level CPU and memory utilization, and respects user privacy with zero cloud telemetry.

### Phase 4 Hardening Deliverables
- **Automated Background Retention Service (`RetentionCleanupService`):**
  - Runs on a background timer (starts 5s after application launch, repeats every 6 hours).
  - Automatically queries user-configured retention policy (`RetentionDays`, default 30 days).
  - Purges browsing sessions and process performance metrics older than the cutoff threshold, ensuring the database stays compact and compliant without requiring manual user action.
- **Privacy-Preserving Diagnostic Logging (`LocalDiagnosticLogger`):**
  - Writes structured non-PII operational events to `%LOCALAPPDATA%/BrowseLens/logs/browselens_diagnostics.log`.
  - Enforces automatic regex sanitization on all logged text: strips URL query strings and fragments, and masks connection authentication tokens (`Token: [REDACTED]`).
  - File size monitoring rotates the active log to `.bak` upon reaching 5 MB to prevent unbounded disk usage.
- **Standalone Release Packaging (`scripts/publish-release.ps1`):**
  - Automates release packaging into `dist/BrowseLens/`.
  - Self-contained structure includes `BrowseLens.Desktop.exe`, dependent assemblies, SQLite native binaries, and the Manifest V3 browser extension unpacked in `dist/BrowseLens/extension/`.
- **Robust IPC & Multi-Tab Tab Accounting:**
  - Binary framing in `LocalHttpEventServer` fixes multi-byte UTF-8 character length hangs and eliminates false duplicate event retries from browser extensions.
  - Background tab closing (`TabRemoved`) and in-page navigation (`TabUpdated`) verified without disrupting active foreground tab sessions.
- **Comprehensive Test Verification:**
  - 7 Unit Test Suites (33 test cases) covering CPU calculations, state machine, realistic multi-tab browsing journeys, event envelopes, privacy redacting, sliding-window aggregator, MVVM presentation, and automated retention/diagnostics.
  - 11 Integration Test Suites validating real SQLite WAL migrations, crash recovery (TC-012), duration aggregations (TC-013), batch process sampling, exclusion pattern matching, retention deletions (TC-019/TC-021), end-to-end event stream ingestion, scheduler flushing, automated retention cleanup, and realistic multi-tab browsing lifecycles.
  - 100% test pass rate across all suites.

---

## Phase 3 Windows Desktop UI & MVVM Presentation (Completed 2026-10-27)

### Implemented & Tested
- **WPF Native Desktop Application:** Full graphical Windows 11 interface targeting `net10.0-windows` with MVVM architecture.
- **Overview Dashboard:** Active tab hero card showing live URL and page title, today's foreground duration and visit counters, live browser resource monitor cards for Chrome and Edge (process count, working set RAM, peak RAM, current CPU%, 15-min avg/peak CPU%), tracking pause banner, and top websites quick glance.
- **Browsing History Inspection Screen:** DataGrid view with local time conversion, in-memory real-time search filtering across domains, titles, and URLs, date range selectors (Today, 7 Days, 30 Days, All History), and single-session deletion.
- **Top Domains & Website Attribution:** Domain analytics view with rank badges, formatted foreground browsing durations, visit counts, and relative percentage-of-total time calculations.
- **Privacy Controls & Settings Screen:** Prominent tracking pause/resume toggle, URL query-string/fragment redaction switch, wildcard domain exclusion rule manager (add and remove patterns with in-memory drop guarantees), retention duration setting (default 30 days), on-demand retention cleanup, and complete database history wipe.
- **Dual-Mode Execution:** Automatic detection of desktop display availability; provides headless/console fallback when executed with `--console` or redirected input.
- **Automated ViewModel Testing:** Unit test suite 6 covering all view models, search filtering, domain attribution calculations, and privacy control workflows (100% pass).

---

## Phase 2 End-to-End Monitoring & Resilient Ingestion (Completed 2026-10-27)

### Implemented & Tested
- **Resilient Extension Queueing:** Manifest V3 background service worker upgraded with bounded FIFO offline queue (maximum 100 events), exponential backoff reconnection (2s to 30s), periodic 25s heartbeats, and browser action badge status indicators ("OK", "QUE", "ERR").
- **Stream Ingestion Coordinator (`MonitoringCoordinator`):** End-to-end event stream pipeline synchronizing incoming browser tab events with SQLite persistence. Atomically calculates interval durations upon tab switches and inserts new active sessions.
- **Privacy Rule Pre-Filtering:** In-memory domain exclusion evaluation strictly before database queries or inserts, guaranteeing zero leakage of excluded sensitive domains into SQLite. Configurable URL query string and fragment stripping.
- **Background Process Sampling Scheduler (`ProcessSamplingScheduler`):** Periodic 2.0s background timer sampling active browser processes with buffered batch persistence (every 10s or 100 samples) to prevent disk I/O bottlenecks.
- **Sliding-Window Resource Aggregator (`ProcessMetricsAggregator`):** In-memory 15-minute sliding window calculating cycle-level working set, current CPU%, peak working set, average CPU%, and peak CPU% per browser family.
- **Heartbeat & Disconnection Management:** 45s heartbeat timeout monitors browser liveliness, transitions instance connection status to "Disconnected", and safely closes dangling sessions with `DisconnectedTimeout`.
- **Test Automation:** 5 automated unit test suites (28 assertions) and 9 automated integration test suites (100% pass rate).

---

## Phase 1 Domain Logic & SQLite Persistence (Completed 2026-10-27)

### Implemented & Tested
- **SQLite Engine:** Embedded local persistence in `%LOCALAPPDATA%/BrowseLens/browselens.db` using `Microsoft.Data.Sqlite` in WAL mode with foreign keys enabled.
- **Database Migrations:** Transactional schema migration engine (`DatabaseMigrator`) managing `SchemaMigrations`, `BrowsingSessions`, `BrowserInstances`, `ProcessSamples`, `AppSettings`, and `ExcludedPatterns`.
- **Repository Layer:** Clean repository abstractions and implementations (`SqliteBrowsingSessionRepository`, `SqliteProcessSampleRepository`, `SqliteSettingsRepository`, `SqliteBrowserInstanceRepository`).
- **Crash & Restart Recovery:** `SessionRecoveryService` detects sessions left dangling by abrupt crashes or app termination and closes them with `EndReason = 'AppCrashRecovery'`, ensuring unobserved downtime is never counted as active browsing.
- **Top Domain & Duration Aggregations:** Aggregates foreground duration by normalized domain and date ranges directly in SQLite.
- **Integration Test Suite:** 7 automated integration tests in `tests/BrowseLens.IntegrationTests/` covering migrations, transactional sessions, crash recovery, process sample purging, and retention deletion.

---

## Phase 0 Feasibility Spike & MVP Foundation (Completed 2026-10-26)

### Implemented & Tested
- **Browser Integration:** Manifest V3 extension (`BrowseLens.BrowserExtension`) observing `chrome.tabs.onActivated`, `chrome.tabs.onUpdated`, `chrome.tabs.onRemoved`, and `chrome.windows.onFocusChanged`. Incognito activity is strictly rejected.
- **Local IPC Transport:** Socket-based authenticated loopback HTTP server (`127.0.0.1:58432`) requiring 256-bit cryptographically secure random token in `X-BrowseLens-Token` header. Strict schema validation, error handling, and payload size bounds (64 KB).
- **Windows Process Metrics:** `WindowsProcessMetricsProvider` enumerates `chrome.exe` and `msedge.exe`, tracks process start identities `(PID, StartTimeUtc)` to guard against PID reuse, and computes normalized CPU% across logical cores and Working Set RAM in MB.
- **Session Accounting State Machine:** Non-overlapping foreground interval calculations, deterministic switching, focus loss/gain handling, pause/resume exclusions, and monotonic sequence deduplication.
- **Privacy Engine:** URL query string and fragment redaction, domain normalization, internal URL scheme detection (`chrome://`, `edge://`, `about:`), and wildcard domain exclusions.
- **Proof of Concept Companion:** Interactive console companion (`BrowseLens.Desktop.exe`) displaying real-time process metrics, connection status, and received active tab events.
- **Automated Test Suite:** 4 automated test suites (23 test cases) covering CPU normalization, process exit handling, schema validation, session state transitions, and privacy rules.

---

## Unreleased — Version 1.0 MVP

### Planned for Phase 1
- Local SQLite persistence (`BrowsingSessions`, `BrowserInstances`, `ProcessSamples`, `AppSettings`).
- Database schema migrations and repository layer.
- Persistence integration tests.

### Planned for Phase 2 & 3
- Full desktop overview dashboard and history interface.
- Daily aggregation and top domain analytics.
- Privacy settings UI and data deletion workflows.

### Known Limitations
- Process CPU and RAM measurements represent whole-process and aggregate-browser utilization; no per-tab or per-extension attribution is claimed or fabricated.
- Incognito/private browsing is strictly filtered out by default.
- Modern .NET 10 SDK is verified and targeted across all project components (`net10.0` / `net10.0-windows`). Native Windows `csc.exe` v4.8 remains available as a secondary fallback.
