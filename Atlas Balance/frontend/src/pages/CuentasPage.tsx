import { useEffect, useMemo, useState } from 'react';
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
import { Link, useNavigate } from 'react-router-dom';
import { AppSelect } from '@/components/common/AppSelect';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { DatePickerField } from '@/components/common/DatePickerField';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import { SignedAmount } from '@/components/common/SignedAmount';
import { DivisaSelector } from '@/components/dashboard/DivisaSelector';
import { EvolucionChart } from '@/components/dashboard/EvolucionChart';
import { PeriodoSelector } from '@/components/dashboard/PeriodoSelector';
import { SaldoPorDivisaCard } from '@/components/dashboard/SaldoPorDivisaCard';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import { usePermisosStore } from '@/stores/permisosStore';
import type {
  Cuenta,
  DashboardEvolucion,
  DashboardPrincipal,
  DashboardTitular,
  DashboardSaldosDivisa,
  PaginatedResponse,
  PeriodoDashboard,
  TipoCuenta,
  TipoTitular,
  Titular,
} from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatCurrency, formatDate } from '@/utils/formatters';

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

export default function CuentasPage() {
  const navigate = useNavigate();
  const usuario = useAuthStore((state) => state.usuario);
  const canViewDashboard = usePermisosStore((state) => state.canViewDashboard);
  const canViewCuenta = usePermisosStore((state) => state.canViewCuenta);
  const isAdmin = usuario?.rol === 'ADMIN';
  const canSeeDashboard = usuario?.rol === 'ADMIN' || (usuario?.rol === 'GERENTE' && canViewDashboard());

  const [items, setItems] = useState<CuentaRow[]>([]);
  const [titulares, setTitulares] = useState<Titular[]>([]);
  const [divisas, setDivisas] = useState<DivisaOption[]>([]);
  const [formatos, setFormatos] = useState<FormatoOption[]>([]);

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [totalPages, setTotalPages] = useState(1);
  const [search, setSearch] = useState('');
  const [titularFilter, setTitularFilter] = useState('');
  const [tipoTitularFilter, setTipoTitularFilter] = useState('');
  const [tipoCuentaFilter, setTipoCuentaFilter] = useState('');
  const [incluirEliminados, setIncluirEliminados] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
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
    () => (principal?.saldos_por_titular ?? []).map((item) => ({
      ...item,
      total_convertido_formatted: formatCurrency(item.total_convertido, principal?.divisa_principal ?? divisaPrincipal),
    })),
    [divisaPrincipal, principal],
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

  const loadAuxData = async () => {
    try {
      const [titularesRes, divisasRes] = await Promise.all([
        api.get<PaginatedResponse<Titular>>('/titulares', { params: { page: 1, pageSize: 500, sortBy: 'nombre', sortDir: 'asc' } }),
        api.get<DivisaOption[]>('/cuentas/divisas-activas'),
      ]);
      setTitulares(titularesRes.data.data ?? []);
      setDivisas(divisasRes.data ?? []);

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
    } catch {
      // silent auxiliary load errors, main list has visible error handling
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
          search: search || undefined,
          titularId: titularFilter || undefined,
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
  }, [page, pageSize, search, titularFilter, tipoTitularFilter, tipoCuentaFilter, incluirEliminados, isAdmin]);

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
          api.get<DashboardPrincipal>('/dashboard/principal', { params: { divisaPrincipal } }),
          api.get<DashboardEvolucion>('/dashboard/evolucion', { params: { periodo, divisaPrincipal } }),
          api.get<DashboardSaldosDivisa>('/dashboard/saldos-divisa', { params: { divisaPrincipal } }),
        ]);
        const cuentasRes = await api.get<PaginatedResponse<CuentaRow>>('/cuentas', {
          params: { page: 1, pageSize: 2000, sortBy: 'nombre', sortDir: 'asc' },
        });
        const bancoByCuentaId = new Map<string, string | null>(
          (cuentasRes.data.data ?? []).map((cuenta) => [cuenta.id, cuenta.banco_nombre ?? null]),
        );
        const titularesDashboard = await Promise.all(
          principalRes.data.saldos_por_titular.map((item) =>
            api.get<DashboardTitular>(`/dashboard/titular/${item.titular_id}`, {
              params: { divisaPrincipal: principalRes.data.divisa_principal },
            }),
          ),
        );
        const cuentaRows = titularesDashboard
          .flatMap((response) =>
            response.data.saldos_por_cuenta.map((cuenta) => ({
              cuenta_id: cuenta.cuenta_id,
              cuenta_nombre: cuenta.cuenta_nombre,
              titular_id: response.data.titular_id,
              titular_nombre: response.data.titular_nombre,
              banco_nombre: cuenta.banco_nombre ?? bancoByCuentaId.get(cuenta.cuenta_id) ?? null,
              divisa: cuenta.divisa,
              saldo_actual: cuenta.saldo_actual,
              saldo_convertido: cuenta.saldo_convertido,
            })),
          )
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
  }, [canSeeDashboard, divisaPrincipal, periodo]);

  const resetForm = () => {
    setEditingId(null);
    setRenewingId(null);
    setForm(() => ({
      ...emptyForm,
      titular_id: titulares[0]?.id ?? '',
      divisa: divisas[0]?.codigo ?? 'EUR',
    }));
  };

  const openCreateModal = () => {
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

  useEffect(() => {
    if (form.tipo_cuenta === 'NORMAL') {
      if (form.formato_id && !formatosDisponibles.some((formato) => formato.id === form.formato_id)) {
        setForm((prev) => ({ ...prev, formato_id: '' }));
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
  }, [form.divisa, form.tipo_cuenta, form.formato_id, formatosDisponibles]);

  const startEdit = async (id: string) => {
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
        formato_id: data.tipo_cuenta === 'NORMAL' ? (data.formato_id ?? '') : '',
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
      setFormError('Titular y nombre son obligatorios');
      return;
    }
    if (form.tipo_cuenta === 'PLAZO_FIJO' && (!form.fecha_inicio || !form.fecha_vencimiento)) {
      setFormError('Fecha de inicio y vencimiento son obligatorias para plazo fijo');
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
      formato_id: form.tipo_cuenta === 'NORMAL' ? (form.formato_id || null) : null,
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
    <section className="phase2-page">
      <header className="phase2-header">
        <h1>Cuentas</h1>
        {isAdmin && <button type="button" onClick={openCreateModal}>Nueva Cuenta</button>}
      </header>

      {canSeeDashboard ? (
        <section className="dashboard-card titulares-dashboard-card">
          <header className="dashboard-card-header titulares-dashboard-header">
            <div>
              <h2>Saldos y evolucion</h2>
              <p className="dashboard-subtitle">Vista consolidada para saldos, divisas y tendencia.</p>
            </div>
            <div className="dashboard-toolbar-actions">
              <PeriodoSelector value={periodo} onChange={setPeriodo} />
              <DivisaSelector value={principal?.divisa_principal ?? divisaPrincipal} options={divisaOptions} onChange={setDivisaPrincipal} />
            </div>
          </header>

          {dashboardError ? <p className="auth-error">{dashboardError}</p> : null}
          {dashboardLoading ? <p className="import-muted">Cargando dashboard de cuentas...</p> : null}

          {!dashboardLoading && !dashboardError && principal ? (
            <>
              <div className="titulares-chart-wrap">
                {chartRows.length === 0 ? (
                  <EmptyState title="No hay saldos para mostrar." />
                ) : (
                  <ResponsiveContainer width="100%" height={340}>
                    <BarChart data={chartRows} margin={{ top: 12, right: 20, left: 8, bottom: 12 }}>
                      <CartesianGrid strokeDasharray="3 3" />
                      <XAxis dataKey="titular_nombre" interval={0} angle={-18} textAnchor="end" height={72} />
                      <YAxis
                        width={120}
                        tickFormatter={(value) => formatCurrency(Number(value), principal.divisa_principal)}
                      />
                      <Tooltip
                        formatter={(value: number) => formatCurrency(value, principal.divisa_principal)}
                        labelFormatter={(value) => `Titular: ${value}`}
                      />
                      <Bar dataKey="total_convertido" name={`Saldo total (${principal.divisa_principal})`}>
                        {chartRows.map((item) => (
                          <Cell
                            key={item.titular_id}
                            fill={item.total_convertido >= 0 ? 'var(--color-success)' : 'var(--color-danger)'}
                          />
                        ))}
                      </Bar>
                    </BarChart>
                  </ResponsiveContainer>
                )}
              </div>

              <div className="cuentas-balance-list" aria-label={`Saldos por cuenta bancaria en ${principal.divisa_principal}`}>
                <div className="cuentas-balance-heading" aria-hidden="true">
                  <span>Cuenta bancaria</span>
                  <span>Banco</span>
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
                      <span className="cuentas-balance-bank">{item.banco_nombre || 'N/A'}</span>
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
                      <span className="cuentas-balance-bank">{item.banco_nombre || 'N/A'}</span>
                      <span className="cuentas-balance-currency">{item.divisa}</span>
                      <SignedAmount value={item.saldo_actual}>
                        {formatCurrency(item.saldo_actual, item.divisa)}
                      </SignedAmount>
                      <span className="dashboard-open-link dashboard-open-link--disabled">Sin acceso</span>
                    </div>
                  );
                })}
              </div>

              {evolucion ? (
                <section className="dashboard-card titulares-evolucion-card">
                  <header className="dashboard-card-header">
                    <h3>Evolucion</h3>
                    <span className="dashboard-subtitle">Ultimo punto: {evolucion.puntos.length ? formatDate(evolucion.puntos[evolucion.puntos.length - 1].fecha) : 'N/A'}</span>
                  </header>
                  <EvolucionChart
                    points={evolucion.puntos}
                    divisa={principal.divisa_principal}
                    colors={principal.chart_colors}
                  />
                </section>
              ) : null}

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
          ariaLabel="Tipo de titular"
          value={tipoTitularFilter}
          options={[
            { value: '', label: 'Todos los tipos de titular' },
            { value: 'EMPRESA', label: 'Empresa' },
            { value: 'AUTONOMO', label: 'Autonomo' },
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

      {error && <p className="auth-error">{error}</p>}

      <div className="phase2-grid">
        <div className="phase2-cards">
          {loading ? <p className="import-muted">Cargando cuentas...</p> : null}
          {!loading && items.length === 0 ? <EmptyState title="Sin cuentas para mostrar." /> : null}
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
                  <span className="pill">{item.tipo_cuenta ?? (item.es_efectivo ? 'EFECTIVO' : 'NORMAL')}</span>
                  {item.plazo_fijo ? <span className="pill">{item.plazo_fijo.estado}</span> : null}
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
                    <strong className="cuenta-card-meta-value">{item.banco_nombre || 'N/A'}</strong>
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
                      <strong className="cuenta-card-meta-value">N/A</strong>
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
                      >
                        Abrir
                      </button>
                    ) : null}
                    {canSeeDashboard && !item.deleted_at && !canOpenDashboardCuenta ? (
                      <span className="dashboard-open-link dashboard-open-link--disabled">Sin acceso</span>
                    ) : null}
                  {isAdmin ? (
                    <button type="button" onClick={() => startEdit(item.id)} disabled={saving}>Editar</button>
                  ) : null}
                  {isAdmin && item.tipo_cuenta === 'PLAZO_FIJO' ? (
                    <button type="button" onClick={() => void startRenew(item.id)} disabled={saving}>Renovar</button>
                  ) : null}
                    {isAdmin && !item.deleted_at ? (
                      <button
                        type="button"
                        onClick={() => setDeleteCandidate({ id: item.id, nombre: item.nombre })}
                        disabled={saving}
                      >
                        Eliminar
                      </button>
                    ) : null}
                    {isAdmin && item.deleted_at ? (
                      <button type="button" onClick={() => restore(item.id)} disabled={saving}>Restaurar</button>
                    ) : null}
                  </div>
                ) : null}
              </article>
            );
          })}
          <div className="users-pagination">
            <button type="button" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1}>Anterior</button>
            <span>Pagina {page} / {totalPages}</span>
            <button type="button" onClick={() => setPage((p) => Math.min(totalPages, p + 1))} disabled={page >= totalPages}>Siguiente</button>
          </div>
        </div>
      </div>

      {isAdmin && isFormModalOpen ? (
        <div className="modal-backdrop users-modal-backdrop" onClick={closeFormModal}>
          <div
            className="users-modal phase2-form-modal phase2-form-modal--wide"
            onClick={(event) => event.stopPropagation()}
            role="dialog"
            aria-modal="true"
            aria-labelledby="cuentas-modal-title"
          >
            <div className="users-modal-header">
              <div>
                <h2 id="cuentas-modal-title">{renewingId ? 'Renovar plazo fijo' : editingId ? 'Editar Cuenta' : 'Nueva Cuenta'}</h2>
                <p>Alta y edición de cuentas bancarias o efectivo asociadas a un titular.</p>
              </div>
              <button type="button" className="users-modal-close" onClick={closeFormModal} disabled={saving}>
                Cerrar
              </button>
            </div>

            <form
              className="users-modal-body phase2-modal-form"
              onSubmit={(e) => {
                e.preventDefault();
                void save();
              }}
            >
              {formError ? <p className="auth-error">{formError}</p> : null}

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

              {form.tipo_cuenta === 'NORMAL' ? (
                <section className="users-modal-section">
                  <h3>Datos bancarios</h3>
                  <div className="users-form-grid">
                    <label>
                      <span>Banco</span>
                      <input value={form.banco_nombre} onChange={(e) => setForm((f) => ({ ...f, banco_nombre: e.target.value }))} />
                    </label>

                    <label>
                      <span>Numero de cuenta</span>
                      <input value={form.numero_cuenta} onChange={(e) => setForm((f) => ({ ...f, numero_cuenta: e.target.value }))} />
                    </label>

                    <label>
                      <span>IBAN</span>
                      <input value={form.iban} onChange={(e) => setForm((f) => ({ ...f, iban: e.target.value }))} />
                    </label>

                    <AppSelect
                      label="Formato de importacion"
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
                <p className="import-muted">Las cuentas de efectivo y plazo fijo no usan datos bancarios ni formato de importacion.</p>
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
                      <span>Interes previsto</span>
                      <input type="number" step="0.01" min="0" value={form.interes_previsto} onChange={(e) => setForm((f) => ({ ...f, interes_previsto: e.target.value }))} />
                    </label>
                    <AppSelect
                      label="Cuenta referencia"
                      value={form.cuenta_referencia_id}
                      options={[
                        { value: '', label: 'Sin cuenta referencia' },
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
            ? `Vas a enviar a papelera la cuenta ${deleteCandidate.nombre}. El movimiento queda auditado y podras restaurarla despues.`
            : ''
        }
        confirmLabel="Confirmar eliminacion"
        loading={saving}
        onCancel={() => setDeleteCandidate(null)}
        onConfirm={remove}
      />
    </section>
  );
}
