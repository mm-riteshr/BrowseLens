# BrowseLens — Local API / Messaging Contract

## 1. Purpose

Define the contract between the browser extension and desktop companion. The exact transport is a technical decision gated by feasibility and security validation. This document specifies the logical envelope and event payloads independent of transport.

## 2. Transport requirements

- Prefer a secure, supported local IPC design compatible with browser extension constraints.
- If HTTP is used, bind only to loopback and authenticate messages.
- Do not bind to all network interfaces.
- Do not treat CORS or `Origin` validation as the only authentication.
- Do not hard-code a shared secret into source code.
- Document how extension authentication material is provisioned and protected.
- Reject malformed or unauthenticated messages.
- Do not expose general-purpose filesystem, shell, or process-control operations.

## 3. Versioned envelope

Logical example:

```json
{
  "schemaVersion": 1,
  "eventId": "uuid",
  "sequence": 42,
  "browser": "Chrome",
  "eventType": "TabActivated",
  "occurredAtUtc": "2026-10-26T12:30:00.000Z",
  "windowKey": "session-window-key",
  "tabKey": "session-tab-key",
  "payload": {
    "url": "https://example.com/path",
    "title": "Example page",
    "isWindowFocused": true
  }
}
```

This is a logical example, not a fixed transport implementation. Browser-specific IDs must be treated as session-scoped identifiers. Do not log the example or actual sensitive URL payloads.

## 4. Supported event types

- `Hello` — extension connection/handshake.
- `Heartbeat` — connection health, no browsing content.
- `TabActivated` — selected tab changed.
- `TabUpdated` — URL/title/loading state changed.
- `NavigationCommitted` — navigation committed where exposed.
- `TabRemoved` — tab closed.
- `WindowFocusChanged` — browser window gained/lost focus where exposed.
- `BrowserStateChanged` — extension/browser lifecycle status.
- `TrackingStateChanged` — pause/resume state if sent by the app/extension protocol.

Do not assume every event is delivered exactly once or in order.

## 5. Validation rules

- `schemaVersion` must be supported.
- `eventId` must be a valid unique event identifier; use it for bounded deduplication.
- `sequence` is monotonic per extension instance when feasible.
- Timestamp must parse as UTC and fall within an allowed skew window; document the threshold.
- `eventType` must be allowlisted.
- Payload must match the event type schema.
- URL schemes must be allowlisted for tracking, normally `http` and `https`.
- Reject oversized payloads and unexpected fields where strict validation is practical.
- Do not persist `chrome://`, `edge://`, extension-internal or other restricted pages as normal web sessions.
- Apply pause/exclusion/redaction policy before persistence.
- Treat all fields as untrusted, even if sent by an installed extension.

## 6. Logical acknowledgements and errors

The desktop side should return an acknowledgement containing:
- `schemaVersion`
- `eventId`
- `accepted` boolean
- Optional safe error code

Suggested error codes:
- `UNSUPPORTED_SCHEMA`
- `UNAUTHENTICATED`
- `INVALID_PAYLOAD`
- `UNSUPPORTED_EVENT`
- `RATE_LIMITED`
- `TRACKING_PAUSED`
- `INTERNAL_ERROR`

Do not include secrets, full URLs, titles, or stack traces in responses.

## 7. Reliability behavior

- Use a bounded retry queue in the extension.
- Avoid unbounded local buffering.
- Deduplicate events by event ID/sequence where possible.
- Do not replay old navigation events in a way that inflates current foreground time.
- If connection is lost, show disconnected state and stop claiming collection is live.
- On reconnect, send current state as a reconciliation snapshot; do not backfill unobserved time.
