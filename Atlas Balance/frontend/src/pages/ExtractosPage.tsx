import { useCallback, useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { AppSelect } from '@/components/common/AppSelect';
import { DatePickerField } from '@/components/common/DatePickerField';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import AddRowForm from '@/components/extractos/AddRowForm';
import AuditCellModal from '@/components/extractos/AuditCellModal';
import ExtractoTable from '@/components/extractos/ExtractoTable';
import api from '@/services/api';
import { usePermisosStore } from '@/stores/permisosStore';
import type { AuditCellEntry, Extracto, PaginatedResponse, TitularConCuentas } from '@/types';
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

function parseDecimalInput(value: string, fieldLabel: string): number {
  const parsed = parseEuropeanNumber(value);
  if (parsed === null) {
    throw new Error(`${fieldLabel} debe ser numérico. Ejemplo: 1.234,56.`);
  }

  return parsed;
}

export default function ExtractosPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const [rows, setRows] = useState<Extracto[]>([]);
  const [sortBy, setSortBy] = useState('fecha');
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('desc');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(200);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [cuentaFiltro, setCuentaFiltro] = useState<string>(() => searchParams.get('cuentaId') ?? '');
  const [titularFiltro, setTitularFiltro] = useState<string>(() => searchParams.get('titularId') ?? '');
  const [fechaDesde, setFechaDesde] = useState<string>(() => searchParams.get('fechaDesde') ?? '');
  const [fechaHasta, setFechaHasta] = useState<string>(() => searchParams.get('fechaHasta') ?? '');
  const [titularesResumen, setTitularesResumen] = useState<TitularConCuentas[]>([]);
  const [visibleColumns, setVisibleColumns] = useState<string[] | null>(null);

  const [auditOpen, setAuditOpen] = useState(false);
  const [auditData, setAuditData] = useState<AuditCellEntry[]>([]);
  const [auditLoading, setAuditLoading] = useState(false);
  const [auditError, setAuditError] = useState<string | null>(null);
  const [auditColumn, setAuditColumn] = useState<string | null>(null);
  const [auditExtractoId, setAuditExtractoId] = useState<string | null>(null);

  const canEditCuenta = usePermisosStore((s) => s.canEditCuenta);
  const canAddInCuenta = usePermisosStore((s) => s.canAddInCuenta);
  const getColumnasEditables = usePermisosStore((s) => s.getColumnasEditables);

  const cuentasOptions = useMemo(() => {
    const items: Array<{ id: string; nombre: string; titular_id: string; titular_nombre: string; divisa: string }> = [];
    titularesResumen.forEach((t) => {
      t.cuentas.forEach((c) => {
        items.push({
          id: c.cuenta_id,
          nombre: c.cuenta_nombre,
          titular_id: t.titular_id,
          titular_nombre: t.titular_nombre,
          divisa: c.divisa
        });
      });
    });
    return items;
  }, [titularesResumen]);

  const cuentasConAlta = useMemo(
    () => cuentasOptions.filter((cuenta) => canAddInCuenta(cuenta.id, cuenta.titular_id)),
    [canAddInCuenta, cuentasOptions]
  );

  const loadResumen = useCallback(async () => {
    try {
      const { data } = await api.get<TitularConCuentas[]>('/extractos/titulares-resumen');
      setTitularesResumen(data);
    } catch (err) {
      setTitularesResumen([]);
      setError(extractErrorMessage(err, 'No se pudieron cargar las cuentas disponibles.'));
    }
  }, []);

  const loadRows = useCallback(async () => {
    setLoading(true);
    setError(null);
    if (fechaDesde && fechaHasta && fechaDesde > fechaHasta) {
      setRows([]);
      setTotalPages(1);
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
          fechaDesde: fechaDesde || undefined,
          fechaHasta: fechaHasta || undefined
        }
      });
      setRows(data.data ?? []);
      setTotalPages(Math.max(1, data.total_pages ?? 1));
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudieron cargar extractos'));
      setRows([]);
      setTotalPages(1);
    } finally {
      setLoading(false);
    }
  }, [page, pageSize, sortBy, sortDir, cuentaFiltro, titularFiltro, fechaDesde, fechaHasta]);

  const loadVisibleColumns = useCallback(async () => {
    try {
      const { data } = await api.get('/extractos/columnas-visibles', { params: { cuentaId: cuentaFiltro || undefined } });
      setVisibleColumns(data.columnas_visibles ?? null);
    } catch (err) {
      setVisibleColumns(null);
      setError(extractErrorMessage(err, 'No se pudieron cargar las preferencias de columnas.'));
    }
  }, [cuentaFiltro]);

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

  const onToggleColumn = async (column: string) => {
    const defaultColumns = [...new Set(rows.flatMap((r) => ['fila_numero', 'checked', 'flagged', 'fecha', 'concepto', 'comentarios', 'monto', 'saldo', ...Object.keys(r.columnas_extra ?? {})]))];
    const current = visibleColumns ?? defaultColumns;
    if (current.includes(column) && current.length <= 1) {
      setError('Debe quedar al menos una columna visible.');
      return;
    }

    const next = current.includes(column) ? current.filter((c) => c !== column) : [...current, column];
    setVisibleColumns(next);
    setError(null);
    try {
      await api.put('/extractos/columnas-visibles', { cuenta_id: cuentaFiltro || null, columnas_visibles: next });
    } catch (err) {
      setVisibleColumns(visibleColumns);
      setError(extractErrorMessage(err, 'No se pudieron guardar las columnas visibles.'));
    }
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
      await loadRows();
    } catch (err) {
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
    try {
      const { data } = await api.get<AuditCellEntry[]>(`/extractos/${row.id}/audit-celda`, { params: { columna: column } });
      setAuditData(data);
    } catch (err) {
      setAuditError(extractErrorMessage(err, 'No se pudo cargar la auditoría de la celda.'));
    } finally {
      setAuditLoading(false);
    }
  };

  const canEditCell = (row: Extracto, column: string) => {
    if (!row.cuenta_id) return false;
    if (!canEditCuenta(row.cuenta_id, row.titular_id)) return false;
    const cols = getColumnasEditables(row.cuenta_id, row.titular_id);
    return cols === null || cols.includes(column);
  };

  return (
    <section className="extractos-page">
      <header className="extractos-header">
        <h1>Extractos</h1>
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

      {error && <p className="auth-error">{error}</p>}

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
        loading={loading}
        sortBy={sortBy}
        sortDir={sortDir}
        visibleColumns={visibleColumns}
        onSort={onSort}
        onToggleColumn={(column) => void onToggleColumn(column)}
        onSaveCell={onSaveCell}
        onToggleCheck={onToggleCheck}
        onToggleFlag={onToggleFlag}
        onOpenAudit={onOpenAudit}
        canEditCell={canEditCell}
      />

      <div className="users-pagination">
        <button type="button" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1}>Anterior</button>
        <span>Página {page} / {totalPages}</span>
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
        onClose={() => {
          setAuditOpen(false);
          setAuditData([]);
          setAuditError(null);
          setAuditColumn(null);
          setAuditExtractoId(null);
        }}
      />
      {auditExtractoId && <span className="sr-only">{auditExtractoId}</span>}
    </section>
  );
}
