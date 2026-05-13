import { useEffect, useMemo, useState } from 'react';
import { AppSelect } from '@/components/common/AppSelect';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import { useNotificacionesAdminStore } from '@/stores/notificacionesAdminStore';
import type { Cuenta, ExportacionItem, PaginatedResponse } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatDateTime, formatNumber } from '@/utils/formatters';

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

function formatBytes(value: number | null): string {
  if (!value || value <= 0) return 'Sin tamaño';
  const mb = value / (1024 * 1024);
  return `${formatNumber(mb)} MB`;
}

function formatEstadoExportacion(value: string) {
  return estadoExportacionLabels[value.toUpperCase()] ?? value;
}

function formatTipoExportacion(value: string) {
  return tipoExportacionLabels[value.toUpperCase()] ?? value;
}

export default function ExportacionesPage() {
  const usuario = useAuthStore((state) => state.usuario);
  const markExportacionesRead = useNotificacionesAdminStore((state) => state.markExportacionesRead);
  const [rows, setRows] = useState<ExportacionItem[]>([]);
  const [cuentas, setCuentas] = useState<Cuenta[]>([]);
  const [selectedCuentaId, setSelectedCuentaId] = useState('');

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [totalPages, setTotalPages] = useState(1);

  const [loading, setLoading] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const totalRowsText = useMemo(() => `${rows.length} exportaciones en esta página`, [rows.length]);

  const loadCuentas = async () => {
    try {
      const { data } = await api.get<PaginatedResponse<Cuenta>>('/cuentas', {
        params: {
          page: 1,
          pageSize: 200,
          sortBy: 'nombre',
          sortDir: 'asc',
        },
      });
      setCuentas(data.data ?? []);
    } catch {
      // Keep page functional even if this fails.
    }
  };

  const loadRows = async () => {
    setLoading(true);
    setError(null);
    try {
      const { data } = await api.get<PaginatedResponse<ExportacionItem>>('/exportaciones', {
        params: {
          page,
          pageSize,
          cuentaId: selectedCuentaId || undefined,
          sortBy: 'fecha_exportacion',
          sortDir: 'desc',
        },
      });
      setRows(data.data ?? []);
      setTotalPages(Math.max(1, data.total_pages ?? 1));
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudieron cargar las exportaciones.'));
      setRows([]);
      setTotalPages(1);
    } finally {
      setLoading(false);
    }
  };

  const createManualExport = async () => {
    if (!selectedCuentaId) {
      setError('Selecciona la cuenta que quieres exportar.');
      return;
    }

    setExporting(true);
    setError(null);
    try {
      await api.post('/exportaciones/manual', { cuenta_id: selectedCuentaId });
      await loadRows();
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
    void loadCuentas();
  }, []);

  useEffect(() => {
    void loadRows();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- recarga controlada por paginación/filtro cuenta
  }, [page, pageSize, selectedCuentaId]);

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
          <p className="dashboard-subtitle">Historial y generación manual de XLSX por cuenta</p>
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
        <button type="button" onClick={createManualExport} disabled={exporting || loading}>
          {exporting ? 'Generando...' : 'Generar exportación'}
        </button>
      </div>

      {error ? <p className="auth-error">{error}</p> : null}

      <div className="users-table-card">
        {loading ? <p className="import-muted">Cargando exportaciones...</p> : null}
        {!loading && rows.length === 0 ? (
          <EmptyState
            title="No hay exportaciones con estos filtros."
            subtitle="Selecciona una cuenta y genera una exportación para descargar el XLSX."
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
