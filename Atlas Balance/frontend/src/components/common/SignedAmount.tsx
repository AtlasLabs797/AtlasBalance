import type { ReactNode } from 'react';
import { getAmountTone } from '@/utils/formatters';

interface SignedAmountProps {
  value: number | string | null | undefined;
  children: ReactNode;
  className?: string;
  tone?: ReturnType<typeof getAmountTone>;
  /**
   * Antepone un signo "+" visible cuando el valor es positivo, para no depender
   * solo del color (accesibilidad para daltonismo). Los negativos ya muestran
   * el "-" del formato, por lo que no se duplica.
   */
  showSign?: boolean;
}

export function SignedAmount({ value, children, className, tone, showSign = false }: SignedAmountProps) {
  const resolvedTone = tone ?? getAmountTone(value);
  const classes = ['signed-amount', `signed-amount--${resolvedTone}`, className].filter(Boolean).join(' ');
  const prefix = showSign && resolvedTone === 'positive' ? '+' : '';
  return <span className={classes}>{prefix}{children}</span>;
}
