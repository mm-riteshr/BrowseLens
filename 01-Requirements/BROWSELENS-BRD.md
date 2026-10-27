# BrowseLens — Business Requirements Document (BRD)

- **Document ID:** BL-BRD-001
- **Version:** 1.0
- **Status:** Baseline for MVP planning
- **Product:** BrowseLens
- **Internal code name:** Project-1
- **Platform:** Windows 11

## 1. Executive summary

BrowseLens is a local-first Windows desktop utility that gives an individual user visibility into browser resource consumption and historical browsing usage. The MVP combines browser activity metadata with Windows process-level performance metrics in one interface.

The goal is not to create a full surveillance or enterprise endpoint product. It is to help the user understand which browser is consuming resources and how time is distributed across websites.

## 2. Business problem

Users commonly have many tabs open and may experience slowdowns or lose track of where browsing time goes. Windows Task Manager can show process-level resource consumption but does not inherently explain the website or browsing context responsible for a given process. Browser history records navigation but does not normally provide an integrated foreground-time and resource overview.

BrowseLens aims to bridge this gap while avoiding unsupported claims about tab-level or extension-level resource attribution.

## 3. Vision and value proposition

**Vision:** Give users a clear, trustworthy view of browser activity, resource consumption, and browsing time from one Windows application.

**Value proposition:** Understand browser performance and website usage without sending browsing history to a cloud service.

## 4. Target user and usage context

- Initial user: an individual Windows 11 user.
- Technical comfort: able to install a desktop application and browser extension using guided instructions.
- Typical context: multiple tabs open during work, research, communication, development, or entertainment.
- Future audiences are not in the MVP scope.

## 5. Business objectives

- **BO-001:** Provide one dashboard for supported-browser status, process-level CPU/RAM, current tab, and usage summary.
- **BO-002:** Build a persistent, searchable history of supported-browser foreground sessions.
- **BO-003:** Make usage duration understandable and avoid double counting.
- **BO-004:** Keep collected activity data local and user-controlled.
- **BO-005:** Establish a modular foundation for future browser integrations and diagnostics.

## 6. Success measures

The MVP is successful when:
1. A user can follow the setup guide and connect a supported browser.
2. Active-tab URL/title events are recorded reliably for supported normal browsing windows.
3. Foreground durations remain correct across tab switches, navigation, focus loss, pause/resume, and restarts.
4. Browser process CPU and memory are displayed with definitions and sampling timestamps.
5. The dashboard can show today's total foreground time and top domains.
6. The user can pause collection, exclude domains, redact URL parameters, and delete history.
7. Core tests pass and no known critical privacy or data-integrity defect remains.
8. The product does not upload browsing data or add undisclosed telemetry.

## 7. Scope

### In scope
- Windows 11 desktop app.
- Chromium Manifest V3 extension.
- Chrome-first integration; Edge support after feasibility validation.
- Active-tab metadata and foreground session tracking.
- Browser process-level CPU and memory monitoring.
- Local SQLite storage.
- Overview and history UI.
- Pause/resume and privacy controls.
- Retention, deletion, basic diagnostics, and documentation.

### Out of scope
- Accurate individual tab CPU/memory attribution.
- Accurate individual extension CPU/memory attribution.
- Browser content capture, screenshots, keystrokes, clipboard, form data, or credentials.
- Incognito/private tracking; disabled by default.
- Cloud services, user accounts, synchronization, team features, employee monitoring, or remote administration.
- Website blocking, AI productivity scoring, and mobile support.
- Kernel drivers or invasive process instrumentation.

## 8. Business requirements

| ID | Requirement | Priority | Measure of success |
|---|---|---|---|
| BR-001 | Provide a unified browser usage and process-resource overview | Must | Overview shows supported browser status, process CPU/RAM, current tab and usage summary |
| BR-002 | Track foreground browsing sessions | Must | Session intervals persist and aggregate without double counting |
| BR-003 | Provide historical website usage summaries | Must | User can inspect daily totals and top domains |
| BR-004 | Keep data local by default | Must | No cloud dependency or product telemetry; database stored locally |
| BR-005 | Give the user control over collection | Must | Pause/resume is visible and effective |
| BR-006 | Provide privacy controls | Must | Domain exclusions, URL redaction, retention, and deletion work |
| BR-007 | Communicate measurement limitations honestly | Must | UI/docs distinguish process-level from tab-level data and unknown values |
| BR-008 | Keep the MVP maintainable | Should | Clear separation between UI, domain logic, browser integration, process metrics and persistence |
| BR-009 | Make activity exportable | Could | CSV export may be included if it does not delay core MVP |

## 9. Assumptions

- The first deployment is for a single local Windows user.
- Browser extension installation is acceptable.
- A browser extension can observe relevant tab/navigation events with supported permissions.
- The desktop app can obtain Windows process metrics through supported OS APIs.
- The exact local IPC mechanism is subject to technical feasibility and security review.
- Browser-specific API behavior can differ; support claims must be based on tests.
- The user is in control of the machine and explicitly chooses to enable tracking.

## 10. Constraints

- Windows 11 is the target operating system.
- Data must remain local.
- The product must not use stealth collection.
- Extension permissions must follow least privilege.
- The UI must not imply that a person was attentive merely because a tab was foreground.
- Resource attribution must not be more granular than the underlying measurements support.

## 11. Risks and mitigations

| Risk | Business impact | Mitigation |
|---|---|---|
| Process-to-tab attribution is unreliable | Misleading results | Only report process-level metrics in v1 |
| Extension disconnects or browser closes unexpectedly | Inaccurate time totals | Checkpointing and session recovery |
| Full URLs contain sensitive information | Privacy exposure | Redaction, exclusions, retention, deletion |
| Too much scope delays the MVP | Slow feedback | Phase delivery and strict out-of-scope list |
| Local messaging is insecure | Local data exposure | Prefer secure IPC; otherwise loopback-only authenticated protocol |

## 12. Acceptance and approval

The BRD baseline is accepted when the product owner agrees with the objectives, scope, and explicit exclusions. A change to core privacy or data collection behavior requires a documented decision and updated requirements.
