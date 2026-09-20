import type { CSSProperties } from 'react';
import { Cell, Pie, PieChart, ResponsiveContainer, Tooltip } from 'recharts';
// recharts 3: los tooltips custom se tipan con TooltipContentProps.
// TooltipProps ya no expone payload/label, que se leen del contexto.
import type { TooltipContentProps } from 'recharts';
import type { NameType, ValueType } from 'recharts/types/component/DefaultTooltipContent';
import type { DashboardConcentracionBanco, DashboardSaldoTitular } from '@/types';
import { formatCompactCurrency, formatCurrency } from '@/utils/formatters';

// La referencia visual limita cada donut a cuatro partes visibles; el resto se
// agrupa en "Otros", que va siempre en --chart-8 (via --chart-series-other).
const DONUT_COLORS = [
  'var(--chart-series-1)',
  'var(--chart-series-2)',
  'var(--chart-series-3)',
  'var(--chart-series-4)',
  'var(--chart-series-5)',
];
const OTROS_COLOR = 'var(--chart-series-other)';
const SMALL_SLICE_THRESHOLD = 1.5;
const MAX_SLICES = 3;

const DONUT_INNER_RADIUS = '62%';
const DONUT_OUTER_RADIUS = '84%';

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
  const ordenadas = [...entries].sort((a, b) => b.value - a.value);
  const candidatas = ordenadas.filter((e) => e.porcentaje >= SMALL_SLICE_THRESHOLD);
  const large = candidatas.slice(0, MAX_SLICES);
  const small = [
    ...candidatas.slice(MAX_SLICES),
    ...ordenadas.filter((e) => e.porcentaje < SMALL_SLICE_THRESHOLD),
  ];
  if (small.length === 0) return large;
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

function formatDonutTotal(amount: number, divisa: string): string {
  try {
    return new Intl.NumberFormat('es-ES', {
      style: 'currency',
      currency: divisa,
      notation: 'compact',
      maximumFractionDigits: amount >= 1_000_000 ? 2 : 1,
    }).format(amount);
  } catch {
    return formatCompactCurrency(amount, divisa);
  }
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
  const formattedTotal = formatDonutTotal(total, divisa);

  return (
    <div className="concentracion-donut-panel">
      <h3 className="concentracion-donut-title">{title}</h3>
      <div className="concentracion-donut-body">
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
                paddingAngle={data.length > 1 ? 3 : 0}
                cornerRadius={10}
                strokeWidth={0}
              >
                {data.map((entry, index) => (
                  <Cell key={entry.name} fill={sliceColor(entry, index)} />
                ))}
              </Pie>
              <Tooltip
                content={(props) => <DonutTooltip {...props} divisa={divisa} />}
                wrapperStyle={{ zIndex: 100 }}
              />
            </PieChart>
          </ResponsiveContainer>
          <div className="concentracion-donut-center" aria-hidden="true">
            <span className="concentracion-donut-center-value">
              {formattedTotal}
            </span>
            <span className="concentracion-donut-center-label">total</span>
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
              <span className="concentracion-donut-legend-pct">
                {Math.round(entry.porcentaje)}%
              </span>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}

function DonutTooltip({
  active,
  payload,
  divisa,
}: TooltipContentProps<ValueType, NameType> & { divisa: string }) {
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
