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
  return (
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
          formatter={(value: number) => formatCurrency(value, divisa)}
          labelFormatter={(value) => `Titular: ${value}`}
        />
        <Bar dataKey="total_convertido" name={`Saldo total (${divisa})`}>
          {rows.map((item) => (
            <Cell
              key={item.titular_id}
              fill={item.total_convertido >= 0 ? 'var(--color-success)' : 'var(--color-danger)'}
            />
          ))}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  );
}
