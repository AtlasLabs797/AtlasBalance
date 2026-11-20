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
import { formatCurrency, formatPercent } from '@/utils/formatters';
import { DivisaSelector } from '@/components/dashboard/DivisaSelector';
import { EmptyState } from '@/components/common/EmptyState';
import { EvolucionChart } from '@/components/dashboard/EvolucionChart';
import { KpiCard } from '@/components/dashboard/KpiCard';
import { DashboardBlockError, DashboardBlockSkeleton, DashboardStaleNotice } from '@/components/dashboard/DashboardBlockStatus';
import { DivisasSinTipoCambioAviso } from '@/components/dashboard/DivisasSinTipoCambioAviso';
import { PeriodoSelector } from '@/components/dashboard/PeriodoSelector';
import { SaldoPorDivisaCard } from '@/components/dashboard/SaldoPorDivisaCard';
import { SignedAmount } from '@/components/common/SignedAmount';
import { extractErrorMessage } from '@/utils/errorMessage';
import { QUERY_STALE_TIMES } from '@/services/queryClient';
import { queryKeys } from '@/queries/queryKeys';
import '@/styles/layout/routes/dashboard-titular.css';

const PERIODOS: PeriodoDashboard[] = ['1m', '3m', '6m', '12m', '24m'];

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
  const titularError = titularQuery.isError && !titular;
  const evolucionError = evolucionQuery.isError && !evolucion;
  const saldosDivisaError = saldosDivisaQuery.isError && !saldosDivisa;
  const refreshFailed =
    (titularQuery.isError && Boolean(titular)) ||
    (evolucionQuery.isError && Boolean(evolucion)) ||
    (saldosDivisaQuery.isError && Boolean(saldosDivisa));
  const updatedAt = Math.min(
    ...[titularQuery.dataUpdatedAt, evolucionQuery.dataUpdatedAt, saldosDivisaQuery.dataUpdatedAt].filter((value) => value > 0),
  );
  const retryAll = () => {
    if (titularQuery.isError) void titularQuery.refetch();
    if (evolucionQuery.isError) void evolucionQuery.refetch();
    if (saldosDivisaQuery.isError) void saldosDivisaQuery.refetch();
  };
  const errorMsg = (query: { error: unknown }) => extractErrorMessage(query.error, 'No se pudo cargar el dashboard del titular.');

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

  const currency = titular?.divisa_principal ?? divisaPrincipal;

  return (
    <section className="dashboard-page page">
      <header className="dashboard-toolbar page-header">
        <div className="page-header__lead">
          <h1 className="page-header__title">{titular?.titular_nombre ?? 'Dashboard titular'}</h1>
          <p className="page-header__subtitle">Dashboard por titular</p>
        </div>
        <div className="dashboard-toolbar-actions">
          <button type="button" onClick={() => navigate(`/dashboard?periodo=${periodo}&divisa=${currency}`)}>
            Volver
          </button>
          <PeriodoSelector value={periodo} onChange={setPeriodo} />
          <DivisaSelector value={currency} options={divisaOptions} onChange={setDivisaPrincipal} />
        </div>
      </header>

      <DashboardStaleNotice updatedAt={updatedAt} failed={refreshFailed} onRetry={retryAll} />

      {titularQuery.isLoading ? (
        <DashboardBlockSkeleton label="resumen del titular" rows={3} />
      ) : titularError ? (
        <DashboardBlockError
          title="No se pudo cargar el resumen del titular."
          message={errorMsg(titularQuery)}
          onRetry={() => void titularQuery.refetch()}
          retrying={titularQuery.isFetching}
        />
      ) : titular ? (
        <div className="dashboard-kpi-grid">
          <KpiCard
            headingLevel="h2"
            title="Saldo total"
            featured
            value={
              <SignedAmount value={titular.total_convertido} showSign>
                {formatCurrency(titular.total_convertido, titular.divisa_principal)}
              </SignedAmount>
            }
            helper={variacionPct !== null ? (
              <span className={variacionPct >= 0 ? 'dashboard-variacion--positive' : 'dashboard-variacion--negative'}>
                {variacionPct > 0 ? '+' : ''}{formatPercent(variacionPct)} vs. inicio del período
              </span>
            ) : undefined}
          />
          {evolucionQuery.isLoading ? (
            <DashboardBlockSkeleton label="ingresos y egresos" rows={2} />
          ) : evolucionError ? (
            <DashboardBlockError
              title="No se pudieron cargar ingresos y egresos."
              message={errorMsg(evolucionQuery)}
              onRetry={() => void evolucionQuery.refetch()}
              retrying={evolucionQuery.isFetching}
            />
          ) : (
            <>
              <KpiCard
                headingLevel="h2"
                title="Ingresos período"
                value={
                  <SignedAmount value={periodTotals.ingresos} tone={Math.round(periodTotals.ingresos * 100) === 0 ? 'neutral' : undefined}>
                    {formatCurrency(periodTotals.ingresos, titular.divisa_principal)}
                  </SignedAmount>
                }
                helper={variacionIngPct !== null ? (
                  <span className={variacionIngPct >= 0 ? 'dashboard-variacion--positive' : 'dashboard-variacion--negative'}>
                    {variacionIngPct > 0 ? '+' : ''}{formatPercent(variacionIngPct)} vs. anterior
                  </span>
                ) : undefined}
              />
              <KpiCard
                headingLevel="h2"
                title="Egresos período"
                value={
                  <SignedAmount value={periodTotals.egresos} tone={Math.round(periodTotals.egresos * 100) === 0 ? 'neutral' : 'negative'}>
                    {formatCurrency(periodTotals.egresos, titular.divisa_principal)}
                  </SignedAmount>
                }
                helper={variacionEgrPct !== null ? (
                  <span className={variacionEgrPct <= 0 ? 'dashboard-variacion--positive' : 'dashboard-variacion--negative'}>
                    {variacionEgrPct > 0 ? '+' : ''}{formatPercent(variacionEgrPct)} vs. anterior
                  </span>
                ) : undefined}
              />
            </>
          )}
        </div>
      ) : null}

      {titular ? <DivisasSinTipoCambioAviso divisas={titular.divisas_sin_tipo_cambio} /> : null}

      <div className="dashboard-grid">
        {saldosDivisaQuery.isLoading ? (
          <DashboardBlockSkeleton label="saldos por divisa" rows={3} />
        ) : saldosDivisaError ? (
          <DashboardBlockError
            title="No se pudieron cargar los saldos por divisa."
            message={errorMsg(saldosDivisaQuery)}
            onRetry={() => void saldosDivisaQuery.refetch()}
            retrying={saldosDivisaQuery.isFetching}
          />
        ) : saldosDivisa ? (
          <SaldoPorDivisaCard items={saldosDivisa.divisas} divisaPrincipal={saldosDivisa.divisa_principal} />
        ) : null}

        <section className="dashboard-section">
          <header className="dashboard-card-header">
            <h2>Desglose por cuenta</h2>
          </header>

          {titularQuery.isLoading ? (
            <DashboardBlockSkeleton label="desglose por cuenta" rows={4} />
          ) : titularError ? (
            <DashboardBlockError
              title="No se pudo cargar el desglose por cuenta."
              message={errorMsg(titularQuery)}
              onRetry={() => void titularQuery.refetch()}
              retrying={titularQuery.isFetching}
            />
          ) : titular && titular.saldos_por_cuenta.length === 0 ? (
            <EmptyState
              variant="setup"
              title="Este titular no tiene cuentas visibles."
              subtitle="Asigna una cuenta o revisa tus permisos para ver el desglose."
            />
          ) : titular ? (
            <div className="dashboard-table-wrap">
              <table>
                <caption className="sr-only">Desglose por cuenta</caption>
                <thead>
                  <tr>
                    <th scope="col">Cuenta</th>
                    <th scope="col">País</th>
                    <th scope="col">Tipo</th>
                    <th scope="col">Saldo ({titular.divisa_principal})</th>
                    <th scope="col">Saldo original</th>
                    <th scope="col">Abrir</th>
                  </tr>
                </thead>
                <tbody>
                  {titular.saldos_por_cuenta.map((cuenta) => (
                    <tr key={cuenta.cuenta_id}>
                      <td>{cuenta.cuenta_nombre}</td>
                      <td>{cuenta.pais_nombre || 'Sin país'}</td>
                      <td>{cuenta.es_efectivo ? 'Efectivo' : 'Bancaria'}</td>
                      <td>
                        <SignedAmount value={cuenta.saldo_convertido} showSign>
                          {formatCurrency(cuenta.saldo_convertido, titular.divisa_principal)}
                        </SignedAmount>
                      </td>
                      <td>
                        <SignedAmount value={cuenta.saldo_actual} showSign>
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
          ) : null}
        </section>
      </div>

      <section className="dashboard-section">
        <header className="dashboard-card-header">
          <h2>Evolución</h2>
        </header>
        {evolucionQuery.isLoading ? (
          <DashboardBlockSkeleton label="evolución" rows={5} tall />
        ) : evolucionError ? (
          <DashboardBlockError
            title="No se pudo cargar la evolución."
            message={errorMsg(evolucionQuery)}
            onRetry={() => void evolucionQuery.refetch()}
            retrying={evolucionQuery.isFetching}
          />
        ) : evolucion && titular ? (
          <EvolucionChart
            points={evolucion.puntos}
            divisa={titular.divisa_principal}
            colors={titular.chart_colors}
          />
        ) : null}
      </section>
    </section>
  );
}
