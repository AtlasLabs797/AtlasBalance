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

const EVOLUTION_AXIS_MIN_WIDTH = 52;
const EVOLUTION_AXIS_MAX_WIDTH = 116;
const EVOLUTION_AXIS_PADDING = 14;
const EVOLUTION_AXIS_TICK_MARGIN = 8;
const EVOLUTION_DOMAIN_PADDING_RATIO = 0.04;
const EVOLUTION_AXIS_TICK_STYLE = {
  fill: 'var(--color-text-secondary)',
  fontFamily: 'var(--font-family-mono)',
  fontSize: 12,
  fontVariantNumeric: 'tabular-nums',
} as const;
const LEGACY_CHART_COLOR_TOKENS: Record<string, string> = {
  '#43b430': 'var(--chart-ingresos)',
  '#ff4757': 'var(--chart-egresos)',
  '#7b7b7b': 'var(--chart-saldo)',
};

export function EvolucionChart({ points, divisa, colors, height = 320 }: EvolucionChartProps) {
  if (points.length === 0) {
    return <p className="dashboard-empty">No hay movimientos en este periodo.</p>;
  }

  const lastPoint = points[points.length - 1];
  const yDomain = getEvolutionDomain(points);
  const axisWidth = getEvolutionAxisWidth(points, divisa, yDomain);
  const chartColors = {
    ingresos: resolveChartColor(colors.ingresos, 'var(--chart-ingresos)'),
    egresos: resolveChartColor(colors.egresos, 'var(--chart-egresos)'),
    saldo: resolveChartColor(colors.saldo, 'var(--chart-saldo)'),
  };

  return (
    <div
      className="dashboard-chart-wrapper"
      role="img"
      aria-label={`Evolución de saldo, ingresos y egresos. Saldo final ${formatCurrency(lastPoint.saldo, divisa)}.`}
    >
      <div className="dashboard-chart-legend" aria-hidden="true">
        <span style={{ '--series-color': chartColors.saldo } as CSSProperties}>Saldo</span>
        <span style={{ '--series-color': chartColors.ingresos } as CSSProperties}>Ingresos</span>
        <span style={{ '--series-color': chartColors.egresos } as CSSProperties}>Egresos</span>
      </div>
      <ResponsiveContainer width="100%" height={height}>
        <LineChart data={points} margin={{ top: 12, right: 18, bottom: 10, left: 0 }}>
          <CartesianGrid stroke="var(--chart-grid)" vertical={false} />
          <XAxis
            dataKey="fecha"
            tickFormatter={(value) => formatDate(value)}
            padding={{ left: 8, right: 8 }}
            axisLine={false}
            tickLine={false}
            tickMargin={10}
            minTickGap={28}
            tick={EVOLUTION_AXIS_TICK_STYLE}
          />
          <YAxis
            tickFormatter={(value) => formatCompactCurrency(value, divisa)}
            domain={yDomain}
            width={axisWidth}
            axisLine={false}
            tickLine={false}
            tickMargin={EVOLUTION_AXIS_TICK_MARGIN}
            tick={EVOLUTION_AXIS_TICK_STYLE}
          />
          <Tooltip content={<DashboardTooltip divisa={divisa} />} cursor={{ stroke: 'var(--chart-grid)' }} />
          <Line
            type="monotone"
            name="Ingresos"
            dataKey="ingresos"
            stroke={chartColors.ingresos}
            dot={false}
            strokeWidth={2.2}
          />
          <Line
            type="monotone"
            name="Egresos"
            dataKey="egresos"
            stroke={chartColors.egresos}
            dot={false}
            strokeWidth={2.2}
          />
          <Line
            type="monotone"
            name="Saldo"
            dataKey="saldo"
            stroke={chartColors.saldo}
            dot={false}
            strokeWidth={2.6}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}

function resolveChartColor(value: string | undefined, fallback: string): string {
  const normalized = value?.trim().toLowerCase();
  if (!normalized) {
    return fallback;
  }

  return LEGACY_CHART_COLOR_TOKENS[normalized] ?? value;
}

function getEvolutionDomain(points: DashboardPuntoEvolucion[]): [number, number] {
  const values = points.flatMap((point) => [point.ingresos, point.egresos, point.saldo]);
  const dataMin = Math.min(...values);
  const dataMax = Math.max(...values);

  if (dataMin === 0 && dataMax === 0) {
    return [0, 1];
  }

  const span = dataMax - dataMin;
  const magnitude = Math.max(Math.abs(dataMin), Math.abs(dataMax), 1);
  const padding = Math.max(span * EVOLUTION_DOMAIN_PADDING_RATIO, magnitude * EVOLUTION_DOMAIN_PADDING_RATIO, 1);

  const min = dataMin < 0 ? dataMin - padding : 0;
  const max = dataMax > 0 ? dataMax + padding : 0;

  if (min === max) {
    return [min - 1, max + 1];
  }

  return [min, max];
}

function getEvolutionAxisWidth(
  points: DashboardPuntoEvolucion[],
  divisa: string,
  domain: [number, number],
): number {
  const labelValues = points.flatMap((point) => [point.ingresos, point.egresos, point.saldo]);
  labelValues.push(domain[0], domain[1], 0);

  const maxLabelWidth = labelValues.reduce((maxWidth, value) => {
    return Math.max(maxWidth, estimateAxisLabelWidth(formatCompactCurrency(value, divisa)));
  }, 0);

  const estimatedWidth = Math.ceil(maxLabelWidth + EVOLUTION_AXIS_TICK_MARGIN + EVOLUTION_AXIS_PADDING);
  return Math.min(EVOLUTION_AXIS_MAX_WIDTH, Math.max(EVOLUTION_AXIS_MIN_WIDTH, estimatedWidth));
}

function estimateAxisLabelWidth(label: string): number {
  return Array.from(label).reduce((width, char) => width + getAxisCharWidth(char), 0);
}

function getAxisCharWidth(char: string): number {
  if (char === ' ') return 3.2;
  if (char === ',' || char === '.') return 3.6;
  if (char === '-' || char === '+') return 5.4;
  if (char === '€') return 7.2;
  if (/\d/.test(char)) return 7.1;
  if (/[A-Z]/.test(char)) return 7.8;
  return 6.8;
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
