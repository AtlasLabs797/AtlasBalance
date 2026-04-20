import type { DashboardSaldoDivisa } from '@/types';
import { SignedAmount } from '@/components/common/SignedAmount';
import { formatCurrency } from '@/utils/formatters';

interface SaldoPorDivisaCardProps {
  items: DashboardSaldoDivisa[];
  divisaPrincipal: string;
}

export function SaldoPorDivisaCard({ items, divisaPrincipal }: SaldoPorDivisaCardProps) {
  return (
    <section className="dashboard-card">
      <header className="dashboard-card-header">
        <h2>Saldos por divisa</h2>
      </header>

      {items.length === 0 ? (
        <p className="dashboard-empty">No hay saldos disponibles.</p>
      ) : (
        <div className="dashboard-divisa-list">
          {items.map((item) => (
            <article key={item.divisa} className="dashboard-divisa-item">
              <h3>{item.divisa}</h3>
              <p>
                <SignedAmount value={item.saldo}>{formatCurrency(item.saldo, item.divisa)}</SignedAmount>
              </p>
              {item.divisa !== divisaPrincipal ? (
                <span className="dashboard-divisa-converted">
                  Equivale a{' '}
                  <SignedAmount value={item.saldo_convertido}>
                    {formatCurrency(item.saldo_convertido, divisaPrincipal)}
                  </SignedAmount>
                </span>
              ) : null}
            </article>
          ))}
        </div>
      )}
    </section>
  );
}
