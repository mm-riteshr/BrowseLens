# Test Suites & Quality Assurance

BrowseLens maintains a deterministic, automated test suite with a 100% pass rate:

- `BrowseLens.UnitTests/`: Automated unit tests covering core domain components:
  1. CPU sample delta & core count normalization (TC-001, TC-002, TC-003, TC-025)
  2. Browser event envelope validation & payload extraction
  3. Session state machine & time accounting (TC-004 to TC-011, TC-017, TC-018, multi-tab lifecycles)
  4. Privacy engine, URL redaction, and domain exclusion patterns (TC-015, TC-016, TC-022)
  5. 15-minute sliding-window process metrics aggregator
  6. WPF ViewModels & presentation logic
  7. Automated retention cleanup and privacy-preserving diagnostic logger

- `BrowseLens.IntegrationTests/`: Live SQLite WAL persistence tests:
  1. Database initialization, migrations, and idempotency
  2. Transactional session persistence & status tracking
  3. Session crash recovery (TC-012)
  4. Top domains & duration aggregation (TC-013)
  5. Process samples batch insertion & expiration purge
  6. Settings & exclusion pattern management
  7. Retention cleanup & history deletion (TC-019, TC-021)
  8. Monitoring coordinator end-to-end event stream & heartbeat timeouts
  9. Background process sampling scheduler & batch DB flush
  10. Automated retention cleanup service with live SQLite DB
  11. Realistic multi-tab browsing lifecycle & SQLite domain attribution

### Running Tests
Execute from PowerShell in the repository root:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-and-test.ps1
```
