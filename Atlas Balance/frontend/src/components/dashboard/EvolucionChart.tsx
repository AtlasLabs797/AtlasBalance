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
  height?: number;
}

const EVOLUTION_AXIS_MIN_WIDTH = 44;
const EVOLUTION_AXIS_MAX_WIDTH = 72;
const EVOLUTION_AXIS_CHAR_WIDTH = 6.2;
const EVOLUTION_AXIS_PADDING = 14;

export function EvolucionChart({ points, divisa, colors, height = 320 }: EvolucionChartProps) {
  if (points.length === 0) {
    return <p className="dashboard-empty">No hay datos de evolucion para este periodo.</p>;
  }

  const lastPoint = points[points.length - 1];
  const axisWidth = getEvolutionAxisWidth(points, divisa);

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
      <ResponsiveContainer width="100%" height={height}>
        <LineChart data={points} margin={{ top: 4, right: 8, bottom: 0, left: 0 }}>
          <CartesianGrid stroke="var(--chart-grid)" vertical={false} />
          <XAxis
            dataKey="fecha"
            tickFormatter={(value) => formatDate(value)}
            axisLine={false}
            tickLine={false}
            tickMargin={10}
            minTickGap={28}
          />
          <YAxis
            tickFormatter={(value) => formatCompactCurrency(value, divisa)}
            width={axisWidth}
            axisLine={false}
            tickLine={false}
            tickMargin={10}
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

function getEvolutionAxisWidth(points: DashboardPuntoEvolucion[], divisa: string): number {
  const maxLabelLength = points.reduce((maxLength, point) => {
    const labels = [
      formatCompactCurrency(point.ingresos, divisa),
      formatCompactCurrency(point.egresos, divisa),
      formatCompactCurrency(point.saldo, divisa),
    ];

    return Math.max(maxLength, ...labels.map((label) => label.length));
  }, 0);

  const estimatedWidth = Math.ceil(maxLabelLength * EVOLUTION_AXIS_CHAR_WIDTH + EVOLUTION_AXIS_PADDING);
  return Math.min(EVOLUTION_AXIS_MAX_WIDTH, Math.max(EVOLUTION_AXIS_MIN_WIDTH, estimatedWidth));
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
