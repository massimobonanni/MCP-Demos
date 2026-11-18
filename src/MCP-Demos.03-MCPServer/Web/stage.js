'use strict';

// Minimal read-only poller; Task 5 replaces this with the full dashboard.
(() => {
  const connection = document.getElementById('connection');
  const scene = document.getElementById('scene');
  const title = document.getElementById('title');
  const subtitle = document.getElementById('subtitle');
  let renderedRevision = null;
  let inFlight = false;

  async function poll() {
    if (inFlight) {
      return;
    }
    inFlight = true;
    try {
      const response = await fetch('/api/stage', { cache: 'no-store' });
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }
      const stage = await response.json();
      document.body.classList.remove('disconnected');
      connection.textContent = 'CONNECTED';
      if (stage.revision !== renderedRevision) {
        renderedRevision = stage.revision;
        scene.textContent = String(stage.scene).toUpperCase();
        title.textContent = stage.title;
        subtitle.textContent = stage.subtitle;
      }
    } catch {
      document.body.classList.add('disconnected');
      connection.textContent = 'DISCONNECTED - Stage Director unreachable';
    } finally {
      inFlight = false;
    }
  }

  poll();
  setInterval(poll, 500);
})();
