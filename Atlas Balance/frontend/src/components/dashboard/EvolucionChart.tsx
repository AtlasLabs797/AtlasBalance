import type { CSSProperties } from 'react';
import {
  Area,
  CartesianGrid,
  ComposedChart,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import type { DashboardChartColors, DashboardPuntoEvolucion } from '@/types';
import { formatCompactCurrency, formatCurrency, formatDate } from '@/utils/formatters';
import { resolveChartColor } from '@/utils/chartColors';
import { DashboardTooltip } from './DashboardTooltip';

interface EvolucionChartProps {
  points: DashboardPuntoEvolucion[];
  divisa: string;
  colors: DashboardChartColors;
  height?: number;
  variant?: 'multiLine' | 'saldoArea';
  xAxisMode?: 'date' | 'month';
}

const EVOLUTION_AXIS_MIN_WIDTH = 52;
const EVOLUTION_AXIS_MAX_WIDTH = 116;
const EVOLUTION_AXIS_PADDING = 14;
const EVOLUTION_AXIS_TICK_MARGIN = 8;
const EVOLUTION_DOMAIN_PADDING_RATIO = 0.04;
const EVOLUTION_AXIS_TICK_STYLE = {
  fill: 'var(--text-secondary)',
  fontFamily: 'var(--font-mono)',
  fontSize: 12,
  fontVariantNumeric: 'tabular-nums',
} as const;
const CHART_COLOR_TOKEN_FALLBACKS = {
  ingresos: 'var(--chart-5)',
  egresos: 'var(--chart-7)',
  saldo: 'var(--chart-1)',
} as const;

export function EvolucionChart({
  points,
  divisa,
  colors,
  height = 320,
  variant = 'multiLine',
  xAxisMode = 'date',
}: EvolucionChartProps) {
  if (points.length === 0) {
    return <p className="dashboard-empty">No hay movimientos en este periodo.</p>;
  }

  const lastPoint = points[points.length - 1];
  const yDomain = getEvolutionDomain(points);
  const axisWidth = getEvolutionAxisWidth(points, divisa, yDomain);
  const saldoAreaAxisWidth = getEvolutionAxisWidthCompact(yDomain);
  const chartColors = {
    ingresos: resolveChartColor(colors.ingresos, CHART_COLOR_TOKEN_FALLBACKS.ingresos),
    egresos: resolveChartColor(colors.egresos, CHART_COLOR_TOKEN_FALLBACKS.egresos),
    saldo: resolveChartColor(colors.saldo, CHART_COLOR_TOKEN_FALLBACKS.saldo),
  };
  const monthMode = xAxisMode === 'month';
  const withYear = monthMode && spansSeveralYears(points);
  // Modo mes: eje temporal numerico, un tick por inicio de mes y equiespaciado en el tiempo.
  const chartData = monthMode ? points.map((point) => ({ ...point, ts: isoToTimestamp(point.fecha) })) : points;
  const xDataKey = monthMode ? 'ts' : 'fecha';
  // Rangos cortos (<= ~95 dias): ticks por fecha corta ("20 oct") espaciados regularmente.
  const shortRange = monthMode && isShortRange(points);
  const tickFormatter = monthMode
    ? shortRange
      ? (value: number | string) => formatShortDateTick(timestampToIso(Number(value)))
      : (value: number | string) => formatMonthTick(timestampToIso(Number(value)), withYear)
    : (value: string) => formatShortDateTick(value);
  const xMinTickGap = monthMode ? (shortRange ? 16 : 28) : 56;
  const xTicks = monthMode ? (shortRange ? getShortRangeTicks(points) : getMonthTicks(points)) : undefined;
  const xAxisScale = monthMode
    ? { type: 'number' as const, scale: 'time' as const, domain: ['dataMin', 'dataMax'] as [string, string] }
    : {};
  const tooltipLabel = monthMode ? (label: unknown) => formatDate(timestampToIso(Number(label))) : undefined;

  if (variant === 'saldoArea') {
    return (
      <div
        className="dashboard-chart-wrapper dashboard-chart-wrapper--saldo-area"
        role="group"
        aria-label={`Evolución de saldo, ingresos y egresos. Saldo final ${formatCurrency(lastPoint.saldo, divisa)}.`}
      >
        <div className="dashboard-chart-legend" aria-hidden="true">
          <span style={{ '--series-color': chartColors.saldo } as CSSProperties}>Saldo</span>
          <span style={{ '--series-color': chartColors.ingresos } as CSSProperties}>Ingresos</span>
          <span style={{ '--series-color': chartColors.egresos } as CSSProperties}>Egresos</span>
        </div>
        <ResponsiveContainer width="100%" height={height}>
          <ComposedChart data={chartData} margin={{ top: 12, right: 12, bottom: 8, left: 0 }}>
            <defs>
              <linearGradient id="dashboardSaldoArea" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor={chartColors.saldo} stopOpacity={0.22} />
                <stop offset="100%" stopColor={chartColors.saldo} stopOpacity={0.04} />
              </linearGradient>
            </defs>
            <CartesianGrid stroke="var(--chart-grid)" vertical={false} />
            <XAxis
              dataKey={xDataKey}
              {...xAxisScale}
              ticks={xTicks}
              tickFormatter={tickFormatter}
              padding={{ left: 8, right: 24 }}
              axisLine={false}
              tickLine={false}
              tickMargin={10}
              minTickGap={xMinTickGap}
              interval="preserveStartEnd"
              tick={EVOLUTION_AXIS_TICK_STYLE}
            />
            <YAxis
              tickFormatter={(value) => formatCompactAxis(value)}
              domain={yDomain}
              width={saldoAreaAxisWidth}
              axisLine={false}
              tickLine={false}
              tickMargin={EVOLUTION_AXIS_TICK_MARGIN}
              tick={EVOLUTION_AXIS_TICK_STYLE}
            />
            <Tooltip
            content={(props) => <DashboardTooltip {...props} divisa={divisa} formatLabel={tooltipLabel} />}
            cursor={{ stroke: 'var(--chart-grid)' }}
          />
            <Area
              type="monotone"
              name="Saldo"
              dataKey="saldo"
              stroke={chartColors.saldo}
              fill="url(#dashboardSaldoArea)"
              strokeWidth={2.8}
              dot={false}
              isAnimationActive={false}
              activeDot={{ r: 5, strokeWidth: 0, fill: chartColors.saldo }}
            />
            <Line
              type="monotone"
              name="Ingresos"
              dataKey="ingresos"
              stroke={chartColors.ingresos}
              dot={false}
              strokeWidth={2.2}
              isAnimationActive={false}
              activeDot={{ r: 4, strokeWidth: 0, fill: chartColors.ingresos }}
            />
            <Line
              type="monotone"
              name="Egresos"
              dataKey="egresos"
              stroke={chartColors.egresos}
              dot={false}
              strokeWidth={2.2}
              isAnimationActive={false}
              activeDot={{ r: 4, strokeWidth: 0, fill: chartColors.egresos }}
            />
          </ComposedChart>
        </ResponsiveContainer>
        <EvolutionDataTable points={points} divisa={divisa} />
      </div>
    );
  }

  return (
    <div
      className="dashboard-chart-wrapper"
      role="group"
      aria-label={`Evolución de saldo, ingresos y egresos. Saldo final ${formatCurrency(lastPoint.saldo, divisa)}.`}
    >
      <div className="dashboard-chart-legend" aria-hidden="true">
        <span style={{ '--series-color': chartColors.saldo } as CSSProperties}>Saldo</span>
        <span style={{ '--series-color': chartColors.ingresos } as CSSProperties}>Ingresos</span>
        <span style={{ '--series-color': chartColors.egresos } as CSSProperties}>Egresos</span>
      </div>
      <ResponsiveContainer width="100%" height={height}>
        <LineChart data={chartData} margin={{ top: 12, right: 18, bottom: 10, left: 0 }}>
          <CartesianGrid stroke="var(--chart-grid)" vertical={false} />
          <XAxis
            dataKey={xDataKey}
            {...xAxisScale}
            ticks={xTicks}
            tickFormatter={tickFormatter}
            padding={{ left: 8, right: 24 }}
            axisLine={false}
            tickLine={false}
            tickMargin={10}
            minTickGap={xMinTickGap}
              interval="preserveStartEnd"
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
          <Tooltip
            content={(props) => <DashboardTooltip {...props} divisa={divisa} formatLabel={tooltipLabel} />}
            cursor={{ stroke: 'var(--chart-grid)' }}
          />
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
      <EvolutionDataTable points={points} divisa={divisa} />
    </div>
  );
}

function EvolutionDataTable({ points, divisa }: Pick<EvolucionChartProps, 'points' | 'divisa'>) {
  return (
    <table className="sr-only">
      <caption>Datos de evolución por fecha</caption>
      <thead>
        <tr>
          <th scope="col">Fecha</th>
          <th scope="col">Ingresos</th>
          <th scope="col">Egresos</th>
          <th scope="col">Saldo</th>
        </tr>
      </thead>
      <tbody>
        {points.map((point) => (
          <tr key={point.fecha}>
            <td>{formatDate(point.fecha)}</td>
            <td>{formatCurrency(point.ingresos, divisa)}</td>
            <td>{formatCurrency(point.egresos, divisa)}</td>
            <td>{formatCurrency(point.saldo, divisa)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function formatMonthTick(value: string, withYear = false): string {
  const match = /^(\d{4})-(\d{2})/.exec(value);
  const date = match ? new Date(Number(match[1]), Number(match[2]) - 1, 1) : new Date(value);
  if (Number.isNaN(date.getTime())) {
    return formatDate(value);
  }

  const month = date.toLocaleDateString('es-ES', { month: 'short' }).replace('.', '');
  return withYear ? `${month} ${String(date.getFullYear()).slice(2)}` : month;
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

function formatCompactAxis(value: number | string): string {
  const numeric = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(numeric)) {
    return String(value);
  }

  return new Intl.NumberFormat('es-ES', {
    notation: 'compact',
    compactDisplay: 'short',
    maximumFractionDigits: 2,
  }).format(numeric);
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

function getEvolutionAxisWidthCompact(domain: [number, number]): number {
  const labelValues = [domain[0], domain[1], 0];
  const maxLabelWidth = labelValues.reduce((maxWidth, value) => {
    return Math.max(maxWidth, estimateAxisLabelWidth(formatCompactAxis(value)));
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

/** Tick corto de fecha: "20 oct" (el tooltip da la fecha completa). */
function formatShortDateTick(value: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  if (!match) return formatDate(value);
  const date = new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
  const month = date.toLocaleDateString('es-ES', { month: 'short' }).replace('.', '');
  return `${Number(match[3])} ${month}`;
}

function isoToTimestamp(fecha: string): number {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(fecha);
  return match ? Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])) : new Date(fecha).getTime();
}

function timestampToIso(ts: number): string {
  return new Date(ts).toISOString().slice(0, 10);
}

/** Un tick por inicio de mes dentro del rango de datos (equiespaciados en el eje temporal). */
function getMonthTicks(points: DashboardPuntoEvolucion[]): number[] {
  const stamps = points.map((point) => isoToTimestamp(point.fecha)).filter(Number.isFinite);
  if (stamps.length === 0) return [];
  const min = Math.min(...stamps);
  const max = Math.max(...stamps);
  const start = new Date(min);
  const year = start.getUTCFullYear();
  let month = start.getUTCMonth();
  if (start.getUTCDate() > 1) month += 1;
  const ticks: number[] = [];
  for (let ts = Date.UTC(year, month, 1); ts <= max; ts = Date.UTC(year, ++month, 1)) {
    ticks.push(ts);
  }
  return ticks.length > 0 ? ticks : [min];
}

const DAY_MS = 86_400_000;
const SHORT_RANGE_MAX_DAYS = 95;

function getStampRange(points: DashboardPuntoEvolucion[]): { min: number; max: number } | null {
  const stamps = points.map((point) => isoToTimestamp(point.fecha)).filter(Number.isFinite);
  if (stamps.length === 0) return null;
  return { min: Math.min(...stamps), max: Math.max(...stamps) };
}

function isShortRange(points: DashboardPuntoEvolucion[]): boolean {
  const range = getStampRange(points);
  return range !== null && (range.max - range.min) / DAY_MS <= SHORT_RANGE_MAX_DAYS;
}

/** Ticks cada 7 dias (<= 35 dias de rango) o cada 15, anclados en el ultimo dia para que no se corte. */
function getShortRangeTicks(points: DashboardPuntoEvolucion[]): number[] {
  const range = getStampRange(points);
  if (!range) return [];
  const stepDays = (range.max - range.min) / DAY_MS <= 35 ? 7 : 15;
  const ticks: number[] = [];
  for (let ts = range.max; ts >= range.min; ts -= stepDays * DAY_MS) {
    ticks.unshift(ts);
  }
  return ticks;
}

function spansSeveralYears(points: DashboardPuntoEvolucion[]): boolean {
  return new Set(points.map((point) => point.fecha.slice(0, 4))).size > 1;
}
