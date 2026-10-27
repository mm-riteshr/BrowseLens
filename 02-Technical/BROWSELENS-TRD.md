# BrowseLens — Technical Requirements Document (TRD)

- **Document ID:** BL-TRD-001
- **Version:** 1.0
- **Status:** Proposed baseline; validate in feasibility spike
- **Related:** BRD, FRD, Architecture, Database Design, API Contract

## 1. Purpose

Define the technical boundaries, preferred stack, security constraints, operational behavior, and quality requirements for BrowseLens v1. Any item marked **Proposed** must be confirmed by a feasibility spike or documented decision before it is treated as final.

## 2. Target environment

- Windows 11, x64 initially.
- Single interactive local user.
- Google Chrome first; Microsoft Edge after explicit validation.
- Local database and local-only communication.
- No mandatory cloud dependency.

## 3. Proposed stack

| Layer | Proposed baseline | Notes |
|---|---|---|
| Desktop UI | C# / current supported .NET LTS / WPF | Preferred for Windows integration; confirm installed SDK/runtime and packaging |
| Core domain | C# class library | Time accounting, session state, models, aggregation |
| Infrastructure | C# class library | SQLite, Windows process sampling, local messaging |
| Browser extension | Chromium Manifest V3, TypeScript or JavaScript | Use supported extension APIs; no page-content injection |
| Database | SQLite | Local file, schema migrations, indexes, retention |
| Tests | .NET unit/integration test framework | Test core logic without requiring a real browser for every test |
| Build | `dotnet` CLI plus extension build scripts | Reproducible local build |
| Packaging | Installer only after app runs reliably | Avoid packaging complexity before feasibility |

A different stack is allowed only if the agent explains the reason and updates `DECISIONS.md`.

## 4. Architectural constraints

- Separate UI, domain logic, browser event ingestion, process monitoring, and persistence.
- The Core layer must not depend on WPF, SQLite, or browser APIs.
- All incoming event payloads are untrusted input.
- No database writes or process sampling on the UI thread.
- All persistent timestamps use UTC.
- Use schema migrations from the first database version.
- Never persist excluded browsing data.
- Do not expose a local endpoint on non-loopback interfaces.
- No hidden monitoring, remote reporting, or telemetry.
- Process-level CPU/RAM must not be represented as tab/extension-level metrics.

## 5. Browser integration requirements

The extension should use only the minimum required permissions for:
- Reading tab metadata where supported.
- Observing tab activation, navigation/update and removal.
- Observing window focus changes where supported.
- Sending events to the local companion through the selected secure transport.

The agent must verify the exact permission/API requirements against current Chromium extension documentation and browser behavior. Do not request `<all_urls>` or page-content access unless a demonstrated requirement makes it unavoidable. If broad host permissions are required for URL capture, explain why, show the permission clearly to the user, and minimize scope.

Private/incognito support must remain disabled by default. Do not enable incognito access in extension settings for v1.

## 6. Windows process metrics

### CPU
- Use Windows process CPU-time counters and monotonic elapsed time.
- Formula should be documented. Example for normalized percentage of total machine capacity:
  `CPU% = (delta process CPU seconds / delta wall-clock seconds / logical processor count) * 100`.
- If displaying per-logical-processor normalization instead, label it clearly.
- Clamp or handle transient anomalies caused by sampling jitter.
- Associate samples with process start identity as well as PID to reduce PID reuse errors.

### Memory
- Choose a named metric, such as working set.
- Show bytes internally and convert to MB/GB for display.
- Explain that process working sets can include shared pages and do not necessarily equal private memory or browser task-manager values.
- Browser aggregate should be calculated from a reliable process inventory and must not double-count a process.

### Sampling
- Proposed default interval: 2 seconds.
- Keep the interval configurable internally; do not expose a complex tuning UI in v1 unless useful.
- Process samples may be stored at a reduced rate or aggregated to bound database growth.
- Measure BrowseLens's own CPU and memory overhead during validation.

## 7. Local IPC security

Preferred order:
1. Use a well-supported local IPC mechanism that can securely identify the intended local client and is compatible with browser extension constraints.
2. If using HTTP, bind strictly to loopback and implement an authentication mechanism suitable for a local extension-to-app boundary.

If HTTP is used:
- Bind only to `127.0.0.1`/IPv6 loopback; never `0.0.0.0`.
- Use a per-install random secret stored using Windows-protected storage where practical.
- Do not put secrets in URLs or logs.
- Validate message schema, size, timestamp range, event type, URL scheme and origin/extension identity as applicable.
- CORS and Origin checks are defense-in-depth, not the sole authentication control.
- Allow only narrow methods/routes.
- Reject malformed, oversized, replayed or unauthenticated messages.
- Add rate limits or bounded queues.
- Avoid exposing arbitrary file or process operations.
- Document how the extension obtains its credential securely; do not hard-code a shared secret in source.
- Test that the listener is unreachable from another machine.

If a secure mechanism cannot be implemented with the chosen extension architecture, stop and document the blocker rather than building an unauthenticated endpoint.

## 8. Data protection

- Store the SQLite database under a per-user local application data directory, not the source repository.
- Do not commit runtime browsing data, database files, logs, secrets, or user-specific config to source control.
- Add `.gitignore` entries for `data/`, `logs/`, local databases, build outputs and secrets.
- Apply retention cleanup.
- Use transactions for multi-row session changes.
- Handle database busy/full/corruption errors safely.
- Do not log full URLs or page titles by default.
- Deletion must remove primary and derived records.

## 9. Performance and reliability requirements

- UI remains responsive while polling process metrics and querying history.
- Use async operations where appropriate.
- Avoid unbounded queues or in-memory event histories.
- Handle extension disconnection, browser restart, sleep/resume and application restart.
- Persist enough session state to recover safely without inventing time during unobserved gaps.
- Use structured logs with levels and rotation/size limits.
- On failure, prefer a clear unavailable/error state over fabricated values.

## 10. API and domain contracts

- Define typed event DTOs and version the message envelope.
- Validate every incoming message.
- Use stable domain event types and explicit UTC timestamps.
- The database layer should be accessed through repositories or equivalent abstractions.
- Domain logic must be independently testable.
- API contract details live in `07-API/API-CONTRACT.md`.

## 11. Quality requirements

- Unit tests for all duration accounting and URL privacy rules.
- Integration tests for persistence and message validation.
- Manual browser/Task Manager comparison procedure.
- Build and test commands documented in README.
- No critical or high-severity privacy/security issue at release.
- No claims of unsupported granularity in UI or documentation.

## 12. Technical feasibility gates

Before full implementation, prove:
1. Tab activation/navigation/focus events are available from the chosen extension permissions.
2. The extension can communicate with the desktop app through a secure supported local mechanism.
3. Process CPU/RAM metrics are collected correctly on Windows 11.
4. Browser process inventory and aggregation behavior are understood.
5. Chrome setup is reproducible; Edge compatibility is separately verified.
6. Private/incognito activity is not collected by default.
7. The app can recover from browser/app restart without double counting.

Document evidence, test setup, and limitations in `09-Operations/TECHNICAL-FEASIBILITY.md`.
