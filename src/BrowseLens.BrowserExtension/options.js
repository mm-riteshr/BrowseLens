document.addEventListener("DOMContentLoaded", () => {
  const serverUrlInput = document.getElementById("serverUrl");
  const authTokenInput = document.getElementById("authToken");
  const statusMessage = document.getElementById("statusMessage");

  chrome.storage.local.get(["serverUrl", "authToken"], (items) => {
    serverUrlInput.value = items.serverUrl || "http://127.0.0.1:58432";
    authTokenInput.value = items.authToken || "";
  });

  document.getElementById("saveBtn").addEventListener("click", () => {
    const serverUrl = serverUrlInput.value.trim() || "http://127.0.0.1:58432";
    const authToken = authTokenInput.value.trim();

    chrome.storage.local.set({ serverUrl, authToken }, () => {
      statusMessage.textContent = "Settings saved successfully.";
      statusMessage.style.color = "#28a745";
      chrome.runtime.sendMessage({ type: "SETTINGS_UPDATED" }, () => {});
    });
  });

  document.getElementById("testBtn").addEventListener("click", () => {
    statusMessage.textContent = "Testing connection to desktop companion...";
    statusMessage.style.color = "#333";

    chrome.runtime.sendMessage({ type: "TEST_CONNECTION" }, (response) => {
      if (!response) {
        statusMessage.textContent = "Unable to contact background service worker.";
        statusMessage.style.color = "#dc3545";
        return;
      }
      if (response.status === "Connected") {
        statusMessage.textContent = "Connection succeeded! Desktop companion is active and authenticated.";
        statusMessage.style.color = "#28a745";
      } else {
        statusMessage.textContent = "Connection failed: " + response.status;
        statusMessage.style.color = "#dc3545";
      }
    });
  });
});

