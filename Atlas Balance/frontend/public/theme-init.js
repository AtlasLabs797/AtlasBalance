// Arranque del tema antes de pintar (evita destello claro).
// Replica en JS plano resolveInitialTheme de src/utils/theme.ts: si se cambia
// una, cambiar la otra. No guarda nada: la preferencia solo se persiste cuando
// el usuario alterna el tema a mano.
(function () {
  var stored = null;
  try {
    stored = window.localStorage.getItem('theme');
  } catch (e) {
    stored = null;
  }
  var theme;
  if (stored === 'dark' || stored === 'light') {
    theme = stored;
  } else {
    var prefersDark = false;
    try {
      prefersDark = !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
    } catch (e) {
      prefersDark = false;
    }
    theme = prefersDark ? 'dark' : 'light';
  }
  document.documentElement.setAttribute('data-theme', theme);
  var meta = document.querySelector('meta[name="theme-color"]');
  if (meta) meta.setAttribute('content', theme === 'dark' ? '#000000' : '#F5F5F7');

  // Densidad de la tabla de Extractos (data-density). Replica en JS plano
  // resolveInitialDensity de src/utils/density.ts: si se cambia una, cambiar
  // la otra. Solo 'compacto' es valido; cualquier otra cosa es 'comodo'.
  var density = 'comodo';
  try {
    if (window.localStorage.getItem('density') === 'compacto') density = 'compacto';
  } catch (e) {
    density = 'comodo';
  }
  document.documentElement.setAttribute('data-density', density);
})();
