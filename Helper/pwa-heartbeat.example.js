// Włącz ten moduł w PWA LiftCall. Nie zawiera i nie wysyła danych połączeń.
(() => {
  const heartbeatUrl = "http://127.0.0.1:32145/heartbeat";
  const heartbeatStopUrl = "http://127.0.0.1:32145/heartbeat/stop";
  const heartbeatIntervalMs = 20_000;
  let timerId = null;
  let stopSent = false;

  function isAuthenticated() {
    const value = window.LiftCallRuntime?.authenticated;
    return typeof value === "boolean" ? value : null;
  }

  async function sendHeartbeat() {
    try {
      const authenticated = isAuthenticated();
      const response = await fetch(heartbeatUrl, {
        method: "POST",
        cache: "no-store",
        mode: "cors",
        headers: authenticated === null ? {} : {
          "X-LiftCall-Authenticated": String(authenticated),
        },
      });

      const payload = await response.json();
      if (!response.ok || payload.ok !== true) {
        throw new Error(`HTTP ${response.status}`);
      }

      console.info("[LiftCall] heartbeat success");
    } catch (error) {
      // Helper availability must never block or interrupt the PWA.
      console.warn("[LiftCall] heartbeat failure", error);
    }
  }

  function startHeartbeat() {
    void sendHeartbeat();
    timerId = window.setInterval(() => {
      void sendHeartbeat();
    }, heartbeatIntervalMs);
  }

  function stopHeartbeat() {
    if (timerId !== null) {
      window.clearInterval(timerId);
      timerId = null;
    }
  }

  function sendHeartbeatStop() {
    if (stopSent) return;
    stopSent = true;
    const body = new Blob([], { type: "text/plain" });
    if (navigator.sendBeacon?.(heartbeatStopUrl, body)) return;
    try {
      void fetch(heartbeatStopUrl, {
        method: "POST",
        mode: "cors",
        cache: "no-store",
        keepalive: true,
        body,
      });
    } catch {
      // Closing the PWA must never block the application.
    }
  }

  function sendHeartbeatWhenVisible() {
    if (document.visibilityState === "visible") {
      void sendHeartbeat();
    }
  }

  window.addEventListener("pagehide", () => {
    stopHeartbeat();
    sendHeartbeatStop();
  }, { once: true });
  window.addEventListener("beforeunload", sendHeartbeatStop, { once: true });
  window.addEventListener("unload", stopHeartbeat, { once: true });
  document.addEventListener("visibilitychange", sendHeartbeatWhenVisible);
  if (document.readyState === "loading") {
    window.addEventListener("DOMContentLoaded", startHeartbeat, { once: true });
  } else {
    startHeartbeat();
  }
})();
