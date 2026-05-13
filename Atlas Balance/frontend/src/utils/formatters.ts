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

export function getAmountTone(value: unknown): AmountTone {
  const safeValue = toSafeNumber(value);
  if (safeValue > 0) return 'positive';
  if (safeValue < 0) return 'negative';
  return 'neutral';
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
      // Fallback for unknown currency codes
      return `${formatNumber(safeAmount)} ${divisa}`;
    }
  }
  return currencyFormatters[divisa].format(safeAmount);
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
      return `${formatNumber(safeAmount, 0)} ${divisa}`;
    }
  }
  return `${compactCurrencyFormatters[divisa].format(safeAmount)} ${divisa}`;
}

export function formatNumber(value: number, decimals: number = 2): string {
  return new Intl.NumberFormat('es-ES', {
    useGrouping: true,
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  }).format(value);
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
  return date.toLocaleString('es-ES', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function formatRelativeTime(dateStr: string): string {
  const now = new Date();
  const date = new Date(dateStr);
  const diffMs = now.getTime() - date.getTime();
  const diffMins = Math.floor(diffMs / 60000);
  const diffHours = Math.floor(diffMins / 60);
  const diffDays = Math.floor(diffHours / 24);

  if (diffMins < 1) return 'ahora mismo';
  if (diffMins < 60) return `hace ${diffMins} min`;
  if (diffHours < 24) return `hace ${diffHours}h`;
  if (diffDays < 7) return `hace ${diffDays}d`;
  return formatDate(dateStr);
}

const COLUMN_LETTERS = ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L'];
const COLUMN_NAMES: Record<string, string> = {
  A: 'Fecha',
  B: 'Concepto',
  C: 'Monto',
  D: 'Saldo',
};

export function getCellReference(filaNumero: number, columnIndex: number): string {
  const letter = COLUMN_LETTERS[columnIndex] ?? `Col${columnIndex}`;
  return `${letter}${filaNumero}`;
}

export function getColumnName(letter: string): string {
  return COLUMN_NAMES[letter] ?? letter;
}
