# Contributing to BrowseLens

Thank you for your interest in contributing to BrowseLens! BrowseLens is an open-source, local-first browser observability tool and Windows companion designed to provide transparent browsing time and resource insights without compromising user privacy.

---

## 1. Core Principles & Privacy Boundaries

When contributing code, you must adhere strictly to BrowseLens's architectural principles:
- **Local-First & Zero Telemetry:** BrowseLens never uploads browsing activity, URLs, titles, or metrics to external servers. All persistence occurs in the local SQLite database (`%LOCALAPPDATA%/BrowseLens/browselens.db`).
- **No Keystroke / Content Capture:** Never inspect page DOM, form field entries, passwords, screenshots, or clipboard data. Only tab-level lifecycle events (`TabActivated`, `TabUpdated`, `TabRemoved`, `WindowFocusChanged`) are permitted.
- **Accurate Resource Metrics:** Process-level metrics reflect browser process tree statistics (Chrome/Edge). Never claim per-tab or per-extension CPU/RAM attribution since OS and browser architectures share process heaps.
- **Explicit Inactivity Clamping:** Unobserved downtime (crashes, OS lock, machine sleep) must never be counted as foreground browsing time.

---

## 2. Getting Started

### Prerequisites
- **Operating System:** Windows 10/11 x64
- **Runtime & SDK:** [.NET 10 SDK](https://dotnet.microsoft.com/download) (`v10.0.403` or later)
- **Browser:** Google Chrome (v120+) or Microsoft Edge (v120+)

### Building and Testing
Before submitting changes or pull requests, verify that the automated test suite passes completely:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-and-test.ps1
```

All 7 unit test suites and 11 integration test suites must pass with a 100% pass rate.

### Packaging a Release
To package a standalone executable and extension bundle:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1
```

---

## 3. Pull Request Guidelines

1. **Keep Pull Requests Focused:** Submit PRs that address a single bug fix, feature, or documentation enhancement.
2. **Add Unit/Integration Tests:** Any modifications to session accounting, deduplication, URL redaction, or IPC framing must include corresponding automated test cases.
3. **Maintain Coding Standards:**
   - Preserve nullable reference type annotations (`#nullable enable`).
   - Use clean asynchronous patterns (`async`/`await` with proper cancellation token passing where applicable).
   - Ensure IPC sockets bind strictly to the loopback interface (`127.0.0.1`).
4. **Update Documentation:** If your change modifies user-facing behavior, update the relevant engineering docs (`01-Requirements` through `09-Operations`).

---

## 4. Reporting Issues

If you discover a security vulnerability or bug:
1. Open a GitHub Issue detailing the steps to reproduce, expected vs. observed behavior, and system environment (.NET version, Windows build, browser version).
2. For sensitive security findings, please disclose responsibly via private issue channels.

