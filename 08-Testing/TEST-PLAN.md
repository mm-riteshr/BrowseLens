# BrowseLens — Test Plan

## 1. Test levels

- Unit tests: deterministic core logic, duration accounting, URL policies, aggregation.
- Integration tests: SQLite, migrations, message validation, retention and deletion.
- Manual validation: real browser, Windows process metrics, focus/lock/sleep behavior.
- Release smoke tests: clean install, setup, launch, browser connect, pause, history, delete, uninstall.

## 2. Unit test cases

| ID | Scenario | Expected result |
|---|---|---|
| TC-001 | CPU delta calculation | Correct normalized percentage using documented formula |
| TC-002 | Process disappears between samples | Missing sample marked unavailable/exited, not zero |
| TC-003 | PID reused by another process | Process identity prevents combining unrelated samples |
| TC-004 | First tab becomes active | One foreground interval starts |
| TC-005 | Switch from tab A to tab B | A closes and B opens at transition; no overlap |
| TC-006 | Navigate in same tab | Old URL interval closes and new URL interval begins according to chosen policy |
| TC-007 | Browser window loses focus | Foreground interval closes/pauses |
| TC-008 | Browser window regains focus | Current selected tab resumes a new interval |
| TC-009 | Tab closes | Interval closes at last reliable event time |
| TC-010 | Duplicate event | Duration is not double-counted |
| TC-011 | Out-of-order event | Event is rejected or reconciled deterministically |
| TC-012 | App restarts with open session | Unobserved downtime is not counted |
| TC-013 | Domain aggregation | Durations sum correctly by normalized domain |
| TC-014 | Local-day boundary | “Today” uses local timezone boundaries |
| TC-015 | Excluded domain | URL/title/session data is not persisted |
| TC-016 | URL redaction enabled | Query and fragment are removed from stored URL |
| TC-017 | Pause tracking | No session time or browsing metadata is captured while paused |
| TC-018 | Resume tracking | New interval starts; paused gap excluded |
| TC-019 | Retention cutoff | Expired sessions are deleted |
| TC-020 | Delete date range | Matching sessions and derived data removed |
| TC-021 | Delete all history | Browsing history is fully removed |
| TC-022 | Invalid/restricted URL | No normal website session is persisted |
| TC-023 | Missing title | Event is handled without crashing |
| TC-024 | Metric unavailable | UI model preserves unavailable/null state, not numeric zero |
| TC-025 | Metric label | Scope and units are present |
| TC-026 | CSV export if implemented | Export matches selected filter and respects redaction |
| TC-027 | Multi-tab realistic user journey | 5+ tabs open, sequential switching, background tab closure, in-page navigation, and domain aggregation |

## 3. Integration tests

- Fresh database initialization and migration.
- Upgrade from prior schema version.
- Session transaction consistency.
- Extension event envelope validation.
- Authentication rejection.
- Oversized payload rejection.
- Unsupported schema/event rejection.
- Bounded deduplication.
- Retention and deletion including derived data.
- App restart and browser reconnection.
- Verify local listener binds only to loopback if HTTP is used.

## 4. Manual validation checklist

- [ ] Install extension using documented instructions.
- [ ] Open multiple tabs and switch rapidly.
- [ ] Navigate within one tab.
- [ ] Minimize or unfocus browser.
- [ ] Lock/unlock Windows.
- [ ] Put machine to sleep and resume.
- [ ] Close/reopen browser.
- [ ] Close/reopen BrowseLens.
- [ ] Pause, browse, resume, and confirm paused time is excluded.
- [ ] Exclude a domain and verify URL/title is not persisted.
- [ ] Enable redaction and verify query/fragment removal.
- [ ] Verify private/incognito activity is not captured by default.
- [ ] Compare process metrics with Task Manager and document definition differences.
- [ ] Confirm no outbound telemetry or upload from BrowseLens.
- [ ] Test delete session/date range/all.
- [ ] Confirm app stays responsive with a large history dataset.

## 5. Release gate

No release until all Must functional requirements are tested, no known critical privacy/security or data-integrity defect remains, and all limitations are documented.
