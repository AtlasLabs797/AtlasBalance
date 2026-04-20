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
  DashboardEvolucion,
  DashboardPrincipal,
  DashboardSaldosDivisa,
  PaginatedResponse,
  PeriodoDashboard,
  TipoTitular,
  Titular,
} from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatCurrency, formatDate } from '@/utils/formatters';

interface TitularCard extends Titular {
  cuentas_count: number;
  deleted_at: string | null;
}

interface TitularFormState {
  nombre: string;
  tipo: TipoTitular;
  notas: string;
}

interface DeleteCandidate {
  id: string;
  nombre: string;
}

const emptyForm: TitularFormState = {
  nombre: '',
  tipo: 'EMPRESA',
  notas: '',
};

function getTitularInitials(nombre: string) {
  const initials = nombre
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('');

  return initials || 'T';
}

export default function TitularesPage() {
  const navigate = useNavigate();
  const usuario = useAuthStore((state) => state.usuario);
  const canViewDashboard = usePermisosStore((state) => state.canViewDashboard);
  const isAdmin = usuario?.rol === 'ADMIN';
  const canSeeDashboard = usuario?.rol === 'ADMIN' || (usuario?.rol === 'GERENTE' && canViewDashboard());

  const [items, setItems] = useState<TitularCard[]>([]);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(24);
  const [totalPages, setTotalPages] = useState(1);
  const [search, setSearch] = useState('');
  const [incluirEliminados, setIncluirEliminados] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  const [periodo, setPeriodo] = useState<PeriodoDashboard>('1m');
  const [divisaPrincipal, setDivisaPrincipal] = useState('EUR');
  const [principal, setPrincipal] = useState<DashboardPrincipal | null>(null);
  const [evolucion, setEvolucion] = useState<DashboardEvolucion | null>(null);
  const [saldosDivisa, setSaldosDivisa] = useState<DashboardSaldosDivisa | null>(null);
  const [dashboardLoading, setDashboardLoading] = useState(false);
  const [dashboardError, setDashboardError] = useState<string | null>(null);

  const [editingId, setEditingId] = useState<string | null>(null);
  const [form, setForm] = useState<TitularFormState>(emptyForm);
  const [isFormModalOpen, setIsFormModalOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [deleteCandidate, setDeleteCandidate] = useState<DeleteCandidate | null>(null);

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

  const saldoTitularById = useMemo(
    () => new Map((principal?.saldos_por_titular ?? []).map((item) => [item.titular_id, item.total_convertido])),
    [principal],
  );

  const saldoDivisa = principal?.divisa_principal ?? divisaPrincipal;

  const loadTitulares = async () => {
    setLoading(true);
    setError(null);
    try {
      const { data } = await api.get<PaginatedResponse<TitularCard>>('/titulares', {
        params: {
          page,
          pageSize,
          search: search || undefined,
          incluirEliminados: incluirEliminados && isAdmin,
          sortBy: 'nombre',
          sortDir: 'asc',
        },
      });
      setItems(data.data ?? []);
      setTotalPages(Math.max(data.total_pages ?? 1, 1));
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudieron cargar titulares'));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadTitulares();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- recarga controlada por filtros y paginacion
  }, [page, pageSize, search, incluirEliminados, isAdmin]);

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

        if (!mounted) {
          return;
        }

        setPrincipal(principalRes.data);
        setEvolucion(evolucionRes.data);
        setSaldosDivisa(saldosDivisaRes.data);
        if (principalRes.data.divisa_principal && principalRes.data.divisa_principal !== divisaPrincipal) {
          setDivisaPrincipal(principalRes.data.divisa_principal);
        }
      } catch (err) {
        if (!mounted) {
          return;
        }

        setDashboardError(extractErrorMessage(err, 'No se pudo cargar el dashboard de titulares.'));
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
    setForm(emptyForm);
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

  const startEdit = async (id: string) => {
    setSaving(true);
    setError(null);
    setFormError(null);
    try {
      const { data } = await api.get<TitularCard>(`/titulares/${id}`, {
        params: { incluirEliminados: true },
      });
      setEditingId(id);
      setForm({
        nombre: data.nombre ?? '',
        tipo: (data.tipo ?? 'EMPRESA') as TipoTitular,
        notas: data.notas ?? '',
      });
      setIsFormModalOpen(true);
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo cargar titular'));
    } finally {
      setSaving(false);
    }
  };

  const save = async () => {
    if (!isAdmin) return;
    if (!form.nombre.trim()) {
      setFormError('Nombre obligatorio');
      return;
    }

    setSaving(true);
    setFormError(null);
    const payload = {
      nombre: form.nombre.trim(),
      tipo: form.tipo,
      notas: form.notas.trim() || null,
      identificacion: null,
      contacto_email: null,
      contacto_telefono: null,
    };

    try {
      if (editingId) {
        await api.put(`/titulares/${editingId}`, payload);
      } else {
        await api.post('/titulares', payload);
      }
      resetForm();
      setIsFormModalOpen(false);
      await loadTitulares();
    } catch (err) {
      setFormError(extractErrorMessage(err, 'No se pudo guardar titular'));
    } finally {
      setSaving(false);
    }
  };

  const remove = async () => {
    if (!isAdmin || !deleteCandidate) return;
    setSaving(true);
    setError(null);
    try {
      await api.delete(`/titulares/${deleteCandidate.id}`);
      setDeleteCandidate(null);
      await loadTitulares();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo eliminar titular'));
    } finally {
      setSaving(false);
    }
  };

  const restore = async (id: string) => {
    if (!isAdmin) return;
    try {
      await api.post(`/titulares/${id}/restaurar`);
      await loadTitulares();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo restaurar titular'));
    }
  };

  return (
    <section className="phase2-page">
      <header className="phase2-header">
        <h1>Titulares</h1>
        {isAdmin && <button type="button" onClick={openCreateModal}>Nuevo Titular</button>}
      </header>

      {canSeeDashboard ? (
        <section className="dashboard-card titulares-dashboard-card">
          <header className="dashboard-card-header titulares-dashboard-header">
            <div>
              <h2>Saldos por titular</h2>
              <p className="dashboard-subtitle">Vista de saldos por titular y evolución consolidada.</p>
            </div>
            <div className="dashboard-toolbar-actions">
              <PeriodoSelector value={periodo} onChange={setPeriodo} />
              <DivisaSelector value={principal?.divisa_principal ?? divisaPrincipal} options={divisaOptions} onChange={setDivisaPrincipal} />
            </div>
          </header>

          {dashboardError ? <p className="auth-error">{dashboardError}</p> : null}
          {dashboardLoading ? <p className="import-muted">Cargando dashboard de titulares...</p> : null}

          {!dashboardLoading && !dashboardError && principal ? (
            <>
              <div className="titulares-chart-wrap">
                {chartRows.length === 0 ? (
                  <EmptyState title="No hay titulares con saldos." />
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

              <div className="titulares-balance-list" aria-label={`Saldos por titular en ${principal.divisa_principal}`}>
                <div className="titulares-balance-heading" aria-hidden="true">
                  <span>Titular</span>
                  <span>Saldo total ({principal.divisa_principal})</span>
                  <span>Detalle</span>
                </div>

                {principal.saldos_por_titular.map((item) => (
                  <Link
                    className="titulares-balance-row"
                    key={item.titular_id}
                    to={`/dashboard/titular/${item.titular_id}?periodo=${periodo}&divisa=${principal.divisa_principal}`}
                    aria-label={`Abrir dashboard de ${item.titular_nombre}`}
                  >
                    <span className="titulares-balance-owner">
                      <span className="titulares-balance-avatar" aria-hidden="true">
                        {getTitularInitials(item.titular_nombre)}
                      </span>
                      <span className="titulares-balance-name">{item.titular_nombre}</span>
                    </span>
                    <SignedAmount value={item.total_convertido}>
                      {formatCurrency(item.total_convertido, principal.divisa_principal)}
                    </SignedAmount>
                    <span className="titulares-balance-open" aria-hidden="true">Abrir</span>
                  </Link>
                ))}
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
          placeholder="Buscar titular"
          value={search}
          onChange={(e) => {
            setPage(1);
            setSearch(e.target.value);
          }}
        />
        <PageSizeSelect
          value={pageSize}
          options={[12, 24, 48]}
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
            Ver eliminados
          </label>
        )}
      </div>

      {error && <p className="auth-error">{error}</p>}

      <div className="phase2-grid">
        <div className="phase2-cards">
          {loading ? <p className="import-muted">Cargando titulares...</p> : null}
          {!loading && items.length === 0 ? <EmptyState title="Sin titulares para mostrar." /> : null}
          {!loading && items.map((item) => {
            const saldoTotal = saldoTitularById.get(item.id);

            return (
              <article className="titular-card" key={item.id}>
                <div className="titular-card-title">
                  <h3>{item.nombre}</h3>
                  <span className="pill">{item.tipo}</span>
                </div>
                <div className="titular-card-meta">
                  <div className="titular-card-meta-item titular-card-notes">
                    <span className="cuenta-card-meta-label">Notas</span>
                    <strong className="cuenta-card-meta-value">{item.notas || 'Sin notas'}</strong>
                  </div>
                  <div className="titular-card-meta-item">
                    <span className="cuenta-card-meta-label">Estado</span>
                    <strong className="cuenta-card-meta-value">{item.deleted_at ? 'Eliminado' : 'Activo'}</strong>
                  </div>
                  <div className="titular-card-meta-item titular-card-balance">
                    <span className="cuenta-card-meta-label">Saldo total</span>
                    {typeof saldoTotal === 'number' ? (
                      <SignedAmount value={saldoTotal}>
                        {formatCurrency(saldoTotal, saldoDivisa)}
                      </SignedAmount>
                    ) : (
                      <strong className="cuenta-card-meta-value">N/A</strong>
                    )}
                  </div>
                </div>
                <div className="phase2-row-actions">
                  {canSeeDashboard && !item.deleted_at ? (
                    <button
                      type="button"
                      className="titular-open-button"
                      onClick={() => navigate(`/dashboard/titular/${item.id}?periodo=${periodo}&divisa=${principal?.divisa_principal ?? divisaPrincipal}`)}
                    >
                      Abrir
                    </button>
                  ) : null}
                  {isAdmin ? (
                    <button type="button" onClick={() => startEdit(item.id)} disabled={saving}>Editar</button>
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
            className="users-modal phase2-form-modal"
            onClick={(event) => event.stopPropagation()}
            role="dialog"
            aria-modal="true"
            aria-labelledby="titulares-modal-title"
          >
            <div className="users-modal-header">
              <div>
                <h2 id="titulares-modal-title">{editingId ? 'Editar Titular' : 'Nuevo Titular'}</h2>
                <p>Alta y edición de titulares usados para agrupar cuentas y permisos.</p>
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
                <h3>Datos del titular</h3>
                <div className="users-form-grid">
                  <label>
                    <span>Nombre</span>
                    <input value={form.nombre} onChange={(e) => setForm((f) => ({ ...f, nombre: e.target.value }))} />
                  </label>

                  <AppSelect
                    label="Tipo"
                    value={form.tipo}
                    options={[
                      { value: 'EMPRESA', label: 'EMPRESA' },
                      { value: 'PARTICULAR', label: 'PARTICULAR' },
                    ]}
                    onChange={(next) => setForm((f) => ({ ...f, tipo: next as TipoTitular }))}
                  />

                  <label className="phase2-modal-wide-field">
                    <span>Notas</span>
                    <textarea rows={4} value={form.notas} onChange={(e) => setForm((f) => ({ ...f, notas: e.target.value }))} />
                  </label>
                </div>
              </section>

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
        title="Eliminar titular"
        message={
          deleteCandidate
            ? `Vas a enviar a papelera a ${deleteCandidate.nombre}. Las cuentas asociadas quedaran ocultas y la accion se auditara.`
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
