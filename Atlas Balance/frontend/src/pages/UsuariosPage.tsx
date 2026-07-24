import { useEffect, useMemo, useState } from 'react';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import { useDialogFocus } from '@/hooks/useDialogFocus';
import UsuarioModal, {
  type CatalogCuenta,
  type CatalogPais,
  type CatalogTitular,
} from '@/components/usuarios/UsuarioModal';
import api from '@/services/api';
import { extractErrorMessage } from '@/utils/errorMessage';

interface UsuarioRow {
  id: string;
  email: string;
  nombre_completo: string;
  rol: 'ADMIN' | 'GERENTE' | 'EMPLEADO';
  activo: boolean;
  primer_login: boolean;
  puede_usar_ia: boolean;
  mfa_enabled: boolean;
  mfa_required: boolean;
  deleted_at: string | null;
}

function mfaLabel(row: UsuarioRow): string {
  if (row.mfa_required && row.mfa_enabled) {
    return 'Obligatorio · configurado';
  }
  if (row.mfa_required && !row.mfa_enabled) {
    return 'Obligatorio · pendiente';
  }
  if (!row.mfa_required && row.mfa_enabled) {
    return 'Opcional · configurado';
  }
  return 'No requerido';
}

function mfaRequiredForCandidate(rows: UsuarioRow[], id: string): boolean {
  return rows.some((row) => row.id === id && row.mfa_required);
}

interface DeleteCandidate {
  id: string;
  email: string;
}

const rolLabels: Record<UsuarioRow['rol'], string> = {
  ADMIN: 'Administrador',
  GERENTE: 'Gerente',
  EMPLEADO: 'Empleado',
};

export default function UsuariosPage() {
  const [rows, setRows] = useState<UsuarioRow[]>([]);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [totalPages, setTotalPages] = useState(1);
  const [total, setTotal] = useState(0);
  const [search, setSearch] = useState('');
  const [incluirEliminados, setIncluirEliminados] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [titulares, setTitulares] = useState<CatalogTitular[]>([]);
  const [cuentas, setCuentas] = useState<CatalogCuenta[]>([]);
  const [paises, setPaises] = useState<CatalogPais[]>([]);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [deleteCandidate, setDeleteCandidate] = useState<DeleteCandidate | null>(null);
  const [mfaCandidate, setMfaCandidate] = useState<DeleteCandidate | null>(null);
  const [actionLoading, setActionLoading] = useState(false);
  const deleteDialogRef = useDialogFocus<HTMLDivElement>(Boolean(deleteCandidate), {
    onEscape: actionLoading ? undefined : () => setDeleteCandidate(null),
  });
  const mfaDialogRef = useDialogFocus<HTMLDivElement>(Boolean(mfaCandidate), {
    onEscape: actionLoading ? undefined : () => setMfaCandidate(null),
  });

  const activeCount = useMemo(
    () => rows.filter((row) => !row.deleted_at && row.activo).length,
    [rows]
  );

  const loadCatalogs = async () => {
    try {
      const { data } = await api.get('/usuarios/catalogos-permisos');
      setTitulares(data.titulares ?? []);
      setCuentas(data.cuentas ?? []);
      setPaises(data.paises ?? []);
    } catch {
      // no-op
    }
  };

  const loadData = async () => {
    setLoading(true);
    setError(null);

    try {
      const { data } = await api.get('/usuarios', {
        params: {
          page,
          pageSize,
          search: search || undefined,
          incluirEliminados,
          sortBy: 'fecha_creacion',
          sortDir: 'desc',
        },
      });

      setRows(data.data ?? []);
      setTotal(data.total ?? 0);
      setTotalPages(Math.max(data.total_pages ?? 1, 1));
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudieron cargar los usuarios.'));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadCatalogs();
  }, []);

  useEffect(() => {
    void loadData();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- recarga controlada por filtros y paginación
  }, [page, pageSize, search, incluirEliminados]);

  const openCreateModal = () => {
    setEditingId(null);
    setIsModalOpen(true);
  };

  const openEditModal = (id: string) => {
    setEditingId(id);
    setIsModalOpen(true);
  };

  const closeModal = () => {
    setIsModalOpen(false);
    setEditingId(null);
  };

  const confirmDelete = (row: UsuarioRow) => {
    setDeleteCandidate({ id: row.id, email: row.email });
  };

  const confirmMfaRevoke = (row: UsuarioRow) => {
    setMfaCandidate({ id: row.id, email: row.email });
  };

  const softDelete = async () => {
    if (!deleteCandidate) {
      return;
    }

    setActionLoading(true);
    setError(null);

    try {
      await api.delete(`/usuarios/${deleteCandidate.id}`);
      setDeleteCandidate(null);
      await loadData();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo enviar el usuario a papelera.'));
    } finally {
      setActionLoading(false);
    }
  };

  const restore = async (id: string) => {
    setActionLoading(true);
    setError(null);

    try {
      await api.post(`/usuarios/${id}/restaurar`);
      await loadData();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo restaurar el usuario.'));
    } finally {
      setActionLoading(false);
    }
  };

  const revokeMfa = async () => {
    if (!mfaCandidate) {
      return;
    }

    setActionLoading(true);
    setError(null);

    try {
      await api.post(`/usuarios/${mfaCandidate.id}/mfa/revocar`);
      setMfaCandidate(null);
      await loadData();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo revocar el Authenticator del usuario.'));
    } finally {
      setActionLoading(false);
    }
  };

  return (
    <section className="users-page">
      <header className="users-header">
        <div>
          <h1>Usuarios</h1>
          <p className="users-subtitle">
            Administra accesos, emails de notificación y permisos granulares.
          </p>
        </div>
        <div className="users-actions">
          <button type="button" className="users-primary-button" onClick={openCreateModal}>
            Nuevo usuario
          </button>
        </div>
      </header>

      <div className="users-summary-grid">
        <article className="users-summary-card">
          <strong>{total}</strong>
          <span>Usuarios en esta vista</span>
        </article>
        <article className="users-summary-card">
          <strong>{activeCount}</strong>
          <span>Activos en esta vista</span>
        </article>
        <article className="users-summary-card">
          <strong>{rows.filter((row) => !!row.deleted_at).length}</strong>
          <span>Eliminados en esta vista</span>
        </article>
      </div>

      <div className="users-filters">
        <input
          type="search"
          aria-label="Buscar usuarios por nombre o email"
          placeholder="Buscar por nombre o email"
          value={search}
          onChange={(event) => {
            setPage(1);
            setSearch(event.target.value);
          }}
        />
        <label>
          <input
            type="checkbox"
            checked={incluirEliminados}
            onChange={(event) => {
              setPage(1);
              setIncluirEliminados(event.target.checked);
            }}
          />
          Ver eliminados
        </label>
      </div>

      {error && <p className="auth-error" role="alert">{error}</p>}

      <div className="users-table-card">
        {loading ? (
          <p>Cargando usuarios...</p>
        ) : rows.length === 0 ? (
          <div className="users-empty-state">
            <h2>Sin usuarios en esta vista</h2>
            <p>Ajusta los filtros o crea un usuario nuevo.</p>
          </div>
        ) : (
          <div className="users-table-scroll">
            <table>
            <thead>
              <tr>
                <th>Email</th>
                <th>Nombre</th>
                <th>Rol</th>
                <th>Estado</th>
                <th>Cambio inicial</th>
                <th>IA</th>
                <th>Authenticator</th>
                <th>Acciones</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.id}>
                  <td>{row.email}</td>
                  <td>{row.nombre_completo}</td>
                  <td>{rolLabels[row.rol] ?? row.rol}</td>
                  <td>
                    <span className={row.deleted_at ? 'users-badge users-badge--danger' : row.activo ? 'users-badge users-badge--ok' : 'users-badge'}>
                      {row.deleted_at ? 'Eliminado' : row.activo ? 'Activo' : 'Inactivo'}
                    </span>
                  </td>
                  <td>{row.primer_login ? 'Pendiente' : 'Completado'}</td>
                  <td>{row.puede_usar_ia ? 'Sí' : 'No'}</td>
                  <td>{mfaLabel(row)}</td>
                  <td className="users-row-actions">
                    <button
                      type="button"
                      onClick={() => openEditModal(row.id)}
                      disabled={actionLoading}
                      aria-label={`Editar usuario ${row.email}`}
                    >
                      Editar
                    </button>
                    {!row.deleted_at ? (
                      <button
                        type="button"
                        className="button-danger"
                        onClick={() => confirmDelete(row)}
                        disabled={actionLoading}
                        aria-label={`Eliminar usuario ${row.email}`}
                      >
                        Eliminar
                      </button>
                    ) : (
                      <button
                        type="button"
                        onClick={() => void restore(row.id)}
                        disabled={actionLoading}
                        aria-label={`Restaurar usuario ${row.email}`}
                      >
                        Restaurar
                      </button>
                    )}
                    {!row.deleted_at && row.mfa_enabled && (
                      <button
                        type="button"
                        className="button-danger"
                        onClick={() => confirmMfaRevoke(row)}
                        disabled={actionLoading}
                        aria-label={`Revocar Authenticator de ${row.email}`}
                      >
                        Revocar Authenticator
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
            </table>
          </div>
        )}

        <div className="users-pagination">
          <button
            type="button"
            onClick={() => setPage((current) => Math.max(1, current - 1))}
            disabled={page <= 1}
          >
            Anterior
          </button>
          <span>
            Página {page} / {totalPages}
          </span>
          <button
            type="button"
            onClick={() => setPage((current) => Math.min(totalPages, current + 1))}
            disabled={page >= totalPages}
          >
            Siguiente
          </button>
          <PageSizeSelect
            value={pageSize}
            options={[10, 20, 50]}
            onChange={(next) => {
              setPage(1);
              setPageSize(next);
            }}
          />
        </div>
      </div>

      <UsuarioModal
        open={isModalOpen}
        editingId={editingId}
        titulares={titulares}
        cuentas={cuentas}
        paises={paises}
        onClose={closeModal}
        onSaved={loadData}
      />

      {deleteCandidate && (
        <div
          className="modal-backdrop users-modal-backdrop"
          onClick={() => setDeleteCandidate(null)}
        >
          <div
            ref={deleteDialogRef}
            className="users-confirm-modal"
            onClick={(event) => event.stopPropagation()}
            role="dialog"
            aria-modal="true"
            aria-labelledby="delete-user-title"
            tabIndex={-1}
          >
            <h2 id="delete-user-title">Eliminar usuario</h2>
            <p>
              Vas a enviar a papelera a <strong>{deleteCandidate.email}</strong>.
            </p>
            <p>Se conservará para restauración posterior y la acción quedará auditada.</p>
            <div className="users-form-actions">
              <button
                type="button"
                onClick={() => setDeleteCandidate(null)}
                disabled={actionLoading}
              >
                Cancelar
              </button>
              <button type="button" className="button-danger" onClick={() => void softDelete()} disabled={actionLoading}>
                {actionLoading ? 'Enviando...' : 'Enviar a papelera'}
              </button>
            </div>
          </div>
        </div>
      )}

      {mfaCandidate && (
        <div
          className="modal-backdrop users-modal-backdrop"
          onClick={() => setMfaCandidate(null)}
        >
          <div
            ref={mfaDialogRef}
            className="users-confirm-modal"
            onClick={(event) => event.stopPropagation()}
            role="dialog"
            aria-modal="true"
            aria-labelledby="mfa-user-title"
            tabIndex={-1}
          >
            <h2 id="mfa-user-title">Revocar Authenticator</h2>
            <p>
              Vas a quitar el Authenticator de <strong>{mfaCandidate.email}</strong>.
            </p>
            {mfaCandidate && mfaRequiredForCandidate(rows, mfaCandidate.id) ? (
              <p>Este usuario es administrador o la politica actual le obliga a usar Authenticator. Tras la revocacion debera configurarlo de nuevo al iniciar sesion.</p>
            ) : (
              <p>El usuario no esta obligado a usar Authenticator por la politica actual. La revocacion solo borra el Authenticator configurado.</p>
            )}
            <p>Se cerraran sus sesiones activas en cualquier caso.</p>
            <div className="users-form-actions">
              <button
                type="button"
                onClick={() => setMfaCandidate(null)}
                disabled={actionLoading}
              >
                Cancelar
              </button>
              <button type="button" className="button-danger" onClick={() => void revokeMfa()} disabled={actionLoading}>
                {actionLoading ? 'Revocando...' : 'Revocar Authenticator'}
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
