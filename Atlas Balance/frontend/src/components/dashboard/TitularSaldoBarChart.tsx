import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { formatCurrency } from '@/utils/formatters';
import { DashboardTooltip } from './DashboardTooltip';

export interface TitularSaldoBarChartRow {
  titular_id: string;
  titular_nombre: string;
  total_convertido: number;
}

interface TitularSaldoBarChartProps {
  rows: TitularSaldoBarChartRow[];
  divisa: string;
}

const AXIS_TICK_MARGIN = 10;
const AXIS_PADDING = 12;
const MIN_Y_AXIS_WIDTH = 56;
const MAX_X_LABELS_WITHOUT_ROTATION = 6;
const compactNumberFormatter = new Intl.NumberFormat('es-ES', {
  notation: 'compact',
  compactDisplay: 'short',
  maximumFractionDigits: 1,
});

/** Etiqueta de tick sin divisa (la unidad se indica una sola vez en el titulo). */
function formatAxisTick(value: number): string {
  return compactNumberFormatter.format(value).replace('-', '−');
}

/** Ancho del eje Y calculado con la etiqueta mas larga (aprox. 7,5 px por caracter a 12px). */
function getYAxisWidth(rows: TitularSaldoBarChartRow[]): number {
  const values = [0, ...rows.map((row) => row.total_convertido)];
  const longest = values.reduce((max, value) => Math.max(max, formatAxisTick(value).length), 0);
  return Math.max(MIN_Y_AXIS_WIDTH, Math.ceil(longest * 7.5 + AXIS_TICK_MARGIN + AXIS_PADDING));
}

function truncateLabel(label: string, max: number): string {
  return label.length > max ? `${label.slice(0, max - 1)}…` : label;
}

export default function TitularSaldoBarChart({ rows, divisa }: TitularSaldoBarChartProps) {
  const chartLabel = rows.length > 0
    ? `Saldos por titular en ${divisa}. ${rows.map((row) => `${row.titular_nombre}: ${formatCurrency(row.total_convertido, divisa)}`).join('; ')}.`
    : `Sin saldos por titular en ${divisa}.`;

  const rotateLabels = rows.length > MAX_X_LABELS_WITHOUT_ROTATION;

  return (
    <>
      <p style={{ margin: 0, color: 'var(--text-secondary)', fontSize: 12 }} aria-hidden="true">
        Importes en {divisa}
      </p>
      <div role="img" aria-label={chartLabel}>
        <ResponsiveContainer width="100%" height={340}>
          <BarChart data={rows} margin={{ top: 18, right: 36, left: 16, bottom: 18 }}>
            <CartesianGrid stroke="var(--chart-grid)" strokeDasharray="3 3" vertical={false} />
            <XAxis
              dataKey="titular_nombre"
              interval={0}
              {...(rotateLabels ? { angle: -18, textAnchor: 'end' as const, height: 72 } : { height: 36 })}
              tickFormatter={(value) => truncateLabel(String(value), rotateLabels ? 28 : 22)}
              padding={{ left: 16, right: 16 }}
            />
            <YAxis
              width={getYAxisWidth(rows)}
              axisLine={false}
              tickLine={false}
              tickMargin={AXIS_TICK_MARGIN}
              tickFormatter={(value) => formatAxisTick(Number(value))}
            />
            <Tooltip
              content={(props) => (
                <DashboardTooltip {...props} divisa={divisa} formatLabel={(label) => `Titular: ${String(label)}`} />
              )}
              cursor={{ fill: 'var(--chart-grid)', opacity: 0.4 }}
            />
            <Bar dataKey="total_convertido" name="Saldo total" fill="var(--accent-solid)">
              {rows.map((item) => (
                <Cell
                  key={item.titular_id}
                  fill={item.total_convertido >= 0 ? 'var(--status-success-text)' : 'var(--status-danger-text)'}
                />
              ))}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>
      <table className="sr-only">
        <caption>Saldos por titular en {divisa}</caption>
        <thead>
          <tr>
            <th scope="col">Titular</th>
            <th scope="col">Saldo</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.titular_id}>
              <td>{row.titular_nombre}</td>
              <td>{formatCurrency(row.total_convertido, divisa)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
