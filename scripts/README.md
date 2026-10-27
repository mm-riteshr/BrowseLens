# Automation Scripts

Reproducible PowerShell automation scripts for building, testing, launching, and publishing BrowseLens:

- `build-and-test.ps1`: Automated build and test script. Detects .NET 10 SDK and runs full compilation followed by all 7 unit test suites and 11 integration test suites.
- `publish-release.ps1`: Release packaging pipeline. Runs the full test suite and packages self-contained binaries and unpacked browser extension into `dist/BrowseLens/`.
- `run-poc.ps1`: Quick launch script for starting the desktop companion application.
