import { useCallback, useEffect, useState } from 'react';
import { ShieldCheck } from 'lucide-react';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import { useConfirmDialog } from '@/hooks/useConfirmDialog';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import api from '@/services/api';
import { useUiStore } from '@/stores/uiStore';
import type { TrustedMfaDevice } from '@/types';
import {
  estaRevocado,
  etiquetaDispositivo,
  etiquetaIp,
  resumenVencimiento,
  textoConfirmacionRevocar,
} from '@/utils/dispositivosMfa';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatDateTime } from '@/utils/formatters';

// V-03.02 (LOTE 6, seccion 8.1 del informe de revision integral): pantalla
// para ver y revocar los dispositivos MFA de confianza del usuario.
// GET    /auth/mfa/trusted-devices      -> lista de dispositivos
// DELETE /auth/mfa/trusted-devices/{id} -> revoca un dispositivo concreto
// DELETE /auth/mfa/trusted-devices      -> revoca el dispositivo actual

export default function DispositivosMfaPage() {
  useDocumentTitle('Dispositivos MFA de confianza');
  const addToast = useUiStore((state) => state.addToast);
  const [devices, setDevices] = useState<TrustedMfaDevice[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [revokingId, setRevokingId] = useState<string | null>(null);
  const { confirm, dialogProps: confirmDialogProps } = useConfirmDialog();

  const loadDevices = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const { data } = await api.get<TrustedMfaDevice[]>('/auth/mfa/trusted-devices');
      setDevices(data ?? []);
    } catch (err: unknown) {
      setError(extractErrorMessage(err, 'No se pudieron cargar los dispositivos MFA de confianza.'));
      setDevices([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadDevices();
  }, [loadDevices]);

  const revokeDevice = async (device: TrustedMfaDevice) => {
    const confirmed = await confirm({
      title: 'Revocar dispositivo de confianza',
      message: textoConfirmacionRevocar(device),
      confirmLabel: 'Revocar dispositivo',
      tone: 'reversible',
    });
    if (!confirmed) {
      return;
    }

    setRevokingId(device.id);
    try {
      await api.delete(`/auth/mfa/trusted-devices/${device.id}`);
      addToast({ type: 'success', message: 'Dispositivo MFA de confianza revocado.' });
      await loadDevices();
    } catch (err: unknown) {
      addToast({
        type: 'error',
        message: extractErrorMessage(err, 'No se pudo revocar el dispositivo MFA de confianza.'),
      });
    } finally {
      setRevokingId(null);
    }
  };

  const revokeCurrentDevice = async () => {
    const current = devices.find((device) => device.current);
    const confirmed = await confirm({
      title: 'Revocar este dispositivo',
      message: current
        ? textoConfirmacionRevocar(current)
        : 'Se revocará la confianza MFA de este dispositivo. La próxima vez que entres tendrás que introducir de nuevo el código MFA.',
      confirmLabel: 'Revocar este dispositivo',
      tone: 'reversible',
    });
    if (!confirmed) {
      return;
    }

    setRevokingId('current');
    try {
      await api.delete('/auth/mfa/trusted-devices');
      addToast({ type: 'success', message: 'La confianza MFA de este dispositivo se ha revocado.' });
      await loadDevices();
    } catch (err: unknown) {
      addToast({
        type: 'error',
        message: extractErrorMessage(err, 'No se pudo revocar la confianza MFA de este dispositivo.'),
      });
    } finally {
      setRevokingId(null);
    }
  };

  const hasCurrentDevice = devices.some((device) => device.current);

  return (
    <section className="users-page page">
      <header className="users-header page-header">
        <div className="page-header__lead">
          <h1 className="page-header__title">Dispositivos MFA de confianza</h1>
          <p className="users-subtitle page-header__subtitle">
            Equipos desde los que ya has validado el código MFA y en los que no te lo volverán a pedir.
          </p>
        </div>
        <div className="users-actions">
          {hasCurrentDevice ? (
            <button
              type="button"
              className="button-danger"
              onClick={() => void revokeCurrentDevice()}
              disabled={revokingId !== null}
            >
              {revokingId === 'current' ? 'Revocando...' : 'Revocar este dispositivo'}
            </button>
          ) : null}
        </div>
      </header>

      <p className="import-muted">
        Un dispositivo de confianza es un navegador o equipo al que diste permiso para saltarte el código MFA.
        La confianza caduca en la fecha que se indica en la tabla (por defecto, 90 días). Si revocas un
        dispositivo, la próxima vez que se acceda a Atlas Balance desde él habrá que introducir de nuevo el
        código MFA.
      </p>

      {error ? (
        <EmptyState
          headingLevel="h2"
          variant="error"
          title="No se pudieron cargar los dispositivos MFA de confianza."
          subtitle={error}
          primaryAction={
            <button type="button" className="button-primary" onClick={() => void loadDevices()}>
              Reintentar
            </button>
          }
        />
      ) : null}

      {!error && loading ? <PageSkeleton rows={4} /> : null}

      {!error && !loading && devices.length === 0 ? (
        <EmptyState
          headingLevel="h2"
          icon={<ShieldCheck size={24} aria-hidden="true" />}
          title="No hay dispositivos MFA de confianza"
          subtitle="Cuando marques 'Recordar este dispositivo durante N días' al entrar con tu código MFA, aparecerá aquí y podrás revocarlo cuando quieras."
        />
      ) : null}

      {!error && !loading && devices.length > 0 ? (
        <div className="users-table-card">
          <div className="users-table-scroll" role="region" tabIndex={0} aria-label="Tabla de dispositivos MFA de confianza">
            <table>
              <caption className="sr-only">Dispositivos MFA de confianza</caption>
              <thead>
                <tr>
                  <th scope="col">Dispositivo</th>
                  <th scope="col">IP</th>
                  <th scope="col">Alta</th>
                  <th scope="col">Último uso</th>
                  <th scope="col">Expira</th>
                  <th scope="col">Acciones</th>
                </tr>
              </thead>
              <tbody>
                {devices.map((device) => (
                  <tr key={device.id}>
                    <td>
                      {etiquetaDispositivo(device)}
                      {device.current ? (
                        <>
                          {' '}
                          <span className="users-badge users-badge--ok">Este dispositivo</span>
                        </>
                      ) : null}
                      {estaRevocado(device) ? (
                        <>
                          {' '}
                          <span className="users-badge users-badge--danger">Revocado</span>
                        </>
                      ) : null}
                    </td>
                    <td>{etiquetaIp(device)}</td>
                    <td>{formatDateTime(device.created_at)}</td>
                    <td>{device.last_used_at ? formatDateTime(device.last_used_at) : 'Nunca'}</td>
                    <td>{estaRevocado(device) ? '—' : resumenVencimiento(device.expires_at)}</td>
                    <td className="users-row-actions">
                      {estaRevocado(device) ? null : (
                        <button
                          type="button"
                          className="button-danger"
                          onClick={() => void revokeDevice(device)}
                          disabled={revokingId !== null}
                          aria-label={`Revocar la confianza MFA de ${etiquetaDispositivo(device)}`}
                        >
                          {revokingId === device.id ? 'Revocando...' : 'Revocar'}
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      ) : null}

      <ConfirmDialog {...confirmDialogProps} />
    </section>
  );
}
