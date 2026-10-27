# BrowseLens — Roadmap and Requirements Traceability

## 1. Delivery phases

### Phase 0 — Feasibility spike (COMPLETED)
- Confirm browser event APIs and permissions. (Done)
- Demonstrate secure extension-to-desktop messaging. (Done)
- Demonstrate Windows process CPU/RAM sampling. (Done)
- Validate Chrome installation and assess Edge compatibility. (Done)
- Document results in `09-Operations/TECHNICAL-FEASIBILITY.md`. (Done)

**Exit Criteria:** A minimal proof of concept works and no unresolved critical security blocker remains. -> **ACHIEVED**

### Phase 1 — Domain logic and persistence (COMPLETED)
- Core session state machine. (Done)
- UTC and foreground duration accounting. (Done)
- URL redaction and exclusion matching. (Done)
- SQLite schema, WAL mode, migrations, and repository layer. (Done)
- Unobserved crash recovery service (TC-012). (Done)
- Unit and persistence integration tests (7 integration suites). (Done)

**Exit Criteria:** Deterministic event sequences produce correct non-overlapping session durations and persist across restart. -> **ACHIEVED**

### Phase 2 — End-to-end monitoring (COMPLETED)
- Browser extension background queueing (bounded FIFO queue of 100 items) and retry resiliency. (Done)
- Secure event ingestion connected to live SQLite storage (`MonitoringCoordinator`). (Done)
- URL query/fragment redaction and in-memory domain exclusion filter prior to DB persistence. (Done)
- Process CPU/RAM background scheduler (`ProcessSamplingScheduler`) with batch persistence. (Done)
- Sliding-window process metrics aggregator (`ProcessMetricsAggregator`) with 15-minute rolling window, peak/average CPU% and peak Working Set. (Done)
- Connection status heartbeats and timeout detection (45s timeout marking instance Disconnected and closing dangling sessions). (Done)
- Unit and integration tests for Phase 2 (5 unit test suites, 9 integration test suites, 100% pass). (Done)

**Exit Criteria:** Supported browser events reach the app and stream into SQLite; live process metrics are sampled and aggregated. -> **ACHIEVED**

### Phase 3 — User interface (COMPLETED)
- Overview dashboard with active tab hero card, today's totals, live resource consumption cards for Chrome & Edge, and tracking pause banner. (Done)
- Top domains & website attribution screen with date-range filters (Today, 7 Days, 30 Days, All History) and percentage calculation. (Done)
- Browsing history inspection screen with in-memory search filter (by domain, title, URL), date filter, and single-session deletion. (Done)
- Privacy controls & settings screen with tracking toggle, URL query/fragment redaction toggle, wildcard domain exclusions manager, and database wipe/retention actions. (Done)
- Dual-mode WPF MVVM architecture with graceful headless/console fallback (`--console`). (Done)
- Unit and ViewModel presentation tests covering all Phase 3 screens (6 unit test suites, 9 integration test suites, 100% pass). (Done)

**Exit Criteria:** User can inspect usage, pause tracking, exclude sites, and delete history. -> **ACHIEVED**

### Phase 4 — Hardening and release (COMPLETED)
- Automated retention cleanup background service (`RetentionCleanupService` scheduled 5s after startup and every 6 hours). (Done)
- Non-PII local diagnostic logger (`LocalDiagnosticLogger`) with token sanitization, URL query/fragment redaction, and 5 MB rotation. (Done)
- Unit and integration testing suites for Phase 4 (Hardening unit suite 7 and Integration suite 10, 100% pass rate). (Done)
- Release packaging automation script (`scripts/publish-release.ps1`) generating production bundle in `dist/BrowseLens/`. (Done)
- Release notes, architectural decision log, and build/run documentation updated. (Done)

**Exit Criteria:** Clean setup works using README; no critical data-integrity/privacy defects. -> **ACHIEVED**

---

## 2. Traceability matrix

Status values: `Proposed`, `In Progress`, `Partially implemented`, `Implemented`, `Tested`, `Deferred`, `Blocked`.

| Business ID | Functional IDs | Technical areas | Test coverage | Current Status | Notes |
|---|---|---|---|---|---|
| BR-001 | FR-001, FR-004, FR-005, FR-007 | Process provider, dashboard POC | TC-001, TC-002, TC-003, TC-025 | Tested | Process enumeration, CPU delta, Working Set RAM, connection status tested in POC |
| BR-002 | FR-002, FR-003, FR-006 | Extension, session state, SQLite persistence | TC-004 to TC-012 | Tested | SQLite session repository, non-overlapping intervals, crash recovery verified |
| BR-003 | FR-007, FR-008 | Aggregation and history UI | TC-013, TC-014 | Tested | Top domains and foreground time aggregations implemented & verified in SQLite repo |
| BR-004 | FR-006, FR-012 | Local storage and no telemetry | TC-015, TC-016 | Tested | Strict 127.0.0.1 binding, local SQLite DB (%LOCALAPPDATA%), zero telemetry |
| BR-005 | FR-009 | Tracking state machine | TC-017, TC-018 | Tested | Pause/resume excludes paused time; persisted in AppSettings |
| BR-006 | FR-010, FR-011 | Privacy policy, deletion, retention | TC-015, TC-016, TC-019, TC-021, TC-022, TC-Incognito | Tested | URL redaction, domain exclusions, incognito rejection, and retention purging verified in SQLite |
| BR-007 | FR-004, FR-005, FR-007 | Metric definitions/UI | TC-024, TC-025 | Tested | Normalized CPU and Working Set RAM definitions verified; non-zero unavailable verified |
| BR-008 | FR-012, FR-014 | Layer boundaries and UX | Build/review checks | Tested | Clean separation: Core, Infrastructure, Desktop, Extension |
| BR-009 | FR-008 | Export service | TC-026 | Deferred/Should | Post-MVP |

---

## 3. Task backlog & progress

1. [x] Inspect repo, environment, and dev tools.
2. [x] Create and populate `TECHNICAL-FEASIBILITY.md`.
3. [x] Prototype Manifest V3 browser extension (`src/BrowseLens.BrowserExtension/`).
4. [x] Prototype secure local authenticated transport (`src/BrowseLens.Infrastructure/Ipc/LocalHttpEventServer.cs`).
5. [x] Prototype Windows process CPU/RAM sampling (`src/BrowseLens.Infrastructure/Process/WindowsProcessMetricsProvider.cs`).
6. [x] Implement core event/session models (`src/BrowseLens.Core/`).
7. [x] Implement and execute automated unit tests (`tests/BrowseLens.UnitTests/`).
8. [x] Implement runnable Proof of Concept companion (`src/BrowseLens.Desktop/Program.cs`).
9. [x] Implement SQLite schema migrations and repositories (Phase 1).
10. [x] Implement unobserved session crash recovery service (Phase 1).
11. [x] Build and verify automated SQLite integration tests (Phase 1).
12. [x] Connect background sampling scheduler & end-to-end stream (Phase 2).
13. [x] Build dashboard UI & history screen (Phase 3).
14. [x] Build privacy controls UI & deletion dialogs (Phase 3).
15. [x] Run release validation, automated retention, non-PII diagnostics, and packaging (Phase 4).
