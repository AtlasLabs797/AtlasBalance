import { useCallback, useEffect, useMemo, useState } from 'react';
import { Fragment } from 'react';
import { Link, Navigate, useLocation, useNavigate, useParams } from 'react-router-dom';
import { AxiosError } from 'axios';
import { Flag, Plus, Trash2 } from 'lucide-react';
import { CloseIconButton } from '@/components/common/CloseIconButton';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { DatePickerField } from '@/components/common/DatePickerField';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
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
import { formatCurrency, formatDate, formatDateTime, getAmountTone, parseEuropeanNumber } from '@/utils/formatters';

const BULK_DELETE_PREVIEW_LIMIT = 6;
const DEFAULT_ACCOUNT_CELL = { ref: 'A1', label: 'Celda', value: 'Selecciona una celda del desglose' };

interface UpdateExtractoPayload {
  fecha?: string;
  concepto?: string;
  comentarios?: string;
  monto?: number;
  saldo?: number;
  columnas_extra?: Record<string, string>;
}

interface InsertRowDraft {
  afterRowId: string;
  insertBeforeFilaNumero: number;
  fecha: string;
  concepto: string;
  comentarios: string;
  monto: string;
  saldo: string;
  columnas_extra: Record<string, string>;
}

interface CreateExtractoResponse {
  id: string;
  fila_numero: number;
}

const PLAZO_ESTADO_LABELS: Record<string, string> = {
  ACTIVO: 'Activo',
  PROXIMO_VENCER: 'Próximo a vencer',
  VENCIDO: 'Vencido',
  RENOVADO: 'Renovado',
  CANCELADO: 'Cancelado',
};

function parseDecimalInput(value: string, fieldLabel: string): number {
  const parsed = parseEuropeanNumber(value);
  if (parsed === null) {
    throw new Error(`${fieldLabel} debe ser numérico. Ejemplo: 1.234,56.`);
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

function formatIban(value: string | null | undefined): string {
  const normalized = (value ?? '').replace(/\s+/g, '').trim();
  if (!normalized) return 'Sin IBAN';
  return normalized.replace(/(.{4})/g, '$1 ').trim();
}

function getAccountCellReference(filaNumero: number, columnIndex: number): string {
  const letters = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';
  const index = Math.max(0, columnIndex);
  const letter =
    index < letters.length
      ? letters[index]
      : `${letters[Math.floor(index / letters.length) - 1] ?? 'Z'}${letters[index % letters.length]}`;
  return `${letter}${filaNumero}`;
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
  const canAddInCuenta = usePermisosStore((state) => state.canAddInCuenta);
  const canImportInCuenta = usePermisosStore((state) => state.canImportInCuenta);
  const getColumnasEditables = usePermisosStore((state) => state.getColumnasEditables);

  const [summary, setSummary] = useState<CuentaResumenKpi | null>(null);
  const [rows, setRows] = useState<Extracto[]>([]);
  const [rowsPage, setRowsPage] = useState(1);
  const [rowsPageSize, setRowsPageSize] = useState(200);
  const [rowsTotal, setRowsTotal] = useState(0);
  const [rowsTotalPages, setRowsTotalPages] = useState(1);
  const [periodo, setPeriodo] = useState<PeriodoDashboard>('1m');
  const [isImportModalOpen, setIsImportModalOpen] = useState(false);
  const [bulkDeleteOpen, setBulkDeleteOpen] = useState(false);
  const [selectedRowIds, setSelectedRowIds] = useState<Set<string>>(new Set());
  const [insertDraft, setInsertDraft] = useState<InsertRowDraft | null>(null);
  const [actionLoading, setActionLoading] = useState(false);
  const [bulkActionStatus, setBulkActionStatus] = useState<string | null>(null);
  const [notesDraft, setNotesDraft] = useState('');
  const [notesSaving, setNotesSaving] = useState(false);
  const [notesStatus, setNotesStatus] = useState<string | null>(null);
  const [selectedCell, setSelectedCell] = useState(DEFAULT_ACCOUNT_CELL);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);

  const allowedDashboard = usuario?.rol === 'ADMIN' || (usuario?.rol === 'GERENTE' && canViewDashboard());
  const canImport = Boolean(cuentaId) && summary ? canImportInCuenta(cuentaId, summary.titular_id) : false;
  const canAddRows = Boolean(cuentaId) && summary ? canAddInCuenta(cuentaId, summary.titular_id) : false;
  const canDeleteRows = Boolean(cuentaId) && summary ? canDeleteInCuenta(cuentaId, summary.titular_id) : false;
  const canOpenAccount = Boolean(cuentaId) && summary ? canViewCuenta(cuentaId, summary.titular_id) : false;
  const canEditAccountNotes = Boolean(cuentaId) && summary ? canEditCuenta(cuentaId, summary.titular_id) : false;
  const plazoFijo = summary?.tipo_cuenta === 'PLAZO_FIJO' ? summary.plazo_fijo : null;
  const hasBankName = Boolean(summary?.banco_nombre?.trim());
  const hasIban = Boolean(summary?.iban?.trim());
  const bankLabel = hasBankName ? summary?.banco_nombre?.trim() : summary?.tipo_cuenta === 'EFECTIVO' ? 'Efectivo' : 'Sin banco';

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
  const canFlagRows = canEditCell('flagged');
  const canSelectRows = canDeleteRows || canFlagRows;

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
          params: { cuentaId: id, page: rowsPage, pageSize: rowsPageSize, sortBy: 'fila_numero', sortDir: 'desc' },
        }),
      ]);

      setSummary(summaryRes.data);
      setRows(rowsRes.data.data ?? []);
      setRowsTotal(rowsRes.data.total ?? rowsRes.data.data?.length ?? 0);
      setRowsTotalPages(Math.max(1, rowsRes.data.total_pages ?? 1));
    } catch (err) {
      if (err instanceof AxiosError && err.response?.status === 403) {
        setForbidden(true);
        setSummary(null);
        setRows([]);
        setRowsTotal(0);
        setRowsTotalPages(1);
        return;
      }

      setError(extractErrorMessage(err, 'No se pudo cargar la cuenta'));
    } finally {
      setLoading(false);
    }
  }, [allowedDashboard, id, periodo, rowsPage, rowsPageSize]);

  useEffect(() => {
    void loadCuentaData();
  }, [loadCuentaData]);

  useEffect(() => {
    setRowsPage(1);
  }, [id]);

  useEffect(() => {
    if (rowsPage > rowsTotalPages) {
      setRowsPage(rowsTotalPages);
    }
  }, [rowsPage, rowsTotalPages]);

  useEffect(() => {
    setNotesDraft(summary?.notas ?? '');
    setNotesStatus(null);
  }, [summary?.cuenta_id, summary?.notas]);

  useEffect(() => {
    setSelectedRowIds((current) => {
      if (current.size === 0) {
        return current;
      }

      const validIds = new Set(rows.map((row) => row.id));
      const next = new Set<string>();
      current.forEach((rowId) => {
        if (validIds.has(rowId)) {
          next.add(rowId);
        }
      });
      return next;
    });
  }, [rows]);

  useEffect(() => {
    if (!canSelectRows) {
      setSelectedRowIds(new Set());
      setBulkDeleteOpen(false);
    }
  }, [canSelectRows]);

  useEffect(() => {
    if (!canAddRows) {
      setInsertDraft(null);
    }
  }, [canAddRows]);

  useEffect(() => {
    if (bulkDeleteOpen && rows.every((row) => !selectedRowIds.has(row.id))) {
      setBulkDeleteOpen(false);
    }
  }, [bulkDeleteOpen, rows, selectedRowIds]);

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
  const detailTableColumnCount = 9 + extraColumns.length + (canSelectRows ? 1 : 0);
  const selectedRows = useMemo(
    () => rows.filter((row) => selectedRowIds.has(row.id)),
    [rows, selectedRowIds]
  );
  const selectedRowsCount = selectedRows.length;
  const allRowsSelected = rows.length > 0 && selectedRowsCount === rows.length;
  const bulkDeleteConfirmLabel = `Eliminar ${selectedRowsCount} ${selectedRowsCount === 1 ? 'movimiento' : 'movimientos'}`;
  const selectedRowsPreview = selectedRows.slice(0, BULK_DELETE_PREVIEW_LIMIT).map((row) => row.fila_numero).join(', ');
  const hiddenSelectedRows = Math.max(0, selectedRowsCount - BULK_DELETE_PREVIEW_LIMIT);
  const rowsStart = rowsTotal === 0 ? 0 : ((rowsPage - 1) * rowsPageSize) + 1;
  const rowsEnd = rowsTotal === 0 ? 0 : Math.min(rowsTotal, rowsStart + rows.length - 1);

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

  const toggleRowSelection = (rowId: string, selected: boolean) => {
    setBulkActionStatus(null);
    setSelectedRowIds((current) => {
      const next = new Set(current);
      if (selected) {
        next.add(rowId);
      } else {
        next.delete(rowId);
      }
      return next;
    });
  };

  const toggleSelectAllRows = (selected: boolean) => {
    setBulkActionStatus(null);
    if (!selected) {
      setSelectedRowIds(new Set());
      return;
    }

    setSelectedRowIds(new Set(rows.map((row) => row.id)));
  };

  const confirmDeleteSelectedRows = async () => {
    if (selectedRowsCount === 0) {
      return;
    }

    setActionLoading(true);
    setError(null);

    let deletedCount = 0;

    for (const row of selectedRows) {
      try {
        await api.delete(`/extractos/${row.id}`);
        deletedCount += 1;
      } catch (err) {
        setError(
          extractErrorMessage(
            err,
            `Se eliminaron ${deletedCount} de ${selectedRowsCount} movimientos. Revisa permisos o vuelve a intentarlo.`
          )
        );
        break;
      }
    }

    try {
      if (deletedCount > 0) {
        const deletedIds = new Set(selectedRows.slice(0, deletedCount).map((row) => row.id));
        setRows((current) => current.filter((row) => !deletedIds.has(row.id)));
        setRowsTotal((current) => Math.max(0, current - deletedIds.size));
        setSelectedRowIds((current) => {
          const next = new Set(current);
          deletedIds.forEach((rowId) => next.delete(rowId));
          return next;
        });
        setBulkDeleteOpen(false);
      }
    } finally {
      setActionLoading(false);
    }
  };

  const openInsertDraftBelow = (row: Extracto) => {
    setInsertDraft({
      afterRowId: row.id,
      insertBeforeFilaNumero: row.fila_numero,
      fecha: row.fecha ?? new Date().toISOString().slice(0, 10),
      concepto: '',
      comentarios: '',
      monto: '0',
      saldo: String(row.saldo ?? 0),
      columnas_extra: Object.fromEntries(extraColumns.map((column) => [column, ''])),
    });
  };

  const updateInsertDraft = (patch: Partial<InsertRowDraft>) => {
    setInsertDraft((current) => (current ? { ...current, ...patch } : current));
  };

  const updateInsertDraftExtra = (column: string, value: string) => {
    setInsertDraft((current) =>
      current
        ? {
            ...current,
            columnas_extra: { ...current.columnas_extra, [column]: value },
          }
        : current
    );
  };

  const submitInsertDraft = async () => {
    if (!summary || !insertDraft) {
      return;
    }

    const monto = parseEuropeanNumber(insertDraft.monto);
    const saldo = parseEuropeanNumber(insertDraft.saldo);
    if (!insertDraft.fecha || monto === null || saldo === null) {
      setError('Completa una fecha válida y usa formato 1.234,56 en Monto y Saldo.');
      return;
    }

    setActionLoading(true);
    setError(null);

    try {
      const { data } = await api.post<CreateExtractoResponse>('/extractos', {
        cuenta_id: summary.cuenta_id,
        insert_before_fila_numero: insertDraft.insertBeforeFilaNumero,
        fecha: insertDraft.fecha,
        concepto: insertDraft.concepto,
        comentarios: insertDraft.comentarios,
        monto,
        saldo,
        columnas_extra: insertDraft.columnas_extra,
      });
      const createdAt = new Date().toISOString();
      const newRow: Extracto = {
        id: data.id,
        cuenta_id: summary.cuenta_id,
        fecha: insertDraft.fecha,
        concepto: insertDraft.concepto.trim() || null,
        comentarios: insertDraft.comentarios.trim() || null,
        monto,
        saldo,
        fila_numero: data.fila_numero,
        checked: false,
        checked_at: null,
        checked_by_id: null,
        flagged: false,
        flagged_nota: null,
        flagged_at: null,
        flagged_by_id: null,
        columnas_extra: insertDraft.columnas_extra,
        fecha_creacion: createdAt,
        fecha_modificacion: null,
        cuenta_nombre: summary.cuenta_nombre,
        titular_id: summary.titular_id,
        titular_nombre: summary.titular_nombre,
        divisa: summary.divisa,
      };
      setRows((current) =>
        [...current.map((row) => (row.fila_numero >= data.fila_numero ? { ...row, fila_numero: row.fila_numero + 1 } : row)), newRow].sort(
          (a, b) => b.fila_numero - a.fila_numero
        )
      );
      setSummary((current) => (current ? { ...current, ultima_actualizacion: createdAt } : current));
      setRowsTotal((current) => current + 1);
      setRowsTotalPages((current) => Math.max(current, Math.ceil((rowsTotal + 1) / rowsPageSize)));
      setInsertDraft(null);
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo insertar el movimiento.'));
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
      else if (column === 'monto') payload.monto = parseDecimalInput(value, 'Importe');
      else if (column === 'saldo') payload.saldo = parseDecimalInput(value, 'Saldo');
      else payload.columnas_extra = { [column]: value };
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Valor inválido.');
      throw err;
    }

    setActionLoading(true);
    setError(null);

    try {
      await api.put(`/extractos/${row.id}`, payload);
      await loadCuentaData();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo modificar el movimiento.'));
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

    const previousRows = rows;
    const changedAt = new Date().toISOString();
    setRows((current) =>
      current.map((item) =>
        item.id === row.id
          ? {
              ...item,
              checked,
              checked_at: checked ? changedAt : null,
              checked_by_id: checked ? usuario?.id ?? item.checked_by_id : null,
              fecha_modificacion: changedAt,
            }
          : item
      )
    );

    try {
      await api.patch(`/extractos/${row.id}/check`, { checked });
    } catch (err) {
      setRows(previousRows);
      setError(extractErrorMessage(err, 'No se pudo marcar el movimiento como revisado.'));
    } finally {
      setActionLoading(false);
    }
  };

  const flagSelectedRows = async () => {
    if (!canFlagRows) {
      return;
    }

    if (selectedRowsCount === 0) {
      setBulkActionStatus('Selecciona al menos un movimiento.');
      return;
    }

    const rowsToFlag = selectedRows.filter((row) => !row.flagged);
    if (rowsToFlag.length === 0) {
      setBulkActionStatus('Los movimientos seleccionados ya están marcados con alerta.');
      return;
    }

    setActionLoading(true);
    setError(null);
    setBulkActionStatus(null);

    const flaggedIds = new Set<string>();
    const changedAt = new Date().toISOString();

    try {
      for (const row of rowsToFlag) {
        await api.patch(`/extractos/${row.id}/flag`, { flagged: true, nota: row.flagged_nota ?? undefined });
        flaggedIds.add(row.id);
        setRows((current) =>
          current.map((item) =>
            item.id === row.id
              ? {
                  ...item,
                  flagged: true,
                  flagged_at: changedAt,
                  flagged_by_id: usuario?.id ?? item.flagged_by_id,
                  fecha_modificacion: changedAt,
                }
              : item
          )
        );
      }
      setBulkActionStatus(
        `${flaggedIds.size} ${flaggedIds.size === 1 ? 'movimiento marcado' : 'movimientos marcados'} con alerta.`
      );
    } catch (err) {
      setError(
        extractErrorMessage(
          err,
          flaggedIds.size > 0
            ? `Se marcaron ${flaggedIds.size} de ${rowsToFlag.length} movimientos seleccionados.`
            : 'No se pudo marcar la selección con alerta.'
        )
      );
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

  const selectAccountCell = (row: Extracto, columnIndex: number, label: string, value: string) => {
    setSelectedCell({
      ref: getAccountCellReference(row.fila_numero, columnIndex),
      label,
      value: value || '-',
    });
  };

  if (!allowedDashboard) {
    return <Navigate to="/extractos" replace />;
  }

  if (!id) {
    return <Navigate to="/dashboard" replace />;
  }

  if (forbidden) {
    return (
      <section className="page-placeholder">
        <EmptyState
          variant="permission"
          title="No tienes permiso para abrir esta cuenta."
          subtitle="La cuenta existe, pero tu usuario no tiene acceso a su desglose."
          primaryAction={<button type="button" onClick={() => navigate(-1)}>Volver</button>}
          secondaryAction={<Link to="/extractos">Ir a Extractos</Link>}
        />
      </section>
    );
  }

  if (loading) return <PageSkeleton rows={4} />;
  if (error) return <p className="auth-error">{error}</p>;
  if (!summary) {
    return (
      <EmptyState
        title="Esta cuenta no tiene movimientos visibles."
        subtitle="Ajusta los filtros, importa movimientos o revisa tus permisos."
      />
    );
  }
  if (!canOpenAccount) {
    return (
      <section className="page-placeholder">
        <EmptyState
          variant="permission"
          title="No tienes permiso para abrir esta cuenta."
          subtitle="Pide acceso sobre la cuenta o el titular si necesitas consultar estos movimientos."
          primaryAction={<Link to="/extractos">Ir a Extractos</Link>}
        />
      </section>
    );
  }

  return (
    <section className="dashboard-page cuenta-detail-page">
      <header className="dashboard-toolbar">
        <div className="dashboard-toolbar-main">
          <div className="cuenta-heading-block">
            <h1>{summary.cuenta_nombre}</h1>
            <p className="dashboard-subtitle">Dashboard por cuenta</p>
          </div>
          <dl className="account-identity-strip" aria-label="Datos de la cuenta">
            <div>
              <dt>Titular</dt>
              <dd>{summary.titular_nombre}</dd>
            </div>
            <div>
              <dt>Banco</dt>
              <dd className={hasBankName || summary.tipo_cuenta === 'EFECTIVO' ? undefined : 'account-identity-value--muted'}>
                {bankLabel}
              </dd>
            </div>
            <div className="account-identity-iban">
              <dt>IBAN</dt>
              <dd className={hasIban ? undefined : 'account-identity-value--muted'}>{formatIban(summary.iban)}</dd>
            </div>
          </dl>
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
        {canSelectRows && rows.length > 0 ? (
          <div className="dashboard-bulk-actions">
            <span className="dashboard-bulk-selection">
              {selectedRowsCount > 0
                ? `${selectedRowsCount} ${selectedRowsCount === 1 ? 'línea seleccionada' : 'líneas seleccionadas'}`
                : 'Selecciona movimientos desde la primera columna'}
            </span>
            <div className="dashboard-bulk-actions-buttons">
              <span className="dashboard-subtitle" aria-live="polite">
                {bulkActionStatus ?? (selectedRowsCount > 0 ? 'Acciones sobre selección' : 'Sin selección activa')}
              </span>
              {canFlagRows ? (
                <button
                  type="button"
                  className="dashboard-icon-action dashboard-flag-selected"
                  disabled={actionLoading || selectedRowsCount === 0}
                  aria-label="Marcar selección con alerta"
                  title="Marcar selección con alerta"
                  onClick={() => void flagSelectedRows()}
                >
                  <Flag size={16} aria-hidden="true" />
                </button>
              ) : null}
              {canDeleteRows ? (
              <button
                type="button"
                className="dashboard-icon-action dashboard-row-delete"
                disabled={actionLoading || selectedRowsCount === 0}
                aria-label="Eliminar movimientos seleccionados"
                title="Eliminar movimientos seleccionados"
                onClick={() => setBulkDeleteOpen(true)}
              >
                <Trash2 size={16} aria-hidden="true" />
              </button>
              ) : null}
            </div>
          </div>
        ) : null}

        {rowsTotal > 0 ? (
          <div className="account-rows-pagination" aria-label="Paginación del desglose de cuenta">
            <span>
              Mostrando {rowsStart}-{rowsEnd} de {rowsTotal} movimientos
            </span>
            <div className="account-rows-pagination-actions">
              <button
                type="button"
                disabled={actionLoading || rowsPage <= 1}
                onClick={() => setRowsPage((current) => Math.max(1, current - 1))}
              >
                Anterior
              </button>
              <span>Página {rowsPage} / {rowsTotalPages}</span>
              <button
                type="button"
                disabled={actionLoading || rowsPage >= rowsTotalPages}
                onClick={() => setRowsPage((current) => Math.min(rowsTotalPages, current + 1))}
              >
                Siguiente
              </button>
              <PageSizeSelect
                value={rowsPageSize}
                options={[100, 200, 500]}
                ariaLabel="Movimientos por página"
                onChange={(next) => {
                  setRowsPageSize(next);
                  setRowsPage(1);
                }}
              />
            </div>
          </div>
        ) : null}

        {rows.length === 0 ? (
          <EmptyState
            title="Esta cuenta aún no tiene movimientos."
            subtitle="Importa un extracto o añade un movimiento manual."
          />
        ) : (
          <>
          <div className="account-formula-bar" aria-live="polite">
            <span className="account-formula-ref">{selectedCell.ref}</span>
            <span className="account-formula-label">{selectedCell.label}</span>
            <output>{selectedCell.value || '-'}</output>
          </div>
          <div className="dashboard-table-wrap account-excel-wrap">
            <table className="account-excel-table">
              <colgroup>
                {canSelectRows ? <col className="account-col-select" /> : null}
                <col className="account-col-row" />
                <col className="account-col-check" />
                <col className="account-col-date" />
                <col className="account-col-text" />
                <col className="account-col-text" />
                <col className="account-col-money" />
                <col className="account-col-money" />
                <col className="account-col-money" />
                <col className="account-col-money" />
                {extraColumns.map((column) => (
                  <col className="account-col-extra" key={column} />
                ))}
              </colgroup>
              <thead>
                <tr>
                  {canSelectRows ? (
                    <th className="account-selection-header">
                      <input
                        type="checkbox"
                        checked={allRowsSelected}
                        disabled={actionLoading || rows.length === 0}
                        aria-label={allRowsSelected ? 'Deseleccionar todos los movimientos' : 'Seleccionar todos los movimientos'}
                        onChange={(event) => toggleSelectAllRows(event.target.checked)}
                      />
                    </th>
                  ) : null}
                  <th className="account-row-header">Nº Fila</th>
                  <th>Revisado</th>
                  <th>Fecha</th>
                  <th>Concepto</th>
                  <th>Comentarios</th>
                  <th>Ingreso ({summary.divisa})</th>
                  <th>Egreso ({summary.divisa})</th>
                  <th>Importe ({summary.divisa})</th>
                  <th>Saldo ({summary.divisa})</th>
                  {extraColumns.map((column) => (
                    <th key={column}>{column}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <Fragment key={row.id}>
                    <tr
                      className={`account-excel-row ${row.flagged ? 'dashboard-row-flagged' : ''}`.trim()}
                      data-flagged={row.flagged ? 'true' : 'false'}
                      style={row.flagged ? { backgroundColor: 'var(--color-row-flagged)' } : undefined}
                    >
                      {canSelectRows ? (
                        <td className="account-selection-cell">
                          <input
                            type="checkbox"
                            checked={selectedRowIds.has(row.id)}
                            disabled={actionLoading}
                            aria-label={`Seleccionar movimiento ${row.fila_numero}`}
                            onChange={(event) => toggleRowSelection(row.id, event.target.checked)}
                          />
                        </td>
                      ) : null}
                      <td
                        className="account-cell-fixed account-row-anchor-cell"
                        tabIndex={0}
                        onClick={() => selectAccountCell(row, 0, 'Nº Fila', String(row.fila_numero))}
                        onFocus={() => selectAccountCell(row, 0, 'Nº Fila', String(row.fila_numero))}
                      >
                        {row.fila_numero}
                        {canAddRows ? (
                          <button
                            type="button"
                            className="account-row-insert-trigger"
                            disabled={actionLoading}
                            aria-label={`Insertar movimiento debajo de ${row.fila_numero}`}
                            title={`Insertar movimiento debajo de ${row.fila_numero}`}
                            onClick={(event) => {
                              event.stopPropagation();
                              openInsertDraftBelow(row);
                            }}
                          >
                            <Plus size={14} aria-hidden="true" />
                          </button>
                        ) : null}
                      </td>
                      <td>
                        <input
                          type="checkbox"
                          checked={row.checked}
                          disabled={!canEditCell('checked') || actionLoading}
                          aria-label={`Marcar movimiento ${row.fila_numero} como revisado`}
                          onChange={(event) => void toggleCheck(row, event.target.checked)}
                        />
                      </td>
                      <td
                        className="account-cell-date account-cell-fixed"
                        tabIndex={0}
                        onClick={() => selectAccountCell(row, 2, 'Fecha', row.fecha ? formatDate(row.fecha) : '')}
                        onFocus={() => selectAccountCell(row, 2, 'Fecha', row.fecha ? formatDate(row.fecha) : '')}
                      >
                        <EditableCell
                          value={row.fecha ?? ''}
                          displayValue={row.fecha ? formatDate(row.fecha) : ''}
                          editable={canEditCell('fecha') && !actionLoading}
                          onSave={(value) => saveCell(row, 'fecha', value)}
                        />
                      </td>
                      <td
                        className="account-cell-text"
                        tabIndex={0}
                        onClick={() => selectAccountCell(row, 3, 'Concepto', row.concepto ?? '')}
                        onFocus={() => selectAccountCell(row, 3, 'Concepto', row.concepto ?? '')}
                      >
                        <EditableCell
                          value={row.concepto ?? ''}
                          editable={canEditCell('concepto') && !actionLoading}
                          onSave={(value) => saveCell(row, 'concepto', value)}
                        />
                      </td>
                      <td
                        className="account-cell-text"
                        tabIndex={0}
                        onClick={() => selectAccountCell(row, 4, 'Comentarios', row.comentarios ?? '')}
                        onFocus={() => selectAccountCell(row, 4, 'Comentarios', row.comentarios ?? '')}
                      >
                        <EditableCell
                          value={row.comentarios ?? ''}
                          editable={canEditCell('comentarios') && !actionLoading}
                          onSave={(value) => saveCell(row, 'comentarios', value)}
                        />
                      </td>
                      <td
                        className="account-cell-money account-cell-fixed"
                        tabIndex={0}
                        onClick={() => selectAccountCell(row, 5, 'Ingreso', row.monto > 0 ? formatCurrency(row.monto, summary.divisa) : '')}
                        onFocus={() => selectAccountCell(row, 5, 'Ingreso', row.monto > 0 ? formatCurrency(row.monto, summary.divisa) : '')}
                      >
                        <SignedAmount value={row.monto > 0 ? row.monto : 0}>
                          {row.monto > 0 ? formatCurrency(row.monto, summary.divisa) : '-'}
                        </SignedAmount>
                      </td>
                      <td
                        className="account-cell-money account-cell-fixed"
                        tabIndex={0}
                        onClick={() => selectAccountCell(row, 6, 'Egreso', row.monto < 0 ? formatCurrency(Math.abs(row.monto), summary.divisa) : '')}
                        onFocus={() => selectAccountCell(row, 6, 'Egreso', row.monto < 0 ? formatCurrency(Math.abs(row.monto), summary.divisa) : '')}
                      >
                        <SignedAmount value={row.monto < 0 ? row.monto : 0} tone="negative">
                          {row.monto < 0 ? formatCurrency(Math.abs(row.monto), summary.divisa) : '-'}
                        </SignedAmount>
                      </td>
                      <td
                        className="account-cell-money account-cell-fixed"
                        tabIndex={0}
                        onClick={() => selectAccountCell(row, 7, 'Monto', formatCurrency(row.monto, summary.divisa))}
                        onFocus={() => selectAccountCell(row, 7, 'Monto', formatCurrency(row.monto, summary.divisa))}
                      >
                        <EditableCell
                          value={String(row.monto ?? '')}
                          displayValue={formatCurrency(row.monto, summary.divisa)}
                          editable={canEditCell('monto') && !actionLoading}
                          displayClassName={`signed-amount--${getAmountTone(row.monto)}`}
                          onSave={(value) => saveCell(row, 'monto', value)}
                        />
                      </td>
                      <td
                        className="account-cell-money account-cell-fixed"
                        tabIndex={0}
                        onClick={() => selectAccountCell(row, 8, 'Saldo', formatCurrency(row.saldo, summary.divisa))}
                        onFocus={() => selectAccountCell(row, 8, 'Saldo', formatCurrency(row.saldo, summary.divisa))}
                      >
                        <EditableCell
                          value={String(row.saldo ?? '')}
                          displayValue={formatCurrency(row.saldo, summary.divisa)}
                          editable={canEditCell('saldo') && !actionLoading}
                          displayClassName={`signed-amount--${getAmountTone(row.saldo)}`}
                          onSave={(value) => saveCell(row, 'saldo', value)}
                        />
                      </td>
                      {extraColumns.map((column, extraIndex) => (
                        <td
                          key={`${row.id}-${column}`}
                          className="account-cell-text"
                          tabIndex={0}
                          onClick={() => selectAccountCell(row, 9 + extraIndex, column, row.columnas_extra?.[column] ?? '')}
                          onFocus={() => selectAccountCell(row, 9 + extraIndex, column, row.columnas_extra?.[column] ?? '')}
                        >
                          <EditableCell
                            value={row.columnas_extra?.[column] ?? ''}
                            editable={canEditCell(column) && !actionLoading}
                            onSave={(value) => saveCell(row, column, value)}
                          />
                        </td>
                      ))}
                    </tr>
                    {insertDraft?.afterRowId === row.id ? (
                      <tr className="dashboard-insert-row">
                        <td colSpan={detailTableColumnCount}>
                          <form
                            className="dashboard-insert-form"
                            onSubmit={(event) => {
                              event.preventDefault();
                              void submitInsertDraft();
                            }}
                          >
                            <div className="dashboard-insert-grid">
                              <DatePickerField
                                label="Fecha"
                                ariaLabel="Fecha del nuevo movimiento"
                                value={insertDraft.fecha}
                                onChange={(value) => updateInsertDraft({ fecha: value })}
                              />
                              <label className="add-row-field">
                                <span>Concepto</span>
                                <input
                                  value={insertDraft.concepto}
                                  onChange={(event) => updateInsertDraft({ concepto: event.target.value })}
                                />
                              </label>
                              <label className="add-row-field">
                                <span>Comentarios</span>
                                <input
                                  value={insertDraft.comentarios}
                                  onChange={(event) => updateInsertDraft({ comentarios: event.target.value })}
                                />
                              </label>
                              <label className="add-row-field">
                                <span>Importe ({summary.divisa})</span>
                                <input
                                  inputMode="decimal"
                                  value={insertDraft.monto}
                                  onChange={(event) => updateInsertDraft({ monto: event.target.value })}
                                />
                              </label>
                              <label className="add-row-field">
                                <span>Saldo ({summary.divisa})</span>
                                <input
                                  inputMode="decimal"
                                  value={insertDraft.saldo}
                                  onChange={(event) => updateInsertDraft({ saldo: event.target.value })}
                                />
                              </label>
                              {extraColumns.map((column) => (
                                <label className="add-row-field" key={column}>
                                  <span>{column}</span>
                                  <input
                                    value={insertDraft.columnas_extra[column] ?? ''}
                                    onChange={(event) => updateInsertDraftExtra(column, event.target.value)}
                                  />
                                </label>
                              ))}
                            </div>
                            <div className="dashboard-insert-actions">
                              <span className="dashboard-subtitle">Nuevo movimiento debajo de Nº {row.fila_numero}</span>
                              <button
                                type="button"
                                disabled={actionLoading}
                                onClick={() => setInsertDraft(null)}
                              >
                                Cancelar
                              </button>
                              <button
                                type="submit"
                                disabled={actionLoading || !insertDraft.fecha || insertDraft.monto === '' || insertDraft.saldo === ''}
                              >
                                {actionLoading ? 'Guardando...' : 'Guardar movimiento'}
                              </button>
                            </div>
                          </form>
                        </td>
                      </tr>
                    ) : null}
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>
          </>
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
              <CloseIconButton
                className="import-modal-close"
                onClick={() => setIsImportModalOpen(false)}
                ariaLabel="Cerrar modal de importación"
              />
            </header>
            <div className="import-modal-body">
              <iframe
                title={`Importación cuenta ${summary.cuenta_nombre}`}
                src={importUrl}
                className="import-modal-frame"
              />
            </div>
          </div>
        </div>
      )}

      <ConfirmDialog
        open={bulkDeleteOpen}
        title="Eliminar movimientos seleccionados"
        message={
          selectedRowsCount > 0
            ? `Vas a enviar a papelera ${selectedRowsCount} ${selectedRowsCount === 1 ? 'movimiento' : 'movimientos'}${selectedRowsPreview ? ` (Nº ${selectedRowsPreview}${hiddenSelectedRows > 0 ? ` y ${hiddenSelectedRows} más` : ''})` : ''}. Se auditarán y podrás restaurarlos desde Papelera.`
            : ''
        }
        confirmLabel={bulkDeleteConfirmLabel}
        loading={actionLoading}
        onCancel={() => setBulkDeleteOpen(false)}
        onConfirm={confirmDeleteSelectedRows}
      />
    </section>
  );
}
