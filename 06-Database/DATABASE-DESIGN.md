# BrowseLens — Database Design

## 1. Storage location

Store the SQLite database under a per-user application data directory, e.g. `%LOCALAPPDATA%/BrowseLens/`. Do not put the live database in the source repository or commit it to version control.

## 2. General rules

- SQLite with foreign keys enabled.
- UTC timestamps in ISO-8601 text or integer epoch milliseconds; choose one consistent representation and document it.
- Schema migrations are mandatory from the first version.
- Parameterized SQL only.
- Transactions for multi-step session changes and deletions.
- Add indexes based on query patterns.
- Retention cleanup must remove dependent/derived data.
- Never persist excluded URL/title records.

## 3. Proposed schema

### `BrowserInstances`
| Column | Type | Notes |
|---|---|---|
| `Id` | INTEGER PK | Local identity |
| `BrowserType` | TEXT NOT NULL | `Chrome`, `Edge` |
| `InstanceKey` | TEXT | Non-secret local key, nullable |
| `FirstSeenUtc` | TEXT NOT NULL | UTC |
| `LastSeenUtc` | TEXT NOT NULL | UTC |
| `ConnectionStatus` | TEXT NOT NULL | Last known state |

### `BrowsingSessions`
| Column | Type | Notes |
|---|---|---|
| `Id` | INTEGER PK | Session row |
| `BrowserInstanceId` | INTEGER FK | Browser instance |
| `WindowKey` | TEXT | Session-scoped browser window key |
| `TabKey` | TEXT | Session-scoped tab key |
| `Url` | TEXT | Subject to redaction |
| `NormalizedDomain` | TEXT | Lowercase host/domain |
| `PageTitle` | TEXT | Subject to privacy settings |
| `StartedAtUtc` | TEXT NOT NULL | UTC |
| `EndedAtUtc` | TEXT NULL | Null only while open |
| `ForegroundDurationMs` | INTEGER NOT NULL DEFAULT 0 | Non-negative |
| `EndReason` | TEXT | Switch, navigation, focus loss, pause, disconnect, recovery |
| `WasUrlRedacted` | INTEGER NOT NULL DEFAULT 0 | Boolean |
| `CreatedAtUtc` | TEXT NOT NULL | UTC |

Rules:
- Avoid storing excluded sessions.
- Session IDs are local and not browser identifiers.
- Browser tab/window IDs are scoped and may be reused; do not treat them as permanent identities.
- Navigation can either close the old URL session and start a new one or update a session model; choose one behavior and test it consistently. Recommended: close prior URL interval and start a new session for a meaningful URL/title change.

### `ProcessSamples`
| Column | Type | Notes |
|---|---|---|
| `Id` | INTEGER PK | Sample row |
| `BrowserType` | TEXT NOT NULL | Browser |
| `ProcessId` | INTEGER NOT NULL | OS PID |
| `ProcessStartIdentity` | TEXT NOT NULL | Protects against PID reuse |
| `SampledAtUtc` | TEXT NOT NULL | UTC |
| `CpuPercent` | REAL NULL | Null when unavailable |
| `MemoryBytes` | INTEGER NULL | Null when unavailable |
| `CpuMetricDefinition` | TEXT NOT NULL | Normalization description |
| `MemoryMetricDefinition` | TEXT NOT NULL | e.g. working set |

Process samples may be retained for a shorter period or aggregated to avoid unbounded growth. Do not store every high-frequency sample indefinitely.

### `AppSettings`
| Column | Type | Notes |
|---|---|---|
| `Key` | TEXT PK | Setting key |
| `Value` | TEXT NOT NULL | Typed/validated value |
| `UpdatedAtUtc` | TEXT NOT NULL | UTC |

Sensitive secrets should use Windows-protected storage rather than plaintext settings.

### `ExcludedPatterns`
| Column | Type | Notes |
|---|---|---|
| `Id` | INTEGER PK | Row ID |
| `Pattern` | TEXT NOT NULL UNIQUE | Document matching syntax |
| `CreatedAtUtc` | TEXT NOT NULL | UTC |
| `Enabled` | INTEGER NOT NULL DEFAULT 1 | Boolean |

If storing exclusion patterns could itself reveal sensitive site usage, keep this table local and include it in deletion/export policy as appropriate.

## 4. Indexes

Recommended:
- `BrowsingSessions(StartedAtUtc)`
- `BrowsingSessions(NormalizedDomain, StartedAtUtc)`
- `BrowsingSessions(BrowserInstanceId, StartedAtUtc)`
- `BrowsingSessions(EndedAtUtc)`
- `ProcessSamples(SampledAtUtc)`
- `ProcessSamples(ProcessId, ProcessStartIdentity, SampledAtUtc)`

## 5. Aggregations

Compute daily totals using local-day boundaries converted to UTC boundaries, or group in application logic using a timezone-aware approach. Do not group by the date portion of UTC when the requirement is “today” in local time.

Top-domain total:
- Sum `ForegroundDurationMs` over sessions within the selected date range.
- Exclude incomplete or invalid sessions.
- Avoid double counting if the same tab/window receives duplicate events.

## 6. Retention and deletion

- Default browsing-session retention: 30 days.
- User-configurable retention.
- Process-sample retention can be shorter; document the chosen default.
- Deleting sessions must also remove derived aggregates/caches if present.
- Delete all browsing history must not leave hidden copies in app-specific caches or exports managed by the app.
- Database vacuum/compaction may be offered after bulk deletion, but should not block the UI.

## 7. Migration policy

- Store schema version.
- Migrations run transactionally.
- Never silently drop user history during an upgrade.
- Back up or fail safely if a migration cannot complete.
- Test fresh install and upgrade from each supported schema version.
