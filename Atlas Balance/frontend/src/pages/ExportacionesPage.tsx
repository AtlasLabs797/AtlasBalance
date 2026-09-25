import { useEffect, useMemo, useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { AppSelect } from '@/components/common/AppSelect';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import { useInvalidateAfterMutation } from '@/hooks/queries/useInvalidateAfterMutation';
import api from '@/services/api';
import { QUERY_STALE_TIMES } from '@/services/queryClient';
import { queryKeys } from '@/queries/queryKeys';
import { useAuthStore } from '@/stores/authStore';
import { useNotificacionesAdminStore } from '@/stores/notificacionesAdminStore';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
import type { Cuenta, ExportacionItem, PaginatedResponse } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatBytes, formatDateTime } from '@/utils/formatters';

const pageSizeOptions = [10, 20, 50];
const estadoExportacionLabels: Record<string, string> = {
  PENDING: 'Pendiente',
  SUCCESS: 'Lista',
  FAILED: 'Fallida',
};
const tipoExportacionLabels: Record<string, string> = {
  AUTO: 'Automática',
  MANUAL: 'Manual',
};

function formatEstadoExportacion(value: string) {
  return estadoExportacionLabels[value.toUpperCase()] ?? value;
}

function formatTipoExportacion(value: string) {
  return tipoExportacionLabels[value.toUpperCase()] ?? value;
}

export default function ExportacionesPage() {
  const usuario = useAuthStore((state) => state.usuario);
  const usuarioId = usuario?.id ?? '';
  const invalidate = useInvalidateAfterMutation();
  const markExportacionesRead = useNotificacionesAdminStore((state) => state.markExportacionesRead);
  const selectedPaisId = usePaisScopeStore((state) => state.selectedPaisId);
  const [rows, setRows] = useState<ExportacionItem[]>([]);
  const [cuentas, setCuentas] = useState<Cuenta[]>([]);
  const [selectedCuentaId, setSelectedCuentaId] = useState('');

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [totalPages, setTotalPages] = useState(1);

  const [loading, setLoading] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const canCreateManualExport = usuario?.rol === 'ADMIN' || usuario?.rol === 'GERENTE';

  const totalRowsText = useMemo(() => `${rows.length} exportaciones en esta página`, [rows.length]);

  // V-03.01 (#8): cuentas (para el selector) y el listado de exportaciones
  // migran de api.get en efectos a React Query, mismo patron que
  // DashboardPage.tsx, con bridge a estado local.
  const cuentasQuery = useQuery({
    queryKey: queryKeys.cuentas.list({ usuarioId, page: 1, pageSize: 200, paisId: selectedPaisId || null, sortBy: 'nombre', sortDir: 'asc' }),
    queryFn: () =>
      api.get<PaginatedResponse<Cuenta>>('/cuentas', {
        params: {
          page: 1,
          pageSize: 200,
          paisId: selectedPaisId || undefined,
          sortBy: 'nombre',
          sortDir: 'asc',
        },
      }).then((res) => res.data),
    enabled: Boolean(usuarioId),
    staleTime: QUERY_STALE_TIMES.LISTADO_PAGINADO_MS,
  });

  const rowsQuery = useQuery<PaginatedResponse<ExportacionItem>>({
    queryKey: queryKeys.exportaciones.list({ usuarioId, page, pageSize, cuentaId: selectedCuentaId || null, paisId: selectedPaisId || null }),
    queryFn: ({ signal }) =>
      api.get<PaginatedResponse<ExportacionItem>>('/exportaciones', {
        params: {
          page,
          pageSize,
          cuentaId: selectedCuentaId || undefined,
          paisId: selectedPaisId || undefined,
          sortBy: 'fecha_exportacion',
          sortDir: 'desc',
        },
        signal,
      }).then((res) => res.data),
    enabled: Boolean(usuarioId),
    placeholderData: keepPreviousData,
    staleTime: QUERY_STALE_TIMES.LISTADO_PAGINADO_MS,
  });

  useEffect(() => {
    if (cuentasQuery.data) {
      setCuentas(cuentasQuery.data.data ?? []);
    }
    // El listado original ignoraba errores en silencio (catch { // no-op }).
  }, [cuentasQuery.data]);

  useEffect(() => {
    const data = rowsQuery.data;
    if (data) {
      setRows(data.data ?? []);
      setTotalPages(Math.max(1, data.total_pages ?? 1));
    } else if (!rowsQuery.isLoading && !rowsQuery.error) {
      setRows([]);
      setTotalPages(1);
    }
  }, [rowsQuery.data, rowsQuery.isLoading, rowsQuery.error]);

  useEffect(() => {
    if (rowsQuery.error) {
      setError(extractErrorMessage(rowsQuery.error, 'No se pudieron cargar las exportaciones.'));
      setRows([]);
      setTotalPages(1);
    } else {
      setError(null);
    }
  }, [rowsQuery.error]);

  useEffect(() => {
    if (rowsQuery.isLoading) {
      setLoading(true);
    } else if (!rowsQuery.isFetching) {
      setLoading(false);
    }
  }, [rowsQuery.isLoading, rowsQuery.isFetching]);

  const createManualExport = async () => {
    if (!canCreateManualExport) {
      return;
    }

    if (!selectedCuentaId) {
      setError('Selecciona la cuenta que quieres exportar.');
      return;
    }

    setExporting(true);
    setError(null);
    try {
      await api.post('/exportaciones/manual', { cuenta_id: selectedCuentaId });
      await invalidate('exportacion');
      if (usuario?.rol === 'ADMIN') {
        await markExportacionesRead();
      }
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo generar exportación manual'));
    } finally {
      setExporting(false);
    }
  };

  const downloadExport = async (id: string) => {
    setError(null);
    try {
      const response = await api.get(`/exportaciones/${id}/descargar`, { responseType: 'blob' });
      const blob = new Blob([response.data], {
        type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      });
      const href = URL.createObjectURL(blob);
      const contentDisposition = response.headers?.['content-disposition'] as string | undefined;
      const filenameMatch = contentDisposition?.match(/filename="?([^";]+)"?/i);
      const filename = filenameMatch?.[1] ?? `exportacion_${id}.xlsx`;

      const anchor = document.createElement('a');
      anchor.href = href;
      anchor.setAttribute('download', filename);
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      URL.revokeObjectURL(href);
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo descargar el archivo.'));
    }
  };

  useEffect(() => {
    setSelectedCuentaId('');
    setPage(1);
  }, [selectedPaisId]);

  useEffect(() => {
    if (usuario?.rol === 'ADMIN') {
      void markExportacionesRead();
    }
  }, [markExportacionesRead, usuario?.rol]);

  return (
    <section className="exportaciones-page">
      <header className="exportaciones-header">
        <div>
          <h1>Exportaciones</h1>
          <p className="dashboard-subtitle">
            {canCreateManualExport
              ? 'Historial y generación manual de XLSX por cuenta'
              : 'Historial de XLSX disponibles dentro de tu alcance'}
          </p>
        </div>
      </header>

      <div className="exportaciones-toolbar">
        <AppSelect
          label="Cuenta"
          value={selectedCuentaId}
          options={[
            { value: '', label: 'Selecciona una cuenta' },
            ...cuentas.map((cuenta) => ({ value: cuenta.id, label: `${cuenta.nombre} (${cuenta.divisa})` })),
          ]}
          onChange={(next) => {
            setSelectedCuentaId(next);
            setPage(1);
          }}
        />
        {canCreateManualExport ? (
          <button type="button" onClick={createManualExport} disabled={exporting || loading}>
            {exporting ? 'Generando...' : 'Generar exportación'}
          </button>
        ) : null}
      </div>

      {error ? <p className="auth-error" role="alert">{error}</p> : null}

      <div className="users-table-card">
        {loading ? <p className="import-muted">Cargando exportaciones...</p> : null}
        {!loading && rows.length === 0 ? (
          <EmptyState
            title="No hay exportaciones con estos filtros."
            subtitle={
              canCreateManualExport
                ? 'Selecciona una cuenta y genera una exportación para descargar el XLSX.'
                : 'Cuando haya exportaciones listas para tus cuentas, aparecerán aquí.'
            }
          />
        ) : null}

        {!loading && rows.length > 0 ? (
          <>
            <div className="users-table-scroll">
              <table>
                <thead>
                  <tr>
                    <th>Fecha</th>
                    <th>Cuenta</th>
                    <th>Titular</th>
                    <th>Estado</th>
                    <th>Tipo</th>
                    <th>Tamaño</th>
                    <th>Iniciado por</th>
                    <th>Acciones</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row) => (
                    <tr key={row.id}>
                      <td>{formatDateTime(row.fecha_exportacion)}</td>
                      <td>{row.cuenta_nombre}</td>
                      <td>{row.titular_nombre}</td>
                      <td>{formatEstadoExportacion(row.estado)}</td>
                      <td>{formatTipoExportacion(row.tipo)}</td>
                      <td>{formatBytes(row.tamanio_bytes)}</td>
                      <td>{row.iniciado_por_nombre ?? 'Sistema'}</td>
                      <td className="users-row-actions">
                        <button type="button" onClick={() => void downloadExport(row.id)} disabled={row.estado !== 'SUCCESS'}>
                          Descargar XLSX
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="users-pagination">
              <button type="button" onClick={() => setPage((prev) => Math.max(1, prev - 1))} disabled={page <= 1}>
                Anterior
              </button>
              <span>
                Página {page} / {totalPages} · {totalRowsText}
              </span>
              <button type="button" onClick={() => setPage((prev) => Math.min(totalPages, prev + 1))} disabled={page >= totalPages}>
                Siguiente
              </button>
              <PageSizeSelect
                value={pageSize}
                options={pageSizeOptions}
                onChange={(next) => {
                  setPageSize(next);
                  setPage(1);
                }}
              />
            </div>
          </>
        ) : null}
      </div>
    </section>
  );
}
