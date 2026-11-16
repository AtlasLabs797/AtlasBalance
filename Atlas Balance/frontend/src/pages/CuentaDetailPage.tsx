import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, Navigate, useLocation, useNavigate, useParams } from 'react-router';
import { AxiosError } from 'axios';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Flag, Trash2 } from 'lucide-react';
import { CloseIconButton } from '@/components/common/CloseIconButton';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { DatePickerField } from '@/components/common/DatePickerField';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import { SignedAmount } from '@/components/common/SignedAmount';
import { KpiCard } from '@/components/dashboard/KpiCard';
import { PeriodoSelector } from '@/components/dashboard/PeriodoSelector';
import ExtractoTable from '@/components/extractos/ExtractoTable';
import { BASE_COLUMNS } from '@/components/extractos/extractoColumns';
import type { InsertExtractoDraftPayload } from '@/components/extractos/ExtractoTable';
import AuditCellModal from '@/components/extractos/AuditCellModal';
import DesgloseModal from '@/components/extractos/DesgloseModal';
import type { DesgloseDraftPayload } from '@/components/extractos/DesgloseModal';
import { useDialogFocus } from '@/hooks/useDialogFocus';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { useInvalidateAfterMutation } from '@/hooks/queries/useInvalidateAfterMutation';
import api from '@/services/api';
import { QUERY_STALE_TIMES, QUERY_GC_TIMES } from '@/services/queryClient';
import { queryKeys } from '@/queries/queryKeys';
import { useAuthStore } from '@/stores/authStore';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
import { usePermisosStore } from '@/stores/permisosStore';
import { IMPORTACION_COMPLETADA_EVENT } from '@/utils/appEvents';
import type { AuditCellEntry, CuentaResumenKpi, Extracto, ExtractoDesgloseResumen, PaginatedResponse, PeriodoDashboard } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatCurrency, formatDate, formatDateTime, parseEuropeanNumber } from '@/utils/formatters';
import { computeBulkFlagToggle } from '@/utils/bulkFlagToggle';

const BULK_DELETE_PREVIEW_LIMIT = 6;

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
  usePermisosStore((state) => state.permisos);
  const selectedPaisId = usePaisScopeStore((state) => state.selectedPaisId);
  const invalidate = useInvalidateAfterMutation();

  const [summary, setSummary] = useState<CuentaResumenKpi | null>(null);
  const [rows, setRows] = useState<Extracto[]>([]);
  const [rowsPage, setRowsPage] = useState(1);
  const [rowsPageSize, setRowsPageSize] = useState(200);
  const [modo, setModo] = useState<'revision' | 'edicion'>('revision');
  const [search, setSearch] = useState('');
  const searchDebounced = useDebouncedValue(search, 250);
  const [fechaDesde, setFechaDesde] = useState('');
  const [fechaHasta, setFechaHasta] = useState('');
  const invalidRange = Boolean(fechaDesde && fechaHasta && fechaDesde > fechaHasta);
  const [sortBy, setSortBy] = useState('fila_numero');
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('desc');
  const [visibleColumns, setVisibleColumns] = useState<string[] | null>(null);
  const [columnOrder, setColumnOrder] = useState<string[] | null>(null);
  const [columnPreferencesSaving, setColumnPreferencesSaving] = useState(false);
  const columnPreferencesSavingRef = useRef(false);
  const preferenceScopeRef = useRef(cuentaId);
  useEffect(() => {
    preferenceScopeRef.current = cuentaId;
    setVisibleColumns(null);
    setColumnOrder(null);
  }, [cuentaId]);
  const [auditColumn, setAuditColumn] = useState<string | null>(null);
  const [auditData, setAuditData] = useState<AuditCellEntry[]>([]);
  const auditAbortRef = useRef<AbortController | null>(null);
  const desgloseAbortRef = useRef<AbortController | null>(null);
  const [auditLoading, setAuditLoading] = useState(false);
  const [auditError, setAuditError] = useState<string | null>(null);
  const [desgloseRow, setDesgloseRow] = useState<Extracto | null>(null);
  const [desgloseData, setDesgloseData] = useState<ExtractoDesgloseResumen | null>(null);
  const [desgloseLoading, setDesgloseLoading] = useState(false);
  const [desgloseSaving, setDesgloseSaving] = useState(false);
  const [desgloseError, setDesgloseError] = useState<string | null>(null);
  const [rowsTotal, setRowsTotal] = useState(0);
  const [rowsTotalPages, setRowsTotalPages] = useState(1);
  const [periodo, setPeriodo] = useState<PeriodoDashboard>('1m');
  const [isImportModalOpen, setIsImportModalOpen] = useState(false);
  const [bulkDeleteOpen, setBulkDeleteOpen] = useState(false);
  const [selectedRowIds, setSelectedRowIds] = useState<Set<string>>(new Set());
  const [actionLoading, setActionLoading] = useState(false);
  const [bulkActionStatus, setBulkActionStatus] = useState<string | null>(null);
  const [notesDraft, setNotesDraft] = useState('');
  const [notesSaving, setNotesSaving] = useState(false);
  const [notesStatus, setNotesStatus] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // V-02.08: separa fallos de CARGA (queries iniciales) de errores de ACCION
  // (guardar celda, marcar, etc.). Antes ambos compartian `error` y un fallo
  // cualquiera reemplazaba toda la pagina por un parrafo sin salida.
  const [loadError, setLoadError] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);

  const allowedDashboard = usuario?.rol === 'ADMIN' || canViewDashboard();
  const canImport = Boolean(cuentaId) && summary ? canImportInCuenta(cuentaId, summary.titular_id, summary.pais_id) : false;
  const canAddRows = Boolean(cuentaId) && summary ? canAddInCuenta(cuentaId, summary.titular_id, summary.pais_id) : false;
  const canDeleteRows = Boolean(cuentaId) && summary ? canDeleteInCuenta(cuentaId, summary.titular_id, summary.pais_id) : false;
  const canOpenAccount = Boolean(cuentaId) && summary ? canViewCuenta(cuentaId, summary.titular_id, summary.pais_id) : false;
  const canEditAccountNotes = Boolean(cuentaId) && summary ? canEditCuenta(cuentaId, summary.titular_id, summary.pais_id) : false;
  const plazoFijo = summary?.tipo_cuenta === 'PLAZO_FIJO' ? summary.plazo_fijo : null;
  const hasBankName = Boolean(summary?.banco_nombre?.trim());
  const hasIban = Boolean(summary?.iban?.trim());
  const bankLabel = hasBankName ? summary?.banco_nombre?.trim() : summary?.tipo_cuenta === 'EFECTIVO' ? 'Efectivo' : 'Sin banco';
  const closeImportModal = useCallback(() => setIsImportModalOpen(false), []);
  const importDialogRef = useDialogFocus<HTMLDivElement>(isImportModalOpen, {
    onEscape: closeImportModal,
  });

  const canEditCell = useCallback(
    (column: string) => {
      if (modo !== 'edicion' || !cuentaId || !summary) {
        return false;
      }

      if (!canEditCuenta(cuentaId, summary.titular_id, summary.pais_id)) {
        return false;
      }

      const editableColumns = getColumnasEditables(cuentaId, summary.titular_id, summary.pais_id);
      return editableColumns === null || editableColumns.includes(column);
    },
    [canEditCuenta, cuentaId, getColumnasEditables, modo, summary]
  );
  const canFlagRows = canEditCell('flagged');
  const canSelectRows = modo === 'edicion' && (canDeleteRows || canFlagRows);

  const loadCuentaDataRequestIdRef = useRef(0);
  const queryClient = useQueryClient();
  const usuarioId = usuario?.id ?? '';

  const excludedColumns = useMemo(() => ['cuenta_nombre', 'banco_nombre', 'titular_nombre'], []);
  const columnPreferencesQuery = useQuery({
    queryKey: queryKeys.extractos.columnasVisibles({ usuarioId, cuentaId, titularId: summary?.titular_id ?? null, paisId: summary?.pais_id ?? selectedPaisId ?? null }),
    queryFn: () => api.get<{ columnas_visibles: string[] | null; orden_columnas: string[] | null }>('/extractos/columnas-visibles', {
      params: { cuentaId, titularId: summary?.titular_id, paisId: summary?.pais_id ?? (selectedPaisId || undefined) },
    }).then((res) => res.data),
    enabled: Boolean(usuarioId && cuentaId && summary?.cuenta_id === cuentaId),
    staleTime: QUERY_STALE_TIMES.EXTRACTOS_MS,
  });
  useEffect(() => {
    if (columnPreferencesQuery.data && !columnPreferencesSaving) {
      setVisibleColumns(columnPreferencesQuery.data.columnas_visibles ?? null);
      setColumnOrder(columnPreferencesQuery.data.orden_columnas ?? null);
    }
  }, [columnPreferencesQuery.data, columnPreferencesSaving]);
  useEffect(() => {
    if (columnPreferencesQuery.error) setError(extractErrorMessage(columnPreferencesQuery.error, 'No se pudieron cargar las preferencias de columnas.'));
  }, [columnPreferencesQuery.error]);

  const summaryQuery = useQuery({
    queryKey: queryKeys.extractos.cuentaResumen({ usuarioId, cuentaId, periodo, paisId: selectedPaisId || null }),
    queryFn: ({ signal }) =>
      api.get<CuentaResumenKpi>(`/extractos/cuentas/${id}/resumen`, {
        params: { periodo, paisId: selectedPaisId || undefined },
        signal,
      }).then((res) => res.data),
    enabled: Boolean(allowedDashboard && id && usuarioId),
    staleTime: QUERY_STALE_TIMES.CUENTA_RESUMEN_MS,
  });

  const rowsQuery = useQuery({
    queryKey: queryKeys.extractos.list({
      usuarioId,
      cuentaId,
      paisId: selectedPaisId || null,
      page: rowsPage,
      pageSize: rowsPageSize,
      search: searchDebounced || null, fechaDesde: fechaDesde || null, fechaHasta: fechaHasta || null,
      sortBy,
      sortDir,
    }),
    queryFn: ({ signal }) =>
      api.get<PaginatedResponse<Extracto>>('/extractos', {
        params: {
          cuentaId: id,
          paisId: selectedPaisId || undefined,
          page: rowsPage,
          pageSize: rowsPageSize,
          search: searchDebounced || undefined, fechaDesde: fechaDesde || undefined, fechaHasta: fechaHasta || undefined,
          sortBy,
          sortDir,
        },
        signal,
      }).then((res) => res.data),
    enabled: Boolean(allowedDashboard && id && usuarioId && !invalidRange),
    staleTime: QUERY_STALE_TIMES.EXTRACTOS_MS,
    gcTime: QUERY_GC_TIMES.EXTRACTOS_MS,
  });

  const selectionEnabled = canSelectRows && search === searchDebounced && !rowsQuery.isFetching && !invalidRange;

  useEffect(() => {
    if (summaryQuery.error) {
      if (summaryQuery.error instanceof AxiosError && summaryQuery.error.response?.status === 403) {
        setForbidden(true);
        setSummary(null);
        setRows([]);
        setRowsTotal(0);
        setRowsTotalPages(1);
      } else {
        setLoadError(extractErrorMessage(summaryQuery.error, 'No se pudo cargar la cuenta'));
      }
    } else {
      setForbidden(false);
      setLoadError(null);
    }
  }, [summaryQuery.error]);

  useEffect(() => {
    if (rowsQuery.error) {
      setLoadError(extractErrorMessage(rowsQuery.error, 'No se pudo cargar la cuenta'));
    }
  }, [rowsQuery.error]);

  useEffect(() => {
    const summary = summaryQuery.data;
    if (summary) {
      setSummary(summary);
      setLoadError(null);
    } else if (!summaryQuery.isLoading && !summaryQuery.error) {
      setSummary(null);
    }
  }, [summaryQuery.data, summaryQuery.isLoading, summaryQuery.error]);

  useEffect(() => {
    const data = rowsQuery.data;
    if (data) {
      setRows(data.data ?? []);
      setRowsTotal(data.total ?? data.data?.length ?? 0);
      setRowsTotalPages(Math.max(1, data.total_pages ?? 1));
    } else if (!rowsQuery.isLoading && !rowsQuery.error) {
      setRows([]);
      setRowsTotal(0);
      setRowsTotalPages(1);
    }
  }, [rowsQuery.data, rowsQuery.isLoading, rowsQuery.error]);

  useEffect(() => {
    const loadingNow = summaryQuery.isLoading || rowsQuery.isLoading;
    if (!loadingNow && !summaryQuery.isFetching && !rowsQuery.isFetching) {
      setLoading(false);
    } else if (loadingNow) {
      setLoading(true);
    }
  }, [summaryQuery.isLoading, rowsQuery.isLoading, summaryQuery.isFetching, rowsQuery.isFetching]);

  const loadCuentaData = useCallback(async () => {
    if (!id || !allowedDashboard) {
      return;
    }

    loadCuentaDataRequestIdRef.current += 1;
    setError(null);
    setForbidden(false);

    try {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.extractos.cuentaResumen({ usuarioId, cuentaId, periodo, paisId: selectedPaisId || null }) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.extractos.list({ usuarioId, cuentaId, paisId: selectedPaisId || null, page: rowsPage, pageSize: rowsPageSize, search: searchDebounced || null, fechaDesde: fechaDesde || null, fechaHasta: fechaHasta || null, sortBy, sortDir }) }),
      ]);
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo cargar la cuenta'));
    }
  }, [allowedDashboard, id, periodo, rowsPage, rowsPageSize, selectedPaisId, queryClient, usuarioId, cuentaId, sortBy, sortDir, searchDebounced, fechaDesde, fechaHasta]);

  useEffect(() => {
    if (!allowedDashboard || !id) {
      return;
    }
    void loadCuentaData();
  }, [loadCuentaData, allowedDashboard, id]);

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

  const selectedRows = useMemo(
    () => rows.filter((row) => selectedRowIds.has(row.id)),
    [rows, selectedRowIds]
  );
  const selectedRowsCount = selectedRows.length;
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
        // El borrado soft toca el saldo derivado (no recalcula), pero
        // cambia la lista y debe liberar el cache para que Dashboard,
        // Alertas y Revision que estuvieran abiertas re-consulten al
        // recuperar el foco.
        await invalidate('extractoDelete');
      }
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
      // PUT /extractos puede cambiar monto/saldo, lo que recalcula el
      // dashboard y dispara EvaluateSaldoPostAsync. Invalida todas las
      // familias afectadas.
      await invalidate('extractoUpdate');
    } catch (err) {
      // V-02.08: resincroniza si otro usuario gano la carrera sobre la celda
      // (409), como ya hace ExtractosPage.
      if (err instanceof AxiosError && err.response?.status === 409) {
        try {
          await loadCuentaData();
        } catch {
          // la carga ya gestiona sus propios errores
        }
      }
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
      // PATCH /check no cambia saldo, pero el campo `checked` se proyecta
      // en /extractos; invalida para que otras vistas que filtren por
      // ese campo re-consulten al recuperar foco.
      await invalidate('extractoCheck');
    } catch (err) {
      // V-02.08: en vez de restaurar un snapshot de closure (que pisa datos
      // frescos si llego un refetch durante el PATCH), resincroniza con el
      // servidor.
      await rowsQuery.refetch().catch(() => undefined);
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

    const { action, targetIds } = computeBulkFlagToggle(selectedRows);

    setActionLoading(true);
    setError(null);
    setBulkActionStatus(null);

    const processedIds = new Set<string>();
    const changedAt = new Date().toISOString();
    const isUnflag = action === 'unflag';

    try {
      for (const row of selectedRows) {
        if (!targetIds.includes(row.id)) {
          continue;
        }
        await api.patch(`/extractos/${row.id}/flag`, {
          flagged: !isUnflag,
          nota: isUnflag ? undefined : row.flagged_nota ?? undefined,
        });
        processedIds.add(row.id);
        setRows((current) =>
          current.map((item) =>
            item.id === row.id
              ? isUnflag
                ? {
                    ...item,
                    flagged: false,
                    flagged_nota: null,
                    flagged_at: null,
                    flagged_by_id: null,
                    fecha_modificacion: changedAt,
                  }
                : {
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
        isUnflag
          ? `Quitada la alerta de ${processedIds.size} ${processedIds.size === 1 ? 'movimiento' : 'movimientos'}.`
          : `${processedIds.size} ${processedIds.size === 1 ? 'movimiento marcado' : 'movimientos marcados'} con alerta.`
      );
      await invalidate('extractoFlag');
    } catch (err) {
      setError(
        extractErrorMessage(
          err,
          processedIds.size > 0
            ? `Se ${isUnflag ? 'quitó la alerta de' : 'marcaron'} ${processedIds.size} de ${targetIds.length} movimientos seleccionados.`
            : `No se pudo ${isUnflag ? 'quitar la alerta de' : 'marcar'} la selección.`
        )
      );
    } finally {
      setActionLoading(false);
    }
  };

  const bulkFlagIntent = useMemo(() => computeBulkFlagToggle(selectedRows), [selectedRows]);
  const bulkFlagAction = bulkFlagIntent.targetIds.length === 0 ? null : bulkFlagIntent.action;

  const saveColumnPreferences = async (nextVisible: string[] | null, nextOrder: string[] | null) => {
    if (!summary || summary.cuenta_id !== cuentaId || !columnPreferencesQuery.data || columnPreferencesQuery.isLoading || columnPreferencesQuery.error || columnPreferencesSavingRef.current) return;
    columnPreferencesSavingRef.current = true;
    setColumnPreferencesSaving(true);
    const savingScope = cuentaId;
    const previousVisible = visibleColumns;
    const previousOrder = columnOrder;
    setVisibleColumns(nextVisible);
    setColumnOrder(nextOrder);
    try {
      await api.put('/extractos/columnas-visibles', {
        cuenta_id: cuentaId, titular_id: summary.titular_id, pais_id: summary.pais_id,
        columnas_visibles: nextVisible, orden_columnas: nextOrder,
      });
      await queryClient.invalidateQueries({ queryKey: queryKeys.extractos.columnasVisibles({ usuarioId, cuentaId, titularId: summary.titular_id, paisId: summary.pais_id ?? selectedPaisId ?? null }) });
    } catch (err) {
      if (preferenceScopeRef.current === savingScope) {
        setVisibleColumns(previousVisible);
        setColumnOrder(previousOrder);
        setError(extractErrorMessage(err, 'No se pudieron guardar las preferencias de columnas.'));
      }
    } finally {
      columnPreferencesSavingRef.current = false;
      setColumnPreferencesSaving(false);
    }
  };

  const completeOrder = (available: string[]) => [...new Set([...(columnOrder ?? BASE_COLUMNS), ...available])];
  const currentVisibleColumns = (available: string[]) => {
    const known = new Set(columnOrder ?? BASE_COLUMNS);
    return visibleColumns === null
      ? completeOrder(available)
      : [...new Set([...visibleColumns, ...available.filter((column) => !known.has(column))])];
  };
  const toggleColumn = async (column: string, available: string[]) => {
    const current = currentVisibleColumns(available);
    const next = current.includes(column) ? current.filter((item) => item !== column) : [...current, column];
    await saveColumnPreferences(next, completeOrder(available));
  };
  const showAllColumns = (available: string[]) => {
    const current = currentVisibleColumns(available);
    void saveColumnPreferences([...new Set([...current, ...available])], completeOrder(available));
  };
  const reorderColumns = (available: string[]) => {
    const complete = completeOrder(available);
    const remaining = [...available];
    const availableSet = new Set(available);
    const reordered = complete.map((column) => availableSet.has(column) ? remaining.shift()! : column);
    void saveColumnPreferences(currentVisibleColumns(available), reordered);
  };

  const toggleSelectVisible = (ids: string[], selected: boolean) => {
    setSelectedRowIds((current) => {
      const next = new Set(current);
      ids.forEach((item) => selected ? next.add(item) : next.delete(item));
      return next;
    });
  };
  const retainVisibleSelection = useCallback((ids: string[]) => {
    const visible = new Set(ids);
    setSelectedRowIds((current) => {
      const next = new Set([...current].filter((item) => visible.has(item)));
      return next.size === current.size ? current : next;
    });
  }, []);

  const toggleFlag = async (row: Extracto, flagged: boolean, nota?: string) => {
    try {
      await api.patch(`/extractos/${row.id}/flag`, { flagged, nota: flagged ? nota : undefined });
      setRows((current) => current.map((item) => item.id === row.id ? { ...item, flagged, flagged_nota: flagged ? nota ?? null : null } : item));
      await invalidate('extractoFlag');
    } catch (err) { setError(extractErrorMessage(err, 'No se pudo actualizar la alerta.')); }
  };
  const insertRow = async (_row: Extracto, payload: InsertExtractoDraftPayload) => {
    try {
      await api.post('/extractos', payload);
      await invalidate('extractoCreate');
      await loadCuentaData();
    } catch (err) {
      const message = extractErrorMessage(err, 'No se pudo insertar el movimiento.');
      setError(message);
      throw new Error(message, { cause: err });
    }
  };
  const openAudit = async (row: Extracto, column: string) => {
    auditAbortRef.current?.abort();
    const controller = new AbortController();
    auditAbortRef.current = controller;
    setAuditColumn(column); setAuditData([]); setAuditError(null); setAuditLoading(true);
    try {
      const { data } = await api.get<AuditCellEntry[]>(`/extractos/${row.id}/audit-celda`, { params: { columna: column }, signal: controller.signal });
      if (!controller.signal.aborted) setAuditData(data);
    } catch (err) { if (!controller.signal.aborted) setAuditError(extractErrorMessage(err, 'No se pudo cargar la auditoría.')); }
    finally { if (!controller.signal.aborted) setAuditLoading(false); }
  };
  const openDesglose = async (row: Extracto) => {
    desgloseAbortRef.current?.abort();
    const controller = new AbortController();
    desgloseAbortRef.current = controller;
    setDesgloseRow(row); setDesgloseData(null); setDesgloseError(null); setDesgloseLoading(true);
    try {
      const { data } = await api.get<ExtractoDesgloseResumen>(`/extractos/${row.id}/desglose`, { signal: controller.signal });
      if (!controller.signal.aborted) setDesgloseData(data);
    } catch (err) { if (!controller.signal.aborted) setDesgloseError(extractErrorMessage(err, 'No se pudo cargar el desglose.')); }
    finally { if (!controller.signal.aborted) setDesgloseLoading(false); }
  };
  const saveDesglose = async (lineas: DesgloseDraftPayload[], version: string) => {
    if (!desgloseRow) return;
    setDesgloseSaving(true); setDesgloseError(null);
    try {
      const { data } = await api.put<ExtractoDesgloseResumen>(`/extractos/${desgloseRow.id}/desglose`, { lineas, version });
      setDesgloseData(data);
      setRows((current) => current.map((item) => item.id === desgloseRow.id ? { ...item, desglose_count: data.count, desglose_total: data.total, desglose_estado: data.estado } : item));
      await invalidate('extractoDesglose');
    } catch (err) { setDesgloseError(extractErrorMessage(err, 'No se pudo guardar el desglose.')); }
    finally { setDesgloseSaving(false); }
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
      // PATCH /cuentas/:id/notas actualiza `Cuenta.Notas` (entrada
      // de DashboardCacheInvalidationInterceptor). Invalida cuentas.
      await invalidate('cuentaNotas');
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
  // V-02.08: fallo de carga inicial con estado propio y salida (reintentar).
  // Antes cualquier `error` —incluido uno de validacion local de una celda—
  // reemplazaba la pagina completa por un parrafo.
  if (!summary && loadError && !forbidden) {
    return (
      <section className="page-placeholder">
        <EmptyState
          title="No se pudo cargar esta cuenta."
          subtitle={loadError}
          primaryAction={
            <button
              type="button"
              onClick={() => {
                setLoadError(null);
                void summaryQuery.refetch();
                void rowsQuery.refetch();
              }}
            >
              Reintentar
            </button>
          }
          secondaryAction={<Link to="/extractos">Ir a Extractos</Link>}
        />
      </section>
    );
  }
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
      {error && (
        <p className="auth-error" role="alert">
          {error}
          <button type="button" className="cuenta-error-dismiss" onClick={() => setError(null)}>
            Cerrar
          </button>
        </p>
      )}
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
            <div>
              <dt>País</dt>
              <dd className={summary.pais_nombre ? undefined : 'account-identity-value--muted'}>
                {summary.pais_nombre || 'Sin pais'}
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
          featured
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
        <div className="extractos-mode-toggle" role="group" aria-label="Modo de extractos">
          <button type="button" className={modo === 'revision' ? 'active' : ''} disabled={actionLoading} onClick={() => { setModo('revision'); setSelectedRowIds(new Set()); }}>
            Revisión
          </button>
          <button type="button" className={modo === 'edicion' ? 'active' : ''} disabled={actionLoading} onClick={() => setModo('edicion')}>
            Edición avanzada
          </button>
        </div>
        <div className="extractos-filters">
          <label>Buscar por concepto o comentario<input type="search" value={search} onChange={(event) => { setSearch(event.target.value); setRowsPage(1); setSelectedRowIds(new Set()); }} /></label>
          <DatePickerField label="Desde" ariaLabel="Fecha desde" value={fechaDesde} onChange={(value) => { setFechaDesde(value); setRowsPage(1); setSelectedRowIds(new Set()); }} />
          <DatePickerField label="Hasta" ariaLabel="Fecha hasta" value={fechaHasta} onChange={(value) => { setFechaHasta(value); setRowsPage(1); setSelectedRowIds(new Set()); }} />
        </div>
        {invalidRange ? <p className="auth-error" role="alert">La fecha desde no puede ser posterior a la fecha hasta.</p> : null}
        <p className="dashboard-subtitle">Los filtros de columna se aplican a la página actual. La selección se limpia cuando los movimientos dejan de estar visibles.</p>
        {selectionEnabled && rows.length > 0 ? (
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
                  aria-label={bulkFlagAction === 'unflag' ? 'Quitar alerta de la selección' : 'Marcar selección con alerta'}
                  title={bulkFlagAction === 'unflag' ? 'Quitar alerta de la selección' : 'Marcar selección con alerta'}
                  aria-pressed={bulkFlagAction === 'unflag'}
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
                onClick={() => { setSelectedRowIds(new Set()); setRowsPage((current) => Math.max(1, current - 1)); }}
              >
                Anterior
              </button>
              <span>Página {rowsPage} / {rowsTotalPages}</span>
              <button
                type="button"
                disabled={actionLoading || rowsPage >= rowsTotalPages}
                onClick={() => { setSelectedRowIds(new Set()); setRowsPage((current) => Math.min(rowsTotalPages, current + 1)); }}
              >
                Siguiente
              </button>
              <PageSizeSelect
                value={rowsPageSize}
                options={[100, 200, 500]}
                ariaLabel="Movimientos por página"
                onChange={(next) => {
                  setRowsPageSize(next);
                  setSelectedRowIds(new Set());
                  setRowsPage(1);
                }}
              />
            </div>
          </div>
        ) : null}

        <ExtractoTable
          rows={invalidRange ? [] : rows}
          loading={loading}
          sortBy={sortBy}
          sortDir={sortDir}
          visibleColumns={visibleColumns}
          columnOrder={columnOrder}
          columnPreferencesSaving={columnPreferencesSaving || !columnPreferencesQuery.data || columnPreferencesQuery.isLoading || Boolean(columnPreferencesQuery.error)}
          excludedColumns={excludedColumns}
          availableExtraColumns={rowsQuery.data?.columnas_disponibles ?? []}
          onSort={(field) => { if (field === sortBy) setSortDir((dir) => dir === 'asc' ? 'desc' : 'asc'); else { setSortBy(field); setSortDir('asc'); } setRowsPage(1); setSelectedRowIds(new Set()); }}
          onToggleColumn={(column, available) => void toggleColumn(column, available)}
          onShowAllColumns={showAllColumns}
          onReorderColumns={reorderColumns}
          onSaveCell={saveCell}
          onToggleCheck={toggleCheck}
          onToggleFlag={toggleFlag}
          onInsertRow={insertRow}
          onOpenAudit={(row, column) => void openAudit(row, column)}
          onOpenDesglose={(row) => void openDesglose(row)}
          canAddRow={() => modo === 'edicion' && canAddRows && !actionLoading}
          canEditCell={(_row, column) => canEditCell(column) && !actionLoading}
          inlineInsertEnabled={modo === 'edicion' && canAddRows}
          onClearFilters={() => { setSearch(''); setFechaDesde(''); setFechaHasta(''); setRowsPage(1); setSelectedRowIds(new Set()); }}
          hasExternalFilters={Boolean(search || fechaDesde || fechaHasta)}
          selectedRowIds={selectionEnabled ? selectedRowIds : undefined}
          onSelectRow={selectionEnabled ? toggleRowSelection : undefined}
          onSelectAllRows={selectionEnabled ? toggleSelectVisible : undefined}
          onFilteredRowsChange={selectionEnabled ? retainVisibleSelection : undefined}
        />
      </section>

      {isImportModalOpen && (
        <div className="modal-backdrop modal-backdrop--stack-top" role="presentation" onClick={closeImportModal}>
          <div
            ref={importDialogRef}
            className="import-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="import-modal-title"
            aria-describedby="import-modal-description"
            tabIndex={-1}
            onClick={(event) => event.stopPropagation()}
          >
            <header className="import-modal-header">
              <div>
                <h2 id="import-modal-title">Importar movimientos</h2>
                <p id="import-modal-description" className="import-muted">Cuenta: {summary.cuenta_nombre}</p>
              </div>
              <CloseIconButton
                className="import-modal-close"
                onClick={closeImportModal}
                ariaLabel="Cerrar modal de importación"
              />
            </header>
            <div className="import-modal-body">
              <iframe
                title={`Importación cuenta ${summary.cuenta_nombre}`}
                src={importUrl}
                className="import-modal-frame"
                tabIndex={0}
              />
            </div>
          </div>
        </div>
      )}

      <AuditCellModal open={auditColumn !== null} column={auditColumn} data={auditData} loading={auditLoading} error={auditError} onClose={() => { auditAbortRef.current?.abort(); setAuditColumn(null); }} />
      <DesgloseModal open={Boolean(desgloseRow)} row={desgloseRow} data={desgloseData} loading={desgloseLoading} saving={desgloseSaving} error={desgloseError} canEdit={canEditCell('desglose')} onClose={() => { if (!desgloseSaving) { desgloseAbortRef.current?.abort(); setDesgloseRow(null); } }} onSave={saveDesglose} />
      <ConfirmDialog
        open={bulkDeleteOpen}
        title="Eliminar movimientos seleccionados"
        message={
          selectedRowsCount > 0
            ? `Vas a enviar a papelera ${selectedRowsCount} ${selectedRowsCount === 1 ? 'movimiento' : 'movimientos'}${selectedRowsPreview ? ` (Nº ${selectedRowsPreview}${hiddenSelectedRows > 0 ? ` y ${hiddenSelectedRows} más` : ''})` : ''}. Se auditarán y podrás restaurarlos desde Papelera.`
            : ''
        }
        confirmLabel={bulkDeleteConfirmLabel}
        tone="reversible"
        loading={actionLoading}
        onCancel={() => setBulkDeleteOpen(false)}
        onConfirm={confirmDeleteSelectedRows}
      />
    </section>
  );
}
