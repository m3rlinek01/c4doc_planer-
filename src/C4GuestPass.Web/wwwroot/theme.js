'use strict';
/* Motyw jasny / ciemny / systemowy – ładowany w <head>, zanim strona się narysuje (bez mignięcia). */
(() => {
  const KEY = 'gp-theme', root = document.documentElement;
  const apply = t => { if (t === 'light' || t === 'dark') root.dataset.theme = t; else delete root.dataset.theme; };
  const get = () => { try { return localStorage.getItem(KEY) || 'system'; } catch { return 'system'; } };
  apply(get());
  window.gpTheme = {
    get,
    set(t) {
      try { if (t === 'system') localStorage.removeItem(KEY); else localStorage.setItem(KEY, t); } catch { /* tryb prywatny */ }
      apply(t);
    },
    labels: { light: 'Jasny', dark: 'Ciemny', system: 'Jak w systemie' },
  };
})();
