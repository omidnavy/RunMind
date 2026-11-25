'use strict';

let availableRunMindThemes = [];
let appearanceStylesheetSequence = 0;
let runMindThemeCatalogReady = false;

function populateThemeSelect(select, themes) {
  if (!select) return;
  const options = themes.map(theme => {
    const option = document.createElement('option');
    option.value = theme.id;
    option.textContent = theme.name;
    return option;
  });
  select.replaceChildren(...options);
}

async function initializeThemeCatalog() {
  let response;
  try {
    response = await callAgentHost('themes.list', {}, undefined, 8000);
  } catch (error) {
    console.warn('Native theme discovery is unavailable.', error);
  }

  const hasNativeCatalog = Array.isArray(response?.themes);
  if (!hasNativeCatalog && runMindThemeCatalogReady) return;
  const themes = hasNativeCatalog ? response.themes : [];
  availableRunMindThemes = themes.filter(theme =>
    theme && (theme.mode === 'dark' || theme.mode === 'light')
    && typeof theme.id === 'string'
    && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(theme.id)
    && theme.fileName === `${theme.mode}-${theme.id}.css`
    && typeof theme.name === 'string'
    && typeof theme.version === 'string');

  // Browser-only previews have no native file enumerator. Keep a small usable fallback
  // only when the native bridge is unavailable; an actual native catalog, including a
  // catalog with one mode missing, remains authoritative.
  if (!hasNativeCatalog) {
    availableRunMindThemes = [
      { id: 'warm-graphite', name: 'Warm Graphite', mode: 'dark', fileName: 'dark-warm-graphite.css', version: 'fallback' },
      { id: 'classic-light', name: 'Classic Light', mode: 'light', fileName: 'light-classic-light.css', version: 'fallback' }
    ];
  }

  darkThemes = availableRunMindThemes.filter(theme => theme.mode === 'dark').map(theme => theme.id);
  lightThemes = availableRunMindThemes.filter(theme => theme.mode === 'light').map(theme => theme.id);
  APPEARANCE_THEME_NAMES = Object.freeze(Object.fromEntries(availableRunMindThemes.map(theme => [theme.id, theme.name])));

  const defaultDark = darkThemes.includes('warm-graphite') ? 'warm-graphite' : darkThemes[0];
  const defaultLight = lightThemes.includes('classic-light') ? 'classic-light' : lightThemes[0];
  APPEARANCE_DEFAULTS = Object.freeze({ mode: 'system', lightTheme: defaultLight, darkTheme: defaultDark });

  populateThemeSelect(lightThemeSelect, availableRunMindThemes.filter(theme => theme.mode === 'light'));
  populateThemeSelect(darkThemeSelect, availableRunMindThemes.filter(theme => theme.mode === 'dark'));
  runMindThemeCatalogReady = true;
}

function findAvailableTheme(mode, id) {
  return availableRunMindThemes.find(theme => theme.mode === mode && theme.id === id)
    || availableRunMindThemes.find(theme => theme.mode === mode)
    || null;
}

function waitForThemeStylesheet(link, timeoutMs = 10000) {
  if (link.sheet) return Promise.resolve();
  return new Promise((resolve, reject) => {
    let timer;
    const cleanup = () => {
      clearTimeout(timer);
      link.removeEventListener('load', onLoad);
      link.removeEventListener('error', onError);
    };
    const onLoad = () => { cleanup(); resolve(); };
    const onError = () => { cleanup(); reject(new Error(`Could not load theme stylesheet ${link.href}.`)); };
    link.addEventListener('load', onLoad, { once: true });
    link.addEventListener('error', onError, { once: true });
    timer = setTimeout(() => { cleanup(); reject(new Error(`Timed out loading theme stylesheet ${link.href}.`)); }, timeoutMs);
  });
}

async function activateAppearanceStylesheet(mode, themeId, appearanceMode, allowFallback = true) {
  const theme = findAvailableTheme(mode, themeId);
  if (!theme) throw new Error(`No ${mode} themes are available.`);

  const sequence = ++appearanceStylesheetSequence;
  const root = document.documentElement;
  const current = document.getElementById('activeThemeStylesheet');
  if (current?.dataset.themeFile === theme.fileName
      && current.dataset.themeVersion === theme.version
      && current.sheet) {
    root.dataset.appearanceMode = appearanceMode;
    root.dataset.theme = mode;
    root.dataset.colorTheme = theme.id;
    root.dataset.themeReady = 'true';
    return true;
  }

  const next = document.createElement('link');
  next.rel = 'stylesheet';
  next.media = 'not all';
  next.href = `ChatWindow/theme/${encodeURIComponent(theme.fileName)}?v=${encodeURIComponent(theme.version)}`;
  next.dataset.themeFile = theme.fileName;
  next.dataset.themeVersion = theme.version;
  const chatStylesheet = document.querySelector('link[href="ChatWindow/styles/chat.css"]');
  document.head.insertBefore(next, chatStylesheet || null);

  try {
    await waitForThemeStylesheet(next);
    if (sequence !== appearanceStylesheetSequence) {
      next.remove();
      return false;
    }

    // Apply attributes and enable the new sheet in one task. The previous sheet stays
    // active until this point, so users never see an unstyled intermediate frame.
    root.dataset.appearanceMode = appearanceMode;
    root.dataset.theme = mode;
    root.dataset.colorTheme = theme.id;
    next.media = 'all';
    next.id = 'activeThemeStylesheet';
    current?.remove();
    root.dataset.themeReady = 'true';
    return true;
  } catch (error) {
    next.remove();
    if (sequence !== appearanceStylesheetSequence) return false;
    const fallback = availableRunMindThemes.find(candidate => candidate.mode === mode && candidate.id !== theme.id);
    if (allowFallback && fallback)
      return activateAppearanceStylesheet(mode, fallback.id, appearanceMode, false);
    throw error;
  }
}
