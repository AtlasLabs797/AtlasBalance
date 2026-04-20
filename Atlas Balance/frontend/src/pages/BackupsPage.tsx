import { useEffect, useMemo, useState } from 'react';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import type { BackupItem, PaginatedResponse, WatchdogState } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';

const pageSizeOptions = [10, 20, 50];

function formatBytes(value: number | null): string {
  if (!value || value <= 0) return 'N/A';
  const mb = value / (1024 * 1024);
  return `${mb.toFixed(2)} MB`;
}

function formatDate(value: string): string {
  return new Date(value).toLocaleString();
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
  const [overlayMessage, setOverlayMessage] = useState('Restauración en progreso...');

  const logout = useAuthStore((state) => state.logout);

  const totalRowsText = useMemo(() => `${rows.length} backups en esta página`, [rows.length]);

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
      setError(extractErrorMessage(err, 'No se pudieron cargar backups'));
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
      setError(extractErrorMessage(err, 'No se pudo crear backup manual'));
    } finally {
      setCreating(false);
    }
  };

  const pollRestoreState = async () => {
    setOverlayVisible(true);
    setOverlayMessage('Restauración en progreso...');
    const timeoutAt = Date.now() + 10 * 60 * 1000;

    while (Date.now() < timeoutAt) {
      try {
        const { data } = await api.get<WatchdogState>('/sistema/estado');
        const state = (data.estado ?? '').toUpperCase();
        if (state === 'RUNNING') {
          setOverlayMessage(data.mensaje || 'Restauración en progreso...');
        } else if (state === 'SUCCESS') {
          setOverlayMessage('Restauración completada. Redirigiendo a login...');
          await new Promise((resolve) => setTimeout(resolve, 1200));
          logout();
          window.location.href = '/login';
          return;
        } else if (state === 'FAILED') {
          setOverlayVisible(false);
          setError(data.mensaje || 'La restauración falló');
          return;
        }
      } catch {
        // Keep polling.
      }

      await new Promise((resolve) => setTimeout(resolve, 2500));
    }

    setOverlayVisible(false);
    setError('Timeout esperando finalización de restauración');
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
      setError(extractErrorMessage(err, 'No se pudo iniciar restauración'));
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
          <h1>Backups</h1>
          <p className="dashboard-subtitle">Backups manuales y automáticos con retención de 6 semanas</p>
        </div>
        <button type="button" onClick={createBackup} disabled={creating || loading}>
          {creating ? 'Creando backup...' : 'Crear backup manual'}
        </button>
      </header>

      {error ? <p className="auth-error">{error}</p> : null}

      <div className="users-table-card">
        {loading ? <p className="import-muted">Cargando backups...</p> : null}
        {!loading && rows.length === 0 ? <EmptyState title="No hay backups registrados." /> : null}

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
                      <td>{formatDate(row.fecha_creacion)}</td>
                      <td>{row.estado}</td>
                      <td>{row.tipo}</td>
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
        title="Confirmar restauración"
        message={confirmTarget ? `Vas a restaurar el backup del ${formatDate(confirmTarget.fecha_creacion)}.` : 'Confirma restauración.'}
        confirmLabel="Continuar"
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
        title="Confirmación final"
        message="Esto reemplaza toda la base de datos y cerrará sesión. ¿Confirmas?"
        confirmLabel="Sí, restaurar"
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
            <h2>Restaurando backup</h2>
            <p>{overlayMessage}</p>
          </div>
        </div>
      ) : null}
    </section>
  );
}
