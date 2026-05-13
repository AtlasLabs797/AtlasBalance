import { Fragment, useEffect, useMemo, useState } from 'react';
import { AppSelect } from '@/components/common/AppSelect';
import { DatePickerField } from '@/components/common/DatePickerField';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import { SignedAmount } from '@/components/common/SignedAmount';
import api from '@/services/api';
import { IntegrationAuditTable } from '@/components/auditoria/IntegrationAuditTable';
import type { AuditoriaFiltros, AuditoriaListItem, PaginatedResponse } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatDateTime } from '@/utils/formatters';

type ExpandedRowsState = Record<string, boolean>;
type AuditTab = 'sistema' | 'integraciones';

const pageSizeOptions = [25, 50, 100];

function mapColumnaNombre(columna: string | null): string {
  if (!columna) return 'Sin datos';

  const key = columna.trim().toLowerCase();
  if (key === 'fecha') return 'Fecha';
  if (key === 'concepto') return 'Concepto';
  if (key === 'monto') return 'Monto';
  if (key === 'saldo') return 'Saldo';
  if (key === 'checked') return 'Revisión';
  if (key === 'flagged') return 'Marca';
  if (key === 'flagged_nota') return 'Nota de marca';
  return columna;
}

function formatTimestamp(value: string): string {
  return formatDateTime(value);
}

function parseDetallesJson(raw: string | null): string | null {
  if (!raw) return null;
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

function isAmountColumn(columna: string | null): boolean {
  const key = columna?.trim().toLowerCase();
  return key === 'monto' || key === 'saldo';
}

export default function AuditoriaPage() {
  const [tab, setTab] = useState<AuditTab>('sistema');
  const [rows, setRows] = useState<AuditoriaListItem[]>([]);
  const [filtros, setFiltros] = useState<AuditoriaFiltros>({ usuarios: [], cuentas: [], tipos_accion: [] });
  const [expandedRows, setExpandedRows] = useState<ExpandedRowsState>({});
  const [loading, setLoading] = useState(false);
  const [loadingFiltros, setLoadingFiltros] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [usuarioId, setUsuarioId] = useState('');
  const [cuentaId, setCuentaId] = useState('');
  const [tipoAccion, setTipoAccion] = useState('');
  const [fechaDesde, setFechaDesde] = useState('');
  const [fechaHasta, setFechaHasta] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [totalPages, setTotalPages] = useState(1);

  const totalRowsText = useMemo(() => `${rows.length} registros en esta página`, [rows.length]);

  const fetchFiltros = async () => {
    setLoadingFiltros(true);
    try {
      const { data } = await api.get<AuditoriaFiltros>('/auditoria/filtros');
      setFiltros(data);
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudieron cargar filtros de auditoría'));
    } finally {
      setLoadingFiltros(false);
    }
  };

  const fetchRows = async () => {
    setLoading(true);
    setError(null);
    try {
      const { data } = await api.get<PaginatedResponse<AuditoriaListItem>>('/auditoria', {
        params: {
          page,
          pageSize,
          usuarioId: usuarioId || undefined,
          cuentaId: cuentaId || undefined,
          tipoAccion: tipoAccion || undefined,
          fechaDesde: fechaDesde || undefined,
          fechaHasta: fechaHasta || undefined,
        },
      });
      setRows(data.data ?? []);
      setTotalPages(Math.max(1, data.total_pages ?? 1));
      setExpandedRows({});
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudieron cargar registros de auditoría'));
      setRows([]);
      setTotalPages(1);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void fetchFiltros();
  }, []);

  useEffect(() => {
    if (tab === 'sistema') {
      void fetchRows();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- filtros del listado controlan la recarga
  }, [tab, page, pageSize, usuarioId, cuentaId, tipoAccion, fechaDesde, fechaHasta]);

  const toggleExpanded = (rowId: string) => {
    setExpandedRows((prev) => ({ ...prev, [rowId]: !prev[rowId] }));
  };

  const resetFiltros = () => {
    setUsuarioId('');
    setCuentaId('');
    setTipoAccion('');
    setFechaDesde('');
    setFechaHasta('');
    setPage(1);
  };

  const exportCsv = async () => {
    setExporting(true);
    setError(null);
    try {
      const response = await api.get('/auditoria/exportar-csv', {
        params: {
          usuarioId: usuarioId || undefined,
          cuentaId: cuentaId || undefined,
          tipoAccion: tipoAccion || undefined,
          fechaDesde: fechaDesde || undefined,
          fechaHasta: fechaHasta || undefined,
        },
        responseType: 'blob',
      });

      const blob = new Blob([response.data], { type: 'text/csv;charset=utf-8;' });
      const href = URL.createObjectURL(blob);
      const contentDisposition = response.headers?.['content-disposition'] as string | undefined;
      const filenameMatch = contentDisposition?.match(/filename="?([^";]+)"?/i);
      const filename = filenameMatch?.[1] ?? `auditoria_${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')}.csv`;

      const anchor = document.createElement('a');
      anchor.href = href;
      anchor.setAttribute('download', filename);
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      URL.revokeObjectURL(href);
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo exportar CSV'));
    } finally {
      setExporting(false);
    }
  };

  return (
    <section className="auditoria-page">
      <header className="auditoria-header">
        <div>
          <h1>Auditoría</h1>
          <p className="dashboard-subtitle">Historial completo con filtros combinados y exportación CSV</p>
        </div>
      </header>

      <div className="config-tabs">
        <button type="button" className={tab === 'sistema' ? 'config-tab config-tab--active' : 'config-tab'} onClick={() => setTab('sistema')}>
          Auditoría Sistema
        </button>
        <button type="button" className={tab === 'integraciones' ? 'config-tab config-tab--active' : 'config-tab'} onClick={() => setTab('integraciones')}>
          Auditoría Integraciones
        </button>
      </div>

      {tab === 'integraciones' ? <IntegrationAuditTable /> : null}

      {tab === 'sistema' ? (
        <>
          <div className="auditoria-header" style={{ marginTop: '12px' }}>
            <button type="button" onClick={exportCsv} disabled={exporting || loading}>
              {exporting ? 'Exportando...' : 'Exportar CSV'}
            </button>
          </div>

          <div className="auditoria-filtros">
            <AppSelect
              label="Usuario"
              value={usuarioId}
              disabled={loadingFiltros}
              options={[
                { value: '', label: 'Todos' },
                ...filtros.usuarios.map((usuario) => ({ value: usuario.id, label: usuario.nombre })),
              ]}
              onChange={(next) => {
                setUsuarioId(next);
                setPage(1);
              }}
            />

            <AppSelect
              label="Cuenta"
              value={cuentaId}
              disabled={loadingFiltros}
              options={[
                { value: '', label: 'Todas' },
                ...filtros.cuentas.map((cuenta) => ({
                  value: cuenta.id,
                  label: `${cuenta.titular_nombre} - ${cuenta.nombre}`,
                })),
              ]}
              onChange={(next) => {
                setCuentaId(next);
                setPage(1);
              }}
            />

            <AppSelect
              label="Tipo"
              value={tipoAccion}
              disabled={loadingFiltros}
              options={[
                { value: '', label: 'Todos' },
                ...filtros.tipos_accion.map((tipo) => ({ value: tipo, label: tipo })),
              ]}
              onChange={(next) => {
                setTipoAccion(next);
                setPage(1);
              }}
            />

            <div className="date-field">
              <span>Fecha desde</span>
              <DatePickerField
                ariaLabel="Fecha desde"
                value={fechaDesde}
                onChange={(next) => {
                  setFechaDesde(next);
                  setPage(1);
                }}
              />
            </div>

            <div className="date-field">
              <span>Fecha hasta</span>
              <DatePickerField
                ariaLabel="Fecha hasta"
                value={fechaHasta}
                onChange={(next) => {
                  setFechaHasta(next);
                  setPage(1);
                }}
              />
            </div>

            <button type="button" onClick={resetFiltros}>
              Limpiar filtros
            </button>
          </div>

          {error ? <p className="auth-error">{error}</p> : null}

          <div className="users-table-card auditoria-table-card">
            {loading ? <p className="import-muted">Cargando auditoría...</p> : null}
            {!loading && rows.length === 0 ? <EmptyState title="Sin registros para los filtros seleccionados." /> : null}
            {!loading && rows.length > 0 ? (
              <>
                <div className="users-table-scroll">
                  <table>
                    <thead>
                      <tr>
                        <th />
                        <th>Fecha</th>
                        <th>Usuario</th>
                        <th>Accion</th>
                        <th>Cuenta</th>
                        <th>Celda</th>
                        <th>Columna</th>
                        <th>IP</th>
                      </tr>
                    </thead>
                    <tbody>
                      {rows.map((row) => (
                        <Fragment key={row.id}>
                          <tr>
                            <td>
                              <button type="button" onClick={() => toggleExpanded(row.id)}>
                                {expandedRows[row.id] ? 'Ocultar' : 'Ver'}
                              </button>
                            </td>
                            <td>{formatTimestamp(row.timestamp)}</td>
                            <td>{row.usuario_nombre ?? 'Sistema'}</td>
                            <td>{row.tipo_accion}</td>
                            <td>{row.cuenta_nombre ? `${row.titular_nombre ?? 'Sin titular'} - ${row.cuenta_nombre}` : 'Sin cuenta'}</td>
                            <td>{row.celda_referencia ?? 'Sin celda'}</td>
                            <td>{mapColumnaNombre(row.columna_nombre)}</td>
                            <td>{row.ip_address ?? 'Sin IP'}</td>
                          </tr>

                          {expandedRows[row.id] ? (
                            <tr>
                              <td colSpan={8}>
                                <div className="auditoria-expanded">
                                  <div>
                                    <strong>Entidad:</strong> {row.entidad_tipo ?? 'Sin entidad'} {row.entidad_id ?? ''}
                                  </div>
                                  <div>
                                    <strong>Valor anterior:</strong>{' '}
                                    {isAmountColumn(row.columna_nombre) && row.valor_anterior !== null ? (
                                      <SignedAmount value={row.valor_anterior}>{row.valor_anterior}</SignedAmount>
                                    ) : (
                                      row.valor_anterior ?? 'Sin valor'
                                    )}
                                  </div>
                                  <div>
                                    <strong>Valor nuevo:</strong>{' '}
                                    {isAmountColumn(row.columna_nombre) && row.valor_nuevo !== null ? (
                                      <SignedAmount value={row.valor_nuevo}>{row.valor_nuevo}</SignedAmount>
                                    ) : (
                                      row.valor_nuevo ?? 'Sin valor'
                                    )}
                                  </div>
                                  <div>
                                    <strong>Referencia legible:</strong>{' '}
                                    {row.celda_referencia ? `${row.celda_referencia} (${mapColumnaNombre(row.columna_nombre)})` : 'Sin referencia'}
                                  </div>
                                  {row.detalles_json ? (
                                    <div>
                                      <strong>Detalles JSON:</strong>
                                      <pre>{parseDetallesJson(row.detalles_json)}</pre>
                                    </div>
                                  ) : null}
                                </div>
                              </td>
                            </tr>
                          ) : null}
                        </Fragment>
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
        </>
      ) : null}
    </section>
  );
}
