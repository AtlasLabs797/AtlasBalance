import type { CSSProperties } from 'react';
import { Cell, Pie, PieChart, ResponsiveContainer, Tooltip } from 'recharts';
import type { TooltipProps } from 'recharts';
import type { NameType, ValueType } from 'recharts/types/component/DefaultTooltipContent';
import type { DashboardConcentracionBanco, DashboardSaldoTitular } from '@/types';
import { formatCompactCurrency, formatCurrency } from '@/utils/formatters';

const DONUT_COLORS = [
  'var(--chart-series-1)',
  'var(--chart-series-2)',
  'var(--chart-series-3)',
  'var(--chart-series-4)',
  'var(--chart-series-5)',
  'var(--chart-series-6)',
  'var(--chart-series-7)',
  'var(--chart-series-8)',
  'var(--chart-series-9)',
  'var(--chart-series-10)',
  'var(--chart-series-11)',
  'var(--chart-series-12)',
  'var(--chart-series-13)',
  'var(--chart-series-14)',
  'var(--chart-series-15)',
];
const OTROS_COLOR = 'var(--chart-series-other)';
const SMALL_SLICE_THRESHOLD = 1.5;

const DONUT_INNER_RADIUS = '55%';
const DONUT_OUTER_RADIUS = '80%';

interface ConcentracionDonutChartsProps {
  bancos: DashboardConcentracionBanco[];
  titulares: DashboardSaldoTitular[];
  divisa: string;
}

interface DonutEntry {
  name: string;
  value: number;
  porcentaje: number;
  isOtros?: boolean;
  otrosCount?: number;
}

function groupSmallSlices(entries: DonutEntry[]): DonutEntry[] {
  const large = entries.filter((e) => e.porcentaje >= SMALL_SLICE_THRESHOLD);
  const small = entries.filter((e) => e.porcentaje < SMALL_SLICE_THRESHOLD);
  if (small.length === 0) return entries;
  const otrosValue = small.reduce((s, e) => s + e.value, 0);
  const otrosPct = small.reduce((s, e) => s + e.porcentaje, 0);
  return [
    ...large,
    { name: 'Otros', value: otrosValue, porcentaje: otrosPct, isOtros: true, otrosCount: small.length },
  ];
}

function buildBancosData(bancos: DashboardConcentracionBanco[]): DonutEntry[] {
  const raw = bancos
    .filter((b) => b.saldo_convertido > 0)
    .map((b) => ({ name: b.banco_nombre, value: b.saldo_convertido, porcentaje: b.porcentaje }));
  return groupSmallSlices(raw);
}

function buildTitularesData(titulares: DashboardSaldoTitular[]): DonutEntry[] {
  const positivos = titulares.filter((t) => t.total_convertido > 0);
  const total = positivos.reduce((sum, t) => sum + t.total_convertido, 0);
  if (total === 0) return [];
  const raw = positivos.map((t) => ({
    name: t.titular_nombre,
    value: t.total_convertido,
    porcentaje: (t.total_convertido / total) * 100,
  }));
  return groupSmallSlices(raw);
}

function sliceColor(entry: DonutEntry, index: number): string {
  return entry.isOtros ? OTROS_COLOR : DONUT_COLORS[index % DONUT_COLORS.length];
}

export function ConcentracionDonutCharts({ bancos, titulares, divisa }: ConcentracionDonutChartsProps) {
  const bancosData = buildBancosData(bancos);
  const titularesData = buildTitularesData(titulares);

  const totalBancos = bancosData.reduce((s, d) => s + d.value, 0);
  const totalTitulares = titularesData.reduce((s, d) => s + d.value, 0);

  if (bancosData.length === 0 && titularesData.length === 0) return null;

  return (
    <div className="concentracion-donuts-grid">
      {bancosData.length > 0 && (
        <DonutPanel
          title="Por banco"
          data={bancosData}
          total={totalBancos}
          divisa={divisa}
          ariaLabel="Concentración de saldo por entidad bancaria"
        />
      )}
      {titularesData.length > 0 && (
        <DonutPanel
          title="Por titular"
          data={titularesData}
          total={totalTitulares}
          divisa={divisa}
          ariaLabel="Concentración de saldo por titular"
        />
      )}
    </div>
  );
}

interface DonutPanelProps {
  title: string;
  data: DonutEntry[];
  total: number;
  divisa: string;
  ariaLabel: string;
}

function DonutPanel({ title, data, total, divisa, ariaLabel }: DonutPanelProps) {
  const entidadesLabel = `${data.reduce((n, e) => n + (e.isOtros ? (e.otrosCount ?? 1) : 1), 0)} entidades`;

  return (
    <div className="concentracion-donut-panel">
      <h3 className="concentracion-donut-title">{title}</h3>
      <div className="concentracion-donut-chart" role="img" aria-label={ariaLabel}>
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie
              data={data}
              cx="50%"
              cy="50%"
              innerRadius={DONUT_INNER_RADIUS}
              outerRadius={DONUT_OUTER_RADIUS}
              dataKey="value"
              nameKey="name"
              startAngle={90}
              endAngle={-270}
              paddingAngle={data.length > 1 ? 2 : 0}
              strokeWidth={0}
            >
              {data.map((entry, index) => (
                <Cell key={entry.name} fill={sliceColor(entry, index)} />
              ))}
            </Pie>
            <Tooltip content={<DonutTooltip divisa={divisa} />} wrapperStyle={{ zIndex: 100 }} />
          </PieChart>
        </ResponsiveContainer>
        <div className="concentracion-donut-center" aria-hidden="true">
          <span className="concentracion-donut-center-value">
            {formatCompactCurrency(total, divisa)}
          </span>
          <span className="concentracion-donut-center-label">{entidadesLabel}</span>
        </div>
      </div>
      <ul className="concentracion-donut-legend">
        {data.map((entry, index) => (
          <li
            key={entry.name}
            className={`concentracion-donut-legend-item${entry.isOtros ? ' concentracion-donut-legend-item--otros' : ''}`}
            style={{ '--entry-color': sliceColor(entry, index) } as CSSProperties}
          >
            <span className="concentracion-donut-legend-dot" aria-hidden="true" />
            <span className="concentracion-donut-legend-name">
              {entry.isOtros ? `Otros (${entry.otrosCount})` : entry.name}
            </span>
            <span className="concentracion-donut-legend-meta">
              <span className="concentracion-donut-legend-amount">
                {formatCompactCurrency(entry.value, divisa)}
              </span>
              <span className="concentracion-donut-legend-sep" aria-hidden="true">·</span>
              <span className="concentracion-donut-legend-pct">
                {entry.porcentaje.toFixed(1)}%
              </span>
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function DonutTooltip({
  active,
  payload,
  divisa,
}: TooltipProps<ValueType, NameType> & { divisa: string }) {
  if (!active || !payload?.length) return null;
  const item = payload[0];
  const entry = item.payload as DonutEntry;
  return (
    <div className="dashboard-chart-tooltip">
      <strong>{entry.isOtros ? `Otros (${entry.otrosCount} entidades)` : entry.name}</strong>
      <span>
        <i style={{ background: String(item.payload.fill ?? item.color) }} />
        {formatCurrency(entry.value, divisa)}
      </span>
      <span style={{ color: 'var(--color-text-secondary)' }}>
        {entry.porcentaje.toFixed(1)}% del total
      </span>
    </div>
  );
}
