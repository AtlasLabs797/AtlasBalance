import type { DashboardSaldoDivisa } from '@/types';
import { SignedAmount } from '@/components/common/SignedAmount';
import { formatCurrency } from '@/utils/formatters';

interface SaldoPorDivisaCardProps {
  items: DashboardSaldoDivisa[];
  divisaPrincipal: string;
  className?: string;
}

export function SaldoPorDivisaCard({ items, divisaPrincipal, className }: SaldoPorDivisaCardProps) {
  const orderedItems = [
    ...items.filter((item) => item.divisa === divisaPrincipal),
    ...items.filter((item) => item.divisa !== divisaPrincipal),
  ];

  return (
    <section className={`dashboard-card${className ? ` ${className}` : ''}`}>
      <header className="dashboard-card-header">
        <h2>Saldos por divisa</h2>
      </header>

      {items.length === 0 ? (
        <p className="dashboard-empty">No hay saldos disponibles.</p>
      ) : (
        <div className="dashboard-divisa-list">
          {orderedItems.map((item) => (
            <article key={item.divisa} className="dashboard-divisa-item">
              <header className="dashboard-divisa-item-header">
                <h3>{item.divisa}</h3>
                {item.divisa === divisaPrincipal ? <span>Base</span> : null}
              </header>
              <p className="dashboard-divisa-total">
                <SignedAmount value={item.saldo_total ?? item.saldo}>
                  {formatCurrency(item.saldo_total ?? item.saldo, item.divisa)}
                </SignedAmount>
              </p>
              <dl className="dashboard-divisa-breakdown">
                <div>
                  <dt>Disponible</dt>
                  <dd>
                    <SignedAmount value={item.saldo_disponible ?? item.saldo}>
                      {formatCurrency(item.saldo_disponible ?? item.saldo, item.divisa)}
                    </SignedAmount>
                  </dd>
                </div>
                <div>
                  <dt>Inmovilizado</dt>
                  <dd>
                    <SignedAmount value={item.saldo_inmovilizado ?? 0}>
                      {formatCurrency(item.saldo_inmovilizado ?? 0, item.divisa)}
                    </SignedAmount>
                  </dd>
                </div>
              </dl>
              {item.divisa !== divisaPrincipal ? (
                <span className="dashboard-divisa-converted">
                  Equivale a{' '}
                  <SignedAmount value={item.saldo_total_convertido ?? item.saldo_convertido}>
                    {formatCurrency(item.saldo_total_convertido ?? item.saldo_convertido, divisaPrincipal)}
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
