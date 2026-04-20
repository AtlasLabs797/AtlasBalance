import type { ReactNode } from 'react';
import { getAmountTone } from '@/utils/formatters';

interface SignedAmountProps {
  value: number | string | null | undefined;
  children: ReactNode;
  className?: string;
  tone?: ReturnType<typeof getAmountTone>;
}

export function SignedAmount({ value, children, className, tone }: SignedAmountProps) {
  const resolvedTone = tone ?? getAmountTone(value);
  const classes = ['signed-amount', `signed-amount--${resolvedTone}`, className].filter(Boolean).join(' ');
  return <span className={classes}>{children}</span>;
}
