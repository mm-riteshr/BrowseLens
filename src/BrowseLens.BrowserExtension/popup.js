document.addEventListener("DOMContentLoaded", () => {
  chrome.runtime.sendMessage({ type: "GET_STATUS" }, (response) => {
    if (!response) return;
    const badge = document.getElementById("statusBadge");
    badge.textContent = response.connectionState;
    badge.className = "badge " + (response.connectionState.startsWith("Connected") ? "Connected" : response.connectionState.startsWith("Error") ? "Error" : response.connectionState);
    document.getElementById("browserName").textContent = response.browserName;
    document.getElementById("eventCount").textContent = response.sequence;
    document.getElementById("queueCount").textContent = response.queueLength || "0";
  });

  document.getElementById("optionsLink").addEventListener("click", () => {
    if (chrome.runtime.openOptionsPage) {
      chrome.runtime.openOptionsPage();
    } else {
      window.open(chrome.runtime.getURL("options.html"));
    }
  });
});
