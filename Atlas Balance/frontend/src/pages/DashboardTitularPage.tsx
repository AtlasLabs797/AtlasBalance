import { useEffect, useMemo, useState } from 'react';
import { Link, Navigate, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import { usePermisosStore } from '@/stores/permisosStore';
import type {
  DashboardEvolucion,
  DashboardSaldosDivisa,
  DashboardTitular,
  PeriodoDashboard,
} from '@/types';
import { formatCurrency } from '@/utils/formatters';
import { DivisaSelector } from '@/components/dashboard/DivisaSelector';
import { EmptyState } from '@/components/common/EmptyState';
import { EvolucionChart } from '@/components/dashboard/EvolucionChart';
import { KpiCard } from '@/components/dashboard/KpiCard';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import { PeriodoSelector } from '@/components/dashboard/PeriodoSelector';
import { SaldoPorDivisaCard } from '@/components/dashboard/SaldoPorDivisaCard';
import { SignedAmount } from '@/components/common/SignedAmount';
import { extractErrorMessage } from '@/utils/errorMessage';

const PERIODOS: PeriodoDashboard[] = ['1m', '3m', '6m', '9m', '12m', '18m', '24m'];

function parsePeriodo(value: string | null): PeriodoDashboard {
  return PERIODOS.includes(value as PeriodoDashboard) ? (value as PeriodoDashboard) : '1m';
}

export default function DashboardTitularPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const usuario = useAuthStore((state) => state.usuario);
  const canViewDashboard = usePermisosStore((state) => state.canViewDashboard);
  const canViewCuenta = usePermisosStore((state) => state.canViewCuenta);

  const [periodo, setPeriodo] = useState<PeriodoDashboard>(() => parsePeriodo(searchParams.get('periodo')));
  const [divisaPrincipal, setDivisaPrincipal] = useState(() => searchParams.get('divisa') ?? 'EUR');
  const [titular, setTitular] = useState<DashboardTitular | null>(null);
  const [evolucion, setEvolucion] = useState<DashboardEvolucion | null>(null);
  const [saldosDivisa, setSaldosDivisa] = useState<DashboardSaldosDivisa | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const allowed = usuario?.rol === 'ADMIN' || (usuario?.rol === 'GERENTE' && canViewDashboard());
  const divisaOptions = useMemo(() => {
    const options = new Set<string>();
    Object.keys(titular?.saldos_por_divisa ?? {}).forEach((item) => options.add(item));
    if (titular?.divisa_principal) {
      options.add(titular.divisa_principal);
    }
    if (options.size === 0) {
      options.add('EUR');
      options.add('USD');
      options.add('MXN');
      options.add('DOP');
    }

    return Array.from(options).sort();
  }, [titular]);

  const periodTotals = useMemo(() => {
    if (!evolucion?.puntos?.length) {
      return {
        ingresos: titular?.ingresos_mes ?? 0,
        egresos: titular?.egresos_mes ?? 0,
      };
    }

    return evolucion.puntos.reduce(
      (acc, point) => ({
        ingresos: acc.ingresos + (Number.isFinite(point.ingresos) ? point.ingresos : 0),
        egresos: acc.egresos + (Number.isFinite(point.egresos) ? point.egresos : 0),
      }),
      { ingresos: 0, egresos: 0 }
    );
  }, [evolucion, titular?.egresos_mes, titular?.ingresos_mes]);

  useEffect(() => {
    const next = new URLSearchParams();
    next.set('periodo', periodo);
    next.set('divisa', divisaPrincipal);
    setSearchParams(next, { replace: true });
  }, [divisaPrincipal, periodo, setSearchParams]);

  useEffect(() => {
    if (!allowed || !id) {
      return;
    }

    let mounted = true;

    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const [titularRes, evolucionRes, saldosDivisaRes] = await Promise.all([
          api.get<DashboardTitular>(`/dashboard/titular/${id}`, { params: { divisaPrincipal } }),
          api.get<DashboardEvolucion>('/dashboard/evolucion', {
            params: { periodo, divisaPrincipal, titularId: id },
          }),
          api.get<DashboardSaldosDivisa>('/dashboard/saldos-divisa', {
            params: { divisaPrincipal, titularId: id },
          }),
        ]);

        if (!mounted) {
          return;
        }

        setTitular(titularRes.data);
        setEvolucion(evolucionRes.data);
        setSaldosDivisa(saldosDivisaRes.data);
        if (titularRes.data.divisa_principal && titularRes.data.divisa_principal !== divisaPrincipal) {
          setDivisaPrincipal(titularRes.data.divisa_principal);
        }
      } catch (err: unknown) {
        if (!mounted) {
          return;
        }

        setError(extractErrorMessage(err, 'No se pudo cargar el dashboard del titular.'));
      } finally {
        if (mounted) {
          setLoading(false);
        }
      }
    };

    load();

    return () => {
      mounted = false;
    };
  }, [allowed, id, periodo, divisaPrincipal]);

  if (!allowed) {
    return <Navigate to="/extractos" replace />;
  }

  if (!id) {
    return <Navigate to="/dashboard" replace />;
  }

  if (loading) {
    return <PageSkeleton rows={4} />;
  }

  if (error || !titular || !evolucion || !saldosDivisa) {
    return (
      <div className="page-placeholder">
        <h1>Dashboard Titular</h1>
        <p>{error ?? 'Carga cuentas o extractos para ver el dashboard de este titular.'}</p>
      </div>
    );
  }

  return (
    <section className="dashboard-page">
      <header className="dashboard-toolbar">
        <div>
          <h1>{titular.titular_nombre}</h1>
          <p className="dashboard-subtitle">Dashboard por titular</p>
        </div>
        <div className="dashboard-toolbar-actions">
          <button type="button" onClick={() => navigate(`/dashboard?periodo=${periodo}&divisa=${titular.divisa_principal}`)}>
            Volver
          </button>
          <PeriodoSelector value={periodo} onChange={setPeriodo} />
          <DivisaSelector value={titular.divisa_principal} options={divisaOptions} onChange={setDivisaPrincipal} />
        </div>
      </header>

      <div className="dashboard-kpi-grid">
        <KpiCard
          title="Saldo total"
          value={
            <SignedAmount value={titular.total_convertido}>
              {formatCurrency(titular.total_convertido, titular.divisa_principal)}
            </SignedAmount>
          }
        />
        <KpiCard
          title="Ingresos período"
          value={
            <SignedAmount value={periodTotals.ingresos}>
              {formatCurrency(periodTotals.ingresos, titular.divisa_principal)}
            </SignedAmount>
          }
        />
        <KpiCard
          title="Egresos período"
          value={
            <SignedAmount value={periodTotals.egresos} tone="negative">
              {formatCurrency(periodTotals.egresos, titular.divisa_principal)}
            </SignedAmount>
          }
        />
      </div>

      <div className="dashboard-grid">
        <SaldoPorDivisaCard items={saldosDivisa.divisas} divisaPrincipal={saldosDivisa.divisa_principal} />

        <section className="dashboard-card">
          <header className="dashboard-card-header">
            <h2>Desglose por cuenta</h2>
          </header>

          {titular.saldos_por_cuenta.length === 0 ? (
            <EmptyState
              title="Este titular no tiene cuentas visibles."
              subtitle="Asigna una cuenta o revisa tus permisos para ver el desglose."
            />
          ) : (
            <div className="dashboard-table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Cuenta</th>
                    <th>Tipo</th>
                    <th>Saldo ({titular.divisa_principal})</th>
                    <th>Saldo original</th>
                    <th>Abrir</th>
                  </tr>
                </thead>
                <tbody>
                  {titular.saldos_por_cuenta.map((cuenta) => (
                    <tr key={cuenta.cuenta_id}>
                      <td>{cuenta.cuenta_nombre}</td>
                      <td>{cuenta.es_efectivo ? 'Efectivo' : 'Bancaria'}</td>
                      <td>
                        <SignedAmount value={cuenta.saldo_convertido}>
                          {formatCurrency(cuenta.saldo_convertido, titular.divisa_principal)}
                        </SignedAmount>
                      </td>
                      <td>
                        <SignedAmount value={cuenta.saldo_actual}>
                          {formatCurrency(cuenta.saldo_actual, cuenta.divisa)}
                        </SignedAmount>
                      </td>
                      <td>
                        {canViewCuenta(cuenta.cuenta_id, titular.titular_id) ? (
                          <Link
                            to={`/dashboard/cuenta/${cuenta.cuenta_id}`}
                            className="dashboard-open-link"
                            aria-label={`Abrir dashboard de cuenta ${cuenta.cuenta_nombre}`}
                          >
                            Abrir
                          </Link>
                        ) : (
                          <span className="dashboard-open-link dashboard-open-link--disabled">Sin acceso</span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      </div>

      <section className="dashboard-card">
        <header className="dashboard-card-header">
          <h2>Evolución</h2>
        </header>
        <EvolucionChart
          points={evolucion.puntos}
          divisa={titular.divisa_principal}
          colors={titular.chart_colors}
        />
      </section>
    </section>
  );
}
