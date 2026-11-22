const currencyFormatters: Record<string, Intl.NumberFormat> = {};

const compactCurrencyFormatters: Record<string, Intl.NumberFormat> = {};

export function toSafeNumber(value: unknown): number {
  if (typeof value === 'number') {
    return Number.isFinite(value) ? value : 0;
  }

  if (typeof value === 'string') {
    return parseEuropeanNumber(value) ?? 0;
  }

  return 0;
}

// Decimal estricto para campos de formulario que antes eran type="number"
// (tasas, importes minimos, intereses): un unico separador, coma o punto, que
// SIEMPRE es decimal. "1.085" vale 1,085 (no 1085). Sin separador de miles:
// cualquier otro formato devuelve null para que el formulario muestre el error.
export function parseStrictDecimal(value: string | null | undefined): number | null {
  const text = (value ?? '').trim();
  if (!/^\d+(?:[.,]\d+)?$/.test(text)) {
    return null;
  }
  const parsed = Number(text.replace(',', '.'));
  return Number.isFinite(parsed) ? parsed : null;
}

export function parseEuropeanNumber(value: string | number | null | undefined): number | null {
  if (typeof value === 'number') {
    return Number.isFinite(value) ? value : null;
  }

  if (value === null || value === undefined) {
    return null;
  }

  let normalized = String(value)
    .replace(/\u00a0/g, ' ')
    .trim();

  if (!normalized) {
    return null;
  }

  let negative = false;
  if (/^\(.*\)$/.test(normalized)) {
    negative = true;
    normalized = normalized.slice(1, -1).trim();
  }

  normalized = normalized
    .replace(/[€$£]/g, '')
    .replace(/\b(EUR|USD|GBP)\b/gi, '')
    .replace(/\s+/g, '')
    .trim();

  if (normalized.startsWith('+')) {
    normalized = normalized.slice(1);
  } else if (normalized.startsWith('-')) {
    negative = !negative;
    normalized = normalized.slice(1);
  }

  if (!/^\d[\d.,]*$/.test(normalized)) {
    return null;
  }

  const commaIndex = normalized.lastIndexOf(',');
  const dotIndex = normalized.lastIndexOf('.');
  if (commaIndex >= 0 && dotIndex >= 0) {
    const decimalSeparator = commaIndex > dotIndex ? ',' : '.';
    const thousandsSeparator = decimalSeparator === ',' ? '.' : ',';
    normalized = normalized.split(thousandsSeparator).join('');
    normalized = normalized.replace(decimalSeparator, '.');
  } else if (commaIndex >= 0) {
    normalized = normalized.replace(',', '.');
  } else if (dotIndex >= 0) {
    const dotParts = normalized.split('.');
    const hasThousandsGrouping =
      dotParts.length > 1 &&
      dotParts[0].length >= 1 &&
      dotParts[0].length <= 3 &&
      // B-13 (LOTE 5): una parte entera a cero no es una agrupacion de miles.
      // "0.500" es 0,5, no 500. Misma regla que el backend
      // (ImportacionService.IsThousandsGrouped).
      Number(dotParts[0]) !== 0 &&
      dotParts.slice(1).every((part) => part.length === 3);
    normalized = hasThousandsGrouping ? dotParts.join('') : normalized;
  }

  if (!/^\d+(\.\d+)?$/.test(normalized)) {
    return null;
  }

  const parsed = Number.parseFloat(normalized);
  if (!Number.isFinite(parsed)) {
    return null;
  }

  return negative ? -parsed : parsed;
}

export type AmountTone = 'positive' | 'negative' | 'neutral';

// Quita el signo positivo de valores numericos crudos solo al mostrarlos.
export function sinSignoPositivo(valor: string): string {
  return parseEuropeanNumber(valor) !== null ? valor.replace(/^(\s*)\+/, '$1') : valor;
}

export function getAmountTone(value: unknown): AmountTone {
  const safeValue = toSafeNumber(value);
  if (safeValue > 0) return 'positive';
  if (safeValue < 0) return 'negative';
  return 'neutral';
}

// DESIGN.md §6: el signo menos debe ser el real "−" (U+2212), no el guion ASCII
// "-" (U+002D) que usa Intl.NumberFormat con locale es-ES. Solo se sustituye el
// caracter de salida (presentacion): el "-" de entrada del usuario lo sigue
// aceptando parseEuropeanNumber sin cambios, porque estas funciones son de solo
// formateo y ningun consumidor vuelve a parsear su resultado (ver EditableCell,
// que edita sobre el valor crudo, no sobre el formateado).
const REAL_MINUS = '−';

function withRealMinusSign(formatted: string): string {
  return formatted.replace('-', REAL_MINUS);
}

export function formatCurrency(amount: number | string | null | undefined, divisa: string = 'EUR'): string {
  const safeAmount = toSafeNumber(amount);
  if (!currencyFormatters[divisa]) {
    try {
      currencyFormatters[divisa] = new Intl.NumberFormat('es-ES', {
        style: 'currency',
        currency: divisa,
        useGrouping: true,
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      });
    } catch {
      // Fallback for unknown currency codes (formatNumber ya aplica el menos real)
      return `${formatNumber(safeAmount)} ${divisa}`;
    }
  }
  return withRealMinusSign(currencyFormatters[divisa].format(safeAmount));
}

export function formatCompactCurrency(amount: number | string | null | undefined, divisa: string = 'EUR'): string {
  const safeAmount = toSafeNumber(amount);
  if (!compactCurrencyFormatters[divisa]) {
    try {
      compactCurrencyFormatters[divisa] = new Intl.NumberFormat('es-ES', {
        notation: 'compact',
        compactDisplay: 'short',
        maximumFractionDigits: 1,
    });
  } catch {
      // formatNumber ya aplica el menos real
      return `${formatNumber(safeAmount, 0)} ${divisa}`;
    }
  }
  return withRealMinusSign(`${compactCurrencyFormatters[divisa].format(safeAmount)} ${divisa}`);
}

export function formatNumber(value: number, decimals: number = 2): string {
  return withRealMinusSign(
    new Intl.NumberFormat('es-ES', {
      useGrouping: true,
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    }).format(value),
  );
}

// D-1 (LOTE 8): los porcentajes se pintan con el locale es-ES (coma decimal),
// el menos real "−" (U+2212) y el separador que el propio locale pone antes del
// "%" (espacio fino no separable, U+00A0). Nunca el "%" pegado.
//
// El valor entra YA en unidades de porcentaje (12.34 -> "12,3 %"), por eso no
// se usa style: 'percent' de Intl: ese multiplica por 100.
//
// Valor ausente o no numerico -> "—", el marcador de valor ausente que ya usa
// la app. Mismo criterio que formatBytes con "Sin tamaño": si no hay dato, se
// dice que no lo hay, en vez de inventar un "0,0 %".
const PERCENT_ABSENT = '—';

const percentFormatters: Record<number, Intl.NumberFormat> = {};

// Separador entre la cifra y el "%" segun el locale. Se extrae de Intl en vez
// de fijarlo a mano para que no se desincronice del locale.
let percentSeparator: string | null = null;

function getPercentSeparator(): string {
  if (percentSeparator === null) {
    const parts = new Intl.NumberFormat('es-ES', { style: 'percent' }).formatToParts(0);
    const signIndex = parts.findIndex((part) => part.type === 'percentSign');
    const previous = signIndex > 0 ? parts[signIndex - 1] : undefined;
    percentSeparator = previous && previous.type === 'literal' ? previous.value : ' ';
  }
  return percentSeparator;
}

function parsePercentValue(value: number | string | null | undefined): number | null {
  if (typeof value === 'number') {
    return Number.isFinite(value) ? value : null;
  }

  if (value === null || value === undefined) {
    return null;
  }

  return parseEuropeanNumber(value);
}

// Redondea al numero de decimales que pinta formatPercent. Un valor que queda
// en cero se devuelve como 0 (sin "-0").
function redondearPorcentaje(numeric: number, decimals: number): number {
  const factor = 10 ** decimals;
  const redondeado = Math.round(numeric * factor) / factor;
  return redondeado === 0 ? 0 : redondeado;
}

// Signo de un porcentaje tal y como se pinta: -1, 0 o 1, decidido sobre el valor
// ya redondeado. Un 0,04 con un decimal es 0 (no positivo): asi la clase
// de color no contradice la cifra mostrada. Valor ausente -> 0.
export function signoPorcentaje(value: number | string | null | undefined, decimals: number = 1): number {
  const numeric = parsePercentValue(value);
  if (numeric === null) return 0;
  return Math.sign(redondearPorcentaje(numeric, decimals));
}

// Porcentaje en formato es-ES: "12,3 %", "−5,2 %", "0,0 %".
// decimals admite 0, 1 (por defecto) o 2.
export function formatPercent(
  value: number | string | null | undefined,
  decimals: number = 1,
): string {
  const numeric = parsePercentValue(value);
  if (numeric === null) return PERCENT_ABSENT;

  // Una cifra que redondea a cero se muestra sin signo: "−0,0 %" no significa
  // nada y el menos real delataria una variacion negativa inexistente.
  const redondeado = redondearPorcentaje(numeric, decimals);
  const safeNumeric = redondeado === 0 ? 0 : numeric;

  if (!percentFormatters[decimals]) {
    percentFormatters[decimals] = new Intl.NumberFormat('es-ES', {
      useGrouping: true,
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    });
  }

  return `${withRealMinusSign(percentFormatters[decimals].format(safeNumeric))}${getPercentSeparator()}%`;
}

export function formatBytes(value: number | null | undefined): string {
  if (!value || value <= 0) return 'Sin tamaño';
  const mb = value / (1024 * 1024);
  return `${formatNumber(mb)} MB`;
}

export function formatDate(dateStr: string): string {
  const dateOnlyMatch = /^(\d{4})-(\d{2})-(\d{2})/.exec(dateStr);
  if (dateOnlyMatch) {
    const [, year, month, day] = dateOnlyMatch;
    return `${day}/${month}/${year}`;
  }

  const date = new Date(dateStr);
  if (Number.isNaN(date.getTime())) {
    return dateStr;
  }

  return date.toLocaleDateString('es-ES', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  });
}

export function formatDateTime(dateStr: string): string {
  const date = new Date(dateStr);
  if (Number.isNaN(date.getTime())) {
    return dateStr;
  }
  return date.toLocaleString('es-ES', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}
