'use strict';

(() => {
  const theme = new URLSearchParams(location.search).get('clawpilotTheme');
  document.documentElement.dataset.theme = ['light', 'dark'].includes(theme)
    ? theme
    : (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');

  const stage = document.getElementById('stage');
  const connection = document.getElementById('connection');
  const connectionDetail = document.getElementById('connection-detail');
  const stale = document.getElementById('stale');
  const scene = document.getElementById('scene');
  const title = document.getElementById('title');
  const subtitle = document.getElementById('subtitle');
  const confetti = document.getElementById('confetti');
  const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
  const hexColor = /^#[0-9a-f]{6}$/i;
  let renderedActivation = null;
  let confettiTimer;
  let inFlight = false;

  function isSnapshot(snapshot) {
    const theme = snapshot?.theme;
    const isColor = value => typeof value === 'string' && hexColor.test(value);
    return Number.isSafeInteger(snapshot?.revision) && snapshot.revision > 0
      && typeof snapshot.activatedAtUtc === 'string' && Number.isFinite(Date.parse(snapshot.activatedAtUtc))
      && ['welcome', 'focus', 'energy', 'celebration'].includes(snapshot.scene)
      && typeof snapshot.title === 'string' && typeof snapshot.subtitle === 'string'
      && ['none', 'ambient', 'pulse', 'confetti'].includes(snapshot.effect)
      && Number.isInteger(snapshot.effectDurationMs) && snapshot.effectDurationMs >= 0
      && isColor(theme?.background) && isColor(theme.accent)
      && ['soft', 'minimal', 'dynamic', 'festive'].includes(theme.intensity)
      && Array.isArray(theme.effectColors) && theme.effectColors.every(isColor);
  }

  function stopConfetti() {
    clearTimeout(confettiTimer);
    confetti.replaceChildren();
  }

  function celebrate(duration, colors) {
    if (reducedMotion.matches || duration <= 0) {
      return;
    }

    const particles = document.createDocumentFragment();
    for (let i = 0; i < 48; i++) {
      const particle = document.createElement('i');
      const delay = Math.random() * duration * 0.3;
      particle.style.setProperty('--x', `${(i + Math.random()) / 48 * 100}%`);
      particle.style.setProperty('--drift', `${Math.random() * 18 - 9}vw`);
      particle.style.setProperty('--spin', `${Math.random() * 720 - 360}deg`);
      particle.style.setProperty('--delay', `${delay}ms`);
      particle.style.setProperty('--duration', `${(duration - delay) * (0.75 + Math.random() * 0.25)}ms`);
      particle.style.setProperty('--cp-stage-particle', colors[i % colors.length]);
      particles.append(particle);
    }
    confetti.append(particles);
    confettiTimer = setTimeout(stopConfetti, duration);
  }

  function render(snapshot) {
    stopConfetti();
    const { background, accent, intensity, effectColors } = snapshot.theme;
    stage.dataset.effect = snapshot.effect;
    stage.dataset.intensity = intensity;
    stage.style.setProperty('--cp-stage-background', background);
    stage.style.setProperty('--cp-stage-accent', accent);
    stage.style.setProperty('--cp-stage-effect', effectColors[0] ?? accent);
    scene.textContent = snapshot.scene.toUpperCase();
    title.textContent = snapshot.title;
    subtitle.textContent = snapshot.subtitle;
    if (snapshot.effect === 'confetti') {
      celebrate(Math.min(snapshot.effectDurationMs, 4000), [accent, ...effectColors]);
    }
  }

  function setConnection(state) {
    if (document.body.dataset.connection === state) {
      return;
    }
    const disconnected = state === 'disconnected';
    document.body.dataset.connection = state;
    connection.textContent = state.toUpperCase();
    connectionDetail.hidden = !disconnected;
    stale.hidden = !disconnected || renderedActivation === null;
    if (disconnected) {
      stopConfetti();
    }
  }

  async function poll() {
    if (inFlight) {
      return;
    }
    inFlight = true;
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 3000);
    try {
      const response = await fetch('/api/stage', {
        method: 'GET',
        cache: 'no-store',
        mode: 'same-origin',
        signal: controller.signal
      });
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }
      const snapshot = await response.json();
      if (!isSnapshot(snapshot)) {
        throw new Error('Invalid stage snapshot');
      }
      // Revisions restart at 1 in a new server process.
      const activation = `${snapshot.revision}:${snapshot.activatedAtUtc}`;
      if (activation !== renderedActivation) {
        render(snapshot);
        renderedActivation = activation;
      }
      setConnection('connected');
    } catch {
      setConnection('disconnected');
    } finally {
      clearTimeout(timeout);
      inFlight = false;
    }
  }

  reducedMotion.addEventListener('change', event => {
    if (event.matches) {
      stopConfetti();
    }
  });
  poll();
  setInterval(poll, 500);
})();
