import { useEffect, useMemo, useState } from 'react';
import { Link, Navigate, useSearchParams } from 'react-router-dom';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
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
import { ConcentracionDonutCharts } from '@/components/dashboard/ConcentracionDonutCharts';
import { EvolucionChart } from '@/components/dashboard/EvolucionChart';
import { KpiCard } from '@/components/dashboard/KpiCard';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import { PeriodoSelector } from '@/components/dashboard/PeriodoSelector';
import { SaldoPorDivisaCard } from '@/components/dashboard/SaldoPorDivisaCard';
import { SignedAmount } from '@/components/common/SignedAmount';
import { extractErrorMessage } from '@/utils/errorMessage';

const PERIODOS: PeriodoDashboard[] = ['1m', '3m', '6m', '9m', '12m', '18m', '24m'];
const TIPO_TITULAR_LABELS = {
  EMPRESA: 'Empresa',
  AUTONOMO: 'Autónomo',
  PARTICULAR: 'Particular',
} as const;
const TIPO_TITULAR_ORDER = ['EMPRESA', 'AUTONOMO', 'PARTICULAR'] as const;

function parsePeriodo(value: string | null): PeriodoDashboard {
  return PERIODOS.includes(value as PeriodoDashboard) ? (value as PeriodoDashboard) : '1m';
}

export default function DashboardPage() {
  const usuario = useAuthStore((state) => state.usuario);
  const canViewDashboard = usePermisosStore((state) => state.canViewDashboard);
  usePermisosStore((state) => state.permisos);
  const [searchParams, setSearchParams] = useSearchParams();
  const selectedPaisId = usePaisScopeStore((state) => state.selectedPaisId);

  const [periodo, setPeriodo] = useState<PeriodoDashboard>(() => parsePeriodo(searchParams.get('periodo')));
  const [divisaPrincipal, setDivisaPrincipal] = useState(() => searchParams.get('divisa') ?? 'EUR');
  const [principal, setPrincipal] = useState<DashboardPrincipal | null>(null);
  const [evolucion, setEvolucion] = useState<DashboardEvolucion | null>(null);
  const [saldosDivisa, setSaldosDivisa] = useState<DashboardSaldosDivisa | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const allowed = usuario?.rol === 'ADMIN' || canViewDashboard();
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

  const liquidezConsolidada = useMemo(() => {
    if (!principal?.saldos_por_titular?.length) return null;
    const disponible = principal.saldos_por_titular.reduce((acc, t) => acc + (t.saldo_disponible_convertido ?? t.total_convertido), 0);
    const inmovilizado = principal.saldos_por_titular.reduce((acc, t) => acc + (t.saldo_inmovilizado_convertido ?? 0), 0);
    return { disponible, inmovilizado };
  }, [principal]);

  const variacionDispPct = useMemo(() => {
    if (!evolucion?.disponible_inicio_periodo || !liquidezConsolidada) return null;
    return ((liquidezConsolidada.disponible - evolucion.disponible_inicio_periodo) / Math.abs(evolucion.disponible_inicio_periodo)) * 100;
  }, [evolucion?.disponible_inicio_periodo, liquidezConsolidada]);

  const variacionInmovPct = useMemo(() => {
    if (!evolucion?.inmovilizado_inicio_periodo || !liquidezConsolidada) return null;
    return ((liquidezConsolidada.inmovilizado - evolucion.inmovilizado_inicio_periodo) / Math.abs(evolucion.inmovilizado_inicio_periodo)) * 100;
  }, [evolucion?.inmovilizado_inicio_periodo, liquidezConsolidada]);
  const saldosPorTipo = useMemo(
    () =>
      TIPO_TITULAR_ORDER.map((tipo) => ({
        tipo,
        items: (principal?.saldos_por_titular ?? [])
          .filter((item) => item.tipo_titular === tipo)
          .sort((a, b) => b.total_convertido - a.total_convertido),
      })),
    [principal?.saldos_por_titular],
  );
  const resumenConsolidado = useMemo(() => {
    const cuentas = principal?.saldos_por_cuenta?.length ?? 0;
    const bancos = new Set((principal?.saldos_por_cuenta ?? []).map((cuenta) => cuenta.banco_nombre).filter(Boolean)).size;
    const divisas = saldosDivisa?.divisas?.length ?? Object.keys(principal?.saldos_por_divisa ?? {}).length;

    return { cuentas, bancos, divisas };
  }, [principal?.saldos_por_cuenta, principal?.saldos_por_divisa, saldosDivisa?.divisas]);
  const disponiblePct = useMemo(() => {
    if (!liquidezConsolidada || principal?.total_convertido === 0) {
      return null;
    }

    return (liquidezConsolidada.disponible / Math.abs(principal?.total_convertido ?? 1)) * 100;
  }, [liquidezConsolidada, principal?.total_convertido]);

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
          api.get<DashboardPrincipal>('/dashboard/principal', { params: { divisaPrincipal, paisId: selectedPaisId || undefined } }),
          api.get<DashboardEvolucion>('/dashboard/evolucion', { params: { periodo, divisaPrincipal, paisId: selectedPaisId || undefined } }),
          api.get<DashboardSaldosDivisa>('/dashboard/saldos-divisa', { params: { divisaPrincipal, paisId: selectedPaisId || undefined } }),
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

        setError(extractErrorMessage(err, 'No se pudo cargar el dashboard.'));
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
  }, [allowed, periodo, divisaPrincipal, selectedPaisId]);

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
        <p>{error ?? 'Carga cuentas o extractos para ver saldos y evolución.'}</p>
      </div>
    );
  }

  return (
    <section className="dashboard-page">
      <header className="dashboard-toolbar">
        <div>
          <h1>Dashboard</h1>
          <p className="dashboard-subtitle">Saldos consolidados, movimiento del periodo y exposición por titular.</p>
        </div>
        <div className="dashboard-toolbar-actions">
          <PeriodoSelector value={periodo} onChange={setPeriodo} />
          <DivisaSelector value={principal.divisa_principal} options={divisaOptions} onChange={setDivisaPrincipal} />
        </div>
      </header>

      <nav className="dashboard-quick-actions" aria-label="Acciones principales">
        <Link to="/importacion">Importar extractos</Link>
        <Link to="/alertas">Revisar alertas</Link>
        <Link to="/conciliacion">Conciliar pendientes</Link>
      </nav>

      <section className="dashboard-hero-card">
        <div className="dashboard-hero-summary">
          <div className="dashboard-hero-balance">
            <p>Saldo consolidado</p>
            <strong>
              <SignedAmount value={principal.total_convertido}>
                {formatCurrency(principal.total_convertido, principal.divisa_principal)}
              </SignedAmount>
            </strong>
            <span className="dashboard-hero-delta">
              {variacionPct !== null ? (
                <>
                  <span className={variacionPct >= 0 ? 'dashboard-variacion-pill dashboard-variacion--positive' : 'dashboard-variacion-pill dashboard-variacion--negative'}>
                    {variacionPct >= 0 ? '+' : ''}{variacionPct.toFixed(1)}%
                  </span>
                  <span>vs. periodo anterior · {formatPeriodoLabel(periodo)}</span>
                </>
              ) : 'Sin comparativa suficiente'}
            </span>
            <small>
              {resumenConsolidado.cuentas} cuenta{resumenConsolidado.cuentas === 1 ? '' : 's'} ·{' '}
              {resumenConsolidado.bancos} banco{resumenConsolidado.bancos === 1 ? '' : 's'} ·{' '}
              {resumenConsolidado.divisas} divisa{resumenConsolidado.divisas === 1 ? '' : 's'}
            </small>
          </div>

          <div className="dashboard-hero-divisas">
            <SaldoPorDivisaCard
              className="dashboard-divisa-strip dashboard-divisa-strip--hero"
              items={saldosDivisa.divisas}
              divisaPrincipal={saldosDivisa.divisa_principal}
            />
          </div>
        </div>

        <div className="dashboard-hero-chart">
          <EvolucionChart
            points={evolucion.puntos}
            divisa={principal.divisa_principal}
            colors={principal.chart_colors}
            height={310}
            variant="saldoArea"
            xAxisMode="month"
          />
        </div>
      </section>

      <div className="dashboard-kpi-grid dashboard-kpi-grid--secondary">
        <KpiCard
          title="Ingresos período"
          value={
            <SignedAmount value={periodTotals.ingresos}>
              {formatCurrency(periodTotals.ingresos, principal.divisa_principal)}
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
              {formatCurrency(periodTotals.egresos, principal.divisa_principal)}
            </SignedAmount>
          }
          helper={variacionEgrPct !== null ? (
            <span className={variacionEgrPct <= 0 ? 'dashboard-variacion--positive' : 'dashboard-variacion--negative'}>
              {variacionEgrPct >= 0 ? '+' : ''}{variacionEgrPct.toFixed(1)}% vs. anterior
            </span>
          ) : undefined}
        />
        {liquidezConsolidada ? (
          <>
            <KpiCard
              title="Disponible"
              value={
                <SignedAmount value={liquidezConsolidada.disponible}>
                  {formatCurrency(liquidezConsolidada.disponible, principal.divisa_principal)}
                </SignedAmount>
              }
              helper={variacionDispPct !== null ? (
                <span className={variacionDispPct >= 0 ? 'dashboard-variacion--positive' : 'dashboard-variacion--negative'}>
                  {disponiblePct !== null ? `${disponiblePct.toFixed(1)}% del saldo total` : `${variacionDispPct >= 0 ? '+' : ''}${variacionDispPct.toFixed(1)}%`}
                </span>
              ) : undefined}
            />
            <KpiCard
              title="Inmovilizado"
              value={formatCurrency(liquidezConsolidada.inmovilizado, principal.divisa_principal)}
              helper={variacionInmovPct !== null && Math.abs(variacionInmovPct) >= 0.1 ? (
                <span className={variacionInmovPct > 0 ? 'dashboard-variacion--neutral' : 'dashboard-variacion--positive'}>
                  {principal.plazos_fijos.total_cuentas} plazo{principal.plazos_fijos.total_cuentas === 1 ? '' : 's'} fijo{principal.plazos_fijos.total_cuentas === 1 ? '' : 's'}
                </span>
              ) : undefined}
            />
          </>
        ) : null}
      </div>

      <div className="dashboard-detail-grid">
        <section className="dashboard-card dashboard-titulares-card dashboard-titulares-card--wide">
          <header className="dashboard-card-header">
            <h2>Saldos por titular</h2>
            <span className="dashboard-card-meta">{principal.saldos_por_titular.length} titulares · cuota sobre el total</span>
          </header>

          {principal.saldos_por_titular.length === 0 ? (
            <EmptyState
              title="No hay titulares con saldo visible."
              subtitle="Importa movimientos o revisa permisos para poblar este resumen."
            />
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
                    {group.items.length === 0 ? <div className="dashboard-titular-empty">Sin saldos visibles</div> : null}
                  </div>
                </article>
              ))}
            </div>
          )}
        </section>

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
            <div className={principal.plazos_fijos.dias_hasta_proximo_vencimiento !== null && principal.plazos_fijos.dias_hasta_proximo_vencimiento <= 7 ? 'dashboard-plazo-metric--warning' : undefined}>
              <span>Próximo vencimiento</span>
              <strong>
                {principal.plazos_fijos.dias_hasta_proximo_vencimiento === null
                  ? 'Sin fecha'
                  : `${principal.plazos_fijos.dias_hasta_proximo_vencimiento} días`}
              </strong>
              {principal.plazos_fijos.proximo_vencimiento ? (
                <small>{formatDate(principal.plazos_fijos.proximo_vencimiento)}</small>
              ) : null}
            </div>
          </div>
        </section>
      </div>

      {principal.saldos_por_pais?.length ? (
        <section className="dashboard-card">
          <header className="dashboard-card-header">
            <h2>Saldos por país</h2>
          </header>
          <div className="titulares-divisa-banners">
            {principal.saldos_por_pais.map((pais) => (
              <article className="dashboard-mini-card" key={pais.pais_id ?? 'sin-pais'}>
                <span>{pais.pais_nombre}</span>
                <strong>{formatCurrency(pais.total_convertido, principal.divisa_principal)}</strong>
                <small>{pais.total_cuentas} cuenta{pais.total_cuentas === 1 ? '' : 's'}</small>
              </article>
            ))}
          </div>
        </section>
      ) : null}

      {((principal.concentracion_bancos ?? []).length > 0 || principal.saldos_por_titular.length > 0) ? (
        <section className="dashboard-card dashboard-bancos-card">
          <header className="dashboard-card-header">
            <h2>Concentración</h2>
          </header>
          <ConcentracionDonutCharts
            bancos={principal.concentracion_bancos ?? []}
            titulares={principal.saldos_por_titular}
            divisa={principal.divisa_principal}
          />
        </section>
      ) : null}
    </section>
  );
}

function formatPeriodoLabel(periodo: PeriodoDashboard): string {
  const labels: Record<PeriodoDashboard, string> = {
    '1m': 'último mes',
    '3m': 'últimos 3 meses',
    '6m': 'últimos 6 meses',
    '9m': 'últimos 9 meses',
    '12m': 'últimos 12 meses',
    '18m': 'últimos 18 meses',
    '24m': 'últimos 24 meses',
  };

  return labels[periodo];
}
