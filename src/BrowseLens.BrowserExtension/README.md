# BrowseLens Companion Extension

Chromium Manifest V3 extension providing active tab metadata to the local BrowseLens desktop companion.

## Permissions requested

- `tabs`: Required to observe active tab changes, URL, and page title.
- `storage`: Stores local connection settings (server port and authentication token).
- `host_permissions: ["http://127.0.0.1/*"]`: Allows local loopback HTTP communication with the desktop companion.

## Installation in Google Chrome

1. Open Google Chrome.
2. Navigate to `chrome://extensions`.
3. Enable **Developer mode** toggle in the top-right corner.
4. Click **Load unpacked**.
5. Select the extension directory: `src/BrowseLens.BrowserExtension` (or `dist/BrowseLens/extension` from a release package).
6. Open the BrowseLens Desktop Companion application to obtain the local authentication token.
7. Click the BrowseLens Companion extension icon in Chrome, select **Configure Connection Token**, paste the token, and click **Save Settings**.
8. Click **Test Connection** to confirm connectivity.

## Installation in Microsoft Edge

1. Open Microsoft Edge.
2. Navigate to `edge://extensions`.
3. Enable **Developer mode** in the left sidebar menu.
4. Click **Load unpacked**.
5. Select the extension directory: `src/BrowseLens.BrowserExtension` (or `dist/BrowseLens/extension` from a release package).
6. Configure the authentication token in the extension options as described above.

## Privacy and Boundaries

- Incognito activity is strictly filtered out and never transmitted.
- Only active tab metadata (URL and Title) is observed; page DOM, inputs, passwords, and form data are never accessed.
- All communication is bound strictly to `127.0.0.1` and never transmitted across the network or internet.

