import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Cloud, CloudOff, DownloadCloud, Link2, RefreshCcw, RotateCcw, Save, ShieldCheck, UploadCloud } from 'lucide-react';
import { AppSelect } from '@/components/common/AppSelect';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import { useBlockingOverlay } from '@/hooks/useBlockingOverlay';
import { useConfirmDialog } from '@/hooks/useConfirmDialog';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import type {
  BackupConfig,
  BackupDestination,
  BackupFrequency,
  BackupItem,
  GoogleDriveBackupFile,
  GoogleDriveLinkStart,
  GoogleDriveLinkStatus,
  PaginatedResponse,
  SaveBackupConfigRequest,
  WatchdogState,
} from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatBytes, formatDateTime } from '@/utils/formatters';

const pageSizeOptions = [10, 20, 50];
const dayOptions = [
  { value: 0, label: 'Domingo' },
  { value: 1, label: 'Lunes' },
  { value: 2, label: 'Martes' },
  { value: 3, label: 'Miercoles' },
  { value: 4, label: 'Jueves' },
  { value: 5, label: 'Viernes' },
  { value: 6, label: 'Sabado' },
];
const frequencyOptions = [
  { value: 'HOURLY', label: 'Cada X horas' },
  { value: 'DAILY', label: 'Diaria' },
  { value: 'WEEKLY', label: 'Semanal' },
  { value: 'MONTHLY', label: 'Mensual' },
];
const destinationOptions = [
  { value: 'LOCAL', label: 'Solo local' },
  { value: 'LOCAL_Y_GOOGLE_DRIVE', label: 'Local + Google Drive' },
];

const estadoCopiaLabels: Record<string, string> = {
  PENDING: 'Pendiente',
  SUCCESS: 'Lista',
  FAILED: 'Fallida',
};
const tipoCopiaLabels: Record<string, string> = {
  AUTO: 'Automatica',
  MANUAL: 'Manual',
};
const cloudEstadoLabels: Record<string, string> = {
  PENDING: 'Subiendo',
  SUCCESS: 'En Drive',
  FAILED: 'Fallida',
  IMPORTED: 'Importada',
};

function formatEstadoCopia(value: string) {
  return estadoCopiaLabels[value.toUpperCase()] ?? value;
}

function formatTipoCopia(value: string) {
  return tipoCopiaLabels[value.toUpperCase()] ?? value;
}

function formatCloudEstado(value: string | null | undefined) {
  if (!value) return 'Solo local';
  return cloudEstadoLabels[value.toUpperCase()] ?? value;
}

function createDraft(config: BackupConfig): SaveBackupConfigRequest {
  return {
    auto_enabled: config.auto_enabled,
    frequency: config.frequency,
    time_utc: config.time_utc,
    day_of_week: config.day_of_week,
    day_of_month: config.day_of_month,
    interval_hours: config.interval_hours,
    destination: config.destination,
    google_drive_client_id: config.google_drive.client_id ?? '',
    google_drive_client_secret: '',
    google_drive_folder_id: config.google_drive.folder_id ?? '',
  };
}

function normalizeFrequency(value: string): BackupFrequency {
  return ['HOURLY', 'DAILY', 'WEEKLY', 'MONTHLY'].includes(value) ? (value as BackupFrequency) : 'WEEKLY';
}

function normalizeDestination(value: string): BackupDestination {
  return value === 'LOCAL_Y_GOOGLE_DRIVE' ? 'LOCAL_Y_GOOGLE_DRIVE' : 'LOCAL';
}

export default function BackupsPage() {
  const [rows, setRows] = useState<BackupItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [creating, setCreating] = useState(false);
  const [restoring, setRestoring] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [config, setConfig] = useState<BackupConfig | null>(null);
  const [draft, setDraft] = useState<SaveBackupConfigRequest | null>(null);
  const [configLoading, setConfigLoading] = useState(false);
  const [savingConfig, setSavingConfig] = useState(false);

  const [cloudBusy, setCloudBusy] = useState(false);
  const { confirm, dialogProps: confirmDialogProps } = useConfirmDialog();
  const [linkStart, setLinkStart] = useState<GoogleDriveLinkStart | null>(null);
  const [linkStatus, setLinkStatus] = useState<GoogleDriveLinkStatus | null>(null);
  const [driveFiles, setDriveFiles] = useState<GoogleDriveBackupFile[]>([]);
  const [driveFilesLoading, setDriveFilesLoading] = useState(false);
  const [importingFileId, setImportingFileId] = useState<string | null>(null);
  const [retryingBackupId, setRetryingBackupId] = useState<string | null>(null);

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [totalPages, setTotalPages] = useState(1);

  const [confirmTarget, setConfirmTarget] = useState<BackupItem | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [doubleConfirmOpen, setDoubleConfirmOpen] = useState(false);

  const [overlayVisible, setOverlayVisible] = useState(false);
  const [overlayMessage, setOverlayMessage] = useState('No cierres esta ventana; al terminar volveras al inicio de sesion.');
  const restoreOverlayRef = useRef<HTMLDivElement | null>(null);
  useBlockingOverlay(overlayVisible);

  const logout = useAuthStore((state) => state.logout);

  const totalRowsText = useMemo(() => `${rows.length} copias en esta pagina`, [rows.length]);
  const latestSuccessfulBackup = useMemo(
    () => rows.find((row) => row.estado.toUpperCase() === 'SUCCESS') ?? null,
    [rows],
  );
  const driveConfigured = Boolean(
    draft?.destination === 'LOCAL_Y_GOOGLE_DRIVE' &&
    draft.google_drive_client_id.trim() &&
    (config?.google_drive.client_secret_configured || draft.google_drive_client_secret.trim()),
  );
  const driveConnected = Boolean(config?.google_drive.connected);
  const linkTerminalState = (linkStatus?.estado ?? '').toUpperCase();
  const linkCanRetry = linkTerminalState === 'FAILED' || linkTerminalState === 'EXPIRED';

  useEffect(() => {
    if (overlayVisible) {
      restoreOverlayRef.current?.focus();
    }
  }, [overlayVisible]);

  const fetchRows = useCallback(async () => {
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
  }, [page, pageSize]);

  const fetchConfig = useCallback(async () => {
    setConfigLoading(true);
    try {
      const { data } = await api.get<BackupConfig>('/backups/config');
      setConfig(data);
      setDraft(createDraft(data));
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo cargar la configuracion de copias.'));
    } finally {
      setConfigLoading(false);
    }
  }, []);

  const fetchDriveFiles = useCallback(async () => {
    if (!driveConnected) return;
    setDriveFilesLoading(true);
    try {
      const { data } = await api.get<{ data: GoogleDriveBackupFile[] }>('/backups/google-drive/files');
      setDriveFiles(data.data ?? []);
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudieron cargar las copias de Google Drive.'));
      setDriveFiles([]);
    } finally {
      setDriveFilesLoading(false);
    }
  }, [driveConnected]);

  const createBackup = async () => {
    setCreating(true);
    setError(null);
    try {
      await api.post('/backups/manual');
      await fetchRows();
      await fetchConfig();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo crear la copia de seguridad manual.'));
    } finally {
      setCreating(false);
    }
  };

  const saveConfig = async () => {
    if (!draft) return;
    setSavingConfig(true);
    setError(null);
    try {
      await api.put('/backups/config', draft);
      await fetchConfig();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo guardar la configuracion de copias.'));
    } finally {
      setSavingConfig(false);
    }
  };

  const startDriveLink = async () => {
    if (!draft) return;
    setCloudBusy(true);
    setError(null);
    setLinkStatus(null);
    try {
      await api.put('/backups/config', draft);
      const { data } = await api.post<GoogleDriveLinkStart>('/backups/google-drive/link/start');
      setLinkStart(data);
      await fetchConfig();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo iniciar la vinculacion con Google Drive.'));
      setCloudBusy(false);
    }
  };

  const testDrive = async () => {
    setCloudBusy(true);
    setError(null);
    try {
      if (draft) {
        await api.put('/backups/config', draft);
      }
      const { data } = await api.post<GoogleDriveLinkStatus>('/backups/google-drive/test');
      setLinkStatus(data);
      await fetchConfig();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo validar Google Drive.'));
    } finally {
      setCloudBusy(false);
    }
  };

  const disconnectDrive = async () => {
    const confirmed = await confirm({
      title: 'Desvincular Google Drive',
      message: 'Se cerrará la conexión con Google Drive. Las copias dejarán de subirse a la nube hasta que vuelvas a vincular la cuenta. ¿Continuar?',
      confirmLabel: 'Desvincular',
    });
    if (!confirmed) {
      return;
    }

    setCloudBusy(true);
    setError(null);
    try {
      await api.post('/backups/google-drive/disconnect');
      setLinkStart(null);
      setLinkStatus(null);
      setDriveFiles([]);
      await fetchConfig();
      await fetchRows();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo desvincular Google Drive.'));
    } finally {
      setCloudBusy(false);
    }
  };

  const retryDriveUpload = async (backup: BackupItem) => {
    setRetryingBackupId(backup.id);
    setError(null);
    try {
      await api.post(`/backups/${backup.id}/google-drive/retry`);
      await fetchRows();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo subir esta copia a Google Drive.'));
    } finally {
      setRetryingBackupId(null);
    }
  };

  const importDriveFile = async (file: GoogleDriveBackupFile) => {
    setImportingFileId(file.file_id);
    setError(null);
    try {
      await api.post('/backups/google-drive/import', { file_id: file.file_id });
      await fetchRows();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo importar la copia desde Google Drive.'));
    } finally {
      setImportingFileId(null);
    }
  };

  const pollRestoreState = async () => {
    setOverlayVisible(true);
    setOverlayMessage('No cierres esta ventana; al terminar volveras al inicio de sesion.');
    const timeoutAt = Date.now() + 10 * 60 * 1000;

    while (Date.now() < timeoutAt) {
      try {
        const { data } = await api.get<WatchdogState>('/sistema/estado');
        const state = (data.estado ?? '').toUpperCase();
        if (state === 'RUNNING') {
          setOverlayMessage(data.mensaje || 'No cierres esta ventana; al terminar volveras al inicio de sesion.');
        } else if (state === 'SUCCESS') {
          setOverlayMessage('Restauracion completada. Volveras al inicio de sesion.');
          await new Promise((resolve) => setTimeout(resolve, 1200));
          logout();
          window.location.href = '/login';
          return;
        } else if (state === 'FAILED') {
          setOverlayVisible(false);
          setError(data.mensaje || 'La restauracion fallo. Revisa el estado del sistema antes de intentarlo de nuevo.');
          return;
        }
      } catch {
        // Keep polling.
      }

      await new Promise((resolve) => setTimeout(resolve, 2500));
    }

    setOverlayVisible(false);
    setError('La restauracion esta tardando mas de lo esperado. Comprueba el estado antes de iniciar otra restauracion.');
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
      setError(extractErrorMessage(err, 'No se pudo iniciar la restauracion.'));
    } finally {
      setRestoring(false);
    }
  };

  useEffect(() => {
    fetchRows();
  }, [fetchRows]);

  useEffect(() => {
    fetchConfig();
  }, [fetchConfig]);

  useEffect(() => {
    if (!linkStart) return;

    let timer: number | undefined;
    let cancelled = false;

    const poll = async () => {
      try {
        const { data } = await api.get<GoogleDriveLinkStatus>(`/backups/google-drive/link/${linkStart.session_id}`);
        if (cancelled) return;
        setLinkStatus(data);
        const state = (data.estado ?? '').toUpperCase();

        if (state === 'CONNECTED') {
          setLinkStart(null);
          setCloudBusy(false);
          await fetchConfig();
          return;
        }

        if (state === 'FAILED' || state === 'EXPIRED') {
          setLinkStart(null);
          setCloudBusy(false);
          return;
        }

        const waitSeconds = Math.max(2, data.poll_after_seconds || linkStart.interval_seconds || 5);
        timer = window.setTimeout(poll, waitSeconds * 1000);
      } catch (err) {
        if (cancelled) return;
        setError(extractErrorMessage(err, 'No se pudo completar la vinculacion con Google Drive.'));
        setCloudBusy(false);
      }
    };

    timer = window.setTimeout(poll, 1200);
    return () => {
      cancelled = true;
      if (timer) window.clearTimeout(timer);
    };
  }, [fetchConfig, linkStart]);

  useEffect(() => {
    if (driveConnected) {
      fetchDriveFiles();
    } else {
      setDriveFiles([]);
    }
  }, [driveConnected, fetchDriveFiles]);

  return (
    <section className="backups-page">
      <header className="backups-header">
        <div>
          <h1>Copias de seguridad</h1>
          <p className="dashboard-subtitle">Programacion, copias locales y subida cifrada a Google Drive</p>
        </div>
        <button type="button" className="button-primary" onClick={createBackup} disabled={creating || loading}>
          <UploadCloud size={18} aria-hidden="true" />
          {creating ? 'Creando...' : 'Crear copia manual'}
        </button>
      </header>

      {error ? <p className="auth-error" role="alert">{error}</p> : null}

      <section className="backup-admin-grid" aria-label="Configuracion de copias">
        <article className="backup-config-panel">
          <div className="backup-panel-heading">
            <h2>Programacion</h2>
            <button type="button" className="button-primary" onClick={saveConfig} disabled={!draft || savingConfig || configLoading}>
              <Save size={17} aria-hidden="true" />
              {savingConfig ? 'Guardando...' : 'Guardar'}
            </button>
          </div>

          {draft ? (
            <div className="backup-config-form">
              <label className="config-check">
                <input
                  type="checkbox"
                  checked={draft.auto_enabled}
                  onChange={(event) => setDraft((prev) => prev ? { ...prev, auto_enabled: event.target.checked } : prev)}
                />
                <span>Copias automaticas activas</span>
              </label>

              <AppSelect
                className="config-field"
                label="Frecuencia"
                value={normalizeFrequency(draft.frequency)}
                options={frequencyOptions}
                onChange={(next) => setDraft((prev) => prev ? { ...prev, frequency: next as BackupFrequency } : prev)}
              />

              {draft.frequency === 'HOURLY' ? (
                <label className="config-field">
                  <span>Intervalo horario</span>
                  <input
                    type="number"
                    min={1}
                    max={168}
                    value={draft.interval_hours}
                    onChange={(event) => setDraft((prev) => prev ? { ...prev, interval_hours: Number(event.target.value) } : prev)}
                  />
                </label>
              ) : (
                <label className="config-field">
                  <span>Hora UTC</span>
                  <input
                    type="time"
                    value={draft.time_utc}
                    onChange={(event) => setDraft((prev) => prev ? { ...prev, time_utc: event.target.value } : prev)}
                  />
                </label>
              )}

              {draft.frequency === 'WEEKLY' ? (
                <AppSelect
                  className="config-field"
                  label="Dia de la semana"
                  value={String(draft.day_of_week ?? 1)}
                  options={dayOptions.map((day) => ({ value: String(day.value), label: day.label }))}
                  onChange={(next) => setDraft((prev) => prev ? { ...prev, day_of_week: Number(next) } : prev)}
                />
              ) : null}

              {draft.frequency === 'MONTHLY' ? (
                <label className="config-field">
                  <span>Dia del mes</span>
                  <input
                    type="number"
                    min={1}
                    max={31}
                    value={draft.day_of_month}
                    onChange={(event) => setDraft((prev) => prev ? { ...prev, day_of_month: Number(event.target.value) } : prev)}
                  />
                </label>
              ) : null}

              <AppSelect
                className="config-field"
                label="Destino"
                value={normalizeDestination(draft.destination)}
                options={destinationOptions}
                onChange={(next) => setDraft((prev) => prev ? { ...prev, destination: next as BackupDestination } : prev)}
              />

              <dl className="backup-config-facts">
                <div>
                  <dt>Ultima ejecucion</dt>
                  <dd>{config?.last_started_utc ? formatDateTime(config.last_started_utc) : 'Sin registro'}</dd>
                </div>
                <div>
                  <dt>Resultado</dt>
                  <dd>{config?.last_result || 'Sin registro'}</dd>
                </div>
              </dl>
            </div>
          ) : (
            <p className="import-muted">Cargando configuracion...</p>
          )}
        </article>

        <article className="backup-config-panel">
          <div className="backup-panel-heading">
            <h2>Google Drive</h2>
            <span className={`backup-status-pill ${driveConnected ? 'backup-status-pill--ok' : 'backup-status-pill--muted'}`}>
              {driveConnected ? <Cloud size={16} aria-hidden="true" /> : <CloudOff size={16} aria-hidden="true" />}
              {driveConnected ? 'Vinculado' : 'Sin vincular'}
            </span>
          </div>

          {draft ? (
            <div className="backup-drive-form">
              <label className="config-field">
                <span>OAuth Client ID</span>
                <input
                  value={draft.google_drive_client_id}
                  onChange={(event) => setDraft((prev) => prev ? { ...prev, google_drive_client_id: event.target.value } : prev)}
                  autoComplete="off"
                />
              </label>
              <label className="config-field">
                <span>OAuth Client Secret</span>
                <input
                  type="password"
                  value={draft.google_drive_client_secret}
                  placeholder={config?.google_drive.client_secret_configured ? 'Configurado; escribe para reemplazar' : ''}
                  onChange={(event) => setDraft((prev) => prev ? { ...prev, google_drive_client_secret: event.target.value } : prev)}
                  autoComplete="new-password"
                />
              </label>
              <label className="config-field">
                <span>Carpeta Drive ID</span>
                <input
                  value={draft.google_drive_folder_id}
                  placeholder="Se creara una carpeta si esta vacio"
                  onChange={(event) => setDraft((prev) => prev ? { ...prev, google_drive_folder_id: event.target.value } : prev)}
                  autoComplete="off"
                />
              </label>

              <div className="backup-drive-state">
                <p><strong>Cuenta:</strong> {config?.google_drive.account_email ?? 'Sin cuenta vinculada'}</p>
                <p><strong>Cifrado:</strong> {config?.google_drive.encryption_key_configured ? 'Clave activa' : 'Se generara en la primera subida'}</p>
                {config?.google_drive.last_error ? <p className="backup-drive-error">{config.google_drive.last_error}</p> : null}
              </div>

              <div className="backup-drive-actions">
                <button type="button" className="button-secondary" onClick={startDriveLink} disabled={!driveConfigured || savingConfig || cloudBusy}>
                  <Link2 size={17} aria-hidden="true" />
                  Vincular
                </button>
                <button type="button" className="button-secondary" onClick={testDrive} disabled={!driveConnected || cloudBusy}>
                  <ShieldCheck size={17} aria-hidden="true" />
                  Probar
                </button>
                <button type="button" className="button-danger" onClick={disconnectDrive} disabled={!driveConnected || cloudBusy}>
                  <CloudOff size={17} aria-hidden="true" />
                  Desvincular
                </button>
              </div>

              {linkStart ? (
                <div className="backup-link-box" role="status">
                  <span>Codigo</span>
                  <strong>{linkStart.user_code}</strong>
                  <a href={linkStart.verification_url} target="_blank" rel="noreferrer">{linkStart.verification_url}</a>
                  <small>{linkStatus?.message ?? 'Esperando autorizacion de Google...'}</small>
                </div>
              ) : linkStatus?.message ? (
                <div className="backup-link-box backup-link-box--status" role="status">
                  <small>{linkStatus.message}</small>
                  {linkCanRetry ? (
                    <button type="button" className="button-secondary" onClick={startDriveLink} disabled={!driveConfigured || cloudBusy}>
                      <Link2 size={17} aria-hidden="true" />
                      Generar nuevo codigo
                    </button>
                  ) : null}
                </div>
              ) : null}
            </div>
          ) : (
            <p className="import-muted">Cargando Google Drive...</p>
          )}
        </article>
      </section>

      {!loading && page === 1 && latestSuccessfulBackup ? (
        <section className="backup-summary-grid" aria-label="Resumen de la ultima copia correcta en esta pagina">
          <article className="backup-summary-card backup-summary-card--primary">
            <span>Ultima copia correcta en esta pagina</span>
            <strong>{formatDateTime(latestSuccessfulBackup.fecha_creacion)}</strong>
          </article>
          <article className="backup-summary-card">
            <span>Tamano</span>
            <strong>{formatBytes(latestSuccessfulBackup.tamanio_bytes)}</strong>
          </article>
          <article className="backup-summary-card">
            <span>Tipo</span>
            <strong>{formatTipoCopia(latestSuccessfulBackup.tipo)}</strong>
          </article>
          <article className="backup-summary-card">
            <span>Nube</span>
            <strong>{formatCloudEstado(latestSuccessfulBackup.cloud_estado)}</strong>
          </article>
        </section>
      ) : null}

      {!loading && page === 1 && rows.length > 0 && !latestSuccessfulBackup ? (
        <p className="config-note config-note--warning" role="status">No hay ninguna copia correcta en esta pagina.</p>
      ) : null}

      {driveConnected ? (
        <section className="backup-config-panel">
          <div className="backup-panel-heading">
            <h2>Copias en Google Drive</h2>
            <button type="button" className="button-secondary" onClick={fetchDriveFiles} disabled={driveFilesLoading}>
              <RefreshCcw size={17} aria-hidden="true" />
              {driveFilesLoading ? 'Cargando...' : 'Actualizar'}
            </button>
          </div>
          {driveFilesLoading ? <p className="import-muted">Cargando archivos de Drive...</p> : null}
          {!driveFilesLoading && driveFiles.length === 0 ? (
            <p className="import-muted">No hay copias creadas por Atlas Balance en esta cuenta.</p>
          ) : null}
          {!driveFilesLoading && driveFiles.length > 0 ? (
            <div className="backup-drive-files">
              {driveFiles.map((file) => (
                <article key={file.file_id} className="backup-drive-file">
                  <div>
                    <strong>{file.name}</strong>
                    <span>{file.created_time ? formatDateTime(file.created_time) : 'Sin fecha'} - {formatBytes(file.size_bytes)}</span>
                  </div>
                  <button
                    type="button"
                    className="button-secondary"
                    onClick={() => importDriveFile(file)}
                    disabled={Boolean(importingFileId)}
                  >
                    <DownloadCloud size={17} aria-hidden="true" />
                    {importingFileId === file.file_id ? 'Importando...' : 'Importar'}
                  </button>
                </article>
              ))}
            </div>
          ) : null}
        </section>
      ) : null}

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
                    <th>Tamano</th>
                    <th>Nube</th>
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
                      <td>
                        <span className={`backup-cloud-badge backup-cloud-badge--${(row.cloud_estado ?? 'local').toLowerCase()}`}>
                          {formatCloudEstado(row.cloud_estado)}
                        </span>
                        {row.cloud_error_message ? <small className="backup-cloud-error">{row.cloud_error_message}</small> : null}
                      </td>
                      <td><span className="backup-file-path" title={row.ruta_archivo}>{row.ruta_archivo}</span></td>
                      <td className="users-row-actions">
                        <button
                          type="button"
                          className="button-secondary"
                          onClick={() => retryDriveUpload(row)}
                          disabled={!driveConnected || config?.destination !== 'LOCAL_Y_GOOGLE_DRIVE' || row.estado !== 'SUCCESS' || retryingBackupId === row.id}
                          aria-label={`Subir copia del ${formatDateTime(row.fecha_creacion)} a Google Drive`}
                        >
                          <RotateCcw size={16} aria-hidden="true" />
                          {retryingBackupId === row.id ? 'Subiendo...' : 'Drive'}
                        </button>
                        <button
                          type="button"
                          className="button-danger"
                          onClick={() => {
                            setConfirmTarget(row);
                            setConfirmOpen(true);
                          }}
                          disabled={row.estado !== 'SUCCESS' || restoring}
                          aria-label={`Restaurar copia del ${formatDateTime(row.fecha_creacion)}`}
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
                Pagina {page} / {totalPages} - {totalRowsText}
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
        message={confirmTarget ? `Vas a restaurar la copia del ${formatDateTime(confirmTarget.fecha_creacion)}.` : 'Confirma que copia quieres restaurar.'}
        confirmLabel="Revisar restauracion"
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
        title="Ultima confirmacion"
        message="Esto reemplazara toda la base de datos y cerrara tu sesion. No sigas si no tienes claro que esta copia es la correcta."
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
          <div
            ref={restoreOverlayRef}
            className="loading-overlay"
            role="alertdialog"
            aria-live="assertive"
            aria-busy="true"
            aria-labelledby="backup-restore-overlay-title"
            aria-describedby="backup-restore-overlay-message"
            tabIndex={-1}
          >
            <h2 id="backup-restore-overlay-title">Restaurando copia de seguridad</h2>
            <p id="backup-restore-overlay-message">{overlayMessage || 'No cierres esta ventana; al terminar volveras al inicio de sesion.'}</p>
          </div>
        </div>
      ) : null}
      <ConfirmDialog {...confirmDialogProps} />
    </section>
  );
}
