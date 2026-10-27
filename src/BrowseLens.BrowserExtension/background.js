// BrowseLens Companion - Background Service Worker (Manifest V3 - Resilient)

const DEFAULT_SERVER_URL = "http://127.0.0.1:58432";
const MAX_QUEUE_SIZE = 100;
const HEARTBEAT_INTERVAL_MS = 25000;

let sequence = 0;
let connectionState = "Disconnected";
let offlineQueue = [];
let isFlushingQueue = false;
let lastEventSentTime = Date.now();
let reconnectTimer = null;
let reconnectDelayMs = 2000;

// Restore persistent sequence across MV3 service worker restarts
chrome.storage.local.get(["sequence"], (items) => {
  if (items && typeof items.sequence === "number") {
    sequence = Math.max(sequence, items.sequence);
  }
});

function getNextSequence() {
  sequence++;
  chrome.storage.local.set({ sequence: sequence });
  return sequence;
}

// Detect browser family
const isEdge = navigator.userAgent.includes("Edg/");
const browserName = isEdge ? "Edge" : "Chrome";

async function getConfig() {
  return new Promise((resolve) => {
    chrome.storage.local.get(["serverUrl", "authToken"], (items) => {
      resolve({
        serverUrl: items.serverUrl || DEFAULT_SERVER_URL,
        authToken: items.authToken || ""
      });
    });
  });
}

function generateEventId() {
  return "evt_" + Date.now().toString(36) + "_" + Math.random().toString(36).substring(2, 9);
}

function updateBadge(text, color) {
  if (chrome.action && chrome.action.setBadgeText) {
    chrome.action.setBadgeText({ text: text });
    chrome.action.setBadgeBackgroundColor({ color: color });
  }
}

// Bounded event dispatcher with retry and offline queue
async function queueOrSendEvent(eventType, windowId, tabId, payload) {
  const config = await getConfig();
  if (!config.authToken) {
    connectionState = "Unconfigured";
    updateBadge("ERR", "#d9534f");
    return;
  }

  const seq = getNextSequence();
  const envelope = {
    schemaVersion: 1,
    eventId: generateEventId(),
    sequence: seq,
    browser: browserName,
    eventType: eventType,
    occurredAtUtc: new Date().toISOString(),
    windowKey: "w-" + (windowId || 0),
    tabKey: "t-" + (tabId || 0),
    payload: payload || {}
  };

  // If there are already queued events, preserve FIFO order by enqueuing and flushing
  if (offlineQueue.length > 0) {
    enqueueEvent(envelope);
    await flushOfflineQueue();
    return;
  }

  // Attempt direct transmission first
  const success = await transmitEnvelope(envelope, config);
  if (!success) {
    enqueueEvent(envelope);
    connectionState = "Disconnected";
    updateBadge(offlineQueue.length > 0 ? "QUE" : "DISC", "#f0ad4e");
    scheduleReconnect();
  }
}

function enqueueEvent(envelope) {
  if (offlineQueue.length >= MAX_QUEUE_SIZE) {
    // Drop oldest to enforce bounded buffer constraint (TRD 9)
    offlineQueue.shift();
  }
  offlineQueue.push(envelope);
}

async function transmitEnvelope(envelope, config) {
  try {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 4000);

    const response = await fetch(`${config.serverUrl}/api/v1/events`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-BrowseLens-Token": config.authToken
      },
      body: JSON.stringify(envelope),
      signal: controller.signal
    });
    clearTimeout(timeoutId);

    if (response.ok) {
      connectionState = "Connected";
      lastEventSentTime = Date.now();
      reconnectDelayMs = 2000; // Reset backoff
      updateBadge("OK", "#5cb85c");
      return true;
    } else {
      connectionState = "Error: " + response.status;
      updateBadge("ERR", "#d9534f");
      return false;
    }
  } catch (err) {
    return false;
  }
}

async function flushOfflineQueue() {
  if (isFlushingQueue || offlineQueue.length === 0) return;
  isFlushingQueue = true;

  const config = await getConfig();
  if (!config.authToken) {
    isFlushingQueue = false;
    return;
  }

  while (offlineQueue.length > 0) {
    const nextEnvelope = offlineQueue[0];
    const success = await transmitEnvelope(nextEnvelope, config);
    if (success) {
      offlineQueue.shift();
    } else {
      // Still unreachable; wait for reconnect cycle
      connectionState = "Disconnected";
      updateBadge("QUE", "#f0ad4e");
      scheduleReconnect();
      break;
    }
  }

  isFlushingQueue = false;
}

function scheduleReconnect() {
  if (reconnectTimer) return;

  reconnectTimer = setTimeout(async () => {
    reconnectTimer = null;
    const config = await getConfig();
    if (!config.authToken) return;

    if (offlineQueue.length > 0) {
      await flushOfflineQueue();
    } else {
      const probe = {
        schemaVersion: 1,
        eventId: generateEventId(),
        sequence: getNextSequence(),
        browser: browserName,
        eventType: "Heartbeat",
        occurredAtUtc: new Date().toISOString(),
        windowKey: "w-0",
        tabKey: "t-0",
        payload: {}
      };

      const connected = await transmitEnvelope(probe, config);
      if (!connected) {
        reconnectDelayMs = Math.min(reconnectDelayMs * 1.5, 30000);
        scheduleReconnect();
      }
    }
  }, reconnectDelayMs);
}

// Periodic heartbeat loop (runs every 25s when connected to keep link alive)
setInterval(async () => {
  if (connectionState === "Connected" && Date.now() - lastEventSentTime >= HEARTBEAT_INTERVAL_MS) {
    await queueOrSendEvent("Heartbeat", 0, 0, {});
  }
}, 10000);

// Helper: captures current active tab
async function captureCurrentActiveTab() {
  try {
    const tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
    if (tabs && tabs.length > 0) {
      const activeTab = tabs[0];
      if (activeTab.incognito) return;
      const rawUrl = activeTab.url || activeTab.pendingUrl || "";
      if (rawUrl) {
        await queueOrSendEvent("TabActivated", activeTab.windowId, activeTab.id, {
          url: rawUrl,
          title: activeTab.title || "",
          isWindowFocused: true,
          isIncognito: false
        });
      }
    }
  } catch (e) { }
}

// --- Browser Event Listeners ---

// 1. Tab Activation
chrome.tabs.onActivated.addListener(async (activeInfo) => {
  try {
    const tab = await chrome.tabs.get(activeInfo.tabId);
    if (!tab) return;
    if (tab.incognito) return; // Never track incognito (BRL-009)

    const rawUrl = tab.url || tab.pendingUrl || "";
    await queueOrSendEvent("TabActivated", activeInfo.windowId, activeInfo.tabId, {
      url: rawUrl,
      title: tab.title || "",
      isWindowFocused: true,
      isIncognito: false
    });
  } catch (e) { }
});

// 2. Tab Updated (Navigation / URL / Title changes)
chrome.tabs.onUpdated.addListener(async (tabId, changeInfo, tab) => {
  try {
    if (!tab || !tab.active) return;
    if (tab.incognito) return;

    if (changeInfo.url || changeInfo.title || changeInfo.status === "complete") {
      const rawUrl = tab.url || tab.pendingUrl || "";
      await queueOrSendEvent("TabUpdated", tab.windowId, tabId, {
        url: rawUrl,
        title: tab.title || "",
        isWindowFocused: true,
        isIncognito: false
      });
    }
  } catch (e) { }
});

// 3. Tab Removed
chrome.tabs.onRemoved.addListener(async (tabId, removeInfo) => {
  try {
    await queueOrSendEvent("TabRemoved", removeInfo.windowId, tabId, {
      url: "",
      title: "",
      isWindowFocused: true,
      isIncognito: false
    });
  } catch (e) { }
});

// 4. Window Focus Changed
chrome.windows.onFocusChanged.addListener(async (windowId) => {
  try {
    if (windowId === chrome.windows.WINDOW_ID_NONE) {
      // Window lost focus
      await queueOrSendEvent("WindowFocusChanged", 0, 0, {
        url: "",
        title: "",
        isWindowFocused: false,
        isIncognito: false
      });
    } else {
      // Window gained focus: find active tab
      const tabs = await chrome.tabs.query({ active: true, windowId: windowId });
      if (tabs && tabs.length > 0) {
        const activeTab = tabs[0];
        if (activeTab.incognito) return;

        const rawUrl = activeTab.url || activeTab.pendingUrl || "";
        await queueOrSendEvent("WindowFocusChanged", windowId, activeTab.id, {
          url: rawUrl,
          title: activeTab.title || "",
          isWindowFocused: true,
          isIncognito: false
        });
      }
    }
  } catch (e) { }
});

// Message listener for popup & options communication
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.type === "GET_STATUS") {
    sendResponse({
      connectionState: connectionState,
      browserName: browserName,
      sequence: sequence,
      queueLength: offlineQueue.length
    });
    return true;
  }
  if (message.type === "TEST_CONNECTION") {
    queueOrSendEvent("Hello", 0, 0, {
      url: "",
      title: "Handshake test",
      isWindowFocused: true,
      isIncognito: false
    }).then(() => {
      sendResponse({ status: connectionState, queueLength: offlineQueue.length });
    });
    return true;
  }
  if (message.type === "SETTINGS_UPDATED") {
    reconnectDelayMs = 2000;
    captureCurrentActiveTab();
    return true;
  }
});

// On startup/reload, capture current active tab immediately
captureCurrentActiveTab();
