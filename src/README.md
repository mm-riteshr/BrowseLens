# Source Code Architecture

BrowseLens follows a clean layered architecture with strict separation of concerns:

- `BrowseLens.Desktop/`: Native Windows desktop application targeting .NET 10 (WPF) with full MVVM architecture (Views, ViewModels, RelayCommands), custom styling, and headless console fallback mode.
- `BrowseLens.Core/`: Pure domain layer containing entities, session accounting models, `SessionStateMachine`, `MonitoringCoordinator`, `UrlRedactor`, `ExclusionPolicy`, and `ProcessMetricsAggregator`. Has no external UI or database dependencies.
- `BrowseLens.Infrastructure/`: Infrastructure implementations including `DatabaseMigrator`, SQLite repositories (`SqliteBrowsingSessionRepository`, `SqliteProcessSampleRepository`, `SqliteSettingsRepository`), loopback HTTP/1.1 IPC server (`LocalHttpEventServer`), Windows process performance sampler (`WindowsProcessMetricsProvider`), diagnostic logging (`LocalDiagnosticLogger`), and automated retention cleanup (`RetentionCleanupService`).
- `BrowseLens.BrowserExtension/`: Lightweight Chromium Manifest V3 companion extension for Google Chrome and Microsoft Edge with bounded offline queueing, exponential backoff reconnection, and connection token authentication.
