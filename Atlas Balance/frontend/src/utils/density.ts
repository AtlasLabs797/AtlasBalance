export type Density = 'comodo' | 'compacto';

export const DENSITY_STORAGE_KEY = 'density';
export const DENSITY_ROW_HEIGHT_COMODO = 44;
export const DENSITY_ROW_HEIGHT_COMPACTO = 36;

// Resuelve la densidad inicial: solo vale 'compacto'; cualquier otra cosa
// (null, vacio, valor invalido) es 'comodo'.
// IMPORTANTE: public/theme-init.js replica esta logica en JS plano. Si se cambia
// aqui, cambiar alli tambien.
export function resolveInitialDensity(stored: string | null): Density {
  return stored === 'compacto' ? 'compacto' : 'comodo';
}

export function isCoarsePointer(): boolean {
  try {
    return typeof window !== 'undefined' && typeof window.matchMedia === 'function'
      ? window.matchMedia('(hover: none), (pointer: coarse)').matches
      : false;
  } catch {
    return false;
  }
}

// Alto de fila de la tabla de Extractos en px. En puntero grueso (tactil) el
// modo compacto no baja de 44px. Mantener sincronizado con --sheet-row-height
// en src/styles/layout/extractos.css.
export function densityRowHeight(density: Density, coarsePointer: boolean): number {
  if (density === 'compacto' && !coarsePointer) return DENSITY_ROW_HEIGHT_COMPACTO;
  return DENSITY_ROW_HEIGHT_COMODO;
}

// Aplica data-density al documento (no toca localStorage).
export function applyDensityToDocument(density: Density): void {
  if (typeof document === 'undefined') return;
  document.documentElement.setAttribute('data-density', density);
}
