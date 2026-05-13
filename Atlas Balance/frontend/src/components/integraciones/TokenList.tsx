import { useEffect, useState } from 'react';
import { EmptyState } from '@/components/common/EmptyState';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import api from '@/services/api';
import type { IntegrationTokenListItem, IntegrationTokenMetrics } from '@/types';
import { formatDateTime, formatNumber } from '@/utils/formatters';

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
  onEliminar: (id: string) => Promise<void>;
}

export function TokenList({ tokens, busy, onRevocar, onEliminar }: TokenListProps) {
  const [metrics, setMetrics] = useState<Record<string, IntegrationTokenMetrics>>({});
  const [confirmTarget, setConfirmTarget] = useState<{ token: IntegrationTokenListItem; action: 'revocar' | 'eliminar' } | null>(null);

  useEffect(() => {
    let active = true;
    const load = async () => {
      if (tokens.length === 0) {
        setMetrics({});
        return;
      }

      const entries = await Promise.all(
        tokens.map(async (token) => {
          try {
            const { data } = await api.get<IntegrationTokenMetrics>(`/integraciones/tokens/${token.id}/metricas`);
            return [token.id, data] as const;
          } catch {
            return [token.id, { total_requests: 0, porcentaje_exito: 0, tiempo_promedio_ms: 0 }] as const;
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
            <th>Creación</th>
            <th>Último uso</th>
            <th>Peticiones</th>
            <th>Éxito</th>
            <th>Tiempo medio (ms)</th>
            <th>Acciones</th>
          </tr>
        </thead>
        <tbody>
          {tokens.map((token) => {
            const m = metrics[token.id] ?? { total_requests: 0, porcentaje_exito: 0, tiempo_promedio_ms: 0 };
            return (
              <tr key={token.id}>
                <td>
                  <strong>{token.nombre}</strong>
                  <div className="import-muted">{token.descripcion || 'Sin descripción'}</div>
                </td>
                <td>{formatTokenEstado(token.estado)}</td>
                <td>{formatDateTime(token.fecha_creacion)}</td>
                <td>{token.fecha_ultima_uso ? formatDateTime(token.fecha_ultima_uso) : 'Sin uso'}</td>
                <td>{m.total_requests}</td>
                <td>{formatNumber(m.porcentaje_exito)}%</td>
                <td>{formatNumber(m.tiempo_promedio_ms)}</td>
                <td className="users-row-actions">
                  <button
                    type="button"
                    onClick={() => setConfirmTarget({ token, action: 'revocar' })}
                    disabled={busy || token.estado === 'revocado'}
                    aria-label={`Revocar token ${token.nombre}`}
                  >
                    Revocar token
                  </button>
                  <button
                    type="button"
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
