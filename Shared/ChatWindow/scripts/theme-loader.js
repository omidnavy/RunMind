'use strict';

let availableRunMindThemes = [];
let appearanceStylesheetSequence = 0;
let runMindThemeCatalogReady = false;
const THEME_COLOR_PROPERTIES = [
  '--color-bg', '--color-surface', '--color-surface-raised', '--color-surface-hover',
  '--color-input', '--color-text', '--color-muted', '--color-accent',
  '--color-accent-contrast', '--color-border', '--color-user-bubble',
  '--color-assistant-bubble', '--color-danger', '--color-success'
];
const THEME_SHADOW_PROPERTIES = ['--shadow-panel', '--shadow-float'];

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
  const seen = new Set();
  availableRunMindThemes = themes.filter(theme => {
    if (!theme || !['dark', 'light'].includes(theme.mode)
        || typeof theme.id !== 'string' || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(theme.id)
        || theme.fileName !== `${theme.id}.css`
        || typeof theme.name !== 'string' || typeof theme.version !== 'string') return false;
    const key = `${theme.mode}:${theme.id}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });

  // Browser-only previews have no native directory enumerator. Native catalogs are
  // authoritative, including when one mode or one theme file is missing/corrupt.
  if (!hasNativeCatalog) {
    availableRunMindThemes = [
      { id: 'warm-graphite', name: 'Warm Graphite', mode: 'dark', fileName: 'warm-graphite.css', version: 'fallback' },
      { id: 'classic-light', name: 'Classic Light', mode: 'light', fileName: 'classic-light.css', version: 'fallback' }
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

function finishInitialThemeApplication() {
  const root = document.documentElement;
  if (root.dataset.themeInitializing !== 'true') return;
  const release = () => {
    if (root.dataset.themeReady === 'true') { root.style.removeProperty('background-color'); root.removeAttribute('data-theme-initializing'); }
  };
  if (typeof requestAnimationFrame === 'function')
    requestAnimationFrame(() => requestAnimationFrame(release));
  else setTimeout(release, 32);
}

function waitForThemeStylesheet(link, timeoutMs = 10000) {
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

function validateThemeStylesheet(link, theme) {
  try {
    const rules = [...link.sheet.cssRules];
    const selector = `:root[data-theme="${theme.mode}"][data-color-theme="${theme.id}"]`.replace(/\s+/g, '').toLowerCase();
    const paletteRules = rules.filter(rule => rule.type === CSSRule.STYLE_RULE
      && String(rule.selectorText || '').replace(/\s+/g, '').toLowerCase() === selector);
    if (paletteRules.length !== 1) return false;
    const style = paletteRules[0].style;
    if (style.getPropertyValue('color-scheme').trim().toLowerCase() !== theme.mode) return false;
    return THEME_COLOR_PROPERTIES.every(property => {
      const value = style.getPropertyValue(property).trim();
      return value.length > 0 && CSS.supports('color', value);
    }) && THEME_SHADOW_PROPERTIES.every(property => {
      const value = style.getPropertyValue(property).trim();
      return value.length > 0 && CSS.supports('box-shadow', value);
    });
  } catch (_) {
    // CSSOM throws for some malformed or inaccessible stylesheets; treat that theme as unavailable.
    return false;
  }
}

async function activateAppearanceStylesheet(mode, themeId, appearanceMode) {
  const preferred = findAvailableTheme(mode, themeId);
  if (!preferred) throw new Error(`No ${mode} themes are available.`);

  const sequence = ++appearanceStylesheetSequence;
  const root = document.documentElement;
  const current = document.getElementById('activeThemeStylesheet');
  const candidates = [preferred, ...availableRunMindThemes.filter(theme =>
    theme.mode === mode && theme.id !== preferred.id)];
  let lastError = null;

  for (const theme of candidates) {
    if (current?.dataset.themeFile === theme.fileName
        && current.dataset.themeVersion === theme.version
        && current.sheet && validateThemeStylesheet(current, theme)) {
      if (sequence !== appearanceStylesheetSequence) return false;
      root.dataset.appearanceMode = appearanceMode;
      root.dataset.theme = mode;
      root.dataset.colorTheme = theme.id;
      root.dataset.themeReady = 'true';
      finishInitialThemeApplication();
      return true;
    }

    const next = document.createElement('link');
    next.rel = 'stylesheet';
    next.media = 'not all';
    next.href = `ChatWindow/theme/${encodeURIComponent(theme.fileName)}?v=${encodeURIComponent(theme.version)}`;
    next.dataset.themeFile = theme.fileName;
    next.dataset.themeVersion = theme.version;
    const loaded = waitForThemeStylesheet(next);
    const chatStylesheet = document.querySelector('link[href="ChatWindow/styles/chat.css"]');
    document.head.insertBefore(next, chatStylesheet || null);

    try {
      await loaded;
      if (sequence !== appearanceStylesheetSequence) {
        next.remove();
        return false;
      }
      if (!validateThemeStylesheet(next, theme))
        throw new Error(`Theme '${theme.id}' has an invalid ${mode} palette.`);

      // Apply the palette only after it has loaded and passed CSSOM checks. The previous
      // stylesheet remains active until this single-task swap completes.
      root.dataset.appearanceMode = appearanceMode;
      root.dataset.theme = mode;
      root.dataset.colorTheme = theme.id;
      next.media = 'all';
      next.id = 'activeThemeStylesheet';
      current?.remove();
      root.dataset.themeReady = 'true';
      finishInitialThemeApplication();
      return true;
    } catch (error) {
      next.remove();
      if (sequence !== appearanceStylesheetSequence) return false;
      lastError = error;
      console.warn(`RunMind skipped invalid ${mode} theme '${theme.id}'.`, error);
    }
  }

  throw lastError || new Error(`No valid ${mode} theme stylesheet could be loaded.`);
}
