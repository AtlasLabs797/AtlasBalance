import { useEffect, useMemo, useState } from 'react';
import { EmptyState } from '@/components/common/EmptyState';
import { SignedAmount } from '@/components/common/SignedAmount';
import api from '@/services/api';
import type { PaginatedResponse } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatCurrency, formatDate as formatDateOnly, formatDateTime } from '@/utils/formatters';

type TabKey = 'titulares' | 'cuentas' | 'extractos' | 'usuarios';

interface TrashRow {
  id: string;
  titulo: string;
  subtitulo: string;
  monto?: number;
  deleted_at: string;
}

interface TitularTrashApiRow {
  id: string;
  nombre: string;
  tipo: string;
  deleted_at: string | null;
}

interface CuentaTrashApiRow {
  id: string;
  nombre: string;
  titular_nombre: string;
  divisa: string;
  deleted_at: string | null;
}

interface ExtractoTrashApiRow {
  id: string;
  cuenta_nombre: string | null;
  fila_numero: number;
  fecha: string;
  concepto: string | null;
  monto: number;
  deleted_at: string | null;
}

interface UsuarioTrashApiRow {
  id: string;
  nombre_completo: string;
  email: string;
  rol: string;
  deleted_at: string | null;
}

const tabs: Array<{ key: TabKey; label: string }> = [
  { key: 'titulares', label: 'Titulares' },
  { key: 'cuentas', label: 'Cuentas' },
  { key: 'extractos', label: 'Extractos' },
  { key: 'usuarios', label: 'Usuarios' },
];

const singularLabels: Record<TabKey, string> = {
  titulares: 'Titular',
  cuentas: 'Cuenta',
  extractos: 'Extracto',
  usuarios: 'Usuario',
};

const restorePaths: Record<TabKey, string> = {
  titulares: '/titulares',
  cuentas: '/cuentas',
  extractos: '/extractos',
  usuarios: '/usuarios',
};

function formatDate(value: string) {
  return formatDateTime(value);
}

export default function PapeleraPage() {
  const [tab, setTab] = useState<TabKey>('titulares');
  const [loading, setLoading] = useState(false);
  const [restoringId, setRestoringId] = useState<string | null>(null);
  const [rows, setRows] = useState<TrashRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const title = useMemo(() => tabs.find((item) => item.key === tab)?.label ?? '', [tab]);

  const load = async () => {
    setLoading(true);
    setError(null);
    setMessage(null);
    try {
      if (tab === 'titulares') {
        const { data } = await api.get<PaginatedResponse<TitularTrashApiRow>>('/titulares', {
          params: { incluirEliminados: true, page: 1, pageSize: 500, sortBy: 'fecha_creacion', sortDir: 'desc' },
        });
        const filtered = (data.data ?? [])
          .filter((row) => !!row.deleted_at)
          .map((row) => ({
            id: row.id,
            titulo: row.nombre,
            subtitulo: row.tipo,
            deleted_at: row.deleted_at ?? '',
          }));
        setRows(filtered);
        return;
      }

      if (tab === 'cuentas') {
        const { data } = await api.get<PaginatedResponse<CuentaTrashApiRow>>('/cuentas', {
          params: { incluirEliminados: true, page: 1, pageSize: 500, sortBy: 'fecha_creacion', sortDir: 'desc' },
        });
        const filtered = (data.data ?? [])
          .filter((row) => !!row.deleted_at)
          .map((row) => ({
            id: row.id,
            titulo: row.nombre,
            subtitulo: `${row.titular_nombre} · ${row.divisa}`,
            deleted_at: row.deleted_at ?? '',
          }));
        setRows(filtered);
        return;
      }

      if (tab === 'extractos') {
        const { data } = await api.get<PaginatedResponse<ExtractoTrashApiRow>>('/extractos', {
          params: { incluirEliminados: true, page: 1, pageSize: 500, sortBy: 'fecha', sortDir: 'desc' },
        });
        const filtered = (data.data ?? [])
          .filter((row) => !!row.deleted_at)
          .map((row) => ({
            id: row.id,
            titulo: `${row.cuenta_nombre ?? 'Cuenta'} · fila ${row.fila_numero}`,
            subtitulo: `${formatDateOnly(row.fecha)} · ${row.concepto ?? 'Sin concepto'}`,
            monto: row.monto,
            deleted_at: row.deleted_at ?? '',
          }));
        setRows(filtered);
        return;
      }

      const { data } = await api.get<PaginatedResponse<UsuarioTrashApiRow>>('/usuarios', {
        params: { incluirEliminados: true, page: 1, pageSize: 500, sortBy: 'fecha_creacion', sortDir: 'desc' },
      });
      const filtered = (data.data ?? [])
        .filter((row) => !!row.deleted_at)
        .map((row) => ({
          id: row.id,
          titulo: row.nombre_completo,
          subtitulo: `${row.email} · ${row.rol}`,
          deleted_at: row.deleted_at ?? '',
        }));
      setRows(filtered);
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo cargar la papelera.'));
      setRows([]);
    } finally {
      setLoading(false);
    }
  };

  const restore = async (id: string) => {
    setRestoringId(id);
    setError(null);
    setMessage(null);
    try {
      await api.post(`${restorePaths[tab]}/${id}/restaurar`);
      setMessage(`${singularLabels[tab]} restaurado correctamente.`);
      await load();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo restaurar.'));
    } finally {
      setRestoringId(null);
    }
  };

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- solo depende del tab activo
  }, [tab]);

  return (
    <section className="trash-page">
      <header className="trash-header">
        <div>
          <h1>Papelera</h1>
          <p className="dashboard-subtitle">Entidades eliminadas con restauración y auditoría.</p>
        </div>
      </header>

      <div className="config-tabs">
        {tabs.map((item) => (
          <button
            key={item.key}
            type="button"
            className={tab === item.key ? 'config-tab config-tab--active' : 'config-tab'}
            onClick={() => setTab(item.key)}
          >
            {item.label}
          </button>
        ))}
      </div>

      {error ? <p className="auth-error">{error}</p> : null}
      {message ? <p className="config-feedback">{message}</p> : null}

      <div className="users-table-card">
        {loading ? <p className="import-muted">Cargando {title.toLowerCase()} eliminados...</p> : null}
        {!loading && rows.length === 0 ? <EmptyState title={`No hay registros eliminados en ${title.toLowerCase()}.`} /> : null}
        {!loading && rows.length > 0 ? (
          <div className="users-table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Elemento</th>
                  <th>Detalle</th>
                  <th>Eliminado en</th>
                  <th>Acciones</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.id}>
                    <td>{row.titulo}</td>
                    <td>
                      {row.monto !== undefined ? (
                        <>
                          {row.subtitulo} · <SignedAmount value={row.monto}>{formatCurrency(row.monto, 'EUR')}</SignedAmount>
                        </>
                      ) : (
                        row.subtitulo
                      )}
                    </td>
                    <td>{formatDate(row.deleted_at)}</td>
                    <td className="users-row-actions">
                      <button
                        type="button"
                        onClick={() => void restore(row.id)}
                        disabled={restoringId === row.id}
                      >
                        {restoringId === row.id ? 'Restaurando...' : 'Restaurar'}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </div>
    </section>
  );
}
