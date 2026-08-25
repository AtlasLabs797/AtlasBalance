import { useEffect, useMemo, useState } from 'react';
import { Link, Navigate, useNavigate, useParams, useSearchParams } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
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
import { QUERY_STALE_TIMES } from '@/services/queryClient';
import { queryKeys } from '@/queries/queryKeys';

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
  usePermisosStore((state) => state.permisos);
  const selectedPaisId = usePaisScopeStore((state) => state.selectedPaisId);

  const [periodo, setPeriodo] = useState<PeriodoDashboard>(() => parsePeriodo(searchParams.get('periodo')));
  const [divisaPrincipal, setDivisaPrincipal] = useState(() => searchParams.get('divisa') ?? 'EUR');

  const allowed = usuario?.rol === 'ADMIN' || canViewDashboard();
  const usuarioId = usuario?.id ?? '';

  const titularQuery = useQuery({
    queryKey: queryKeys.dashboard.titular({ usuarioId, titularId: id ?? '', divisaPrincipal, paisId: selectedPaisId || null }),
    queryFn: ({ signal }) =>
      api.get<DashboardTitular>(`/dashboard/titular/${id}`, {
        params: { divisaPrincipal, paisId: selectedPaisId || undefined },
        signal,
      }).then((res) => res.data),
    enabled: Boolean(allowed && id && usuarioId),
    staleTime: QUERY_STALE_TIMES.DASHBOARD_MS,
  });

  const evolucionQuery = useQuery({
    queryKey: queryKeys.dashboard.evolucion({ usuarioId, paisId: selectedPaisId || null, divisaPrincipal, periodo, titularId: id ?? null }),
    queryFn: ({ signal }) =>
      api.get<DashboardEvolucion>('/dashboard/evolucion', {
        params: { periodo, divisaPrincipal, titularId: id, paisId: selectedPaisId || undefined },
        signal,
      }).then((res) => res.data),
    enabled: Boolean(allowed && id && usuarioId),
    staleTime: QUERY_STALE_TIMES.DASHBOARD_MS,
  });

  const saldosDivisaQuery = useQuery({
    queryKey: queryKeys.dashboard.saldosDivisa({ usuarioId, paisId: selectedPaisId || null, divisaPrincipal, titularId: id ?? null }),
    queryFn: ({ signal }) =>
      api.get<DashboardSaldosDivisa>('/dashboard/saldos-divisa', {
        params: { divisaPrincipal, titularId: id, paisId: selectedPaisId || undefined },
        signal,
      }).then((res) => res.data),
    enabled: Boolean(allowed && id && usuarioId),
    staleTime: QUERY_STALE_TIMES.DASHBOARD_MS,
  });

  const titular = titularQuery.data ?? null;
  const evolucion = evolucionQuery.data ?? null;
  const saldosDivisa = saldosDivisaQuery.data ?? null;
  const loading = titularQuery.isLoading || evolucionQuery.isLoading || saldosDivisaQuery.isLoading;
  const error =
    titularQuery.error ? extractErrorMessage(titularQuery.error, 'No se pudo cargar el dashboard del titular.') :
    evolucionQuery.error ? extractErrorMessage(evolucionQuery.error, 'No se pudo cargar el dashboard del titular.') :
    saldosDivisaQuery.error ? extractErrorMessage(saldosDivisaQuery.error, 'No se pudo cargar el dashboard del titular.') :
    null;

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

  const variacionPct = useMemo(() => {
    if (!evolucion?.puntos?.length || !evolucion.saldo_inicio_periodo) return null;
    const saldoFinal = evolucion.puntos[evolucion.puntos.length - 1].saldo;
    return ((saldoFinal - evolucion.saldo_inicio_periodo) / Math.abs(evolucion.saldo_inicio_periodo)) * 100;
  }, [evolucion]);

  const variacionIngPct = useMemo(() => {
    if (!evolucion?.ingresos_anterior) return null;
    return ((periodTotals.ingresos - evolucion.ingresos_anterior) / evolucion.ingresos_anterior) * 100;
  }, [evolucion?.ingresos_anterior, periodTotals.ingresos]);

  const variacionEgrPct = useMemo(() => {
    if (!evolucion?.egresos_anterior) return null;
    return ((periodTotals.egresos - evolucion.egresos_anterior) / evolucion.egresos_anterior) * 100;
  }, [evolucion?.egresos_anterior, periodTotals.egresos]);

  useEffect(() => {
    const next = new URLSearchParams();
    next.set('periodo', periodo);
    next.set('divisa', divisaPrincipal);
    setSearchParams(next, { replace: true });
  }, [divisaPrincipal, periodo, setSearchParams]);

  useEffect(() => {
    if (titular?.divisa_principal && titular.divisa_principal !== divisaPrincipal) {
      setDivisaPrincipal(titular.divisa_principal);
    }
  }, [titular, divisaPrincipal]);

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
          helper={variacionPct !== null ? (
            <span className={variacionPct >= 0 ? 'dashboard-variacion--positive' : 'dashboard-variacion--negative'}>
              {variacionPct >= 0 ? '+' : ''}{variacionPct.toFixed(1)}% vs. inicio del período
            </span>
          ) : undefined}
        />
        <KpiCard
          title="Ingresos período"
          value={
            <SignedAmount value={periodTotals.ingresos}>
              {formatCurrency(periodTotals.ingresos, titular.divisa_principal)}
            </SignedAmount>
          }
          helper={variacionIngPct !== null ? (
            <span className={variacionIngPct >= 0 ? 'dashboard-variacion--positive' : 'dashboard-variacion--negative'}>
              {variacionIngPct >= 0 ? '+' : ''}{variacionIngPct.toFixed(1)}% vs. anterior
            </span>
          ) : undefined}
        />
        <KpiCard
          title="Egresos período"
          value={
            <SignedAmount value={periodTotals.egresos} tone="negative">
              {formatCurrency(periodTotals.egresos, titular.divisa_principal)}
            </SignedAmount>
          }
          helper={variacionEgrPct !== null ? (
            <span className={variacionEgrPct <= 0 ? 'dashboard-variacion--positive' : 'dashboard-variacion--negative'}>
              {variacionEgrPct >= 0 ? '+' : ''}{variacionEgrPct.toFixed(1)}% vs. anterior
            </span>
          ) : undefined}
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
                    <th>País</th>
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
                      <td>{cuenta.pais_nombre || 'Sin pais'}</td>
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
                        {canViewCuenta(cuenta.cuenta_id, titular.titular_id, cuenta.pais_id) ? (
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
