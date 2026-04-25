import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, Navigate, useLocation, useNavigate, useParams } from 'react-router-dom';
import { AxiosError } from 'axios';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import { SignedAmount } from '@/components/common/SignedAmount';
import { KpiCard } from '@/components/dashboard/KpiCard';
import { PeriodoSelector } from '@/components/dashboard/PeriodoSelector';
import EditableCell from '@/components/extractos/EditableCell';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import { usePermisosStore } from '@/stores/permisosStore';
import { IMPORTACION_COMPLETADA_EVENT } from '@/utils/appEvents';
import type { CuentaResumenKpi, Extracto, PaginatedResponse, PeriodoDashboard } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatCurrency, formatDate, formatDateTime, getAmountTone } from '@/utils/formatters';

interface DeleteCandidate {
  id: string;
  filaNumero: number;
  concepto: string | null;
}

interface UpdateExtractoPayload {
  fecha?: string;
  concepto?: string;
  comentarios?: string;
  monto?: number;
  saldo?: number;
  columnas_extra?: Record<string, string>;
}

const PLAZO_ESTADO_LABELS: Record<string, string> = {
  ACTIVO: 'Activo',
  PROXIMO_VENCER: 'Próximo a vencer',
  VENCIDO: 'Vencido',
  RENOVADO: 'Renovado',
  CANCELADO: 'Cancelado',
};

function parseDecimalInput(value: string, fieldLabel: string): number {
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) {
    throw new Error(`${fieldLabel} debe ser numerico.`);
  }

  return parsed;
}

function getDateOnlyDiffDays(value: string): number {
  const [year, month, day] = value.split('-').map(Number);
  const target = new Date(year, month - 1, day);
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());

  return Math.round((target.getTime() - today.getTime()) / 86_400_000);
}

function formatPlazoTiming(fechaVencimiento: string): string {
  const diffDays = getDateOnlyDiffDays(fechaVencimiento);

  if (diffDays < 0) return `Vencido hace ${Math.abs(diffDays)} días`;
  if (diffDays === 0) return 'Vence hoy';
  if (diffDays === 1) return 'Vence mañana';
  return `Faltan ${diffDays} días`;
}

function formatPlazoEstado(estado: string): string {
  return PLAZO_ESTADO_LABELS[estado] ?? estado.toLowerCase().replace(/_/g, ' ');
}

export default function CuentaDetailPage() {
  const { id } = useParams();
  const cuentaId = id ?? '';
  const location = useLocation();
  const navigate = useNavigate();
  const usuario = useAuthStore((state) => state.usuario);
  const canViewDashboard = usePermisosStore((state) => state.canViewDashboard);
  const canViewCuenta = usePermisosStore((state) => state.canViewCuenta);
  const canEditCuenta = usePermisosStore((state) => state.canEditCuenta);
  const canDeleteInCuenta = usePermisosStore((state) => state.canDeleteInCuenta);
  const canImportInCuenta = usePermisosStore((state) => state.canImportInCuenta);
  const getColumnasEditables = usePermisosStore((state) => state.getColumnasEditables);

  const [summary, setSummary] = useState<CuentaResumenKpi | null>(null);
  const [rows, setRows] = useState<Extracto[]>([]);
  const [periodo, setPeriodo] = useState<PeriodoDashboard>('1m');
  const [isImportModalOpen, setIsImportModalOpen] = useState(false);
  const [deleteCandidate, setDeleteCandidate] = useState<DeleteCandidate | null>(null);
  const [actionLoading, setActionLoading] = useState(false);
  const [notesDraft, setNotesDraft] = useState('');
  const [notesSaving, setNotesSaving] = useState(false);
  const [notesStatus, setNotesStatus] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);

  const allowedDashboard = usuario?.rol === 'ADMIN' || (usuario?.rol === 'GERENTE' && canViewDashboard());
  const canImport = Boolean(cuentaId) && summary ? canImportInCuenta(cuentaId, summary.titular_id) : false;
  const canDeleteRows = Boolean(cuentaId) && summary ? canDeleteInCuenta(cuentaId, summary.titular_id) : false;
  const canOpenAccount = Boolean(cuentaId) && summary ? canViewCuenta(cuentaId, summary.titular_id) : false;
  const canEditAccountNotes = Boolean(cuentaId) && summary ? canEditCuenta(cuentaId, summary.titular_id) : false;
  const plazoFijo = summary?.tipo_cuenta === 'PLAZO_FIJO' ? summary.plazo_fijo : null;

  const canEditCell = useCallback(
    (column: string) => {
      if (!cuentaId || !summary) {
        return false;
      }

      if (!canEditCuenta(cuentaId, summary.titular_id)) {
        return false;
      }

      const editableColumns = getColumnasEditables(cuentaId, summary.titular_id);
      return editableColumns === null || editableColumns.includes(column);
    },
    [canEditCuenta, cuentaId, getColumnasEditables, summary]
  );

  const loadCuentaData = useCallback(async () => {
    if (!id || !allowedDashboard) {
      return;
    }

    setLoading(true);
    setError(null);
    setForbidden(false);

    try {
      const [summaryRes, rowsRes] = await Promise.all([
        api.get<CuentaResumenKpi>(`/extractos/cuentas/${id}/resumen`, { params: { periodo } }),
        api.get<PaginatedResponse<Extracto>>('/extractos', {
          params: { cuentaId: id, page: 1, pageSize: 500, sortBy: 'fecha', sortDir: 'desc' },
        }),
      ]);

      setSummary(summaryRes.data);
      setRows(rowsRes.data.data ?? []);
    } catch (err) {
      if (err instanceof AxiosError && err.response?.status === 403) {
        setForbidden(true);
        setSummary(null);
        setRows([]);
        return;
      }

      setError(extractErrorMessage(err, 'No se pudo cargar la cuenta'));
    } finally {
      setLoading(false);
    }
  }, [allowedDashboard, id, periodo]);

  useEffect(() => {
    void loadCuentaData();
  }, [loadCuentaData]);

  useEffect(() => {
    setNotesDraft(summary?.notas ?? '');
    setNotesStatus(null);
  }, [summary?.cuenta_id, summary?.notas]);

  useEffect(() => {
    if (!id || !allowedDashboard) {
      return;
    }

    const handleImportCompleted = (event: MessageEvent) => {
      if (event.origin !== window.location.origin) {
        return;
      }

      const payload = event.data as { type?: string; cuentaId?: string } | null;
      if (!payload || payload.type !== IMPORTACION_COMPLETADA_EVENT || payload.cuentaId !== id) {
        return;
      }

      setIsImportModalOpen(false);
      void loadCuentaData();
    };

    window.addEventListener('message', handleImportCompleted);
    return () => window.removeEventListener('message', handleImportCompleted);
  }, [allowedDashboard, id, loadCuentaData]);

  useEffect(() => {
    if (!isImportModalOpen) {
      return;
    }

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setIsImportModalOpen(false);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isImportModalOpen]);

  const extraColumns = useMemo(
    () => [...new Set(rows.flatMap((row) => Object.keys(row.columnas_extra ?? {})))],
    [rows]
  );

  const importUrl = useMemo(() => {
    if (!summary) {
      return '/importacion';
    }

    const params = new URLSearchParams({
      cuentaId: summary.cuenta_id,
      autoClose: '1',
      embedded: '1',
      returnTo: `${location.pathname}${location.search}`,
      source: 'dashboard-cuenta',
    });

    return `/importacion?${params.toString()}`;
  }, [location.pathname, location.search, summary]);

  const confirmDeleteRow = async () => {
    if (!deleteCandidate) {
      return;
    }

    setActionLoading(true);
    setError(null);

    try {
      await api.delete(`/extractos/${deleteCandidate.id}`);
      setDeleteCandidate(null);
      await loadCuentaData();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo eliminar la linea del desglose'));
    } finally {
      setActionLoading(false);
    }
  };

  const saveCell = async (row: Extracto, column: string, value: string) => {
    if (!canEditCell(column)) {
      return;
    }

    const payload: UpdateExtractoPayload = {};
    try {
      if (column === 'fecha') payload.fecha = value;
      else if (column === 'concepto') payload.concepto = value;
      else if (column === 'comentarios') payload.comentarios = value;
      else if (column === 'monto') payload.monto = parseDecimalInput(value, 'Monto');
      else if (column === 'saldo') payload.saldo = parseDecimalInput(value, 'Saldo');
      else payload.columnas_extra = { [column]: value };
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Valor invalido.');
      throw err;
    }

    setActionLoading(true);
    setError(null);

    try {
      await api.put(`/extractos/${row.id}`, payload);
      await loadCuentaData();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo modificar la linea del desglose'));
      throw err;
    } finally {
      setActionLoading(false);
    }
  };

  const toggleCheck = async (row: Extracto, checked: boolean) => {
    if (!canEditCell('checked')) {
      return;
    }

    setActionLoading(true);
    setError(null);

    try {
      await api.patch(`/extractos/${row.id}/check`, { checked });
      await loadCuentaData();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo actualizar el check de la linea'));
    } finally {
      setActionLoading(false);
    }
  };

  const toggleFlag = async (row: Extracto, flagged: boolean) => {
    if (!canEditCell('flagged')) {
      return;
    }

    setActionLoading(true);
    setError(null);

    try {
      await api.patch(`/extractos/${row.id}/flag`, { flagged, nota: row.flagged_nota ?? undefined });
      await loadCuentaData();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo actualizar el flag de la linea'));
    } finally {
      setActionLoading(false);
    }
  };

  const saveGeneralNotes = async () => {
    if (!summary || !canEditAccountNotes) {
      return;
    }

    setNotesSaving(true);
    setNotesStatus(null);

    try {
      const { data } = await api.patch<{ notas: string | null }>(`/cuentas/${summary.cuenta_id}/notas`, {
        notas: notesDraft,
      });
      setSummary((current) => (current ? { ...current, notas: data.notas ?? null } : current));
      setNotesStatus('Notas guardadas');
    } catch (err) {
      setNotesStatus(extractErrorMessage(err, 'No se pudieron guardar las notas'));
    } finally {
      setNotesSaving(false);
    }
  };

  if (!allowedDashboard) {
    return <Navigate to="/extractos" replace />;
  }

  if (!id) {
    return <Navigate to="/dashboard" replace />;
  }

  if (forbidden) {
    return <Navigate to="/dashboard" replace />;
  }

  if (loading) return <PageSkeleton rows={4} />;
  if (error) return <p className="auth-error">{error}</p>;
  if (!summary) return <EmptyState title="Sin datos." />;
  if (!canOpenAccount) return <Navigate to="/dashboard" replace />;

  return (
    <section className="dashboard-page cuenta-detail-page">
      <header className="dashboard-toolbar">
        <div>
          <h1>{summary.cuenta_nombre}</h1>
          <p className="dashboard-subtitle">Dashboard por cuenta</p>
          {plazoFijo ? (
            <div className="cuenta-plazo-summary" aria-label={`Vencimiento del plazo fijo ${formatDate(plazoFijo.fecha_vencimiento)}`}>
              <span className="pill">Plazo fijo</span>
              <strong>Vence el {formatDate(plazoFijo.fecha_vencimiento)}</strong>
              <span>{formatPlazoTiming(plazoFijo.fecha_vencimiento)}</span>
              <span>Estado: {formatPlazoEstado(plazoFijo.estado)}</span>
            </div>
          ) : null}
        </div>
        <div className="dashboard-toolbar-actions">
          <PeriodoSelector value={periodo} onChange={setPeriodo} />
          <button type="button" onClick={() => navigate(`/dashboard/titular/${summary.titular_id}`)}>
            Volver al titular
          </button>
          <Link to={`/extractos?cuentaId=${summary.cuenta_id}`} className="dashboard-open-link">
            Ver en extractos
          </Link>
          <button
            type="button"
            className={`dashboard-open-link ${canImport ? '' : 'dashboard-open-link--disabled'}`.trim()}
            disabled={!canImport}
            onClick={(event) => {
              if (!canImport) {
                event.preventDefault();
                return;
              }
              setIsImportModalOpen(true);
            }}
          >
            Importar movimientos
          </button>
        </div>
      </header>

      <div className="dashboard-kpi-grid">
        <KpiCard
          title="Saldo total"
          value={<SignedAmount value={summary.saldo_actual}>{formatCurrency(summary.saldo_actual, summary.divisa)}</SignedAmount>}
        />
        <KpiCard
          title="Ingresos período"
          value={<SignedAmount value={summary.ingresos_mes}>{formatCurrency(summary.ingresos_mes, summary.divisa)}</SignedAmount>}
        />
        <KpiCard
          title="Egresos período"
          value={
            <SignedAmount value={summary.egresos_mes} tone="negative">
              {formatCurrency(summary.egresos_mes, summary.divisa)}
            </SignedAmount>
          }
        />
      </div>

      <section className="dashboard-card account-notes-card">
        <header className="dashboard-card-header">
          <h2>Notas generales</h2>
          <span className="dashboard-subtitle">{canEditAccountNotes ? 'Editables para esta cuenta' : 'Solo lectura'}</span>
        </header>
        <textarea
          className="account-notes-textarea"
          aria-label={`Notas generales de ${summary.cuenta_nombre}`}
          value={notesDraft}
          disabled={!canEditAccountNotes || notesSaving}
          placeholder="Anotaciones generales de la cuenta"
          onChange={(event) => {
            setNotesDraft(event.target.value);
            setNotesStatus(null);
          }}
        />
        <div className="account-notes-actions">
          <span className="dashboard-subtitle">{notesStatus ?? ' '}</span>
          {canEditAccountNotes ? (
            <button
              type="button"
              disabled={notesSaving || notesDraft === (summary.notas ?? '')}
              onClick={() => void saveGeneralNotes()}
            >
              {notesSaving ? 'Guardando...' : 'Guardar notas'}
            </button>
          ) : null}
        </div>
      </section>

      <section className="dashboard-card">
        <header className="dashboard-card-header">
          <h2>Desglose de la cuenta</h2>
          <span className="dashboard-subtitle">
            Última actualización:{' '}
            {summary.ultima_actualizacion ? formatDateTime(summary.ultima_actualizacion) : 'Sin movimientos'}
          </span>
        </header>

        {rows.length === 0 ? (
          <EmptyState title="Esta cuenta no tiene movimientos todavía." />
        ) : (
          <div className="dashboard-table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Nº Fila</th>
                  <th>Check</th>
                  <th>Flag</th>
                  <th>Fecha</th>
                  <th>Concepto</th>
                  <th>Comentarios</th>
                  <th>Monto ({summary.divisa})</th>
                  <th>Saldo ({summary.divisa})</th>
                  {extraColumns.map((column) => (
                    <th key={column}>{column}</th>
                  ))}
                  {canDeleteRows ? <th>Acciones</th> : null}
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr
                    key={row.id}
                    className={row.flagged ? 'dashboard-row-flagged' : undefined}
                    data-flagged={row.flagged ? 'true' : 'false'}
                    style={row.flagged ? { backgroundColor: 'var(--color-row-flagged)' } : undefined}
                  >
                    <td>{row.fila_numero}</td>
                    <td>
                      <input
                        type="checkbox"
                        checked={row.checked}
                        disabled={!canEditCell('checked') || actionLoading}
                        onChange={(event) => void toggleCheck(row, event.target.checked)}
                      />
                    </td>
                    <td>
                      <input
                        type="checkbox"
                        checked={row.flagged}
                        disabled={!canEditCell('flagged') || actionLoading}
                        onChange={(event) => void toggleFlag(row, event.target.checked)}
                      />
                    </td>
                    <td>
                      <EditableCell
                        value={row.fecha ?? ''}
                        editable={canEditCell('fecha') && !actionLoading}
                        onSave={(value) => saveCell(row, 'fecha', value)}
                      />
                    </td>
                    <td>
                      <EditableCell
                        value={row.concepto ?? ''}
                        editable={canEditCell('concepto') && !actionLoading}
                        onSave={(value) => saveCell(row, 'concepto', value)}
                      />
                    </td>
                    <td>
                      <EditableCell
                        value={row.comentarios ?? ''}
                        editable={canEditCell('comentarios') && !actionLoading}
                        onSave={(value) => saveCell(row, 'comentarios', value)}
                      />
                    </td>
                    <td>
                      <EditableCell
                        value={String(row.monto ?? '')}
                        editable={canEditCell('monto') && !actionLoading}
                        displayClassName={`signed-amount--${getAmountTone(row.monto)}`}
                        onSave={(value) => saveCell(row, 'monto', value)}
                      />
                    </td>
                    <td>
                      <EditableCell
                        value={String(row.saldo ?? '')}
                        editable={canEditCell('saldo') && !actionLoading}
                        displayClassName={`signed-amount--${getAmountTone(row.saldo)}`}
                        onSave={(value) => saveCell(row, 'saldo', value)}
                      />
                    </td>
                    {extraColumns.map((column) => (
                      <td key={`${row.id}-${column}`}>
                        <EditableCell
                          value={row.columnas_extra?.[column] ?? ''}
                          editable={canEditCell(column) && !actionLoading}
                          onSave={(value) => saveCell(row, column, value)}
                        />
                      </td>
                    ))}
                    {canDeleteRows ? (
                      <td>
                        <button
                          type="button"
                          className="dashboard-row-delete"
                          disabled={actionLoading}
                          onClick={() =>
                            setDeleteCandidate({
                              id: row.id,
                              filaNumero: row.fila_numero,
                              concepto: row.concepto,
                            })
                          }
                        >
                          Eliminar
                        </button>
                      </td>
                    ) : null}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {isImportModalOpen && (
        <div className="modal-backdrop import-modal-backdrop" role="presentation" onClick={() => setIsImportModalOpen(false)}>
          <div
            className="import-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="import-modal-title"
            onClick={(event) => event.stopPropagation()}
          >
            <header className="import-modal-header">
              <div>
                <h2 id="import-modal-title">Importar movimientos</h2>
                <p className="import-muted">Cuenta: {summary.cuenta_nombre}</p>
              </div>
              <button type="button" onClick={() => setIsImportModalOpen(false)}>
                Cerrar
              </button>
            </header>
            <div className="import-modal-body">
              <iframe
                title={`Importacion cuenta ${summary.cuenta_nombre}`}
                src={importUrl}
                className="import-modal-frame"
              />
            </div>
          </div>
        </div>
      )}

      <ConfirmDialog
        open={!!deleteCandidate}
        title="Eliminar linea"
        message={
          deleteCandidate
            ? `Vas a enviar a papelera la linea ${deleteCandidate.filaNumero}${deleteCandidate.concepto ? ` (${deleteCandidate.concepto})` : ''}. Se auditara y podra restaurarse desde Papelera.`
            : ''
        }
        confirmLabel="Eliminar"
        loading={actionLoading}
        onCancel={() => setDeleteCandidate(null)}
        onConfirm={confirmDeleteRow}
      />
    </section>
  );
}
