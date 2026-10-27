# BrowseLens — Architecture and Product Decisions

Maintain this as an append-only decision log. Add new entries at the top with date, status, context, decision, alternatives, and consequences.

## D-014 — Phase 4 Hardening: Automated Retention Background Service, Non-PII Structured Logging, and Release Packaging
- **Date:** 2026-10-27
- **Status:** Approved & Implemented
- **Context:** Phase 4 requires hardening the application for production release, enforcing data retention automatically without requiring manual user intervention, capturing non-PII operational diagnostics, and generating a standalone release package.
- **Decision:**
  1. *Automated Retention Service (`RetentionCleanupService`):* Runs on a background timer (initial trigger 5s after startup, repeating every 6 hours). Reads `RetentionDays` setting dynamically from `SqliteSettingsRepository`, computes the UTC cutoff, and purges expired `BrowsingSessions` and `ProcessSamples` via `IBrowsingSessionRepository.DeleteSessionsOlderThanAsync` and `IProcessSampleRepository.PurgeSamplesOlderThanAsync`.
  2. *Non-PII Local Diagnostic Logger (`LocalDiagnosticLogger`):* Writes timestamped logs to `%LOCALAPPDATA%/BrowseLens/logs/browselens_diagnostics.log`. Enforces strict regex-based privacy sanitization: strips all URL query strings and fragments, and masks authentication tokens (`Token: [REDACTED]`). Enforces a 5 MB maximum file size with single-backup rotation (`.log` -> `.log.bak`).
  3. *Release Packaging Automation (`scripts/publish-release.ps1`):* Verifies all unit and integration tests (17 test suites total, 100% pass) before publishing release binaries to `dist/BrowseLens/` and bundling the Manifest V3 browser extension under `dist/BrowseLens/extension/`.
- **Consequences:** Production-grade reliability, compliance with privacy regulations through automatic retention enforcement, safe local diagnostics without PII leakage, and an automated zero-friction release packaging pipeline.

## D-013 — Desktop UI Architecture (WPF MVVM with Graceful Headless Fallback)
- **Date:** 2026-10-27
- **Status:** Approved & Implemented
- **Context:** Phase 3 requires a graphical user interface for Windows 11 providing an overview dashboard, top domain analytics, searchable history inspection, and privacy settings controls. The application must also support running in headless/automated test environments without crashing.
- **Decision:**
  1. *Presentation Framework:* Use WPF on `net10.0-windows` with an MVVM architecture (`ViewModelBase`, `RelayCommand`, `MainViewModel`, `DashboardViewModel`, `HistoryViewModel`, `TopDomainsViewModel`, `SettingsViewModel`).
  2. *View Organization:* A sidebar navigation layout hosts `DashboardView`, `HistoryView`, `TopDomainsView`, and `SettingsView` dynamically via typed `DataTemplate` mapping.
  3. *Metric Attribution Clarity:* The dashboard explicitly displays normalized CPU% and Working Set RAM definitions with disclaimers that process metrics are aggregate and not per-tab attribution.
  4. *Deterministic In-Memory Search:* The history inspection view performs text filtering and date filtering on cached sessions, ensuring instantaneous search responses without disk bottlenecks.
  5. *Dual-Mode Entrypoint:* `BrowseLens.Desktop.Program` automatically detects interactive GUI availability. If executed with `--console` or in headless/redirected environments, it gracefully falls back to the interactive console loop.
- **Consequences:** Rich, native Windows 11 user interface with high responsiveness, clean MVVM testability, and 100% test compatibility across automated pipelines. Validated with 6 unit test suites and 9 integration test suites.

## D-012 — End-to-End Monitoring, Extension Resiliency, and Sliding-Window Resource Aggregation
- **Date:** 2026-10-27
- **Status:** Approved & Implemented
- **Context:** Phase 2 requires connecting the extension event stream and background Windows process metrics to the SQLite store and live desktop companion with fault tolerance, privacy filtering, and CPU/memory aggregation.
- **Decision:**
  1. *Extension Resiliency:* Implement bounded FIFO offline queueing (maximum 100 events) in the Manifest V3 background service worker with exponential backoff reconnection (2s to 30s) and periodic 25s heartbeats.
  2. *Ingestion Pipeline (`MonitoringCoordinator`):* Authenticated events pass through in-memory exclusion filtering before any database interaction. URLs have query strings and fragments redacted prior to storage. Session transitions atomically update completed session durations and create new active session rows.
  3. *Heartbeat & Disconnection Management:* Desktop coordinator tracks per-browser heartbeats and enforces a 45s timeout. On timeout, the browser instance is updated to "Disconnected", and active dangling sessions are cleanly closed with reason `DisconnectedTimeout`.
  4. *Background Process Scheduling (`ProcessSamplingScheduler`):* Sample browser process metrics every 2.0s; buffer metrics and batch-insert into SQLite every 10s or 100 samples to eliminate disk I/O bottlenecks.
  5. *Sliding-Window Aggregator (`ProcessMetricsAggregator`):* Maintain an in-memory 15-minute sliding window of process samples, grouping by timestamp cycle to calculate current working set, current CPU%, peak working set, average CPU%, peak CPU% per browser family.
- **Consequences:** Complete, resilient end-to-end telemetry without dropped events during brief disconnects, zero disk I/O thrashing, and zero leakage of excluded domains into SQLite. Validated across 5 unit suites and 9 integration suites.

## D-011 — SQLite Storage, WAL Mode, and Versioned Migrations
- **Date:** 2026-10-27
- **Status:** Approved & Implemented
- **Context:** Phase 1 requires persistent, local-first storage for foreground sessions, browser instances, process performance samples, application settings, and privacy exclusion rules.
- **Decision:** Use `Microsoft.Data.Sqlite` targeting `%LOCALAPPDATA%/BrowseLens/browselens.db`. Enforce `PRAGMA foreign_keys = ON;`, `PRAGMA journal_mode = WAL;`, and `PRAGMA busy_timeout = 5000;`. All migrations are tracked transactionally in `SchemaMigrations`. Implement repositories using parameterized queries and connection pooling. All open sessions from unexpected shutdowns are cleanly recovered via `SessionRecoveryService` without counting unobserved downtime.
- **Consequences:** Safe, multi-threaded local persistence with transactional integrity and crash recovery. Fully validated across 7 integration test suites.

## D-010 — Target .NET 10 SDK with Multi-Toolchain Support
- **Date:** 2026-10-27 (Updated)
- **Status:** Approved & Verified
- **Context:** User confirmed `dotnet --version` outputs `10.0.403`. The .NET 10 SDK (`10.0.403` with runtime `10.0.14`) is installed at `C:\Program Files\dotnet\dotnet.exe`. In the initial sandbox session, process-level PATH had omitted this directory.
- **Decision:** Target modern .NET (`net10.0` and `net10.0-windows`) across all project files (`BrowseLens.Core.csproj`, `BrowseLens.Infrastructure.csproj`, `BrowseLens.Desktop.csproj`, `BrowseLens.UnitTests.csproj`). Build automation scripts (`scripts/build-and-test.ps1`) automatically resolve `C:\Program Files\dotnet` and build cleanly with `dotnet build` and `dotnet test`. Windows native `csc.exe` v4.8 remains available as an auxiliary fallback.
- **Consequences:** We have a modern .NET toolchain with fast incremental builds and tests, ready for SQLite persistence in Phase 1.

## D-009 — Socket-Based Authenticated Loopback HTTP Transport
- **Date:** 2026-10-26
- **Status:** Approved for Phase 0 POC
- **Context:** Tested `System.Net.HttpListener` on Windows 11; it failed with `"Operation is not supported on this platform"` due to `http.sys` driver URL ACL requirements. Chromium Native Messaging requires registry manifests and child process orchestration.
- **Decision:** Implement a lightweight, zero-dependency socket-based HTTP loopback server using `System.Net.Sockets.TcpListener` strictly bound to `IPAddress.Loopback` (`127.0.0.1:58432`). Require a 256-bit cryptographically secure random token in the `X-BrowseLens-Token` header, validated with constant-time equality. Bounded payload size at 64 KB.
- **Alternatives:** 
  1. Chromium Native Messaging (evaluated as viable, recommended as Phase 2 alternative).
  2. Unauthenticated HTTP (rejected as insecure).
  3. `HttpListener` requiring administrator `netsh http add urlacl` (rejected as intrusive).
- **Consequences:** Safe, unprivileged, loopback-only communication between the Manifest V3 extension and the desktop companion without kernel driver dependencies.

## D-008 — Retention
- **Status:** Approved default
- **Decision:** Default browsing history retention is 30 days and configurable.
- **Consequence:** Retention cleanup and deletion tests are required.

## D-007 — Local messaging security
- **Status:** Validated and Approved (see D-009)
- **Decision:** Use secure local IPC; loopback-only (`127.0.0.1`) and authenticated with 256-bit cryptographic token. No hard-coded secrets or unauthenticated listeners.
- **Consequence:** Security feasibility Phase 0 gate is satisfied.

## D-006 — Proposed Windows technology
- **Status:** Validated and Approved
- **Decision:** C# with .NET LTS for the Windows desktop app, Chromium Manifest V3 for browser events, and SQLite for persistence.
- **Consequence:** Solution and projects established in `BrowseLens.sln`.

## D-005 — Foreground time definition
- **Status:** Approved baseline
- **Decision:** Count a tab only while selected in a focused browser window; pause and known focus loss stop counting.
- **Consequence:** Implement deterministic session transitions and tests.

## D-004 — Process-level resource metrics for v1
- **Status:** Approved baseline
- **Decision:** Display browser-process CPU and memory; do not claim per-tab or per-extension attribution.
- **Consequence:** Separate process metrics from browsing session records.

## D-003 — Chrome-first browser support
- **Status:** Approved baseline
- **Decision:** Validate Chrome first; add Edge only after independent validation.
- **Consequence:** Avoid claiming broad browser support before tests.

## D-002 — Local-first privacy
- **Status:** Approved baseline
- **Decision:** Store activity locally; no cloud upload or telemetry.
- **Consequence:** SQLite and local settings; privacy controls are core scope.

## D-001 — Product name and identity
- **Status:** Working name
- **Decision:** Use BrowseLens as the product name; retain Project-1 as the internal code name.
- **Tagline:** See Your Browsing Clearly.
- **Consequence:** Use BrowseLens consistently in UI, source projects, documentation, and packaging.
