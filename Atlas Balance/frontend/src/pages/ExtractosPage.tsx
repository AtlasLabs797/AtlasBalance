import { useCallback, useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { AppSelect } from '@/components/common/AppSelect';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import AddRowForm from '@/components/extractos/AddRowForm';
import AuditCellModal from '@/components/extractos/AuditCellModal';
import ExtractoTable from '@/components/extractos/ExtractoTable';
import api from '@/services/api';
import { usePermisosStore } from '@/stores/permisosStore';
import type { AuditCellEntry, Extracto, PaginatedResponse, TitularConCuentas } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';

interface UpdateExtractoPayload {
  fecha?: string;
  concepto?: string;
  comentarios?: string;
  monto?: number;
  saldo?: number;
  columnas_extra?: Record<string, string>;
}

function parseDecimalInput(value: string, fieldLabel: string): number {
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) {
    throw new Error(`${fieldLabel} debe ser numerico.`);
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
  const [titularesResumen, setTitularesResumen] = useState<TitularConCuentas[]>([]);
  const [visibleColumns, setVisibleColumns] = useState<string[] | null>(null);

  const [auditOpen, setAuditOpen] = useState(false);
  const [auditData, setAuditData] = useState<AuditCellEntry[]>([]);
  const [auditLoading, setAuditLoading] = useState(false);
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
    const { data } = await api.get<TitularConCuentas[]>('/extractos/titulares-resumen');
    setTitularesResumen(data);
  }, []);

  const loadRows = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const { data } = await api.get<PaginatedResponse<Extracto>>('/extractos', {
        params: {
          page,
          pageSize,
          sortBy,
          sortDir,
          cuentaId: cuentaFiltro || undefined,
          titularId: titularFiltro || undefined
        }
      });
      setRows(data.data ?? []);
      setTotalPages(Math.max(1, data.total_pages ?? 1));
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudieron cargar extractos'));
    } finally {
      setLoading(false);
    }
  }, [page, pageSize, sortBy, sortDir, cuentaFiltro, titularFiltro]);

  const loadVisibleColumns = useCallback(async () => {
    const { data } = await api.get('/extractos/columnas-visibles', { params: { cuentaId: cuentaFiltro || undefined } });
    setVisibleColumns(data.columnas_visibles ?? null);
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

    setCuentaFiltro((current) => (current === nextCuentaId ? current : nextCuentaId));
    setTitularFiltro((current) => (current === nextTitularId ? current : nextTitularId));
    setPage(1);
  }, [searchParams]);

  const updateFilterParams = (next: { titularId?: string; cuentaId?: string }) => {
    const params = new URLSearchParams(searchParams);

    if (next.titularId !== undefined) {
      if (next.titularId) params.set('titularId', next.titularId);
      else params.delete('titularId');
    }

    if (next.cuentaId !== undefined) {
      if (next.cuentaId) params.set('cuentaId', next.cuentaId);
      else params.delete('cuentaId');
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
    const next = current.includes(column) ? current.filter((c) => c !== column) : [...current, column];
    setVisibleColumns(next);
    await api.put('/extractos/columnas-visibles', { cuenta_id: cuentaFiltro || null, columnas_visibles: next });
  };

  const onSaveCell = async (row: Extracto, column: string, value: string) => {
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

    await api.put(`/extractos/${row.id}`, payload);
    await loadRows();
  };

  const onToggleCheck = async (row: Extracto, checked: boolean) => {
    await api.patch(`/extractos/${row.id}/check`, { checked });
    setRows((prev) => prev.map((r) => (r.id === row.id ? { ...r, checked } : r)));
  };

  const onToggleFlag = async (row: Extracto, flagged: boolean, nota?: string) => {
    await api.patch(`/extractos/${row.id}/flag`, { flagged, nota });
    setRows((prev) => prev.map((r) => (r.id === row.id ? { ...r, flagged, flagged_nota: nota ?? null } : r)));
  };

  const onOpenAudit = async (row: Extracto, column: string) => {
    setAuditOpen(true);
    setAuditLoading(true);
    setAuditColumn(column);
    setAuditExtractoId(row.id);
    try {
      const { data } = await api.get<AuditCellEntry[]>(`/extractos/${row.id}/audit-celda`, { params: { columna: column } });
      setAuditData(data);
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
        </div>
      </header>

      {error && <p className="auth-error">{error}</p>}

      {cuentasConAlta.length > 0 ? (
        <AddRowForm
          cuentas={cuentasConAlta}
          extraColumns={[...new Set(rows.flatMap((r) => Object.keys(r.columnas_extra ?? {})))]}
          onCreate={async (payload) => {
            await api.post('/extractos', payload);
            await loadRows();
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
        <span>Pagina {page} / {totalPages}</span>
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
        onClose={() => {
          setAuditOpen(false);
          setAuditData([]);
          setAuditColumn(null);
          setAuditExtractoId(null);
        }}
      />
      {auditExtractoId && <span className="sr-only">{auditExtractoId}</span>}
    </section>
  );
}
