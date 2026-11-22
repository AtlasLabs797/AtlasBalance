// D-8 (LOTE 8): los colores del grafico de Configuracion se guardan como
// hexadecimal, pero un hexadecimal escrito a mano no se adapta al tema: el
// valor guardado seria el mismo en claro y en oscuro.
//
// Aqui se resuelve el caso contrario: si el hexadecimal guardado es
// exactamente el de un token de grafico (--chart-1 ... --chart-10), se
// devuelve la VARIABLE del token, no el literal. Asi el grafico vuelve a
// seguir al tema, y el valor guardado sigue siendo legible en la
// configuracion.
//
// Los hexadecimales vienen de:
//   - styles/design-system/tokens/colors.css  (tema claro)
//   - styles/design-system/tokens/themes.css   (tema oscuro)
// Se mapean los dos: si el administrador elige un token de la paleta en
// cualquiera de los dos temas, el grafico lo resuelve por variable.

const CHART_COLOR_TOKENS: Record<string, string> = {
  // Tema claro (colors.css).
  '#0066cc': 'var(--chart-1)',
  '#0f8b8d': 'var(--chart-2)',
  '#e0761a': 'var(--chart-3)',
  '#8b5cf6': 'var(--chart-4)',
  '#1c8a4b': 'var(--chart-5)',
  '#4046d6': 'var(--chart-6)',
  '#c92b3f': 'var(--chart-7)',
  '#6e6e73': 'var(--chart-8)',
  '#b8478f': 'var(--chart-9)',
  '#6b8f1e': 'var(--chart-10)',
  // Tema oscuro (themes.css).
  '#5c8ceb': 'var(--chart-1)',
  '#3fb6b8': 'var(--chart-2)',
  '#f0a24e': 'var(--chart-3)',
  '#a184f9': 'var(--chart-4)',
  '#4fb87a': 'var(--chart-5)',
  '#2997ff': 'var(--chart-6)',
  '#ee909b': 'var(--chart-7)',
  '#8da2bb': 'var(--chart-8)',
  '#d97cb4': 'var(--chart-9)',
  '#a3c455': 'var(--chart-10)',
  // Paleta anterior (V-02.x), que el backend todavia siembra por defecto.
  '#43b430': 'var(--chart-5)',
  '#ff4757': 'var(--chart-7)',
  '#7b7b7b': 'var(--chart-1)',
};

/**
 * Devuelve el color que se pinta en el grafico.
 *
 * - Si no hay valor (o es un espacio), se usa el token de reserva.
 * - Si el valor es el hexadecimal de un token de grafico, se devuelve la
 *   variable del token, para que siga al tema.
 * - Si es un hexadecimal propio del administrador, se devuelve tal cual.
 */
export function resolveChartColor(value: string | undefined | null, fallback: string): string {
  const normalized = value?.trim().toLowerCase();
  if (!normalized) {
    return fallback;
  }

  return CHART_COLOR_TOKENS[normalized] ?? value;
}
