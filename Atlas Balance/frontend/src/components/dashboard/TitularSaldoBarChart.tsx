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
import { formatCompactCurrency, formatCurrency } from '@/utils/formatters';
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

export default function TitularSaldoBarChart({ rows, divisa }: TitularSaldoBarChartProps) {
  const chartLabel = rows.length > 0
    ? `Saldos por titular en ${divisa}. ${rows.map((row) => `${row.titular_nombre}: ${formatCurrency(row.total_convertido, divisa)}`).join('; ')}.`
    : `Sin saldos por titular en ${divisa}.`;

  return (
    <>
      <div role="img" aria-label={chartLabel}>
        <ResponsiveContainer width="100%" height={340}>
          <BarChart data={rows} margin={{ top: 18, right: 36, left: 16, bottom: 18 }}>
            <CartesianGrid stroke="var(--chart-grid)" strokeDasharray="3 3" vertical={false} />
            <XAxis
              dataKey="titular_nombre"
              interval={0}
              angle={-18}
              textAnchor="end"
              height={72}
              padding={{ left: 16, right: 16 }}
            />
            <YAxis
              width={72}
              axisLine={false}
              tickLine={false}
              tickMargin={10}
              tickFormatter={(value) => formatCompactCurrency(Number(value), divisa)}
            />
            <Tooltip
              content={(props) => (
                <DashboardTooltip {...props} divisa={divisa} signed formatLabel={(label) => `Titular: ${String(label)}`} />
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
