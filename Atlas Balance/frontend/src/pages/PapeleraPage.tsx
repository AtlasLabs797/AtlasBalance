import { useEffect, useMemo, useRef, useState } from 'react';
import { EmptyState } from '@/components/common/EmptyState';
import { SignedAmount } from '@/components/common/SignedAmount';
import api from '@/services/api';
import type { PaginatedResponse } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatCurrency, formatDate as formatDateOnly, formatDateTime } from '@/utils/formatters';
import { crearSecuenciaPeticion, type SecuenciaPeticion } from '@/utils/peticionSecuencial';

type TabKey = 'titulares' | 'cuentas' | 'extractos' | 'usuarios';

// F-2: la papelera pide una pagina real al backend (no 500 filas) y deja que
// el backend filtre solo lo eliminado.
const PAGE_SIZE = 50;

interface PapeleraResultado {
  rows: TrashRow[];
  total: number;
  totalPages: number;
}

// F-2: la peticion se hace fuera del componente y devuelve el resultado ya
// normalizado. Asi el estado se escribe en un unico sitio, despues de
// comprobar que la respuesta sigue siendo la vigente.
async function fetchPapelera(tab: TabKey, page: number): Promise<PapeleraResultado> {
  if (tab === 'titulares') {
    const { data } = await api.get<PaginatedResponse<TitularTrashApiRow>>('/titulares', {
      params: { soloEliminados: true, page, pageSize: PAGE_SIZE, sortBy: 'fecha_creacion', sortDir: 'desc' },
    });
    return {
      rows: (data.data ?? []).map((row) => ({
        id: row.id,
        titulo: row.nombre,
        subtitulo: row.tipo,
        deleted_at: row.deleted_at ?? '',
      })),
      total: data.total ?? 0,
      totalPages: Math.max(data.total_pages ?? 1, 1),
    };
  }

  if (tab === 'cuentas') {
    const { data } = await api.get<PaginatedResponse<CuentaTrashApiRow>>('/cuentas', {
      params: { soloEliminados: true, page, pageSize: PAGE_SIZE, sortBy: 'fecha_creacion', sortDir: 'desc' },
    });
    return {
      rows: (data.data ?? []).map((row) => ({
        id: row.id,
        titulo: row.nombre,
        subtitulo: `${row.titular_nombre} · ${row.divisa}`,
        deleted_at: row.deleted_at ?? '',
      })),
      total: data.total ?? 0,
      totalPages: Math.max(data.total_pages ?? 1, 1),
    };
  }

  if (tab === 'extractos') {
    const { data } = await api.get<PaginatedResponse<ExtractoTrashApiRow>>('/extractos', {
      params: { soloEliminados: true, page, pageSize: PAGE_SIZE, sortBy: 'fecha', sortDir: 'desc' },
    });
    return {
      rows: (data.data ?? []).map((row) => ({
        id: row.id,
        titulo: `${row.cuenta_nombre ?? 'Cuenta'} · fila ${row.fila_numero}`,
        subtitulo: `${formatDateOnly(row.fecha)} · ${row.concepto ?? 'Sin concepto'}`,
        monto: row.monto,
        divisa: row.divisa,
        deleted_at: row.deleted_at ?? '',
      })),
      total: data.total ?? 0,
      totalPages: Math.max(data.total_pages ?? 1, 1),
    };
  }

  const { data } = await api.get<PaginatedResponse<UsuarioTrashApiRow>>('/usuarios', {
    params: { soloEliminados: true, page, pageSize: PAGE_SIZE, sortBy: 'fecha_creacion', sortDir: 'desc' },
  });
  return {
    rows: (data.data ?? []).map((row) => ({
      id: row.id,
      titulo: row.nombre_completo,
      subtitulo: `${row.email} · ${row.rol}`,
      deleted_at: row.deleted_at ?? '',
    })),
    total: data.total ?? 0,
    totalPages: Math.max(data.total_pages ?? 1, 1),
  };
}

interface TrashRow {
  id: string;
  titulo: string;
  subtitulo: string;
  monto?: number;
  divisa?: string;
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
  divisa: string;
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

export default function PapeleraPage() {
  const [tab, setTab] = useState<TabKey>('titulares');
  const [loading, setLoading] = useState(false);
  const [restoringId, setRestoringId] = useState<string | null>(null);
  const [rows, setRows] = useState<TrashRow[]>([]);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const title = useMemo(() => tabs.find((item) => item.key === tab)?.label ?? '', [tab]);

  // F-2: la secuencia descarta las respuestas fuera de orden. Si el usuario
  // cambia de pestaña o de pagina mientras una peticion esta en vuelo, la
  // respuesta vieja no debe pisar la nueva ni apagar su estado de carga.
  const secuenciaRef = useRef<SecuenciaPeticion>(crearSecuenciaPeticion());

  const load = async () => {
    const secuencia = secuenciaRef.current.siguiente();
    setLoading(true);
    setError(null);
    setMessage(null);
    try {
      const resultado = await fetchPapelera(tab, page);
      if (!secuenciaRef.current.esActual(secuencia)) {
        return;
      }
      setRows(resultado.rows);
      setTotal(resultado.total);
      setTotalPages(resultado.totalPages);
    } catch (err) {
      if (!secuenciaRef.current.esActual(secuencia)) {
        return;
      }
      setError(extractErrorMessage(err, 'No se pudo cargar la papelera.'));
      setRows([]);
      setTotal(0);
      setTotalPages(1);
    } finally {
      if (secuenciaRef.current.esActual(secuencia)) {
        setLoading(false);
      }
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
    // eslint-disable-next-line react-hooks/exhaustive-deps -- depende del tab activo y de la pagina
  }, [tab, page]);

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
            onClick={() => {
              setPage(1);
              setTab(item.key);
            }}
          >
            {item.label}
          </button>
        ))}
      </div>

      {error ? <p className="auth-error" role="alert">{error}</p> : null}
      {message ? <p className="config-feedback">{message}</p> : null}

      <div className="users-table-card">
        {loading ? <p className="import-muted">Cargando {title.toLowerCase()} eliminados...</p> : null}
        {!loading && rows.length === 0 ? (
          <EmptyState
            title={`No hay registros eliminados en ${title.toLowerCase()}.`}
            subtitle="Cuando elimines una entidad de este tipo aparecerá aquí para restaurarla o purgarla."
          />
        ) : null}
        {!loading && rows.length > 0 ? (
          <div className="users-table-scroll" role="region" tabIndex={0} aria-label="Tabla de elementos eliminados">
            <table className="table-stack-mobile">
              <thead>
                <tr>
                  <th scope="col">Elemento</th>
                  <th scope="col">Detalle</th>
                  <th scope="col">Eliminado en</th>
                  <th scope="col">Acciones</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.id}>
                    <td data-label="Elemento">{row.titulo}</td>
                    <td data-label="Detalle">
                      {row.monto !== undefined ? (
                        <>
                          {row.subtitulo} · <SignedAmount value={row.monto}>{formatCurrency(row.monto, row.divisa ?? 'EUR')}</SignedAmount>
                        </>
                      ) : (
                        row.subtitulo
                      )}
                    </td>
                    <td data-label="Eliminado en">{formatDateTime(row.deleted_at)}</td>
                    <td className="users-row-actions" data-label="Acciones">
                      <button
                        type="button"
                        onClick={() => void restore(row.id)}
                        disabled={restoringId === row.id}
                        aria-label={`Restaurar ${row.titulo}`}
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

        <div className="users-pagination">
          <button
            type="button"
            onClick={() => setPage((current) => Math.max(1, current - 1))}
            disabled={loading || page <= 1}
          >
            Anterior
          </button>
          <span>
            Página {page} / {totalPages} · {total} eliminados
          </span>
          <button
            type="button"
            onClick={() => setPage((current) => Math.min(totalPages, current + 1))}
            disabled={loading || page >= totalPages}
          >
            Siguiente
          </button>
        </div>
      </div>
    </section>
  );
}
