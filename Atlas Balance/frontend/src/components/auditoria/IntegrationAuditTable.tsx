import { useCallback, useEffect, useState } from 'react';
import { AppSelect } from '@/components/common/AppSelect';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import api from '@/services/api';
import type { IntegrationAuditItem, IntegrationTokenListItem, PaginatedResponse } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatDateTime } from '@/utils/formatters';

const pageSizeOptions = [25, 50, 100];
const EMPTY_VALUE = 'Sin dato';

export function IntegrationAuditTable() {
  const [rows, setRows] = useState<IntegrationAuditItem[]>([]);
  const [tokens, setTokens] = useState<IntegrationTokenListItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [tokenId, setTokenId] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [totalPages, setTotalPages] = useState(1);

  const loadTokens = useCallback(async () => {
    const { data } = await api.get<PaginatedResponse<IntegrationTokenListItem>>('/integraciones/tokens', {
      params: { page: 1, pageSize: 200, incluirEliminados: true },
    });
    setTokens(data.data ?? []);
  }, []);

  const loadRows = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const { data } = await api.get<PaginatedResponse<IntegrationAuditItem>>('/integraciones/tokens/auditoria', {
        params: {
          page,
          pageSize,
          tokenId: tokenId || undefined,
        },
      });
      setRows(data.data ?? []);
      setTotalPages(Math.max(1, data.total_pages ?? 1));
    } catch (err) {
      setRows([]);
      setTotalPages(1);
      setError(extractErrorMessage(err, 'No se pudo cargar auditoría de integración.'));
    } finally {
      setLoading(false);
    }
  }, [page, pageSize, tokenId]);

  useEffect(() => {
    void loadTokens();
  }, [loadTokens]);

  useEffect(() => {
    void loadRows();
  }, [loadRows]);

  return (
    <section className="config-card">
      <h2>Auditoría de Integraciones</h2>
      <div className="auditoria-filtros">
        <AppSelect
          label="Token"
          value={tokenId}
          options={[
            { value: '', label: 'Todos' },
            ...tokens.map((token) => ({ value: token.id, label: token.nombre })),
          ]}
          onChange={(next) => {
            setTokenId(next);
            setPage(1);
          }}
        />
      </div>
      {error ? <p className="auth-error">{error}</p> : null}
      <div className="users-table-card auditoria-table-card">
        {loading ? <p>Cargando auditoría de integraciones...</p> : null}
        {!loading && rows.length === 0 ? (
          <EmptyState
            title="No hay llamadas de integración con estos filtros."
            subtitle="Cuando OpenClaw use un token, sus accesos aparecerán aquí."
          />
        ) : null}
        {!loading && rows.length > 0 ? (
          <>
            <div className="users-table-scroll">
              <table>
                <thead>
                  <tr>
                    <th>Fecha</th>
                    <th>Token</th>
                    <th>Método</th>
                    <th>Endpoint</th>
                    <th>Código</th>
                    <th>Tiempo (ms)</th>
                    <th>IP</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row) => (
                    <tr key={row.id}>
                      <td>{formatDateTime(row.timestamp)}</td>
                      <td>{row.token_nombre ?? row.token_id}</td>
                      <td>{row.metodo}</td>
                      <td>{row.endpoint}</td>
                      <td>{row.codigo_respuesta ?? EMPTY_VALUE}</td>
                      <td>{row.tiempo_ejecucion_ms ?? EMPTY_VALUE}</td>
                      <td>{row.ip_address ?? EMPTY_VALUE}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="users-pagination">
              <button type="button" onClick={() => setPage((prev) => Math.max(1, prev - 1))} disabled={page <= 1}>
                Anterior
              </button>
              <span>Página {page} / {totalPages}</span>
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
