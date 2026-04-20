import type { CSSProperties } from 'react';
import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import type { TooltipProps } from 'recharts';
import type { NameType, ValueType } from 'recharts/types/component/DefaultTooltipContent';
import type { DashboardChartColors, DashboardPuntoEvolucion } from '@/types';
import { formatCompactCurrency, formatCurrency, formatDate } from '@/utils/formatters';

interface EvolucionChartProps {
  points: DashboardPuntoEvolucion[];
  divisa: string;
  colors: DashboardChartColors;
}

export function EvolucionChart({ points, divisa, colors }: EvolucionChartProps) {
  if (points.length === 0) {
    return <p className="dashboard-empty">No hay datos de evolucion para este periodo.</p>;
  }

  const lastPoint = points[points.length - 1];

  return (
    <div
      className="dashboard-chart-wrapper"
      role="img"
      aria-label={`Evolucion de saldo, ingresos y egresos. Saldo final ${formatCurrency(lastPoint.saldo, divisa)}.`}
    >
      <div className="dashboard-chart-legend" aria-hidden="true">
        <span style={{ '--series-color': colors.saldo } as CSSProperties}>Saldo</span>
        <span style={{ '--series-color': colors.ingresos } as CSSProperties}>Ingresos</span>
        <span style={{ '--series-color': colors.egresos } as CSSProperties}>Egresos</span>
      </div>
      <ResponsiveContainer width="100%" height={320}>
        <LineChart data={points}>
          <CartesianGrid stroke="var(--chart-grid)" vertical={false} />
          <XAxis
            dataKey="fecha"
            tickFormatter={(value) => formatDate(value)}
            axisLine={false}
            tickLine={false}
            minTickGap={28}
          />
          <YAxis
            tickFormatter={(value) => formatCompactCurrency(value, divisa)}
            width={116}
            axisLine={false}
            tickLine={false}
          />
          <Tooltip content={<DashboardTooltip divisa={divisa} />} cursor={{ stroke: 'var(--chart-grid)' }} />
          <Line
            type="monotone"
            name="Ingresos"
            dataKey="ingresos"
            stroke={colors.ingresos}
            dot={false}
            strokeWidth={2.2}
          />
          <Line
            type="monotone"
            name="Egresos"
            dataKey="egresos"
            stroke={colors.egresos}
            dot={false}
            strokeWidth={2.2}
          />
          <Line
            type="monotone"
            name="Saldo"
            dataKey="saldo"
            stroke={colors.saldo}
            dot={false}
            strokeWidth={2.6}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}

function DashboardTooltip({ active, payload, label, divisa }: TooltipProps<ValueType, NameType> & { divisa: string }) {
  if (!active || !payload?.length) {
    return null;
  }

  return (
    <div className="dashboard-chart-tooltip">
      <strong>{formatDate(String(label))}</strong>
      {payload.map((item) => (
        <span key={String(item.dataKey)}>
          <i style={{ background: item.color }} />
          {item.name}: {formatCurrency(Number(item.value ?? 0), divisa)}
        </span>
      ))}
    </div>
  );
}
