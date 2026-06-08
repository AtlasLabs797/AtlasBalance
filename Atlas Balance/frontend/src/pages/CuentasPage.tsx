import { lazy, Suspense, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { AppSelect } from '@/components/common/AppSelect';
import { CloseIconButton } from '@/components/common/CloseIconButton';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { DatePickerField } from '@/components/common/DatePickerField';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import { SignedAmount } from '@/components/common/SignedAmount';
import { DivisaSelector } from '@/components/dashboard/DivisaSelector';
import { PeriodoSelector } from '@/components/dashboard/PeriodoSelector';
import { SaldoPorDivisaCard } from '@/components/dashboard/SaldoPorDivisaCard';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { useDialogFocus } from '@/hooks/useDialogFocus';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import { usePermisosStore } from '@/stores/permisosStore';
import type {
  Cuenta,
  DashboardEvolucion,
  DashboardPrincipal,
  DashboardSaldosDivisa,
  PaginatedResponse,
  Pais,
  PeriodoDashboard,
  TipoCuenta,
  TipoTitular,
  Titular,
} from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatCurrency, formatDate } from '@/utils/formatters';

const EvolucionChart = lazy(() =>
  import('@/components/dashboard/EvolucionChart').then((module) => ({ default: module.EvolucionChart }))
);
const TitularSaldoBarChart = lazy(() => import('@/components/dashboard/TitularSaldoBarChart'));

interface CuentaRow extends Cuenta {
  titular_nombre: string;
  deleted_at: string | null;
}

interface DivisaOption {
  codigo: string;
  nombre: string | null;
}

interface FormatoOption {
  id: string;
  nombre: string;
  banco_nombre: string | null;
  divisa: string | null;
}

interface CuentaFormState {
  titular_id: string;
  nombre: string;
  numero_cuenta: string;
  iban: string;
  banco_nombre: string;
  divisa: string;
  formato_id: string;
  pais_id: string;
  tipo_cuenta: TipoCuenta;
  activa: boolean;
  notas: string;
  fecha_inicio: string;
  fecha_vencimiento: string;
  interes_previsto: string;
  renovable: boolean;
  cuenta_referencia_id: string;
  plazo_fijo_notas: string;
}

interface DeleteCandidate {
  id: string;
  nombre: string;
}

interface DashboardCuentaRow {
  cuenta_id: string;
  cuenta_nombre: string;
  titular_id: string;
  titular_nombre: string;
  banco_nombre: string | null;
  pais_id: string | null;
  pais_nombre: string | null;
  divisa: string;
  saldo_actual: number;
  saldo_convertido: number;
}

const emptyForm: CuentaFormState = {
  titular_id: '',
  nombre: '',
  numero_cuenta: '',
  iban: '',
  banco_nombre: '',
  divisa: 'EUR',
  formato_id: '',
  pais_id: '',
  tipo_cuenta: 'NORMAL',
  activa: true,
  notas: '',
  fecha_inicio: '',
  fecha_vencimiento: '',
  interes_previsto: '',
  renovable: false,
  cuenta_referencia_id: '',
  plazo_fijo_notas: '',
};

function getCuentaInitials(nombre: string) {
  const initials = nombre
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('');

  return initials || 'C';
}

const tipoCuentaLabels: Record<string, string> = {
  NORMAL: 'Cuenta bancaria',
  EFECTIVO: 'Efectivo',
  PLAZO_FIJO: 'Plazo fijo',
};

const estadoPlazoLabels: Record<string, string> = {
  ACTIVO: 'Activo',
  VENCIDO: 'Vencido',
  CANCELADO: 'Cancelado',
};

function formatCatalogLabel(labels: Record<string, string>, value?: string | null) {
  if (!value) return 'Sin dato';
  return labels[value] ?? value;
}

export default function CuentasPage() {
  const navigate = useNavigate();
  const usuario = useAuthStore((state) => state.usuario);
  const canViewDashboard = usePermisosStore((state) => state.canViewDashboard);
  const canViewCuenta = usePermisosStore((state) => state.canViewCuenta);
  usePermisosStore((state) => state.permisos);
  const isAdmin = usuario?.rol === 'ADMIN';
  const canSeeDashboard = usuario?.rol === 'ADMIN' || (usuario?.rol === 'GERENTE' && canViewDashboard());

  const [items, setItems] = useState<CuentaRow[]>([]);
  const [titulares, setTitulares] = useState<Titular[]>([]);
  const [divisas, setDivisas] = useState<DivisaOption[]>([]);
  const [formatos, setFormatos] = useState<FormatoOption[]>([]);
  const [paises, setPaises] = useState<Pais[]>([]);

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [totalPages, setTotalPages] = useState(1);
  const [search, setSearch] = useState('');
  const [titularFilter, setTitularFilter] = useState('');
  const [tipoTitularFilter, setTipoTitularFilter] = useState('');
  const [tipoCuentaFilter, setTipoCuentaFilter] = useState('');
  const [paisFilter, setPaisFilter] = useState('');
  const [incluirEliminados, setIncluirEliminados] = useState(false);
  const debouncedSearch = useDebouncedValue(search);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [auxError, setAuxError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  const [periodo, setPeriodo] = useState<PeriodoDashboard>('1m');
  const [divisaPrincipal, setDivisaPrincipal] = useState('EUR');
  const [principal, setPrincipal] = useState<DashboardPrincipal | null>(null);
  const [evolucion, setEvolucion] = useState<DashboardEvolucion | null>(null);
  const [saldosDivisa, setSaldosDivisa] = useState<DashboardSaldosDivisa | null>(null);
  const [saldosCuentaRows, setSaldosCuentaRows] = useState<DashboardCuentaRow[]>([]);
  const [dashboardLoading, setDashboardLoading] = useState(false);
  const [dashboardError, setDashboardError] = useState<string | null>(null);

  const [editingId, setEditingId] = useState<string | null>(null);
  const [renewingId, setRenewingId] = useState<string | null>(null);
  const [form, setForm] = useState<CuentaFormState>(emptyForm);
  const [isFormModalOpen, setIsFormModalOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [deleteCandidate, setDeleteCandidate] = useState<DeleteCandidate | null>(null);
  const formatosDisponibles = useMemo(
    () => formatos.filter((formato) => !formato.divisa || formato.divisa === form.divisa),
    [formatos, form.divisa],
  );
  const cuentaReferenciaOptions = useMemo(
    () => items.filter((item) => item.id !== editingId && item.tipo_cuenta !== 'PLAZO_FIJO' && item.activa),
    [editingId, items],
  );

  const chartRows = useMemo(
    () => principal?.saldos_por_titular ?? [],
    [principal?.saldos_por_titular],
  );

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

  const saldoCuentaById = useMemo(
    () => new Map(saldosCuentaRows.map((row) => [row.cuenta_id, { saldo: row.saldo_actual, divisa: row.divisa }])),
    [saldosCuentaRows],
  );
  const accountCatalogsReady = titulares.length > 0 && divisas.length > 0 && !auxError;

  const loadAuxData = async () => {
    setAuxError(null);
    try {
      const [titularesRes, divisasRes, paisesRes] = await Promise.all([
        api.get<PaginatedResponse<Titular>>('/titulares', { params: { page: 1, pageSize: 500, sortBy: 'nombre', sortDir: 'asc' } }),
        api.get<DivisaOption[]>('/cuentas/divisas-activas'),
        api.get<Pais[]>('/paises'),
      ]);
      setTitulares(titularesRes.data.data ?? []);
      setDivisas(divisasRes.data ?? []);
      setPaises(paisesRes.data ?? []);

      if (isAdmin) {
        const { data } = await api.get<PaginatedResponse<FormatoOption>>('/formatos-importacion', {
          params: { page: 1, pageSize: 500, sortBy: 'nombre', sortDir: 'asc' },
        });
        setFormatos(data.data ?? []);
      } else {
        setFormatos([]);
      }

      if (!form.titular_id && titularesRes.data.data?.length) {
        setForm((prev) => ({
          ...prev,
          titular_id: titularesRes.data.data[0].id,
          divisa: divisasRes.data[0]?.codigo ?? prev.divisa,
        }));
      }
    } catch (err) {
      setAuxError(extractErrorMessage(err, 'No se pudieron cargar titulares, divisas o formatos. Revisa la conexión antes de crear o editar cuentas.'));
    }
  };

  const loadData = async () => {
    setLoading(true);
    setError(null);
    try {
      const { data } = await api.get<PaginatedResponse<CuentaRow>>('/cuentas', {
        params: {
          page,
          pageSize,
          search: debouncedSearch || undefined,
          titularId: titularFilter || undefined,
          paisId: paisFilter || undefined,
          tipoTitular: tipoTitularFilter || undefined,
          tipoCuenta: tipoCuentaFilter || undefined,
          incluirEliminados: incluirEliminados && isAdmin,
          sortBy: 'fecha_creacion',
          sortDir: 'desc',
        },
      });
      setItems(data.data ?? []);
      setTotalPages(Math.max(data.total_pages ?? 1, 1));
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudieron cargar cuentas'));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadAuxData();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- solo refresca catalogos al cambiar rol admin
  }, [isAdmin]);

  useEffect(() => {
    void loadData();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- recarga controlada por filtros/paginacion
  }, [page, pageSize, debouncedSearch, titularFilter, paisFilter, tipoTitularFilter, tipoCuentaFilter, incluirEliminados, isAdmin]);

  useEffect(() => {
    if (!canSeeDashboard) {
      setDashboardError(null);
      return;
    }

    let mounted = true;

    const loadDashboard = async () => {
      setDashboardLoading(true);
      setDashboardError(null);
      try {
        const [principalRes, evolucionRes, saldosDivisaRes] = await Promise.all([
          api.get<DashboardPrincipal>('/dashboard/principal', { params: { divisaPrincipal, paisId: paisFilter || undefined } }),
          api.get<DashboardEvolucion>('/dashboard/evolucion', { params: { periodo, divisaPrincipal, paisId: paisFilter || undefined } }),
          api.get<DashboardSaldosDivisa>('/dashboard/saldos-divisa', { params: { divisaPrincipal, paisId: paisFilter || undefined } }),
        ]);
        const cuentaRows = (principalRes.data.saldos_por_cuenta ?? [])
          .map((cuenta) => ({
            cuenta_id: cuenta.cuenta_id,
            cuenta_nombre: cuenta.cuenta_nombre,
            titular_id: cuenta.titular_id,
            titular_nombre: cuenta.titular_nombre,
            banco_nombre: cuenta.banco_nombre ?? null,
            pais_id: cuenta.pais_id ?? null,
            pais_nombre: cuenta.pais_nombre ?? null,
            divisa: cuenta.divisa,
            saldo_actual: cuenta.saldo_actual,
            saldo_convertido: cuenta.saldo_convertido,
          }))
          .sort((a, b) => b.saldo_convertido - a.saldo_convertido);

        if (!mounted) {
          return;
        }

        setPrincipal(principalRes.data);
        setEvolucion(evolucionRes.data);
        setSaldosDivisa(saldosDivisaRes.data);
        setSaldosCuentaRows(cuentaRows);
        if (principalRes.data.divisa_principal && principalRes.data.divisa_principal !== divisaPrincipal) {
          setDivisaPrincipal(principalRes.data.divisa_principal);
        }
      } catch (err) {
        if (!mounted) {
          return;
        }

        setDashboardError(extractErrorMessage(err, 'No se pudo cargar el dashboard de cuentas.'));
      } finally {
        if (mounted) {
          setDashboardLoading(false);
        }
      }
    };

    void loadDashboard();

    return () => {
      mounted = false;
    };
  }, [canSeeDashboard, divisaPrincipal, periodo, paisFilter]);

  const resetForm = () => {
    setEditingId(null);
    setRenewingId(null);
    setForm(() => ({
      ...emptyForm,
      titular_id: titulares[0]?.id ?? '',
      divisa: divisas[0]?.codigo ?? 'EUR',
      pais_id: '',
    }));
  };

  const openCreateModal = () => {
    if (!accountCatalogsReady) {
      setError(auxError ?? 'Carga titulares y divisas antes de crear una cuenta.');
      return;
    }

    resetForm();
    setFormError(null);
    setIsFormModalOpen(true);
  };

  const closeFormModal = () => {
    if (saving) {
      return;
    }

    setIsFormModalOpen(false);
    setFormError(null);
    resetForm();
  };

  const formDialogRef = useDialogFocus<HTMLDivElement>(isFormModalOpen, {
    onEscape: saving ? undefined : closeFormModal,
  });

  useEffect(() => {
    if (form.tipo_cuenta !== 'PLAZO_FIJO') {
      if (form.formato_id && !formatosDisponibles.some((formato) => formato.id === form.formato_id)) {
        setForm((prev) => ({ ...prev, formato_id: '' }));
      }
      if (form.tipo_cuenta === 'EFECTIVO' && (form.numero_cuenta || form.iban || form.banco_nombre)) {
        setForm((prev) => ({
          ...prev,
          numero_cuenta: '',
          iban: '',
          banco_nombre: '',
        }));
      }
      return;
    }

    setForm((prev) => ({
      ...prev,
      numero_cuenta: '',
      iban: '',
      banco_nombre: '',
      formato_id: '',
    }));
  }, [form.banco_nombre, form.divisa, form.formato_id, form.iban, form.numero_cuenta, form.tipo_cuenta, formatosDisponibles]);

  const startEdit = async (id: string) => {
    if (!accountCatalogsReady) {
      setError(auxError ?? 'Carga titulares y divisas antes de editar una cuenta.');
      return;
    }

    setSaving(true);
    setError(null);
    setFormError(null);
    try {
      const { data } = await api.get<CuentaRow>(`/cuentas/${id}`, { params: { incluirEliminados: true } });
      setEditingId(id);
      setRenewingId(null);
      setForm({
        titular_id: data.titular_id,
        nombre: data.nombre,
        numero_cuenta: data.numero_cuenta ?? '',
        iban: data.iban ?? '',
        banco_nombre: data.banco_nombre ?? '',
        divisa: data.divisa,
        formato_id: data.tipo_cuenta === 'PLAZO_FIJO' ? '' : (data.formato_id ?? ''),
        pais_id: data.pais_id ?? '',
        tipo_cuenta: data.tipo_cuenta ?? (data.es_efectivo ? 'EFECTIVO' : 'NORMAL'),
        activa: data.activa,
        notas: data.notas ?? '',
        fecha_inicio: data.plazo_fijo?.fecha_inicio ?? '',
        fecha_vencimiento: data.plazo_fijo?.fecha_vencimiento ?? '',
        interes_previsto: data.plazo_fijo?.interes_previsto?.toString() ?? '',
        renovable: data.plazo_fijo?.renovable ?? false,
        cuenta_referencia_id: data.plazo_fijo?.cuenta_referencia_id ?? '',
        plazo_fijo_notas: data.plazo_fijo?.notas ?? '',
      });
      setIsFormModalOpen(true);
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo cargar cuenta'));
    } finally {
      setSaving(false);
    }
  };

  const startRenew = async (id: string) => {
    await startEdit(id);
    setEditingId(null);
    setRenewingId(id);
  };

  const save = async () => {
    if (!isAdmin) return;
    if (!form.titular_id || !form.nombre.trim()) {
      setFormError('Selecciona un titular y escribe el nombre de la cuenta.');
      return;
    }
    if (form.tipo_cuenta === 'PLAZO_FIJO' && (!form.fecha_inicio || !form.fecha_vencimiento)) {
      setFormError('Indica fecha de inicio y fecha de vencimiento para el plazo fijo.');
      return;
    }

    setSaving(true);
    setFormError(null);
    const payload = {
      titular_id: form.titular_id,
      nombre: form.nombre.trim(),
      numero_cuenta: form.numero_cuenta.trim() || null,
      iban: form.iban.trim() || null,
      banco_nombre: form.banco_nombre.trim() || null,
      divisa: form.divisa,
      formato_id: form.tipo_cuenta === 'PLAZO_FIJO' ? null : (form.formato_id || null),
      pais_id: form.pais_id || null,
      tipo_cuenta: form.tipo_cuenta,
      es_efectivo: form.tipo_cuenta === 'EFECTIVO',
      activa: form.activa,
      notas: form.notas.trim() || null,
      plazo_fijo: form.tipo_cuenta === 'PLAZO_FIJO' ? {
        fecha_inicio: form.fecha_inicio,
        fecha_vencimiento: form.fecha_vencimiento,
        interes_previsto: form.interes_previsto ? Number(form.interes_previsto) : null,
        renovable: form.renovable,
        cuenta_referencia_id: form.cuenta_referencia_id || null,
        notas: form.plazo_fijo_notas.trim() || null,
      } : null,
    };

    try {
      if (renewingId) {
        await api.post(`/cuentas/${renewingId}/plazo-fijo/renovar`, {
          nueva_fecha_inicio: form.fecha_inicio,
          nueva_fecha_vencimiento: form.fecha_vencimiento,
          interes_previsto: form.interes_previsto ? Number(form.interes_previsto) : null,
          renovable: form.renovable,
          notas: form.plazo_fijo_notas.trim() || null,
        });
      } else if (editingId) {
        await api.put(`/cuentas/${editingId}`, payload);
      } else {
        await api.post('/cuentas', payload);
      }
      resetForm();
      setIsFormModalOpen(false);
      await loadData();
    } catch (err) {
      setFormError(extractErrorMessage(err, 'No se pudo guardar cuenta'));
    } finally {
      setSaving(false);
    }
  };

  const remove = async () => {
    if (!isAdmin || !deleteCandidate) return;
    setSaving(true);
    setError(null);
    try {
      await api.delete(`/cuentas/${deleteCandidate.id}`);
      setDeleteCandidate(null);
      await loadData();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo eliminar cuenta'));
    } finally {
      setSaving(false);
    }
  };

  const restore = async (id: string) => {
    if (!isAdmin) return;
    try {
      await api.post(`/cuentas/${id}/restaurar`);
      await loadData();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo restaurar cuenta'));
    }
  };

  return (
    <section className="phase2-page cuentas-page">
      <header className="phase2-header">
        <div>
          <h1>Cuentas</h1>
          <p className="dashboard-subtitle">Saldos, bancos y plazos fijos por titular.</p>
        </div>
        {isAdmin && (
          <button type="button" className="button-primary" onClick={openCreateModal} disabled={!accountCatalogsReady || saving}>
            Nueva Cuenta
          </button>
        )}
      </header>

      {canSeeDashboard ? (
        <section className="dashboard-card titulares-dashboard-card">
          <header className="dashboard-card-header titulares-dashboard-header">
            <div>
              <h2>Saldos y evolución</h2>
              <p className="dashboard-subtitle">Vista consolidada para saldos, divisas y tendencia.</p>
            </div>
            <div className="dashboard-toolbar-actions">
              <PeriodoSelector value={periodo} onChange={setPeriodo} />
              <DivisaSelector value={principal?.divisa_principal ?? divisaPrincipal} options={divisaOptions} onChange={setDivisaPrincipal} />
            </div>
          </header>

          {dashboardError ? <p className="auth-error" role="alert">{dashboardError}</p> : null}
          {dashboardLoading ? <p className="import-muted">Cargando dashboard de cuentas...</p> : null}

          {!dashboardLoading && !dashboardError && principal ? (
            <>
              <div className="titulares-chart-wrap">
                {chartRows.length === 0 ? (
                  <EmptyState
                    title="No hay saldos visibles."
                    subtitle="Importa movimientos o revisa permisos para poblar este resumen."
                  />
                ) : (
                  <Suspense fallback={<p className="import-muted">Cargando gráfica...</p>}>
                    <TitularSaldoBarChart rows={chartRows} divisa={principal.divisa_principal} />
                  </Suspense>
                )}
              </div>

              {evolucion ? (
                <section className="dashboard-card titulares-evolucion-card">
                  <header className="dashboard-card-header">
                    <h3>Evolución</h3>
                    <span className="dashboard-subtitle">Último punto: {evolucion.puntos.length ? formatDate(evolucion.puntos[evolucion.puntos.length - 1].fecha) : 'Sin dato'}</span>
                  </header>
                  <Suspense fallback={<p className="import-muted">Cargando evolución...</p>}>
                    <EvolucionChart
                      points={evolucion.puntos}
                      divisa={principal.divisa_principal}
                      colors={principal.chart_colors}
                    />
                  </Suspense>
                </section>
              ) : null}

              {principal.saldos_por_pais?.length ? (
                <div className="titulares-divisa-banners" aria-label="Saldos por país">
                  {principal.saldos_por_pais.slice(0, 6).map((pais) => (
                    <article className="dashboard-mini-card" key={pais.pais_id ?? 'sin-pais'}>
                      <span>{pais.pais_nombre}</span>
                      <strong>{formatCurrency(pais.total_convertido, principal.divisa_principal)}</strong>
                      <small>{pais.total_cuentas} cuenta{pais.total_cuentas === 1 ? '' : 's'}</small>
                    </article>
                  ))}
                </div>
              ) : null}

              <div className="cuentas-balance-list" aria-label={`Saldos por cuenta bancaria en ${principal.divisa_principal}`}>
              <div className="cuentas-balance-heading" aria-hidden="true">
                <span>Cuenta bancaria</span>
                <span>Banco</span>
                <span>País</span>
                <span>Divisa</span>
                <span>Saldo total</span>
                <span>Detalle</span>
                </div>

                {saldosCuentaRows.map((item) => {
                  const canOpenDashboardCuenta = canViewCuenta(item.cuenta_id, item.titular_id);

                  return canOpenDashboardCuenta ? (
                    <Link
                      className="cuentas-balance-row"
                      key={item.cuenta_id}
                      to={`/dashboard/cuenta/${item.cuenta_id}`}
                      aria-label={`Abrir dashboard de cuenta ${item.cuenta_nombre}`}
                    >
                      <span className="cuentas-balance-account">
                        <span className="cuentas-balance-avatar" aria-hidden="true">
                          {getCuentaInitials(item.cuenta_nombre)}
                        </span>
                        <span className="cuentas-balance-copy">
                          <span className="cuentas-balance-name">{item.cuenta_nombre}</span>
                          <span className="cuentas-balance-owner">{item.titular_nombre}</span>
                        </span>
                      </span>
                      <span className="cuentas-balance-bank">{item.banco_nombre || 'Sin banco'}</span>
                      <span className="cuentas-balance-bank">{item.pais_nombre || 'Sin pais'}</span>
                      <span className="cuentas-balance-currency">{item.divisa}</span>
                      <SignedAmount value={item.saldo_actual}>
                        {formatCurrency(item.saldo_actual, item.divisa)}
                      </SignedAmount>
                      <span className="cuentas-balance-open" aria-hidden="true">Abrir</span>
                    </Link>
                  ) : (
                    <div className="cuentas-balance-row" key={item.cuenta_id} aria-disabled="true">
                      <span className="cuentas-balance-account">
                        <span className="cuentas-balance-avatar" aria-hidden="true">
                          {getCuentaInitials(item.cuenta_nombre)}
                        </span>
                        <span className="cuentas-balance-copy">
                          <span className="cuentas-balance-name">{item.cuenta_nombre}</span>
                          <span className="cuentas-balance-owner">{item.titular_nombre}</span>
                        </span>
                      </span>
                      <span className="cuentas-balance-bank">{item.banco_nombre || 'Sin banco'}</span>
                      <span className="cuentas-balance-bank">{item.pais_nombre || 'Sin pais'}</span>
                      <span className="cuentas-balance-currency">{item.divisa}</span>
                      <SignedAmount value={item.saldo_actual}>
                        {formatCurrency(item.saldo_actual, item.divisa)}
                      </SignedAmount>
                      <span className="dashboard-open-link dashboard-open-link--disabled">Sin acceso</span>
                    </div>
                  );
                })}
              </div>

              {saldosDivisa ? (
                <div className="titulares-divisa-banners">
                  <SaldoPorDivisaCard items={saldosDivisa.divisas} divisaPrincipal={saldosDivisa.divisa_principal} />
                </div>
              ) : null}
            </>
          ) : null}
        </section>
      ) : null}

      <div className="phase2-filters">
        <input
          type="search"
          aria-label="Buscar cuentas por cuenta, banco o IBAN"
          placeholder="Buscar por cuenta, banco, IBAN..."
          value={search}
          onChange={(e) => {
            setPage(1);
            setSearch(e.target.value);
          }}
        />
        <AppSelect
          ariaLabel="Titular"
          value={titularFilter}
          options={[
            { value: '', label: 'Todos los titulares' },
            ...titulares.map((titular) => ({ value: titular.id, label: titular.nombre })),
          ]}
          onChange={(next) => {
            setPage(1);
            setTitularFilter(next);
          }}
        />
        <AppSelect
          ariaLabel="País"
          value={paisFilter}
          options={[
            { value: '', label: 'Todos los países' },
            ...paises.map((pais) => ({ value: pais.id, label: pais.nombre })),
          ]}
          onChange={(next) => {
            setPage(1);
            setPaisFilter(next);
          }}
        />
        <AppSelect
          ariaLabel="Tipo de titular"
          value={tipoTitularFilter}
          options={[
            { value: '', label: 'Todos los tipos de titular' },
            { value: 'EMPRESA', label: 'Empresa' },
            { value: 'AUTONOMO', label: 'Autónomo' },
            { value: 'PARTICULAR', label: 'Particular' },
          ]}
          onChange={(next) => {
            setPage(1);
            setTipoTitularFilter(next as TipoTitular | '');
          }}
        />
        <AppSelect
          ariaLabel="Tipo de cuenta"
          value={tipoCuentaFilter}
          options={[
            { value: '', label: 'Todos los tipos de cuenta' },
            { value: 'NORMAL', label: 'Normal' },
            { value: 'EFECTIVO', label: 'Efectivo' },
            { value: 'PLAZO_FIJO', label: 'Plazo fijo' },
          ]}
          onChange={(next) => {
            setPage(1);
            setTipoCuentaFilter(next as TipoCuenta | '');
          }}
        />
        <PageSizeSelect
          value={pageSize}
          options={[10, 20, 50]}
          onChange={(next) => {
            setPage(1);
            setPageSize(next);
          }}
        />
        {isAdmin && (
          <label>
            <input
              type="checkbox"
              checked={incluirEliminados}
              onChange={(e) => {
                setPage(1);
                setIncluirEliminados(e.target.checked);
              }}
            />
            Ver eliminadas
          </label>
        )}
      </div>

      {auxError ? <p className="auth-error" role="alert">{auxError}</p> : null}
      {error && <p className="auth-error" role="alert">{error}</p>}

      <div className="phase2-grid">
        <div className="phase2-cards">
          {loading ? <p className="import-muted">Cargando cuentas...</p> : null}
          {!loading && items.length === 0 ? (
            <EmptyState
              title="No hay cuentas con estos filtros."
              subtitle="Ajusta la búsqueda o crea una cuenta nueva."
            />
          ) : null}
          {!loading && items.map((item) => {
            const saldoCuenta = saldoCuentaById.get(item.id);
            const fallbackSaldo = typeof item.saldo_actual === 'number' ? item.saldo_actual : null;
            const saldoValue = saldoCuenta?.saldo ?? fallbackSaldo;
            const saldoCurrency = saldoCuenta?.divisa ?? item.divisa;
            const canOpenDashboardCuenta = canViewCuenta(item.id, item.titular_id);

            return (
              <article className="titular-card cuenta-card" key={item.id}>
                <div className="titular-card-title">
                  <h3>{item.nombre}</h3>
                  <span className="pill">{formatCatalogLabel(tipoCuentaLabels, item.tipo_cuenta ?? (item.es_efectivo ? 'EFECTIVO' : 'NORMAL'))}</span>
                  {item.plazo_fijo ? <span className="pill">{formatCatalogLabel(estadoPlazoLabels, item.plazo_fijo.estado)}</span> : null}
                </div>
                <div className="cuenta-card-meta">
                  <div className="cuenta-card-meta-item">
                    <span className="cuenta-card-meta-label">Titular</span>
                    <strong className="cuenta-card-meta-value">{item.titular_nombre}</strong>
                  </div>
                  <div className="cuenta-card-meta-item">
                    <span className="cuenta-card-meta-label">Divisa</span>
                    <strong className="cuenta-card-meta-value">{item.divisa}</strong>
                  </div>
                  <div className="cuenta-card-meta-item">
                    <span className="cuenta-card-meta-label">Banco</span>
                    <strong className="cuenta-card-meta-value">{item.banco_nombre || 'Sin banco'}</strong>
                  </div>
                  <div className="cuenta-card-meta-item">
                    <span className="cuenta-card-meta-label">País</span>
                    <strong className="cuenta-card-meta-value">{item.pais_nombre || 'Sin pais'}</strong>
                  </div>
                  <div className="cuenta-card-meta-item">
                    <span className="cuenta-card-meta-label">Estado</span>
                    <strong className="cuenta-card-meta-value">{item.deleted_at ? 'Eliminada' : (item.activa ? 'Activa' : 'Inactiva')}</strong>
                  </div>
                  {item.plazo_fijo ? (
                    <div className="cuenta-card-meta-item">
                      <span className="cuenta-card-meta-label">Vencimiento</span>
                      <strong className="cuenta-card-meta-value">{formatDate(item.plazo_fijo.fecha_vencimiento)}</strong>
                    </div>
                  ) : null}
                  <div className="cuenta-card-meta-item cuenta-card-balance">
                    <span className="cuenta-card-meta-label">Saldo total</span>
                    {saldoValue === null ? (
                      <strong className="cuenta-card-meta-value">Sin saldo</strong>
                    ) : (
                      <SignedAmount value={saldoValue}>
                        {formatCurrency(saldoValue, saldoCurrency)}
                      </SignedAmount>
                    )}
                  </div>
                  {item.notas ? (
                    <div className="cuenta-card-meta-item cuenta-card-notes">
                      <span className="cuenta-card-meta-label">Notas</span>
                      <strong className="cuenta-card-meta-value">{item.notas}</strong>
                    </div>
                  ) : null}
                </div>
                {(canSeeDashboard || isAdmin) ? (
                  <div className="phase2-row-actions">
                    {canSeeDashboard && !item.deleted_at && canOpenDashboardCuenta ? (
                      <button
                        type="button"
                        className="cuenta-open-button"
                        onClick={() => navigate(`/dashboard/cuenta/${item.id}`)}
                        aria-label={`Abrir dashboard de cuenta ${item.nombre}`}
                      >
                        Abrir
                      </button>
                    ) : null}
                    {canSeeDashboard && !item.deleted_at && !canOpenDashboardCuenta ? (
                      <span className="dashboard-open-link dashboard-open-link--disabled">Sin acceso</span>
                    ) : null}
                  {isAdmin ? (
                    <button type="button" onClick={() => startEdit(item.id)} disabled={saving || !accountCatalogsReady} aria-label={`Editar cuenta ${item.nombre}`}>Editar</button>
                  ) : null}
                  {isAdmin && item.tipo_cuenta === 'PLAZO_FIJO' ? (
                    <button type="button" onClick={() => void startRenew(item.id)} disabled={saving || !accountCatalogsReady} aria-label={`Renovar plazo fijo ${item.nombre}`}>Renovar</button>
                  ) : null}
                    {isAdmin && !item.deleted_at ? (
                      <button
                        type="button"
                        className="button-danger"
                        onClick={() => setDeleteCandidate({ id: item.id, nombre: item.nombre })}
                        disabled={saving}
                        aria-label={`Eliminar cuenta ${item.nombre}`}
                      >
                        Eliminar
                      </button>
                    ) : null}
                    {isAdmin && item.deleted_at ? (
                      <button type="button" onClick={() => restore(item.id)} disabled={saving} aria-label={`Restaurar cuenta ${item.nombre}`}>Restaurar</button>
                    ) : null}
                  </div>
                ) : null}
              </article>
            );
          })}
          <div className="users-pagination">
            <button type="button" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1}>Anterior</button>
            <span>Página {page} / {totalPages}</span>
            <button type="button" onClick={() => setPage((p) => Math.min(totalPages, p + 1))} disabled={page >= totalPages}>Siguiente</button>
          </div>
        </div>
      </div>

      {isAdmin && isFormModalOpen ? (
        <div className="modal-backdrop users-modal-backdrop" onClick={closeFormModal}>
          <div
            ref={formDialogRef}
            className="users-modal phase2-form-modal phase2-form-modal--wide"
            onClick={(event) => event.stopPropagation()}
            role="dialog"
            aria-modal="true"
            aria-labelledby="cuentas-modal-title"
            tabIndex={-1}
          >
            <div className="users-modal-header">
              <div>
                <h2 id="cuentas-modal-title">{renewingId ? 'Renovar plazo fijo' : editingId ? 'Editar cuenta' : 'Nueva cuenta'}</h2>
                <p>Alta y edición de cuentas bancarias o efectivo asociadas a un titular.</p>
              </div>
              <CloseIconButton
                className="users-modal-close"
                onClick={closeFormModal}
                disabled={saving}
                ariaLabel="Cerrar modal de cuenta"
              />
            </div>

            <form
              className="users-modal-body phase2-modal-form"
              onSubmit={(e) => {
                e.preventDefault();
                void save();
              }}
            >
              {formError ? <p className="auth-error" role="alert">{formError}</p> : null}

              <section className="users-modal-section">
                <h3>Datos base</h3>
                <div className="users-form-grid">
                  <AppSelect
                    label="Titular"
                    value={form.titular_id}
                    options={[
                      { value: '', label: 'Selecciona titular' },
                      ...titulares.map((titular) => ({ value: titular.id, label: titular.nombre })),
                    ]}
                    onChange={(next) => setForm((f) => ({ ...f, titular_id: next }))}
                  />

                  <label>
                    <span>Nombre</span>
                    <input value={form.nombre} onChange={(e) => setForm((f) => ({ ...f, nombre: e.target.value }))} />
                  </label>

                  <AppSelect
                    label="Divisa"
                    value={form.divisa}
                    options={divisas.map((divisa) => ({
                      value: divisa.codigo,
                      label: `${divisa.codigo} ${divisa.nombre ? `- ${divisa.nombre}` : ''}`,
                    }))}
                    onChange={(next) => setForm((f) => ({ ...f, divisa: next }))}
                  />

                  <AppSelect
                    label="País"
                    value={form.pais_id}
                    options={[
                      { value: '', label: 'Sin país' },
                      ...paises.map((pais) => ({
                        value: pais.id,
                        label: pais.codigo_iso2 ? `${pais.nombre} (${pais.codigo_iso2})` : pais.nombre,
                      })),
                    ]}
                    onChange={(next) => setForm((f) => ({ ...f, pais_id: next }))}
                  />

                  <AppSelect
                    label="Tipo de cuenta"
                    value={form.tipo_cuenta}
                    options={[
                      { value: 'NORMAL', label: 'Normal' },
                      { value: 'EFECTIVO', label: 'Efectivo' },
                      { value: 'PLAZO_FIJO', label: 'Plazo fijo' },
                    ]}
                    onChange={(next) => setForm((f) => ({ ...f, tipo_cuenta: next as TipoCuenta }))}
                  />
                </div>

                <div className="users-check-row">
                  <label>
                    <input
                      type="checkbox"
                      checked={form.activa}
                      onChange={(e) => setForm((f) => ({ ...f, activa: e.target.checked }))}
                    />
                    Cuenta activa
                  </label>
                </div>
                <label className="users-form-full">
                  <span>Notas generales</span>
                  <textarea
                    value={form.notas}
                    onChange={(e) => setForm((f) => ({ ...f, notas: e.target.value }))}
                  />
                </label>
              </section>

              {form.tipo_cuenta !== 'PLAZO_FIJO' ? (
                <section className="users-modal-section">
                  <h3>{form.tipo_cuenta === 'NORMAL' ? 'Datos bancarios' : 'Importación'}</h3>
                  <div className="users-form-grid">
                    {form.tipo_cuenta === 'NORMAL' ? (
                      <>
                        <label>
                          <span>Banco</span>
                          <input value={form.banco_nombre} onChange={(e) => setForm((f) => ({ ...f, banco_nombre: e.target.value }))} />
                        </label>

                        <label>
                          <span>Número de cuenta</span>
                          <input value={form.numero_cuenta} onChange={(e) => setForm((f) => ({ ...f, numero_cuenta: e.target.value }))} />
                        </label>

                        <label>
                          <span>IBAN</span>
                          <input value={form.iban} onChange={(e) => setForm((f) => ({ ...f, iban: e.target.value }))} />
                        </label>
                      </>
                    ) : null}

                    <AppSelect
                      label="Formato de importación"
                      value={form.formato_id}
                      options={[
                        { value: '', label: 'Sin formato' },
                        ...formatosDisponibles.map((formato) => ({
                          value: formato.id,
                          label: `${formato.nombre}${formato.banco_nombre ? ` (${formato.banco_nombre})` : ''}`,
                        })),
                      ]}
                      onChange={(next) => setForm((f) => ({ ...f, formato_id: next }))}
                    />
                  </div>
                </section>
              ) : (
                <p className="import-muted">Las cuentas de plazo fijo no usan datos bancarios ni formato de importación.</p>
              )}

              {form.tipo_cuenta === 'PLAZO_FIJO' || renewingId ? (
                <section className="users-modal-section">
                  <h3>Plazo fijo</h3>
                  <div className="users-form-grid">
                    <div className="date-field">
                      <span>Fecha inicio</span>
                      <DatePickerField
                        ariaLabel="Fecha inicio"
                        value={form.fecha_inicio}
                        onChange={(next) => setForm((f) => ({ ...f, fecha_inicio: next }))}
                      />
                    </div>
                    <div className="date-field">
                      <span>Fecha vencimiento</span>
                      <DatePickerField
                        ariaLabel="Fecha vencimiento"
                        value={form.fecha_vencimiento}
                        onChange={(next) => setForm((f) => ({ ...f, fecha_vencimiento: next }))}
                      />
                    </div>
                    <label>
                      <span>Interés previsto</span>
                      <input type="number" step="0.01" min="0" value={form.interes_previsto} onChange={(e) => setForm((f) => ({ ...f, interes_previsto: e.target.value }))} />
                    </label>
                    <AppSelect
                      label="Cuenta de referencia"
                      value={form.cuenta_referencia_id}
                      options={[
                        { value: '', label: 'Sin cuenta de referencia' },
                        ...cuentaReferenciaOptions.map((cuenta) => ({
                          value: cuenta.id,
                          label: `${cuenta.titular_nombre} - ${cuenta.nombre}`,
                        })),
                      ]}
                      onChange={(next) => setForm((f) => ({ ...f, cuenta_referencia_id: next }))}
                    />
                  </div>
                  <div className="users-check-row">
                    <label>
                      <input
                        type="checkbox"
                        checked={form.renovable}
                        onChange={(e) => setForm((f) => ({ ...f, renovable: e.target.checked }))}
                      />
                      Renovable
                    </label>
                  </div>
                  <label className="users-form-full">
                    <span>Notas de plazo fijo</span>
                    <textarea value={form.plazo_fijo_notas} onChange={(e) => setForm((f) => ({ ...f, plazo_fijo_notas: e.target.value }))} />
                  </label>
                </section>
              ) : null}

              <div className="users-form-actions phase2-modal-actions">
                <button type="button" onClick={closeFormModal} disabled={saving}>Cancelar</button>
                <button type="submit" disabled={saving}>{saving ? 'Guardando...' : 'Guardar'}</button>
              </div>
            </form>
          </div>
        </div>
      ) : null}

      <ConfirmDialog
        open={!!deleteCandidate}
        title="Eliminar cuenta"
        message={
          deleteCandidate
            ? `Vas a enviar a papelera la cuenta ${deleteCandidate.nombre}. El movimiento queda auditado y podrás restaurarla después.`
            : ''
        }
        confirmLabel="Confirmar eliminación"
        loadingLabel="Enviando..."
        loading={saving}
        onCancel={() => setDeleteCandidate(null)}
        onConfirm={remove}
      />
    </section>
  );
}
