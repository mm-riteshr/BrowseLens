# BrowseLens — Coding Agent Instructions

**This is the primary execution instruction file for any AI coding agent working on BrowseLens.**

## 1. Mandatory reading order

Before modifying code, read:
1. `README.md`
2. `01-Requirements/BROWSELENS-BRD.md`
3. `01-Requirements/BROWSELENS-FRD.md`
4. `02-Technical/BROWSELENS-TRD.md`
5. `05-Architecture/ARCHITECTURE.md`
6. `06-Database/DATABASE-DESIGN.md`
7. `07-API/API-CONTRACT.md`
8. `08-Testing/TEST-PLAN.md`
9. `03-Delivery/ROADMAP-AND-TRACEABILITY.md`
10. `03-Delivery/DECISIONS.md`

If the repository already contains code, inspect it before proposing structural changes. Preserve working behavior unless a documented requirement calls for a change.

## 2. Source of truth and conflict resolution

- BRD defines why and business boundaries.
- FRD defines user-visible behavior and acceptance criteria.
- TRD defines technical constraints.
- Architecture, database and API documents define implementation contracts.
- Decisions log records approved changes.
- Tests prove behavior.
- If documents conflict, do not guess silently. Prefer privacy/security constraints first, then approved decisions, then FRD/BRD scope. Record the conflict and propose a resolution in `DECISIONS.md`.
- Do not silently expand Version 1 scope.

## 3. Required execution workflow

### Step A — Inspect and report
- Inspect files, SDK versions, git status, existing tests, and build configuration.
- Report a concise implementation plan, risks, and unresolved technical assumptions.
- Do not overwrite or delete existing user work.

### Step B — Feasibility spike
Before building the full UI, create a minimal proof of concept for:
- Browser tab activation and navigation events.
- Secure extension-to-desktop communication.
- Windows process CPU and memory sampling.
- Connection/reconnection behavior.
- Chrome installation steps.
Document findings in `09-Operations/TECHNICAL-FEASIBILITY.md`.

Do not claim success until demonstrated. If secure local communication is blocked by browser constraints, stop and propose alternatives.

### Step C — Implement in vertical slices
Build one end-to-end capability at a time:
1. Core domain models and session accounting.
2. SQLite persistence and migrations.
3. Browser event ingestion and connection state.
4. Process metrics provider.
5. Overview dashboard.
6. History and aggregation.
7. Privacy settings and deletion.
8. Packaging and documentation.

Keep the app buildable after each slice.

### Step D — Test
- Add tests alongside each feature.
- Run unit tests after domain changes.
- Run integration tests after persistence/API changes.
- Perform manual browser and Task Manager validation before release.
- Fix regressions before proceeding.

### Step E — Update docs
For every behavior or architecture change:
- Update the relevant requirement or decision.
- Update API/database docs if contracts change.
- Add/update tests.
- Update traceability status.
- Update release notes.

## 4. Coding standards

- Prefer simple, explicit, maintainable code.
- Use nullable reference types and typed DTOs in .NET where applicable.
- Use cancellation and bounded queues for background loops.
- Avoid blocking UI-thread operations.
- Avoid unnecessary frameworks, dependencies, services, and abstractions.
- Separate domain logic from Windows/browser-specific code.
- Use parameterized SQL and transactions.
- Validate all external inputs.
- Use UTC timestamps and a clear duration model.
- Use structured logging; never log full URLs/title by default.
- Add comments for non-obvious platform behavior, not for every line.
- Keep secrets out of source control.
- Provide a `.gitignore` covering local data, databases, logs, secrets and build outputs.

## 5. Non-negotiable product constraints

- Local-first; no cloud upload or telemetry.
- No stealth or covert monitoring.
- No capture of page content, keystrokes, passwords, clipboard, screenshots or form values.
- Incognito/private browsing tracking disabled by default.
- No inaccurate attribution of process CPU/RAM to individual tabs or extensions.
- `Unavailable` and `Unknown` are not `0`.
- Visible pause/resume and working privacy controls.
- Excluded sites must not have URL/title/session records persisted.
- No unauthenticated local network listener.

## 6. Scope discipline

Version 1 must prioritize:
- Chrome-first browser event capture.
- Foreground duration accounting.
- Browser process CPU/RAM.
- SQLite history.
- Overview and history views.
- Pause/resume, exclusions, redaction, retention and deletion.

Do not implement future-version features just because they seem attractive. Add them to the roadmap instead.

## 7. Required repository organization

Maintain the folder structure described in `README.md`. Place:
- UI code under `src/BrowseLens.Desktop/`.
- Domain logic under `src/BrowseLens.Core/`.
- OS/process/database/IPC adapters under `src/BrowseLens.Infrastructure/`.
- Extension code under `src/BrowseLens.BrowserExtension/`.
- Unit tests under `tests/BrowseLens.UnitTests/`.
- Integration tests under `tests/BrowseLens.IntegrationTests/`.
- Schema/migration scripts and design notes under `06-Database/` or the agreed database project location.
- API DTOs/contracts under the agreed source project and document them in `07-API/`.
- Utility scripts under `scripts/`.

Do not store runtime user browsing data inside the repository; use the local application data directory.

## 8. Definition of done for a feature

A feature is done only when:
- Requirement is implemented.
- Acceptance criteria are met.
- Relevant tests exist and pass.
- Error and privacy cases are handled.
- Documentation and traceability are updated.
- UI and logs do not overstate capability.
- No unrelated behavior was broken.

## 9. Reporting format

At the end of each implementation increment, report:
1. What changed.
2. Requirement IDs addressed.
3. Files created/modified.
4. Tests run and results.
5. Manual validation still required.
6. Known limitations and risks.
7. Next smallest useful increment.

Do not say a feature is complete if only a mockup or stub exists. Mark it `Partial` and explain what remains.

## 10. First task

Start with repository inspection and the technical feasibility spike. Do not immediately generate the entire application. Prove the riskiest integration path, document findings, then proceed with a vertical slice.
