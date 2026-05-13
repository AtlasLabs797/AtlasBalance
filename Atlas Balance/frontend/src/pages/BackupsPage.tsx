import { useEffect, useMemo, useState } from 'react';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import type { BackupItem, PaginatedResponse, WatchdogState } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatDateTime, formatNumber } from '@/utils/formatters';

const pageSizeOptions = [10, 20, 50];
const estadoCopiaLabels: Record<string, string> = {
  PENDING: 'Pendiente',
  SUCCESS: 'Lista',
  FAILED: 'Fallida',
};
const tipoCopiaLabels: Record<string, string> = {
  AUTO: 'Automática',
  MANUAL: 'Manual',
};

function formatBytes(value: number | null): string {
  if (!value || value <= 0) return 'Sin tamaño';
  const mb = value / (1024 * 1024);
  return `${formatNumber(mb)} MB`;
}

function formatEstadoCopia(value: string) {
  return estadoCopiaLabels[value.toUpperCase()] ?? value;
}

function formatTipoCopia(value: string) {
  return tipoCopiaLabels[value.toUpperCase()] ?? value;
}

export default function BackupsPage() {
  const [rows, setRows] = useState<BackupItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [creating, setCreating] = useState(false);
  const [restoring, setRestoring] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [totalPages, setTotalPages] = useState(1);

  const [confirmTarget, setConfirmTarget] = useState<BackupItem | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [doubleConfirmOpen, setDoubleConfirmOpen] = useState(false);

  const [overlayVisible, setOverlayVisible] = useState(false);
  const [overlayMessage, setOverlayMessage] = useState('No cierres esta ventana; al terminar volverás al inicio de sesión.');

  const logout = useAuthStore((state) => state.logout);

  const totalRowsText = useMemo(() => `${rows.length} copias en esta página`, [rows.length]);

  const fetchRows = async () => {
    setLoading(true);
    setError(null);
    try {
      const { data } = await api.get<PaginatedResponse<BackupItem>>('/backups', {
        params: {
          page,
          pageSize,
          sortBy: 'fecha_creacion',
          sortDir: 'desc',
        },
      });
      setRows(data.data ?? []);
      setTotalPages(Math.max(1, data.total_pages ?? 1));
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudieron cargar las copias de seguridad.'));
      setRows([]);
      setTotalPages(1);
    } finally {
      setLoading(false);
    }
  };

  const createBackup = async () => {
    setCreating(true);
    setError(null);
    try {
      await api.post('/backups/manual');
      await fetchRows();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo crear la copia de seguridad manual.'));
    } finally {
      setCreating(false);
    }
  };

  const pollRestoreState = async () => {
    setOverlayVisible(true);
    setOverlayMessage('No cierres esta ventana; al terminar volverás al inicio de sesión.');
    const timeoutAt = Date.now() + 10 * 60 * 1000;

    while (Date.now() < timeoutAt) {
      try {
        const { data } = await api.get<WatchdogState>('/sistema/estado');
        const state = (data.estado ?? '').toUpperCase();
        if (state === 'RUNNING') {
          setOverlayMessage(data.mensaje || 'No cierres esta ventana; al terminar volverás al inicio de sesión.');
        } else if (state === 'SUCCESS') {
          setOverlayMessage('Restauración completada. Volverás al inicio de sesión.');
          await new Promise((resolve) => setTimeout(resolve, 1200));
          logout();
          window.location.href = '/login';
          return;
        } else if (state === 'FAILED') {
          setOverlayVisible(false);
          setError(data.mensaje || 'La restauración falló. Revisa el estado del sistema antes de intentarlo de nuevo.');
          return;
        }
      } catch {
        // Keep polling.
      }

      await new Promise((resolve) => setTimeout(resolve, 2500));
    }

    setOverlayVisible(false);
    setError('La restauración está tardando más de lo esperado. Comprueba el estado antes de iniciar otra restauración.');
  };

  const triggerRestore = async () => {
    if (!confirmTarget) return;
    setRestoring(true);
    setError(null);
    try {
      await api.post(`/backups/${confirmTarget.id}/restaurar`, { confirmacion: 'RESTAURAR' });
      setConfirmTarget(null);
      setDoubleConfirmOpen(false);
      await pollRestoreState();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo iniciar la restauración.'));
    } finally {
      setRestoring(false);
    }
  };

  useEffect(() => {
    fetchRows();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- paginación controla la carga
  }, [page, pageSize]);

  return (
    <section className="backups-page">
      <header className="backups-header">
        <div>
          <h1>Copias de seguridad</h1>
          <p className="dashboard-subtitle">Copias manuales y automáticas con retención de 6 semanas</p>
        </div>
        <button type="button" onClick={createBackup} disabled={creating || loading}>
          {creating ? 'Creando copia...' : 'Crear copia manual'}
        </button>
      </header>

      {error ? <p className="auth-error">{error}</p> : null}

      <div className="users-table-card">
        {loading ? <p className="import-muted">Cargando copias de seguridad...</p> : null}
        {!loading && rows.length === 0 ? (
          <EmptyState
            title="No hay copias de seguridad registradas."
            subtitle="Crea una copia manual antes de hacer cambios de riesgo."
          />
        ) : null}

        {!loading && rows.length > 0 ? (
          <>
            <div className="users-table-scroll">
              <table>
                <thead>
                  <tr>
                    <th>Fecha</th>
                    <th>Estado</th>
                    <th>Tipo</th>
                    <th>Tamaño</th>
                    <th>Iniciado por</th>
                    <th>Archivo</th>
                    <th>Acciones</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row) => (
                    <tr key={row.id}>
                      <td>{formatDateTime(row.fecha_creacion)}</td>
                      <td>{formatEstadoCopia(row.estado)}</td>
                      <td>{formatTipoCopia(row.tipo)}</td>
                      <td>{formatBytes(row.tamanio_bytes)}</td>
                      <td>{row.iniciado_por_nombre ?? 'Sistema'}</td>
                      <td>{row.ruta_archivo}</td>
                      <td className="users-row-actions">
                        <button
                          type="button"
                          onClick={() => {
                            setConfirmTarget(row);
                            setConfirmOpen(true);
                          }}
                          disabled={row.estado !== 'SUCCESS' || restoring}
                        >
                          Restaurar
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

      <ConfirmDialog
        open={confirmOpen}
        title="Restaurar copia"
        message={confirmTarget ? `Vas a restaurar la copia del ${formatDateTime(confirmTarget.fecha_creacion)}.` : 'Confirma qué copia quieres restaurar.'}
        confirmLabel="Revisar restauración"
        onCancel={() => {
          setConfirmOpen(false);
          setConfirmTarget(null);
        }}
        onConfirm={() => {
          setConfirmOpen(false);
          setDoubleConfirmOpen(true);
        }}
      />

      <ConfirmDialog
        open={doubleConfirmOpen}
        title="Última confirmación"
        message="Esto reemplazará toda la base de datos y cerrará tu sesión. No sigas si no tienes claro que esta copia es la correcta."
        confirmLabel="Restaurar base de datos"
        loadingLabel="Restaurando..."
        loading={restoring}
        onCancel={() => {
          setDoubleConfirmOpen(false);
          setConfirmTarget(null);
        }}
        onConfirm={triggerRestore}
      />

      {overlayVisible ? (
        <div className="modal-backdrop">
          <div className="loading-overlay">
            <h2>Restaurando copia de seguridad</h2>
            <p>{overlayMessage || 'No cierres esta ventana; al terminar volverás al inicio de sesión.'}</p>
          </div>
        </div>
      ) : null}
    </section>
  );
}
