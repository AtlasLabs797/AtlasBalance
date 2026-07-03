import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import axios from 'axios';
import { AppSelect } from '@/components/common/AppSelect';
import { DatePickerField } from '@/components/common/DatePickerField';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import AddRowForm from '@/components/extractos/AddRowForm';
import AuditCellModal from '@/components/extractos/AuditCellModal';
import DesgloseModal from '@/components/extractos/DesgloseModal';
import type { DesgloseDraftPayload } from '@/components/extractos/DesgloseModal';
import ExtractoTable from '@/components/extractos/ExtractoTable';
import api from '@/services/api';
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

function parseDecimalInput(value: string, fieldLabel: string): number {
  const parsed = parseEuropeanNumber(value);
  if (parsed === null) {
    throw new Error(`${fieldLabel} debe ser numérico. Ejemplo: 1.234,56.`);
  }

  return parsed;
}

export default function ExtractosPage() {
  const [searchParams, setSearchParams] = useSearchParams();
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
  const [cuentaFiltro, setCuentaFiltro] = useState<string>(() => searchParams.get('cuentaId') ?? '');
  const [titularFiltro, setTitularFiltro] = useState<string>(() => searchParams.get('titularId') ?? '');
  const [fechaDesde, setFechaDesde] = useState<string>(() => searchParams.get('fechaDesde') ?? '');
  const [fechaHasta, setFechaHasta] = useState<string>(() => searchParams.get('fechaHasta') ?? '');
  const [modo, setModo] = useState<'revision' | 'edicion'>('revision');
  const [titularesResumen, setTitularesResumen] = useState<TitularConCuentas[]>([]);
  const [visibleColumns, setVisibleColumns] = useState<string[] | null>(null);
  const [availableExtraColumns, setAvailableExtraColumns] = useState<string[]>([]);

  const [auditOpen, setAuditOpen] = useState(false);
  const [auditData, setAuditData] = useState<AuditCellEntry[]>([]);
  const auditAbortRef = useRef<AbortController | null>(null);

  const closeAudit = () => {
    // F-NEW-14: cancelar peticion pendiente al cerrar el modal.
    auditAbortRef.current?.abort();
    auditAbortRef.current = null;
    setAuditOpen(false);
    setAuditData([]);
    setAuditError(null);
    setAuditColumn(null);
    setAuditExtractoId(null);
  };
  const [auditLoading, setAuditLoading] = useState(false);
  const [auditError, setAuditError] = useState<string | null>(null);
  const [auditColumn, setAuditColumn] = useState<string | null>(null);
  const [auditExtractoId, setAuditExtractoId] = useState<string | null>(null);
  const [desgloseRow, setDesgloseRow] = useState<Extracto | null>(null);
  const [desgloseData, setDesgloseData] = useState<ExtractoDesgloseResumen | null>(null);
  const [desgloseLoading, setDesgloseLoading] = useState(false);
  const [desgloseSaving, setDesgloseSaving] = useState(false);
  const [desgloseError, setDesgloseError] = useState<string | null>(null);

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

  const cuentasConAlta = useMemo(
    () => cuentasOptions.filter((cuenta) => canAddInCuenta(cuenta.id, cuenta.titular_id, cuenta.pais_id)),
    [canAddInCuenta, cuentasOptions]
  );
  const selectedCuenta = useMemo(
    () => cuentasOptions.find((cuenta) => cuenta.id === cuentaFiltro) ?? null,
    [cuentaFiltro, cuentasOptions]
  );

  const loadResumen = useCallback(async () => {
    try {
      const { data } = await api.get<TitularConCuentas[]>('/extractos/titulares-resumen', {
        params: { paisId: selectedPaisId || undefined },
      });
      setTitularesResumen(data);
    } catch (err) {
      setTitularesResumen([]);
      setError(extractErrorMessage(err, 'No se pudieron cargar las cuentas disponibles.'));
    }
  }, [selectedPaisId]);

  const loadRows = useCallback(async () => {
    setLoading(true);
    setError(null);
    if (fechaDesde && fechaHasta && fechaDesde > fechaHasta) {
      setRows([]);
      setAvailableExtraColumns([]);
      setTotalPages(1);
      setTotalRows(0);
      setError('La fecha desde no puede ser posterior a la fecha hasta.');
      setLoading(false);
      return;
    }

    try {
      const { data } = await api.get<PaginatedResponse<Extracto>>('/extractos', {
        params: {
          page,
          pageSize,
          sortBy,
          sortDir,
          cuentaId: cuentaFiltro || undefined,
          titularId: titularFiltro || undefined,
          paisId: selectedPaisId || undefined,
          fechaDesde: fechaDesde || undefined,
          fechaHasta: fechaHasta || undefined
        }
      });
      setRows(data.data ?? []);
      setAvailableExtraColumns(data.columnas_disponibles ?? []);
      setTotalPages(Math.max(1, data.total_pages ?? 1));
      setTotalRows(data.total ?? data.data?.length ?? 0);
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudieron cargar extractos'));
      setRows([]);
      setAvailableExtraColumns([]);
      setTotalPages(1);
      setTotalRows(0);
    } finally {
      setLoading(false);
    }
  }, [page, pageSize, sortBy, sortDir, cuentaFiltro, titularFiltro, selectedPaisId, fechaDesde, fechaHasta]);

  const loadVisibleColumns = useCallback(async () => {
    try {
      const { data } = await api.get('/extractos/columnas-visibles', {
        params: {
          cuentaId: asUuidOrUndefined(cuentaFiltro),
          titularId: asUuidOrUndefined(selectedCuenta?.titular_id) ?? asUuidOrUndefined(titularFiltro),
          paisId: asUuidOrUndefined(selectedCuenta?.pais_id) ?? asUuidOrUndefined(selectedPaisId)
        }
      });
      setVisibleColumns(data.columnas_visibles ?? null);
    } catch (err) {
      setVisibleColumns(null);
      setError(extractErrorMessage(err, 'No se pudieron cargar las preferencias de columnas.'));
    }
  }, [cuentaFiltro, selectedCuenta, selectedPaisId, titularFiltro]);

  useEffect(() => {
    void loadResumen();
  }, [loadResumen]);

  useEffect(() => {
    void loadRows();
  }, [loadRows]);

  useEffect(() => {
    void loadVisibleColumns();
  }, [loadVisibleColumns]);

  useEffect(() => {
    setCuentaFiltro('');
    setTitularFiltro('');
    setPage(1);
    updateFilterParams({ cuentaId: '', titularId: '' });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reset local filters when global country changes
  }, [selectedPaisId]);

  useEffect(() => {
    const nextCuentaId = searchParams.get('cuentaId') ?? '';
    const nextTitularId = searchParams.get('titularId') ?? '';
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
      if (cuentaFiltro) {
        payload.cuenta_id = cuentaFiltro;
      }
      const titularScope = selectedCuenta?.titular_id ?? titularFiltro;
      if (titularScope) {
        payload.titular_id = titularScope;
      }
      const paisScope = selectedCuenta?.pais_id ?? selectedPaisId;
      if (paisScope) {
        payload.pais_id = paisScope;
      }

      await api.put('/extractos/columnas-visibles', payload);
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
            if (payload.monto !== undefined) patch.monto = payload.monto;
            if (payload.saldo !== undefined) patch.saldo = payload.saldo;
            return { ...r, ...patch };
          })
        );
      }
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
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo marcar la fila como revisada.'));
    }
  };

  const onToggleFlag = async (row: Extracto, flagged: boolean, nota?: string) => {
    setError(null);
    try {
      await api.patch(`/extractos/${row.id}/flag`, { flagged, nota });
      setRows((prev) => prev.map((r) => (r.id === row.id ? { ...r, flagged, flagged_nota: nota ?? null } : r)));
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo actualizar la alerta de la fila.'));
    }
  };

  const onOpenAudit = async (row: Extracto, column: string) => {
    setAuditOpen(true);
    setAuditLoading(true);
    setAuditError(null);
    setAuditData([]);
    setAuditColumn(column);
    setAuditExtractoId(row.id);
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
    setDesgloseRow(row);
    setDesgloseData(null);
    setDesgloseError(null);
    setDesgloseLoading(true);
    try {
      const { data } = await api.get<ExtractoDesgloseResumen>(`/extractos/${row.id}/desglose`);
      setDesgloseData(data);
    } catch (err) {
      setDesgloseError(extractErrorMessage(err, 'No se pudo cargar el desglose.'));
    } finally {
      setDesgloseLoading(false);
    }
  };

  const onCloseDesglose = () => {
    if (desgloseSaving) return;
    setDesgloseRow(null);
    setDesgloseData(null);
    setDesgloseError(null);
  };

  const onSaveDesglose = async (lineas: DesgloseDraftPayload[]) => {
    if (!desgloseRow) return;
    setDesgloseSaving(true);
    setDesgloseError(null);
    try {
      const { data } = await api.put<ExtractoDesgloseResumen>(`/extractos/${desgloseRow.id}/desglose`, { lineas });
      setDesgloseData(data);
      setRows((prev) =>
        prev.map((row) =>
          row.id === desgloseRow.id
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
        current && current.id === desgloseRow.id
          ? {
              ...current,
              desglose_count: data.count,
              desglose_total: data.total,
              desglose_estado: data.estado,
            }
          : current,
      );
    } catch (err) {
      setDesgloseError(extractErrorMessage(err, 'No se pudo guardar el desglose.'));
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

  return (
    <section className="extractos-page">
      <header className="extractos-header">
        <div className="extractos-heading">
          <h1>Extractos</h1>
          <p>Movimientos bancarios con edición controlada, auditoría y revisión por cuenta.</p>
        </div>
        <div className="extractos-mode-toggle" role="group" aria-label="Modo de extractos">
          <button
            type="button"
            className={modo === 'revision' ? 'active' : ''}
            onClick={() => setModo('revision')}
          >
            Revision
          </button>
          <button
            type="button"
            className={modo === 'edicion' ? 'active' : ''}
            onClick={() => setModo('edicion')}
          >
            Edicion avanzada
          </button>
        </div>
        <div className="extractos-filters">
          <AppSelect
            ariaLabel="Titular"
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
          <AppSelect
            ariaLabel="Cuenta"
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
          <div className="extractos-date-field">
            <span>Desde</span>
            <DatePickerField
              ariaLabel="Fecha desde"
              value={fechaDesde}
              placeholder="Desde"
              onChange={(next) => {
                setFechaDesde(next);
                setPage(1);
                updateFilterParams({ fechaDesde: next });
              }}
            />
          </div>
          <div className="extractos-date-field">
            <span>Hasta</span>
            <DatePickerField
              ariaLabel="Fecha hasta"
              value={fechaHasta}
              placeholder="Hasta"
              onChange={(next) => {
                setFechaHasta(next);
                setPage(1);
                updateFilterParams({ fechaHasta: next });
              }}
            />
          </div>
          {(fechaDesde || fechaHasta) ? (
            <button
              type="button"
              className="extractos-clear-period"
              onClick={() => {
                setFechaDesde('');
                setFechaHasta('');
                setPage(1);
                updateFilterParams({ fechaDesde: '', fechaHasta: '' });
              }}
            >
              Limpiar período
            </button>
          ) : null}
        </div>
      </header>

      {error && <p className="auth-error" role="alert">{error}</p>}

      {cuentasConAlta.length > 0 ? (
        <AddRowForm
          cuentas={cuentasConAlta}
          extraColumns={[...new Set(rows.flatMap((r) => Object.keys(r.columnas_extra ?? {})))]}
          onCreate={async (payload) => {
            setError(null);
            try {
              await api.post('/extractos', payload);
              await loadRows();
            } catch (err) {
              setError(extractErrorMessage(err, 'No se pudo agregar la fila manual.'));
              throw err;
            }
          }}
        />
      ) : null}

      <ExtractoTable
        rows={rows}
        totalRows={totalRows}
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
        onOpenAudit={onOpenAudit}
        onOpenDesglose={(row) => void onOpenDesglose(row)}
        canEditCell={canEditCell}
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
        canEdit={Boolean(desgloseRow && modo === 'edicion' && canEditCuenta(desgloseRow.cuenta_id, desgloseRow.titular_id, desgloseRow.pais_id))}
        onClose={onCloseDesglose}
        onSave={onSaveDesglose}
      />
      {auditExtractoId && <span className="sr-only">{auditExtractoId}</span>}
    </section>
  );
}
