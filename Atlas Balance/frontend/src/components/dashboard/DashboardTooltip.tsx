// recharts 3: los tooltips custom se tipan con TooltipContentProps.
import type { TooltipContentProps } from 'recharts';
import type { NameType, ValueType } from 'recharts/types/component/DefaultTooltipContent';
import { formatCurrency, formatDate } from '@/utils/formatters';

type DashboardTooltipProps = TooltipContentProps<ValueType, NameType> & {
  divisa: string;
  /** Texto de la cabecera; por defecto el label se formatea como fecha. */
  formatLabel?: (label: unknown) => string;
};

export function DashboardTooltip({ active, payload, label, divisa, formatLabel }: DashboardTooltipProps) {
  if (!active || !payload?.length) {
    return null;
  }

  return (
    <div className="dashboard-chart-tooltip">
      <strong>{formatLabel ? formatLabel(label) : formatDate(String(label))}</strong>
      {payload.map((item) => (
        <span key={String(item.dataKey)}>
          <i style={{ background: item.color }} />
          {item.name}: {formatCurrency(Number(item.value ?? 0), divisa)}
        </span>
      ))}
    </div>
  );
}
