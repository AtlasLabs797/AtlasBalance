// Lote 5 (B-1): el backend excluye del total convertido las divisas sin tipo
// de cambio utilizable y las devuelve en `divisas_sin_tipo_cambio`. Aqui se
// decide que divisas se listan y con que texto se avisa, para que el total
// no se lea mas bajo sin explicacion.

export interface AvisoDivisasSinTipoCambio {
  lead: string;
  detalle: string;
}

export function normalizarDivisasSinTipoCambio(divisas: string[] | null | undefined): string[] {
  if (!divisas?.length) {
    return [];
  }

  const limpias = divisas
    .map((divisa) => divisa.trim().toUpperCase())
    .filter((divisa) => divisa.length > 0);

  return [...new Set(limpias)].sort((a, b) => a.localeCompare(b, 'es'));
}

export function buildAvisoDivisasSinTipoCambio(
  divisas: string[] | null | undefined
): AvisoDivisasSinTipoCambio | null {
  const lista = normalizarDivisasSinTipoCambio(divisas);
  if (lista.length === 0) {
    return null;
  }

  const enumeracion = lista.length === 1
    ? lista[0]
    : `${lista.slice(0, -1).join(', ')} y ${lista[lista.length - 1]}`;

  return {
    lead: `El total no incluye ${enumeracion}.`,
    detalle: lista.length === 1
      ? 'No hay tipo de cambio para esa divisa.'
      : 'No hay tipo de cambio para esas divisas.'
  };
}
