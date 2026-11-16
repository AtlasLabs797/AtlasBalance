// recharts 3: los tooltips custom se tipan con TooltipContentProps.
import type { TooltipContentProps } from 'recharts';
import type { NameType, ValueType } from 'recharts/types/component/DefaultTooltipContent';
import { formatCurrency, formatDate } from '@/utils/formatters';

type DashboardTooltipProps = TooltipContentProps<ValueType, NameType> & {
  divisa: string;
  /** Texto de la cabecera; por defecto el label se formatea como fecha. */
  formatLabel?: (label: unknown) => string;
  /** Antepone "+" a los importes positivos para no depender solo del color. */
  signed?: boolean;
};

function formatValue(value: number, divisa: string, signed: boolean): string {
  const text = formatCurrency(value, divisa);
  return signed && value > 0 ? `+${text}` : text;
}

export function DashboardTooltip({ active, payload, label, divisa, formatLabel, signed = false }: DashboardTooltipProps) {
  if (!active || !payload?.length) {
    return null;
  }

  return (
    <div className="dashboard-chart-tooltip">
      <strong>{formatLabel ? formatLabel(label) : formatDate(String(label))}</strong>
      {payload.map((item) => (
        <span key={String(item.dataKey)}>
          <i style={{ background: item.color }} />
          {item.name}: {formatValue(Number(item.value ?? 0), divisa, signed)}
        </span>
      ))}
    </div>
  );
}
