import { useEffect, useState } from 'react';
import { EmptyState } from '@/components/common/EmptyState';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import api from '@/services/api';
import type { IntegrationTokenListItem, IntegrationTokenMetrics } from '@/types';
import { formatDateTime, formatNumber } from '@/utils/formatters';

type TokenMetricState =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'ready'; data: IntegrationTokenMetrics };

const tokenEstadoLabels: Record<string, string> = {
  activo: 'Activo',
  revocado: 'Revocado',
};

function formatTokenEstado(value: string) {
  return tokenEstadoLabels[value.toLowerCase()] ?? value;
}

interface TokenListProps {
  tokens: IntegrationTokenListItem[];
  busy: boolean;
  onRevocar: (id: string) => Promise<void>;
  onRotar: (id: string) => Promise<void>;
  onEliminar: (id: string) => Promise<void>;
}

export function TokenList({ tokens, busy, onRevocar, onRotar, onEliminar }: TokenListProps) {
  const [metrics, setMetrics] = useState<Record<string, TokenMetricState>>({});
  const [confirmTarget, setConfirmTarget] = useState<{ token: IntegrationTokenListItem; action: 'revocar' | 'eliminar' } | null>(null);

  useEffect(() => {
    let active = true;
    const load = async () => {
      if (tokens.length === 0) {
        setMetrics({});
        return;
      }

      setMetrics(Object.fromEntries(tokens.map((token) => [token.id, { status: 'loading' } satisfies TokenMetricState])));

      const entries = await Promise.all(
        tokens.map(async (token) => {
          try {
            const { data } = await api.get<IntegrationTokenMetrics>(`/integraciones/tokens/${token.id}/metricas`);
            return [token.id, { status: 'ready', data } satisfies TokenMetricState] as const;
          } catch {
            return [token.id, { status: 'error' } satisfies TokenMetricState] as const;
          }
        })
      );

      if (!active) {
        return;
      }

      setMetrics(Object.fromEntries(entries));
    };

    void load();
    return () => {
      active = false;
    };
  }, [tokens]);

  if (tokens.length === 0) {
    return (
      <EmptyState
        title="Aún no hay tokens de integración."
        subtitle="Crea un token para que OpenClaw acceda solo a los alcances permitidos."
      />
    );
  }

  return (
    <>
    <div className="users-table-scroll">
      <table>
        <thead>
          <tr>
            <th>Nombre</th>
            <th>Estado</th>
            <th>Expira</th>
            <th>Ultimo uso</th>
            <th>Scopes</th>
            <th>Peticiones</th>
            <th>Exito</th>
            <th>Tiempo medio (ms)</th>
            <th>Acciones</th>
          </tr>
        </thead>
        <tbody>
          {tokens.map((token) => {
            const metricState: TokenMetricState = metrics[token.id] ?? { status: 'loading' };
            const metricsUnavailable = metricState.status === 'error';
            return (
              <tr key={token.id}>
                <td>
                  <strong>{token.nombre}</strong>
                  <div className="import-muted">{token.descripcion || 'Sin descripción'}</div>
                </td>
                <td>{formatTokenEstado(token.estado)}</td>
                <td>{formatDateTime(token.fecha_creacion)}</td>
                <td>{token.fecha_expiracion ? formatDateTime(token.fecha_expiracion) : 'Sin expiracion'}</td>
                <td>
                  {token.fecha_ultima_uso ? formatDateTime(token.fecha_ultima_uso) : 'Sin uso'}
                  {token.last_used_ip_address ? <div className="import-muted">{token.last_used_ip_address}</div> : null}
                </td>
                <td>{token.scopes.length > 0 ? token.scopes.join(', ') : 'Legacy'}</td>
                <td>{metricState.status === 'ready' ? metricState.data.total_requests : metricsUnavailable ? 'No disponible' : 'Cargando'}</td>
                <td>{metricState.status === 'ready' ? `${formatNumber(metricState.data.porcentaje_exito)}%` : metricsUnavailable ? 'No disponible' : 'Cargando'}</td>
                <td>{metricState.status === 'ready' ? formatNumber(metricState.data.tiempo_promedio_ms) : metricsUnavailable ? 'No disponible' : 'Cargando'}</td>
                <td className="users-row-actions">
                  <button
                    type="button"
                    className="button-secondary"
                    onClick={() => void onRotar(token.id)}
                    disabled={busy || token.estado === 'revocado'}
                    aria-label={`Rotar token ${token.nombre}`}
                  >
                    Rotar
                  </button>
                  <button
                    type="button"
                    className="button-danger"
                    onClick={() => setConfirmTarget({ token, action: 'revocar' })}
                    disabled={busy || token.estado === 'revocado'}
                    aria-label={`Revocar token ${token.nombre}`}
                  >
                    Revocar token
                  </button>
                  <button
                    type="button"
                    className="button-danger"
                    onClick={() => setConfirmTarget({ token, action: 'eliminar' })}
                    disabled={busy}
                    aria-label={`Eliminar token ${token.nombre}`}
                  >
                    Eliminar token
                  </button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
    <ConfirmDialog
      open={confirmTarget !== null}
      title={confirmTarget?.action === 'revocar' ? 'Revocar token' : 'Eliminar token'}
      message={
        confirmTarget
          ? confirmTarget.action === 'revocar'
            ? `El token "${confirmTarget.token.nombre}" dejará de aceptar nuevas llamadas.`
            : `El token "${confirmTarget.token.nombre}" se moverá a papelera y no podrá usarse.`
          : ''
      }
      confirmLabel={confirmTarget?.action === 'revocar' ? 'Revocar token' : 'Eliminar token'}
      loadingLabel={confirmTarget?.action === 'revocar' ? 'Revocando...' : 'Eliminando...'}
      loading={busy}
      onCancel={() => setConfirmTarget(null)}
      onConfirm={async () => {
        if (!confirmTarget) return;
        const target = confirmTarget;
        if (target.action === 'revocar') {
          await onRevocar(target.token.id);
        } else {
          await onEliminar(target.token.id);
        }
        setConfirmTarget(null);
      }}
    />
    </>
  );
}
