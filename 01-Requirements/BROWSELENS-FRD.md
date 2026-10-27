# BrowseLens — Functional Requirements Document (FRD)

- **Document ID:** BL-FRD-001
- **Version:** 1.0
- **Status:** Baseline for MVP implementation
- **Related:** `BROWSELENS-BRD.md`, `BROWSELENS-TRD.md`

## 1. Purpose

Define testable user-visible behavior for BrowseLens v1. Each requirement has an ID used by implementation, test cases, and traceability.

## 2. Personas and primary scenarios

### Persona: Individual Windows user
Wants to understand browser resource usage and website time without uploading browsing history.

### Primary scenarios
1. Launch BrowseLens and see whether browser tracking is connected.
2. Browse normally and have active-tab metadata collected.
3. Review the current site, process-level CPU/RAM, and today's usage.
4. Search historical sessions.
5. Pause tracking before sensitive browsing.
6. Exclude selected domains and redact URL query strings/fragments.
7. Delete historical data and configure retention.

## 3. Requirement priorities
- **Must:** Required for Version 1 acceptance.
- **Should:** Include if it does not compromise Must requirements.
- **Could:** Optional; defer if implementation risk or time is high.

## 4. Functional requirements

### FR-001 — Connection status (Must)
The UI shall show `Connected`, `Disconnected`, `Paused`, or `Error` for each configured browser integration.
- A stale connection must not be shown as connected indefinitely.
- The UI shall provide setup/reconnect guidance.
- Empty state explains how to install/enable the extension.

**Acceptance:** Disconnect the extension; UI changes status within a defined timeout and does not claim new activity is being captured.

### FR-002 — Active tab metadata (Must)
Capture, when exposed by the browser API:
- Browser type.
- Session-scoped tab/window identifiers.
- URL and page title.
- Event type and UTC timestamp.
- Active-tab state and browser-window focus state where available.
- Navigation, tab activation, tab creation/removal, window focus and browser lifecycle events.

Do not capture page DOM/content or inject scripts into websites.
**Acceptance:** Switching tabs closes the previous foreground interval and starts the new interval; same-tab navigation updates the URL without creating overlapping intervals.

### FR-003 — Foreground time accounting (Must)
Calculate time during which a tab is selected in a focused browser window.
- Do not count time while browser-window focus is known to be lost.
- Do not count paused intervals.
- Store UTC timestamps and display local time.
- Use monotonic elapsed-time measurement where practical.
- Recover open sessions after a crash/restart without inventing unobserved time.
- The UI must call the metric `Foreground time`, not `Attention time` or a claim of human engagement.

**Acceptance:** Tests cover switching, navigation, focus loss, closure, pause/resume, duplicate/out-of-order events, and restart recovery. Durations cannot be negative or overlap for a given browser window.

### FR-004 — Process-level CPU (Must)
Display CPU utilization for browser processes where reliable Windows counters are available.
- Define normalization, e.g. percentage of total logical-processor capacity.
- Calculate utilization from CPU-time deltas and elapsed time.
- Identify process instances robustly enough to avoid PID-reuse confusion.
- Display sample timestamp and process ID.
- If measurement is unavailable, show `Unavailable`, not `0`.

**Acceptance:** Values update on a stable sampling interval and a documented manual comparison with Task Manager is completed.

### FR-005 — Process-level memory (Must)
Display a clearly defined memory metric for browser processes, such as working set.
- Label the chosen metric and units.
- Distinguish per-process values from total browser aggregate.
- Aggregate only processes that can be confidently associated with the browser.
- Never allocate a process's memory to an individual tab in v1.

**Acceptance:** The UI identifies the metric definition and avoids duplicate process aggregation.

### FR-006 — Local browsing history (Must)
Persist foreground sessions with browser, session-scoped tab key, URL subject to privacy settings, normalized domain, page title subject to settings, start/end UTC timestamps, duration, and end reason.
- Handle restricted/internal URLs safely.
- Avoid duplicate records from repeated events.
- Support session recovery after application restart.
- Never persist excluded URL/title data.

**Acceptance:** History remains available after restart and totals match deterministic test event sequences.

### FR-007 — Overview dashboard (Must)
Display:
- Integration connection state.
- Current active website/title when available.
- Process-level CPU and memory.
- Total foreground time today.
- Top domains by foreground time today.
- Recent sessions.
- Visible pause/resume state.

**Acceptance:** UI distinguishes live, historical, calculated, unknown, and unavailable values.

### FR-008 — History screen (Must)
Provide:
- Date range filter.
- Browser filter.
- Domain grouping.
- Search by title, domain and URL, subject to redaction.
- Sorting by duration, most recent, or visit count.
- Session details.
- CSV export is a Should requirement; defer if it threatens core scope.

### FR-009 — Pause/resume (Must)
Provide an obvious tracking toggle.
- When paused, do not persist new browsing URL/title/session data.
- Do not backfill the paused period.
- State must be visible in the UI.
- Default behavior pauses both browsing collection and historical capture.
- Resume starts a new valid interval; never add the paused gap to a session.

### FR-010 — Privacy settings (Must)
Provide:
- Excluded domains/patterns.
- URL query-string and fragment redaction.
- Configurable retention period, default 30 days.
- Delete one session, a date range, or all history.
- A plain-language explanation of what is collected and where it is stored.
- Incognito/private browsing collection disabled by default and not enabled by the MVP.

Excluded sites must not persist URL/title records. Match exclusions in memory when possible and avoid writing excluded sensitive URLs to logs.

### FR-011 — Retention (Must)
Automatically remove expired browsing sessions and associated derived data according to the configured retention period. Document whether process samples have a shorter retention period or are aggregated.
**Acceptance:** Tests verify the cutoff and that related data is deleted consistently.

### FR-012 — Diagnostics (Must)
Provide useful local diagnostic logs for errors, connection lifecycle and unexpected states.
- Do not log full URLs, titles, or browsing contents by default.
- Validate all extension messages.
- Do not block the UI during database or sampling operations.
- No outbound telemetry.

### FR-013 — Browser support (Must for Chrome; Edge conditional)
Chrome is the first supported browser. Edge is supported only after the extension installation and event behavior are validated.
- Browser support must be documented per tested browser/version.
- Do not claim universal Chromium support based solely on browser family.

### FR-014 — Accessibility and error states (Should)
Keyboard-accessible primary actions, readable contrast, clear empty/loading/error states, and tooltips for metric definitions.

## 5. Business rules

- **BRL-001:** Storage timestamps are UTC.
- **BRL-002:** Display time is converted to local time.
- **BRL-003:** Foreground time requires active tab plus focused browser window.
- **BRL-004:** A single browser window cannot have overlapping foreground intervals.
- **BRL-005:** Paused periods never contribute to usage totals.
- **BRL-006:** Excluded sites do not persist browsing URL/title/session history.
- **BRL-007:** Unknown is not equivalent to zero.
- **BRL-008:** Browser process metrics are not tab metrics.
- **BRL-009:** Incognito/private activity is not collected by default.
- **BRL-010:** Retention and deletion apply to derived data as well as primary records.

## 6. Error and edge cases

- Extension unavailable or permissions removed.
- Browser closed without final event.
- App closes/crashes while a session is open.
- Duplicate and out-of-order extension events.
- Invalid URL or restricted browser page.
- Title missing or delayed.
- PID reuse or process exit between samples.
- System sleep, resume, lock and unlock.
- Time-zone change or daylight-saving transition.
- Database locked/full/corrupt.
- User pauses tracking during an open interval.
- Exclusion rules changed while a session is open.
- Very large history dataset.

## 7. Acceptance criteria for v1

Version 1 passes when all Must requirements are implemented and tested, no critical privacy/data-integrity issues remain, and the README provides a reproducible setup path. Any partial Must requirement must be explicitly listed as a release blocker or accepted limitation by the product owner.
