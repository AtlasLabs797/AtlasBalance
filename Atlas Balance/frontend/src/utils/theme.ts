export type Theme = 'light' | 'dark';

const THEME_COLOR_LIGHT = '#F5F5F7';
const THEME_COLOR_DARK = '#000000';

// Resuelve el tema inicial: la preferencia guardada (solo si es valida) gana;
// si no hay o es invalida, se sigue al sistema.
// IMPORTANTE: public/theme-init.js replica esta logica en JS plano. Si se cambia
// aqui, cambiar alli tambien.
export function resolveInitialTheme(stored: string | null, prefersDark: boolean): Theme {
  if (stored === 'dark' || stored === 'light') return stored;
  return prefersDark ? 'dark' : 'light';
}

export function systemPrefersDark(): boolean {
  try {
    return typeof window !== 'undefined' && typeof window.matchMedia === 'function'
      ? window.matchMedia('(prefers-color-scheme: dark)').matches
      : false;
  } catch {
    return false;
  }
}

// Sincroniza <meta name="theme-color"> con el tema activo (barra del navegador).
function applyThemeColorMeta(theme: Theme): void {
  if (typeof document === 'undefined') return;
  const meta = document.querySelector('meta[name="theme-color"]');
  if (meta) meta.setAttribute('content', theme === 'dark' ? THEME_COLOR_DARK : THEME_COLOR_LIGHT);
}

// Aplica data-theme y theme-color al documento (no toca localStorage).
export function applyThemeToDocument(theme: Theme): void {
  if (typeof document === 'undefined') return;
  document.documentElement.setAttribute('data-theme', theme);
  applyThemeColorMeta(theme);
}
