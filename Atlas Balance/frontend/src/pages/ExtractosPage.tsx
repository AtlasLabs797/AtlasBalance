import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router';
import axios from 'axios';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { DatePickerField } from '@/components/common/DatePickerField';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import { SearchableSelect } from '@/components/common/SearchableSelect';
import AuditCellModal from '@/components/extractos/AuditCellModal';
import DesgloseModal from '@/components/extractos/DesgloseModal';
import type { DesgloseDraftPayload } from '@/components/extractos/DesgloseModal';
import ExtractoTable from '@/components/extractos/ExtractoTable';
import type { InsertExtractoDraftPayload } from '@/components/extractos/ExtractoTable';
import { useInvalidateAfterMutation } from '@/hooks/queries/useInvalidateAfterMutation';
import api from '@/services/api';
import { QUERY_GC_TIMES, QUERY_STALE_TIMES } from '@/services/queryClient';
import { queryKeys } from '@/queries/queryKeys';
import { useAuthStore } from '@/stores/authStore';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
import { usePermisosStore } from '@/stores/permisosStore';
import type { AuditCellEntry, Extracto, ExtractoDesgloseResumen, PaginatedResponse, TitularConCuentas } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { parseEuropeanNumber } from '@/utils/formatters';

interface UpdateExtractoPayload {
  fecha?: string;
  concepto?: string;
  comentarios?: string;
  monto?: number;
  saldo?: number;
  columnas_extra?: Record<string, string>;
}

// BUG-COLUMNAS (V-02-04): los ids de scope pueden venir de la URL o de
// localStorage con valores corruptos ('undefined', ids antiguos, vacios).
// Un valor no-GUID en el payload provocaba un 400 y el toggle de columnas
// se revertia sin feedback visible. Solo enviamos UUIDs reales.
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function asUuidOrUndefined(value: string | null | undefined): string | undefined {
  return value && UUID_PATTERN.test(value) ? value : undefined;
}

function asUuidOrEmpty(value: string | null | undefined): string {
  return asUuidOrUndefined(value) ?? '';
}

function parseDecimalInput(value: string, fieldLabel: string): number {
  const parsed = parseEuropeanNumber(value);
  if (parsed === null) {
    throw new Error(`${fieldLabel} debe ser numérico. Ejemplo: 1.234,56.`);
  }

  return parsed;
}

function getLocalDesgloseEstado(count: number | undefined, total: number | undefined, monto: number): Extracto['desglose_estado'] {
  if (!count) return 'sin_desglose';
  return Math.round((total ?? 0) * 10000) === Math.round(monto * 10000) ? 'cuadrado' : 'descuadrado';
}

export default function ExtractosPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const usuarioId = useAuthStore((state) => state.usuario?.id ?? '');
  const queryClient = useQueryClient();
  const invalidate = useInvalidateAfterMutation();
  const selectedPaisId = usePaisScopeStore((state) => state.selectedPaisId);
  const [rows, setRows] = useState<Extracto[]>([]);
  const [sortBy, setSortBy] = useState('fecha');
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('desc');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(200);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRows, setTotalRows] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [cuentaFiltro, setCuentaFiltro] = useState<string>(() => asUuidOrEmpty(searchParams.get('cuentaId')));
  const [titularFiltro, setTitularFiltro] = useState<string>(() => asUuidOrEmpty(searchParams.get('titularId')));
  const [fechaDesde, setFechaDesde] = useState<string>(() => searchParams.get('fechaDesde') ?? '');
  const [fechaHasta, setFechaHasta] = useState<string>(() => searchParams.get('fechaHasta') ?? '');
  const [modo, setModo] = useState<'revision' | 'edicion'>('revision');
  const [titularesResumen, setTitularesResumen] = useState<TitularConCuentas[]>([]);
  const [visibleColumns, setVisibleColumns] = useState<string[] | null>(null);
  const [availableExtraColumns, setAvailableExtraColumns] = useState<string[]>([]);

  const [auditOpen, setAuditOpen] = useState(false);
  const [auditData, setAuditData] = useState<AuditCellEntry[]>([]);
  const auditAbortRef = useRef<AbortController | null>(null);
  const didMountPaisScopeRef = useRef(false);

  const closeAudit = () => {
    // F-NEW-14: cancelar peticion pendiente al cerrar el modal.
    auditAbortRef.current?.abort();
    auditAbortRef.current = null;
    setAuditOpen(false);
    setAuditData([]);
    setAuditError(null);
    setAuditColumn(null);
  };
  const [auditLoading, setAuditLoading] = useState(false);
  const [auditError, setAuditError] = useState<string | null>(null);
  const [auditColumn, setAuditColumn] = useState<string | null>(null);
  const [desgloseRow, setDesgloseRow] = useState<Extracto | null>(null);
  const [desgloseData, setDesgloseData] = useState<ExtractoDesgloseResumen | null>(null);
  const [desgloseLoading, setDesgloseLoading] = useState(false);
  const [desgloseSaving, setDesgloseSaving] = useState(false);
  const [desgloseError, setDesgloseError] = useState<string | null>(null);
  const desgloseAbortRef = useRef<AbortController | null>(null);
  const desgloseRequestIdRef = useRef<string | null>(null);

  const canEditCuenta = usePermisosStore((s) => s.canEditCuenta);
  const canAddInCuenta = usePermisosStore((s) => s.canAddInCuenta);
  const getColumnasEditables = usePermisosStore((s) => s.getColumnasEditables);
  usePermisosStore((s) => s.permisos);

  const cuentasOptions = useMemo(() => {
    const items: Array<{ id: string; nombre: string; titular_id: string; titular_nombre: string; divisa: string; pais_id: string | null }> = [];
    titularesResumen.forEach((t) => {
      t.cuentas.forEach((c) => {
        items.push({
          id: c.cuenta_id,
          nombre: c.cuenta_nombre,
          titular_id: t.titular_id,
          titular_nombre: t.titular_nombre,
          divisa: c.divisa,
          pais_id: c.pais_id
        });
      });
    });
    return items;
  }, [titularesResumen]);

  const selectedCuenta = useMemo(
    () => cuentasOptions.find((cuenta) => cuenta.id === cuentaFiltro) ?? null,
    [cuentaFiltro, cuentasOptions]
  );

  const activeScopeFilterCount = [titularFiltro, cuentaFiltro, fechaDesde, fechaHasta].filter(Boolean).length;

  // V-03.01 (#8): las 3 lecturas de esta pagina (resumen de cuentas, listado
  // paginado de movimientos y preferencias de columnas visibles) migran de
  // api.get en efectos manuales a React Query, siguiendo el mismo patron que
  // CuentaDetailPage.tsx (useQuery + bridge a estado local para no tocar el
  // resto del componente, que sigue leyendo `rows`/`titularesResumen`/
  // `visibleColumns` como antes).
  const fechaRangoInvalido = Boolean(fechaDesde && fechaHasta && fechaDesde > fechaHasta);

  const resumenQuery = useQuery({
    queryKey: queryKeys.extractos.titularesResumen({ usuarioId, paisId: selectedPaisId || null }),
    queryFn: ({ signal }) =>
      api.get<TitularConCuentas[]>('/extractos/titulares-resumen', {
        params: { paisId: selectedPaisId || undefined },
        signal,
      }).then((res) => res.data),
    enabled: Boolean(usuarioId),
    staleTime: QUERY_STALE_TIMES.EXTRACTOS_MS,
  });

  const rowsQueryParams = useMemo(
    () => ({
      usuarioId,
      cuentaId: asUuidOrUndefined(cuentaFiltro) ?? null,
      titularId: asUuidOrUndefined(titularFiltro) ?? null,
      paisId: asUuidOrUndefined(selectedPaisId) ?? null,
      fechaDesde: fechaDesde || null,
      fechaHasta: fechaHasta || null,
      page,
      pageSize,
      sortBy,
      sortDir,
    }),
    [usuarioId, cuentaFiltro, titularFiltro, selectedPaisId, fechaDesde, fechaHasta, page, pageSize, sortBy, sortDir]
  );

  const rowsQuery = useQuery<PaginatedResponse<Extracto>>({
    queryKey: queryKeys.extractos.list(rowsQueryParams),
    queryFn: ({ signal }) =>
      api.get<PaginatedResponse<Extracto>>('/extractos', {
        params: {
          page,
          pageSize,
          sortBy,
          sortDir,
          cuentaId: asUuidOrUndefined(cuentaFiltro),
          titularId: asUuidOrUndefined(titularFiltro),
          paisId: asUuidOrUndefined(selectedPaisId),
          fechaDesde: fechaDesde || undefined,
          fechaHasta: fechaHasta || undefined
        },
        signal,
      }).then((res) => res.data),
    enabled: Boolean(usuarioId) && !fechaRangoInvalido,
    placeholderData: keepPreviousData,
    staleTime: QUERY_STALE_TIMES.EXTRACTOS_MS,
    gcTime: QUERY_GC_TIMES.EXTRACTOS_MS,
  });

  const columnasVisiblesParams = useMemo(
    () => ({
      usuarioId,
      cuentaId: asUuidOrUndefined(cuentaFiltro) ?? null,
      titularId: asUuidOrUndefined(selectedCuenta?.titular_id) ?? asUuidOrUndefined(titularFiltro) ?? null,
      paisId: asUuidOrUndefined(selectedCuenta?.pais_id) ?? asUuidOrUndefined(selectedPaisId) ?? null,
    }),
    [usuarioId, cuentaFiltro, selectedCuenta, titularFiltro, selectedPaisId]
  );

  const visibleColumnsQuery = useQuery({
    queryKey: queryKeys.extractos.columnasVisibles(columnasVisiblesParams),
    queryFn: () =>
      api.get<{ columnas_visibles: string[] | null }>('/extractos/columnas-visibles', {
        params: {
          cuentaId: columnasVisiblesParams.cuentaId ?? undefined,
          titularId: columnasVisiblesParams.titularId ?? undefined,
          paisId: columnasVisiblesParams.paisId ?? undefined,
        },
      }).then((res) => res.data),
    enabled: Boolean(usuarioId),
    staleTime: QUERY_STALE_TIMES.EXTRACTOS_MS,
  });

  useEffect(() => {
    if (resumenQuery.data) {
      setTitularesResumen(resumenQuery.data);
    } else if (!resumenQuery.isLoading && !resumenQuery.error) {
      setTitularesResumen([]);
    }
  }, [resumenQuery.data, resumenQuery.isLoading, resumenQuery.error]);

  useEffect(() => {
    if (resumenQuery.error) {
      setTitularesResumen([]);
      setError(extractErrorMessage(resumenQuery.error, 'No se pudieron cargar las cuentas disponibles.'));
    }
  }, [resumenQuery.error]);

  useEffect(() => {
    if (fechaRangoInvalido) {
      setRows([]);
      setAvailableExtraColumns([]);
      setTotalPages(1);
      setTotalRows(0);
      setError('La fecha desde no puede ser posterior a la fecha hasta.');
      return;
    }

    const data = rowsQuery.data;
    if (data) {
      setRows(data.data ?? []);
      setAvailableExtraColumns(data.columnas_disponibles ?? []);
      setTotalPages(Math.max(1, data.total_pages ?? 1));
      setTotalRows(data.total ?? data.data?.length ?? 0);
    } else if (!rowsQuery.isLoading && !rowsQuery.error) {
      setRows([]);
      setAvailableExtraColumns([]);
      setTotalPages(1);
      setTotalRows(0);
    }
  }, [rowsQuery.data, rowsQuery.isLoading, rowsQuery.error, fechaRangoInvalido]);

  useEffect(() => {
    if (rowsQuery.error && !fechaRangoInvalido) {
      setError(extractErrorMessage(rowsQuery.error, 'No se pudieron cargar extractos'));
      setRows([]);
      setAvailableExtraColumns([]);
      setTotalPages(1);
      setTotalRows(0);
    }
  }, [rowsQuery.error, fechaRangoInvalido]);

  useEffect(() => {
    // Igual que en CuentaDetailPage: solo la carga inicial de una clave nueva
    // (isLoading) enciende el indicador de carga de la tabla. Un refetch de
    // fondo tras invalidar cache (p. ej. al guardar una celda) no debe volver
    // a mostrar el loading ni interrumpir la tabla virtualizada.
    if (rowsQuery.isLoading) {
      setLoading(true);
    } else if (!rowsQuery.isFetching) {
      setLoading(false);
    }
  }, [rowsQuery.isLoading, rowsQuery.isFetching]);

  useEffect(() => {
    if (visibleColumnsQuery.data) {
      setVisibleColumns(visibleColumnsQuery.data.columnas_visibles ?? null);
    } else if (!visibleColumnsQuery.isLoading && !visibleColumnsQuery.error) {
      setVisibleColumns(null);
    }
  }, [visibleColumnsQuery.data, visibleColumnsQuery.isLoading, visibleColumnsQuery.error]);

  useEffect(() => {
    if (visibleColumnsQuery.error) {
      setVisibleColumns(null);
      setError(extractErrorMessage(visibleColumnsQuery.error, 'No se pudieron cargar las preferencias de columnas.'));
    }
  }, [visibleColumnsQuery.error]);

  const loadRows = useCallback(async () => {
    await queryClient.invalidateQueries({ queryKey: queryKeys.extractos.list(rowsQueryParams) });
  }, [queryClient, rowsQueryParams]);

  useEffect(() => {
    if (!didMountPaisScopeRef.current) {
      didMountPaisScopeRef.current = true;
      return;
    }

    setCuentaFiltro('');
    setTitularFiltro('');
    setPage(1);
    updateFilterParams({ cuentaId: '', titularId: '' });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reset local filters when global country changes
  }, [selectedPaisId]);

  useEffect(() => {
    const nextCuentaId = asUuidOrEmpty(searchParams.get('cuentaId'));
    const nextTitularId = asUuidOrEmpty(searchParams.get('titularId'));
    const nextFechaDesde = searchParams.get('fechaDesde') ?? '';
    const nextFechaHasta = searchParams.get('fechaHasta') ?? '';

    setCuentaFiltro((current) => (current === nextCuentaId ? current : nextCuentaId));
    setTitularFiltro((current) => (current === nextTitularId ? current : nextTitularId));
    setFechaDesde((current) => (current === nextFechaDesde ? current : nextFechaDesde));
    setFechaHasta((current) => (current === nextFechaHasta ? current : nextFechaHasta));
    setPage(1);
  }, [searchParams]);

  const updateFilterParams = (next: { titularId?: string; cuentaId?: string; fechaDesde?: string; fechaHasta?: string }) => {
    const params = new URLSearchParams(searchParams);

    if (next.titularId !== undefined) {
      if (next.titularId) params.set('titularId', next.titularId);
      else params.delete('titularId');
    }

    if (next.cuentaId !== undefined) {
      if (next.cuentaId) params.set('cuentaId', next.cuentaId);
      else params.delete('cuentaId');
    }

    if (next.fechaDesde !== undefined) {
      if (next.fechaDesde) params.set('fechaDesde', next.fechaDesde);
      else params.delete('fechaDesde');
    }

    if (next.fechaHasta !== undefined) {
      if (next.fechaHasta) params.set('fechaHasta', next.fechaHasta);
      else params.delete('fechaHasta');
    }

    setSearchParams(params, { replace: true });
  };

  const onSort = (field: string) => {
    if (sortBy === field) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortBy(field);
      setSortDir('asc');
    }
  };

  const saveVisibleColumns = async (next: string[]) => {
    setVisibleColumns(next);
    setError(null);
    try {
      const payload: {
        cuenta_id?: string;
        titular_id?: string;
        pais_id?: string;
        columnas_visibles: string[];
      } = {
        columnas_visibles: next
      };
      const safeCuentaId = asUuidOrUndefined(cuentaFiltro);
      if (safeCuentaId) {
        payload.cuenta_id = safeCuentaId;
      }
      const titularScope = asUuidOrUndefined(selectedCuenta?.titular_id) ?? asUuidOrUndefined(titularFiltro);
      if (titularScope) {
        payload.titular_id = titularScope;
      }
      const paisScope = asUuidOrUndefined(selectedCuenta?.pais_id) ?? asUuidOrUndefined(selectedPaisId);
      if (paisScope) {
        payload.pais_id = paisScope;
      }

      await api.put('/extractos/columnas-visibles', payload);
      await invalidate('extractoColumnasVisibles');
    } catch (err) {
      setVisibleColumns(visibleColumns);
      setError(extractErrorMessage(err, 'No se pudieron guardar las columnas visibles.'));
    }
  };

  const onToggleColumn = async (column: string, availableColumns: string[]) => {
    const availableSet = new Set(availableColumns);
    const current = (visibleColumns ?? availableColumns).filter((item) => availableSet.has(item));
    if (current.includes(column) && current.length <= 1) {
      setError('Debe quedar al menos una columna visible.');
      return;
    }

    const next = current.includes(column) ? current.filter((c) => c !== column) : [...current, column];
    await saveVisibleColumns(next);
  };

  const onShowAllColumns = async (availableColumns: string[]) => {
    await saveVisibleColumns(availableColumns);
  };

  const onSaveCell = async (row: Extracto, column: string, value: string) => {
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

    try {
      await api.put(`/extractos/${row.id}`, payload);
      // La edicion solo afecta a esta fila (el saldo es un valor almacenado, no
      // recalculado en cascada). Para evitar recargar toda la pagina virtualizada
      // en cada celda (coste y salto de scroll con 50k filas), parcheamos la fila
      // localmente. Excepcion: cambiar la fecha puede reordenar/reformatear, asi
      // que ahi si recargamos.
      if (column === 'fecha') {
        await loadRows();
      } else {
        setRows((prev) =>
          prev.map((r) => {
            if (r.id !== row.id) return r;
            if (payload.columnas_extra) {
              return { ...r, columnas_extra: { ...(r.columnas_extra ?? {}), ...payload.columnas_extra } };
            }
            const patch: Partial<Extracto> = {};
            if (payload.concepto !== undefined) patch.concepto = payload.concepto;
            if (payload.comentarios !== undefined) patch.comentarios = payload.comentarios;
            if (payload.monto !== undefined) {
              patch.monto = payload.monto;
              patch.desglose_estado = getLocalDesgloseEstado(r.desglose_count, r.desglose_total, payload.monto);
            }
            if (payload.saldo !== undefined) patch.saldo = payload.saldo;
            return { ...r, ...patch };
          })
        );
      }
      // PUT /extractos puede recalcular saldo/dashboard/alertas segun la
      // columna; invalida esas familias en segundo plano (no reactiva el
      // loading de esta tabla, ver el bridge de rowsQuery mas arriba).
      await invalidate('extractoUpdate');
    } catch (err) {
      // Conflicto de concurrencia (otro usuario edito la fila): recargamos para
      // que el usuario vea el dato fresco antes de reintentar. El interceptor ya
      // muestra el mensaje del backend.
      if (axios.isAxiosError(err) && err.response?.status === 409) {
        await loadRows();
      }
      setError(extractErrorMessage(err, 'No se pudo guardar la celda.'));
      throw err;
    }
  };

  const onToggleCheck = async (row: Extracto, checked: boolean) => {
    setError(null);
    try {
      await api.patch(`/extractos/${row.id}/check`, { checked });
      setRows((prev) => prev.map((r) => (r.id === row.id ? { ...r, checked } : r)));
      await invalidate('extractoCheck');
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo marcar la fila como revisada.'));
    }
  };

  const onToggleFlag = async (row: Extracto, flagged: boolean, nota?: string) => {
    setError(null);
    const nextNota = flagged ? nota ?? null : null;
    try {
      await api.patch(`/extractos/${row.id}/flag`, { flagged, nota: flagged ? nota : undefined });
      setRows((prev) => prev.map((r) => (r.id === row.id ? { ...r, flagged, flagged_nota: nextNota } : r)));
      await invalidate('extractoFlag');
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo actualizar la alerta de la fila.'));
    }
  };

  const onInsertRow = async (_anchorRow: Extracto, payload: InsertExtractoDraftPayload) => {
    setError(null);
    try {
      await api.post('/extractos', payload);
      await loadRows();
      await invalidate('extractoCreate');
    } catch (err) {
      const message = extractErrorMessage(err, 'No se pudo insertar la fila.');
      setError(message);
      throw new Error(message, { cause: err });
    }
  };

  const onOpenAudit = async (row: Extracto, column: string) => {
    setAuditOpen(true);
    setAuditLoading(true);
    setAuditError(null);
    setAuditData([]);
    setAuditColumn(column);
    // F-NEW-14 (V-02-03): cancelar peticion pendiente si el usuario abre
    // otra celda antes de que llegue la primera. Evita que la respuesta
    // tardia se cargue en el modal equivocado.
    const ac = new AbortController();
    auditAbortRef.current = ac;
    try {
      const { data } = await api.get<AuditCellEntry[]>(`/extractos/${row.id}/audit-celda`, {
        params: { columna: column },
        signal: ac.signal,
      });
      if (ac.signal.aborted) return;
      setAuditData(data);
    } catch (err) {
      if (axios.isAxiosError(err) && err.name === 'CanceledError') {
        return;
      }
      if (ac.signal.aborted) return;
      setAuditError(extractErrorMessage(err, 'No se pudo cargar la auditoría de la celda.'));
    } finally {
      if (!ac.signal.aborted) {
        setAuditLoading(false);
      }
    }
  };

  const onOpenDesglose = async (row: Extracto) => {
    desgloseAbortRef.current?.abort();
    const ac = new AbortController();
    desgloseAbortRef.current = ac;
    desgloseRequestIdRef.current = row.id;
    setDesgloseRow(row);
    setDesgloseData(null);
    setDesgloseError(null);
    setDesgloseLoading(true);
    try {
      const { data } = await api.get<ExtractoDesgloseResumen>(`/extractos/${row.id}/desglose`, {
        signal: ac.signal,
      });
      if (ac.signal.aborted || desgloseRequestIdRef.current !== row.id) return;
      setDesgloseData(data);
    } catch (err) {
      if (axios.isAxiosError(err) && err.name === 'CanceledError') {
        return;
      }
      if (ac.signal.aborted || desgloseRequestIdRef.current !== row.id) return;
      setDesgloseError(extractErrorMessage(err, 'No se pudo cargar el desglose.'));
    } finally {
      if (!ac.signal.aborted && desgloseRequestIdRef.current === row.id) {
        setDesgloseLoading(false);
      }
    }
  };

  const onCloseDesglose = () => {
    if (desgloseSaving) return;
    desgloseAbortRef.current?.abort();
    desgloseAbortRef.current = null;
    desgloseRequestIdRef.current = null;
    setDesgloseRow(null);
    setDesgloseData(null);
    setDesgloseError(null);
  };

  const onSaveDesglose = async (lineas: DesgloseDraftPayload[], version: string) => {
    if (!desgloseRow) return;
    const rowId = desgloseRow.id;
    setDesgloseSaving(true);
    setDesgloseError(null);
    try {
      const { data } = await api.put<ExtractoDesgloseResumen>(`/extractos/${rowId}/desglose`, { version, lineas });
      setDesgloseData(data);
      setRows((prev) =>
        prev.map((row) =>
          row.id === rowId
            ? {
                ...row,
                desglose_count: data.count,
                desglose_total: data.total,
                desglose_estado: data.estado,
              }
            : row,
        ),
      );
      setDesgloseRow((current) =>
        current && current.id === rowId
          ? {
              ...current,
              desglose_count: data.count,
              desglose_total: data.total,
              desglose_estado: data.estado,
            }
          : current,
      );
      await invalidate('extractoDesglose');
    } catch (err) {
      const message = extractErrorMessage(err, 'No se pudo guardar el desglose.');
      if (axios.isAxiosError(err) && err.response?.status === 409) {
        try {
          const { data } = await api.get<ExtractoDesgloseResumen>(`/extractos/${rowId}/desglose`);
          setDesgloseData(data);
          setRows((prev) =>
            prev.map((row) =>
              row.id === rowId
                ? {
                    ...row,
                    desglose_count: data.count,
                    desglose_total: data.total,
                    desglose_estado: data.estado,
                  }
                : row,
            ),
          );
          setDesgloseError(`${message} Se recargo la version vigente.`);
        } catch {
          setDesgloseError(message);
        }
      } else {
        setDesgloseError(message);
      }
    } finally {
      setDesgloseSaving(false);
    }
  };

  const canEditCell = (row: Extracto, column: string) => {
    if (modo !== 'edicion') return false;
    if (!row.cuenta_id) return false;
    if (!canEditCuenta(row.cuenta_id, row.titular_id, row.pais_id)) return false;
    const cols = getColumnasEditables(row.cuenta_id, row.titular_id, row.pais_id);
    return cols === null || cols.includes(column);
  };

  const clearExternalFilters = () => {
    setTitularFiltro('');
    setCuentaFiltro('');
    setFechaDesde('');
    setFechaHasta('');
    setPage(1);
    updateFilterParams({ titularId: '', cuentaId: '', fechaDesde: '', fechaHasta: '' });
  };

  return (
    <section className="extractos-page">
      <header className="extractos-header">
        <div className="extractos-heading">
          <span className="extractos-eyebrow">Tesorería / Movimientos</span>
          <h1>Extractos</h1>
          <p>Movimientos bancarios con edición controlada, auditoría y revisión por cuenta.</p>
        </div>
        <div className="extractos-header-actions">
          <div className="extractos-mode-toggle" role="group" aria-label="Modo de extractos">
            <button
              type="button"
              className={modo === 'revision' ? 'active' : ''}
              onClick={() => setModo('revision')}
            >
              Revisión
            </button>
            <button
              type="button"
              className={modo === 'edicion' ? 'active' : ''}
              onClick={() => setModo('edicion')}
            >
              Edición avanzada
            </button>
          </div>
        </div>
      </header>

      <section className="extractos-filter-bar" aria-labelledby="extractos-filter-title">
        <div className="extractos-filter-bar-head">
          <div>
            <span className="extractos-filter-eyebrow">Ámbito de consulta</span>
            <h2 id="extractos-filter-title">Filtrar movimientos</h2>
          </div>
          <span className="extractos-filter-summary" aria-live="polite">
            {activeScopeFilterCount === 0
              ? 'Todos los movimientos visibles'
              : `${activeScopeFilterCount} filtro${activeScopeFilterCount === 1 ? '' : 's'} activo${activeScopeFilterCount === 1 ? '' : 's'}`}
          </span>
        </div>

        <div className="extractos-filters">
          <SearchableSelect
            label="Titular"
            ariaLabel="Titular"
            placeholder="Buscar titular"
            value={titularFiltro}
            options={[
              { value: '', label: 'Todos los titulares' },
              ...titularesResumen.map((t) => ({ value: t.titular_id, label: t.titular_nombre })),
            ]}
            onChange={(next) => {
              setTitularFiltro(next);
              setCuentaFiltro('');
              setPage(1);
              updateFilterParams({ titularId: next, cuentaId: '' });
            }}
          />
          <SearchableSelect
            label="Cuenta"
            ariaLabel="Cuenta"
            placeholder="Buscar cuenta"
            value={cuentaFiltro}
            options={[
              { value: '', label: 'Todas las cuentas' },
              ...cuentasOptions
                .filter((c) => !titularFiltro || titularesResumen.find((t) => t.titular_id === titularFiltro)?.cuentas.some((x) => x.cuenta_id === c.id))
                .map((c) => ({ value: c.id, label: `${c.titular_nombre} - ${c.nombre}` })),
            ]}
            onChange={(next) => {
              setCuentaFiltro(next);
              setPage(1);
              updateFilterParams({ cuentaId: next });
            }}
          />
          <DatePickerField
            label="Desde"
            ariaLabel="Fecha desde"
            value={fechaDesde}
            placeholder="Todas"
            onChange={(next) => {
              setFechaDesde(next);
              setPage(1);
              updateFilterParams({ fechaDesde: next });
            }}
          />
          <DatePickerField
            label="Hasta"
            ariaLabel="Fecha hasta"
            value={fechaHasta}
            placeholder="Todas"
            onChange={(next) => {
              setFechaHasta(next);
              setPage(1);
              updateFilterParams({ fechaHasta: next });
            }}
          />
          {activeScopeFilterCount > 0 ? (
            <button
              type="button"
              className="extractos-clear-period"
              onClick={clearExternalFilters}
            >
              Restablecer
            </button>
          ) : null}
        </div>
      </section>

      {error && <p className="auth-error" role="alert">{error}</p>}

      <ExtractoTable
        rows={rows}
        loading={loading}
        sortBy={sortBy}
        sortDir={sortDir}
        visibleColumns={visibleColumns}
        availableExtraColumns={availableExtraColumns}
        onSort={onSort}
        onToggleColumn={(column, availableColumns) => void onToggleColumn(column, availableColumns)}
        onShowAllColumns={(availableColumns) => void onShowAllColumns(availableColumns)}
        onSaveCell={onSaveCell}
        onToggleCheck={onToggleCheck}
        onToggleFlag={onToggleFlag}
        onInsertRow={onInsertRow}
        onOpenAudit={onOpenAudit}
        onOpenDesglose={(row) => void onOpenDesglose(row)}
        canAddRow={(row) => modo === 'edicion' && canAddInCuenta(row.cuenta_id, row.titular_id, row.pais_id)}
        canEditCell={canEditCell}
        inlineInsertEnabled={modo === 'edicion'}
        hasExternalFilters={activeScopeFilterCount > 0}
        onClearFilters={clearExternalFilters}
      />

      <div className="users-pagination">
        <button type="button" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1}>Anterior</button>
        <span>Página {page} / {totalPages} · {totalRows.toLocaleString('es-ES')} movimientos</span>
        <button type="button" onClick={() => setPage((p) => Math.min(totalPages, p + 1))} disabled={page >= totalPages}>Siguiente</button>
        <PageSizeSelect
          value={pageSize}
          options={[100, 200, 500]}
          onChange={(next) => {
            setPageSize(next);
            setPage(1);
          }}
        />
      </div>

      <AuditCellModal
        open={auditOpen}
        column={auditColumn}
        data={auditData}
        loading={auditLoading}
        error={auditError}
        onClose={closeAudit}
      />
      <DesgloseModal
        open={Boolean(desgloseRow)}
        row={desgloseRow}
        data={desgloseData}
        loading={desgloseLoading}
        saving={desgloseSaving}
        error={desgloseError}
        canEdit={Boolean(desgloseRow && canEditCell(desgloseRow, 'desglose'))}
        onClose={onCloseDesglose}
        onSave={onSaveDesglose}
      />
    </section>
  );
}
