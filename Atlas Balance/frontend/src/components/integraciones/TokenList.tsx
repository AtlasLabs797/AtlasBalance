import { useEffect, useState } from 'react';
import api from '@/services/api';
import type { IntegrationTokenListItem, IntegrationTokenMetrics } from '@/types';

interface TokenListProps {
  tokens: IntegrationTokenListItem[];
  busy: boolean;
  onRevocar: (id: string) => Promise<void>;
  onEliminar: (id: string) => Promise<void>;
}

export function TokenList({ tokens, busy, onRevocar, onEliminar }: TokenListProps) {
  const [metrics, setMetrics] = useState<Record<string, IntegrationTokenMetrics>>({});

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
    return <p>No hay tokens.</p>;
  }

  return (
    <div className="users-table-scroll">
      <table>
        <thead>
          <tr>
            <th>Nombre</th>
            <th>Estado</th>
            <th>Creación</th>
            <th>Último uso</th>
            <th>Total req</th>
            <th>% éxito</th>
            <th>Avg ms</th>
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
                <td>{token.estado}</td>
                <td>{new Date(token.fecha_creacion).toLocaleString('es-ES')}</td>
                <td>{token.fecha_ultima_uso ? new Date(token.fecha_ultima_uso).toLocaleString('es-ES') : 'Sin uso'}</td>
                <td>{m.total_requests}</td>
                <td>{m.porcentaje_exito.toFixed(2)}%</td>
                <td>{m.tiempo_promedio_ms.toFixed(2)}</td>
                <td className="users-row-actions">
                  <button type="button" onClick={() => void onRevocar(token.id)} disabled={busy || token.estado === 'revocado'}>
                    Revocar
                  </button>
                  <button type="button" onClick={() => void onEliminar(token.id)} disabled={busy}>
                    Eliminar
                  </button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
