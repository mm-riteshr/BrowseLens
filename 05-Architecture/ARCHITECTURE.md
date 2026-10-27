# BrowseLens — Architecture

## 1. Architectural goal

Use a small modular monolith for the Windows application plus a browser extension. Keep browser and OS integrations behind interfaces so the core session/time logic is deterministic and testable.

## 2. Logical components

```text
┌──────────────────────────┐
│ Chromium Browser         │
│ Manifest V3 Extension    │
│ - tab/navigation events  │
│ - window focus events    │
└─────────────┬────────────┘
              │ versioned, authenticated local messages
              ▼
┌──────────────────────────┐
│ BrowseLens.Infrastructure│
│ - IPC receiver/validator │
│ - Windows process metrics│
│ - SQLite repositories   │
│ - retention service     │
└─────────────┬────────────┘
              │ typed events/repositories
              ▼
┌──────────────────────────┐
│ BrowseLens.Core          │
│ - session state machine  │
│ - time accounting        │
│ - URL privacy rules      │
│ - aggregation            │
│ - interfaces/models      │
└─────────────┬────────────┘
              │ view models/use cases
              ▼
┌──────────────────────────┐
│ BrowseLens.Desktop       │
│ - overview dashboard     │
│ - history                │
│ - privacy/settings       │
│ - connection state       │
└──────────────────────────┘
```

## 3. Project responsibilities

### `BrowseLens.Core`
- Domain models and enums.
- Session state machine.
- Foreground duration accounting.
- URL normalization/redaction policy interfaces.
- Aggregation and summary calculations.
- Interfaces for repositories, clock, metrics and event transport.
- No WPF, SQLite, Windows APIs or browser API dependencies.

### `BrowseLens.Infrastructure`
- SQLite database and migrations.
- Browser event message receiver/transport.
- Windows process enumeration and sampling.
- Application settings storage.
- Retention/deletion.
- Structured diagnostics.
- Secure local IPC.

### `BrowseLens.Desktop`
- WPF UI and view models.
- Overview and history screens.
- Settings, pause/resume, connection status.
- User-facing errors and help.
- Composition root/dependency wiring.

### `BrowseLens.BrowserExtension`
- Browser event listeners.
- Minimal event DTO creation.
- Connection status/retry queue with bounded memory.
- User-visible permission and installation behavior.
- No page-content injection.

## 4. Main event flow

1. Browser extension receives tab/navigation/focus event.
2. Extension creates a versioned event with browser/tab identifiers and UTC timestamp.
3. Event is sent over the approved local transport.
4. Infrastructure authenticates and validates the message.
5. Core session processor applies pause/exclusion rules and updates session state.
6. Repository persists session changes transactionally.
7. Dashboard queries summaries and displays current state.
8. Process metrics provider samples browser processes independently and updates live metrics.

## 5. Session state model

Suggested states:
- `NoForegroundTab`
- `Foreground`
- `Paused`
- `Disconnected`
- `Recovering`

A state transition must be idempotent where possible. Duplicate events must not produce duplicate time. Out-of-order events should be rejected or reconciled using event timestamps and sequence numbers. Use monotonic elapsed time for active interval duration where available, while preserving UTC event timestamps for audit/history.

## 6. Process metric model

Represent process samples separately from browsing sessions. A process sample has:
- Browser type.
- PID.
- Process start identity.
- CPU metric value and definition.
- Memory metric value and definition.
- Sample timestamp.

Do not create a direct `TabId -> ProcessSample` ownership relation in v1. Browser process models can be multi-process and shared.

## 7. Failure and recovery behavior

- Extension disconnected: show status, stop opening sessions after known disconnect, close/reconcile open sessions using last confirmed timestamp.
- Browser closed: process exit plus extension disconnect should not inflate duration.
- Desktop app restarted: reconcile any open database sessions; do not count unobserved downtime as foreground time.
- Database unavailable: show an actionable error; do not silently discard data.
- IPC invalid message: reject and log safe metadata only.
- Process disappears during sampling: mark it exited; do not treat missing sample as zero.

## 8. Cross-cutting requirements

- Local-only data.
- UTC persistence.
- Typed/versioned messages.
- Schema migrations.
- Bounded queues and sampling.
- Structured logs with sensitive fields redacted.
- No UI thread blocking.
- Clear measurement scope.
