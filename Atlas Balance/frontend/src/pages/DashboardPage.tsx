import { useEffect, useMemo, useState } from 'react';
import { Link, Navigate, useSearchParams } from 'react-router-dom';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import { usePermisosStore } from '@/stores/permisosStore';
import type {
  DashboardEvolucion,
  DashboardPrincipal,
  DashboardSaldosDivisa,
  PeriodoDashboard,
} from '@/types';
import { formatCurrency, formatDate } from '@/utils/formatters';
import { DivisaSelector } from '@/components/dashboard/DivisaSelector';
import { EmptyState } from '@/components/common/EmptyState';
import { EvolucionChart } from '@/components/dashboard/EvolucionChart';
import { KpiCard } from '@/components/dashboard/KpiCard';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import { PeriodoSelector } from '@/components/dashboard/PeriodoSelector';
import { SaldoPorDivisaCard } from '@/components/dashboard/SaldoPorDivisaCard';
import { SignedAmount } from '@/components/common/SignedAmount';

const PERIODOS: PeriodoDashboard[] = ['1m', '3m', '6m', '9m', '12m', '18m', '24m'];
const TIPO_TITULAR_LABELS = {
  EMPRESA: 'Empresa',
  AUTONOMO: 'Autonomo',
  PARTICULAR: 'Particular',
} as const;
const TIPO_TITULAR_ORDER = ['EMPRESA', 'AUTONOMO', 'PARTICULAR'] as const;

function parsePeriodo(value: string | null): PeriodoDashboard {
  return PERIODOS.includes(value as PeriodoDashboard) ? (value as PeriodoDashboard) : '1m';
}

export default function DashboardPage() {
  const usuario = useAuthStore((state) => state.usuario);
  const canViewDashboard = usePermisosStore((state) => state.canViewDashboard);
  const [searchParams, setSearchParams] = useSearchParams();

  const [periodo, setPeriodo] = useState<PeriodoDashboard>(() => parsePeriodo(searchParams.get('periodo')));
  const [divisaPrincipal, setDivisaPrincipal] = useState(() => searchParams.get('divisa') ?? 'EUR');
  const [principal, setPrincipal] = useState<DashboardPrincipal | null>(null);
  const [evolucion, setEvolucion] = useState<DashboardEvolucion | null>(null);
  const [saldosDivisa, setSaldosDivisa] = useState<DashboardSaldosDivisa | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const allowed = usuario?.rol === 'ADMIN' || (usuario?.rol === 'GERENTE' && canViewDashboard());
  const divisaOptions = useMemo(() => {
    const options = new Set<string>();
    Object.keys(principal?.saldos_por_divisa ?? {}).forEach((item) => options.add(item));
    if (principal?.divisa_principal) {
      options.add(principal.divisa_principal);
    }
    if (options.size === 0) {
      options.add('EUR');
      options.add('USD');
      options.add('MXN');
      options.add('DOP');
    }

    return Array.from(options).sort();
  }, [principal]);

  const periodTotals = useMemo(() => {
    if (!evolucion?.puntos?.length) {
      return {
        ingresos: principal?.ingresos_mes ?? 0,
        egresos: principal?.egresos_mes ?? 0,
      };
    }

    return evolucion.puntos.reduce(
      (acc, point) => ({
        ingresos: acc.ingresos + (Number.isFinite(point.ingresos) ? point.ingresos : 0),
        egresos: acc.egresos + (Number.isFinite(point.egresos) ? point.egresos : 0),
      }),
      { ingresos: 0, egresos: 0 }
    );
  }, [evolucion, principal?.egresos_mes, principal?.ingresos_mes]);
  const saldosPorTipo = useMemo(
    () =>
      TIPO_TITULAR_ORDER
        .map((tipo) => ({
          tipo,
          items: (principal?.saldos_por_titular ?? [])
            .filter((item) => item.tipo_titular === tipo)
            .sort((a, b) => b.total_convertido - a.total_convertido),
        }))
        .filter((group) => group.items.length > 0),
    [principal?.saldos_por_titular],
  );

  useEffect(() => {
    const next = new URLSearchParams();
    next.set('periodo', periodo);
    next.set('divisa', divisaPrincipal);
    setSearchParams(next, { replace: true });
  }, [divisaPrincipal, periodo, setSearchParams]);

  useEffect(() => {
    if (!allowed) {
      return;
    }

    let mounted = true;

    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const [principalRes, evolucionRes, divisaRes] = await Promise.all([
          api.get<DashboardPrincipal>('/dashboard/principal', { params: { divisaPrincipal } }),
          api.get<DashboardEvolucion>('/dashboard/evolucion', { params: { periodo, divisaPrincipal } }),
          api.get<DashboardSaldosDivisa>('/dashboard/saldos-divisa', { params: { divisaPrincipal } }),
        ]);

        if (!mounted) {
          return;
        }

        setPrincipal(principalRes.data);
        setEvolucion(evolucionRes.data);
        setSaldosDivisa(divisaRes.data);
        if (principalRes.data.divisa_principal && principalRes.data.divisa_principal !== divisaPrincipal) {
          setDivisaPrincipal(principalRes.data.divisa_principal);
        }
      } catch (err: unknown) {
        if (!mounted) {
          return;
        }

        setError(err instanceof Error ? err.message : 'No se pudo cargar el dashboard.');
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
  }, [allowed, periodo, divisaPrincipal]);

  if (!allowed) {
    return <Navigate to="/extractos" replace />;
  }

  if (loading) {
    return <PageSkeleton rows={4} variant="dashboard" />;
  }

  if (error || !principal || !evolucion || !saldosDivisa) {
    return (
      <div className="page-placeholder">
        <h1>Dashboard</h1>
        <p>{error ?? 'No hay datos disponibles.'}</p>
      </div>
    );
  }

  return (
    <section className="dashboard-page">
      <header className="dashboard-toolbar">
        <div>
          <h1>Dashboard principal</h1>
          <p className="dashboard-subtitle">Saldos consolidados, movimiento del periodo y exposicion por titular.</p>
        </div>
        <div className="dashboard-toolbar-actions">
          <PeriodoSelector value={periodo} onChange={setPeriodo} />
          <DivisaSelector value={principal.divisa_principal} options={divisaOptions} onChange={setDivisaPrincipal} />
        </div>
      </header>

      <div className="dashboard-kpi-grid">
        <KpiCard
          title="Saldo total"
          featured
          helper={`Divisa base ${principal.divisa_principal}`}
          value={
            <SignedAmount value={principal.total_convertido}>
              {formatCurrency(principal.total_convertido, principal.divisa_principal)}
            </SignedAmount>
          }
        />
        <KpiCard
          title="Ingresos período"
          value={
            <SignedAmount value={periodTotals.ingresos}>
              {formatCurrency(periodTotals.ingresos, principal.divisa_principal)}
            </SignedAmount>
          }
        />
        <KpiCard
          title="Egresos período"
          value={
            <SignedAmount value={periodTotals.egresos} tone="negative">
              {formatCurrency(periodTotals.egresos, principal.divisa_principal)}
            </SignedAmount>
          }
        />
      </div>

      <section className="dashboard-card dashboard-plazo-card">
        <header className="dashboard-card-header">
          <h2>Plazos fijos</h2>
          <span className="dashboard-card-meta">{principal.plazos_fijos.total_cuentas} cuentas</span>
        </header>
        <div className="dashboard-plazo-metrics">
          <div>
            <span>Monto total</span>
            <strong>
              <SignedAmount value={principal.plazos_fijos.monto_total_convertido}>
                {formatCurrency(principal.plazos_fijos.monto_total_convertido, principal.divisa_principal)}
              </SignedAmount>
            </strong>
          </div>
          <div>
            <span>Intereses aprox.</span>
            <strong>
              <SignedAmount value={principal.plazos_fijos.intereses_previstos_convertidos}>
                {formatCurrency(principal.plazos_fijos.intereses_previstos_convertidos, principal.divisa_principal)}
              </SignedAmount>
            </strong>
          </div>
          <div>
            <span>Proximo vencimiento</span>
            <strong>
              {principal.plazos_fijos.dias_hasta_proximo_vencimiento === null
                ? 'Sin fecha'
                : `${principal.plazos_fijos.dias_hasta_proximo_vencimiento} dias`}
            </strong>
            {principal.plazos_fijos.proximo_vencimiento ? (
              <small>{formatDate(principal.plazos_fijos.proximo_vencimiento)}</small>
            ) : null}
          </div>
        </div>
      </section>

      <div className="dashboard-grid">
        <SaldoPorDivisaCard items={saldosDivisa.divisas} divisaPrincipal={saldosDivisa.divisa_principal} />

        <section className="dashboard-card">
          <header className="dashboard-card-header">
            <h2>Saldos por titular</h2>
          </header>

          {principal.saldos_por_titular.length === 0 ? (
            <EmptyState title="No hay titulares con saldos." />
          ) : (
            <div className="dashboard-titular-groups">
              {saldosPorTipo.map((group) => (
                <article className="dashboard-titular-group" key={group.tipo}>
                  <h3>{TIPO_TITULAR_LABELS[group.tipo]}</h3>
                  <div className="dashboard-titular-list">
                    {group.items.map((item) => (
                      <Link
                        key={item.titular_id}
                        className="dashboard-titular-item"
                        to={`/dashboard/titular/${item.titular_id}?periodo=${periodo}&divisa=${principal.divisa_principal}`}
                      >
                        <span>{item.titular_nombre}</span>
                        <strong>
                          <SignedAmount value={item.total_convertido}>
                            {formatCurrency(item.total_convertido, principal.divisa_principal)}
                          </SignedAmount>
                        </strong>
                        <small>
                          Disponible {formatCurrency(item.saldo_disponible_convertido ?? item.total_convertido, principal.divisa_principal)}
                          {' · '}
                          Inmovilizado {formatCurrency(item.saldo_inmovilizado_convertido ?? 0, principal.divisa_principal)}
                        </small>
                      </Link>
                    ))}
                  </div>
                </article>
              ))}
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
          divisa={principal.divisa_principal}
          colors={principal.chart_colors}
        />
      </section>
    </section>
  );
}
