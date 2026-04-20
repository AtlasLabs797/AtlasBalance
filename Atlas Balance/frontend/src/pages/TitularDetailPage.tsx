import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import { SignedAmount } from '@/components/common/SignedAmount';
import { PeriodoSelector } from '@/components/dashboard/PeriodoSelector';
import api from '@/services/api';
import type { PeriodoDashboard, TitularConCuentas } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatCurrency, formatDateTime } from '@/utils/formatters';

export default function TitularDetailPage() {
  const { id } = useParams();
  const [data, setData] = useState<TitularConCuentas | null>(null);
  const [periodo, setPeriodo] = useState<PeriodoDashboard>('1m');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!id) return;
    setLoading(true);
    setError(null);
    void api.get<TitularConCuentas>(`/extractos/titulares/${id}/cuentas`, { params: { periodo } })
      .then((res) => setData(res.data))
      .catch((err) => setError(extractErrorMessage(err, 'No se pudo cargar el titular')))
      .finally(() => setLoading(false));
  }, [id, periodo]);

  if (loading) return <PageSkeleton rows={3} />;
  if (error) return <p className="auth-error">{error}</p>;
  if (!data) return <EmptyState title="Sin datos." />;

  return (
    <section className="page-placeholder">
      <header className="dashboard-toolbar">
        <div>
          <h1>{data.titular_nombre}</h1>
          <p className="dashboard-subtitle">Detalle por cuenta</p>
        </div>
        <PeriodoSelector value={periodo} onChange={setPeriodo} />
      </header>
      <table>
        <thead>
          <tr>
            <th>Cuenta</th>
            <th>Divisa</th>
            <th>Saldo</th>
            <th>Ingresos período</th>
            <th>Egresos período</th>
            <th>Últ. Actualización</th>
          </tr>
        </thead>
        <tbody>
          {data.cuentas.map((c) => (
            <tr key={c.cuenta_id}>
              <td>{c.cuenta_nombre}</td>
              <td>{c.divisa}</td>
              <td>
                <SignedAmount value={c.saldo_actual}>{formatCurrency(c.saldo_actual, c.divisa)}</SignedAmount>
              </td>
              <td>
                <SignedAmount value={c.ingresos_mes}>{formatCurrency(c.ingresos_mes, c.divisa)}</SignedAmount>
              </td>
              <td>
                <SignedAmount value={c.egresos_mes}>{formatCurrency(c.egresos_mes, c.divisa)}</SignedAmount>
              </td>
              <td>{c.ultima_actualizacion ? formatDateTime(c.ultima_actualizacion) : '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
