import { useEffect, useMemo, useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import Modal from '@/components/common/Modal';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { useInvalidateAfterMutation } from '@/hooks/queries/useInvalidateAfterMutation';
import UsuarioModal, {
  type CatalogCuenta,
  type CatalogPais,
  type CatalogTitular,
} from '@/components/usuarios/UsuarioModal';
import api from '@/services/api';
import { QUERY_STALE_TIMES } from '@/services/queryClient';
import { queryKeys } from '@/queries/queryKeys';
import { useAuthStore } from '@/stores/authStore';
import { useUiStore } from '@/stores/uiStore';
import type { PaginatedResponse } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatearTiempoRestante, isUsuarioBloqueado } from '@/utils/usuarioBloqueo';
import '@/styles/layout/routes/usuarios.css';

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
  // A-2: bloqueo por intentos fallidos de login (ISO-8601 UTC) e intentos
  // acumulados queTodavia no han bloqueado la cuenta.
  bloqueado_hasta: string | null;
  intentos_fallidos: number;
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

interface CatalogosPermisos {
  titulares: CatalogTitular[];
  cuentas: CatalogCuenta[];
  paises: CatalogPais[];
}

export default function UsuariosPage() {
  const usuarioId = useAuthStore((state) => state.usuario?.id ?? '');
  const invalidate = useInvalidateAfterMutation();
  const addToast = useUiStore((state) => state.addToast);
  const [rows, setRows] = useState<UsuarioRow[]>([]);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [totalPages, setTotalPages] = useState(1);
  const [total, setTotal] = useState(0);
  const [search, setSearch] = useState('');
  const debouncedSearch = useDebouncedValue(search);
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
  const [unblockCandidate, setUnblockCandidate] = useState<DeleteCandidate | null>(null);
  const [actionLoading, setActionLoading] = useState(false);

  const activeCount = useMemo(
    () => rows.filter((row) => !row.deleted_at && row.activo).length,
    [rows]
  );

  // A-2: instante de referencia para saber si el bloqueo sigue vigente.
  const ahora = new Date();

  // V-03.01 (#8): catalogos-permisos y el listado paginado migran de
  // api.get en efectos a React Query (mismo patron que DashboardPage.tsx /
  // CuentaDetailPage.tsx), manteniendo el resto del componente igual via
  // un bridge a estado local.
  const catalogosQuery = useQuery({
    queryKey: queryKeys.catalogo.catalogosPermisos({ usuarioId }),
    queryFn: () => api.get<CatalogosPermisos>('/usuarios/catalogos-permisos').then((res) => res.data),
    enabled: Boolean(usuarioId),
    staleTime: QUERY_STALE_TIMES.CATALOGO_CORTO_MS,
  });

  const listQuery = useQuery<PaginatedResponse<UsuarioRow>>({
    queryKey: queryKeys.usuarios.list({ usuarioId, page, pageSize, search: debouncedSearch || null, incluirEliminados }),
    queryFn: ({ signal }) =>
      api.get<PaginatedResponse<UsuarioRow>>('/usuarios', {
        params: {
          page,
          pageSize,
          search: debouncedSearch || undefined,
          incluirEliminados,
          sortBy: 'fecha_creacion',
          sortDir: 'desc',
        },
        signal,
      }).then((res) => res.data),
    enabled: Boolean(usuarioId),
    placeholderData: keepPreviousData,
    staleTime: QUERY_STALE_TIMES.USUARIOS_MS,
  });

  useEffect(() => {
    if (catalogosQuery.data) {
      setTitulares(catalogosQuery.data.titulares ?? []);
      setCuentas(catalogosQuery.data.cuentas ?? []);
      setPaises(catalogosQuery.data.paises ?? []);
    }
    // El catalogo original ignoraba errores en silencio (catch { // no-op }).
  }, [catalogosQuery.data]);

  useEffect(() => {
    const data = listQuery.data;
    if (data) {
      setRows(data.data ?? []);
      setTotal(data.total ?? 0);
      setTotalPages(Math.max(data.total_pages ?? 1, 1));
    } else if (!listQuery.isLoading && !listQuery.error) {
      setRows([]);
      setTotal(0);
      setTotalPages(1);
    }
  }, [listQuery.data, listQuery.isLoading, listQuery.error]);

  useEffect(() => {
    if (listQuery.error) {
      setError(extractErrorMessage(listQuery.error, 'No se pudieron cargar los usuarios.'));
    } else {
      setError(null);
    }
  }, [listQuery.error]);

  useEffect(() => {
    if (listQuery.isLoading) {
      setLoading(true);
    } else if (!listQuery.isFetching) {
      setLoading(false);
    }
  }, [listQuery.isLoading, listQuery.isFetching]);

  const loadData = async () => {
    await invalidate('usuario');
  };

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

  // A-2: accion de desbloqueo de una cuenta bloqueada por intentos fallidos.
  const confirmUnblock = (row: UsuarioRow) => {
    setUnblockCandidate({ id: row.id, email: row.email });
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

  // A-2: POST /usuarios/{id}/desbloquear. Si la API responde 400 ("El usuario no
  // esta bloqueado") o 404, el mensaje del servidor se muestra como toast de
  // error y el modal sigue abierto para no dejar al admin sin contexto.
  const unblock = async () => {
    if (!unblockCandidate) {
      return;
    }

    setActionLoading(true);
    setError(null);

    try {
      const res = await api.post<{ message?: string }>(`/usuarios/${unblockCandidate.id}/desbloquear`);
      setUnblockCandidate(null);
      addToast({ type: 'success', message: res.data?.message || 'Usuario desbloqueado.' });
      await loadData();
    } catch (err) {
      addToast({
        type: 'error',
        message: extractErrorMessage(err, 'No se pudo desbloquear el usuario.'),
      });
    } finally {
      setActionLoading(false);
    }
  };

  return (
    <section className="users-page page">
      <header className="users-header page-header">
        <div className="page-header__lead">
          <h1 className="page-header__title">Usuarios</h1>
          <p className="page-header__subtitle">
            Administra accesos, emails de notificación y permisos granulares.
          </p>
        </div>
        <div className="users-actions">
          <button type="button" className="users-primary-button" onClick={openCreateModal}>
            Nuevo usuario
          </button>
        </div>
      </header>

      <div className="users-summary-grid" role="group" aria-label="Resumen de usuarios en esta vista">
        <article className="users-summary-card">
          <strong>{total}</strong>
          <span>Usuarios</span>
        </article>
        <article className="users-summary-card">
          <strong>{activeCount}</strong>
          <span>Activos</span>
        </article>
        <article className="users-summary-card">
          <strong>{rows.filter((row) => !!row.deleted_at).length}</strong>
          <span>Eliminados</span>
        </article>
      </div>

      <div className="users-filters">
        <input
          type="search"
          autoComplete="off"
          autoCapitalize="none"
          autoCorrect="off"
          spellCheck={false}
          enterKeyHint="search"
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
          <PageSkeleton variant="table" rows={5} columns={9} label="Cargando usuarios..." />
        ) : rows.length === 0 ? (
          <div className="users-empty-state">
            <h2>Sin usuarios en esta vista</h2>
            <p>Ajusta los filtros o crea un usuario nuevo.</p>
          </div>
        ) : (
          <div className="users-table-scroll" role="region" tabIndex={0} aria-label="Tabla de usuarios">
            <table>
            <caption className="sr-only">Usuarios</caption>
            <thead>
              <tr>
                <th scope="col">Email</th>
                <th scope="col">Nombre</th>
                <th scope="col">Rol</th>
                <th scope="col">Estado</th>
                <th scope="col">Bloqueo</th>
                <th scope="col">Cambio inicial</th>
                <th scope="col">IA</th>
                <th scope="col">Authenticator</th>
                <th scope="col">Acciones</th>
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
                  <td>
                    {isUsuarioBloqueado(row.bloqueado_hasta, ahora) ? (
                      <span className="users-badge users-badge--danger">
                        Bloqueado · {formatearTiempoRestante(row.bloqueado_hasta ?? '', ahora)}
                      </span>
                    ) : null}
                    {row.intentos_fallidos > 0 ? (
                      <span className="users-badge">
                        {row.intentos_fallidos} {row.intentos_fallidos === 1 ? 'intento' : 'intentos'}
                      </span>
                    ) : null}
                    {!isUsuarioBloqueado(row.bloqueado_hasta, ahora) && !(row.intentos_fallidos > 0) ? '—' : null}
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
                    {!row.deleted_at && isUsuarioBloqueado(row.bloqueado_hasta, ahora) && (
                      <button
                        type="button"
                        className="button-secondary"
                        onClick={() => confirmUnblock(row)}
                        disabled={actionLoading}
                        aria-label={`Desbloquear usuario ${row.email}`}
                      >
                        Desbloquear
                      </button>
                    )}
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
                        aria-label={`Revocar MFA de ${row.email}`}
                      >
                        Revocar MFA
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

      <Modal
        open={Boolean(deleteCandidate)}
        onClose={() => setDeleteCandidate(null)}
        titleId="delete-user-title"
        title="Eliminar usuario"
        variant="fullscreen-mobile"
        panelClassName="users-confirm-modal"
        headerClassName=""
        showCloseButton={false}
        bareBody
        busy={actionLoading}
      >
        <p>
          Vas a enviar a papelera a <strong>{deleteCandidate?.email}</strong>.
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
      </Modal>

      <Modal
        open={Boolean(mfaCandidate)}
        onClose={() => setMfaCandidate(null)}
        titleId="mfa-user-title"
        title="Revocar MFA"
        variant="fullscreen-mobile"
        panelClassName="users-confirm-modal"
        headerClassName=""
        showCloseButton={false}
        bareBody
        busy={actionLoading}
      >
        <p>
          Vas a quitar el Authenticator de <strong>{mfaCandidate?.email}</strong>.
        </p>
        {mfaCandidate && mfaRequiredForCandidate(rows, mfaCandidate.id) ? (
          <p>Este usuario es administrador o la política actual le obliga a usar Authenticator. Tras la revocación deberá configurarlo de nuevo al iniciar sesión.</p>
        ) : (
          <p>El usuario no esta obligado a usar Authenticator por la política actual. La revocación solo borra el Authenticator configurado.</p>
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
            {actionLoading ? 'Revocando...' : 'Revocar MFA'}
          </button>
        </div>
      </Modal>

      <Modal
        open={Boolean(unblockCandidate)}
        onClose={() => setUnblockCandidate(null)}
        titleId="unblock-user-title"
        title="Desbloquear usuario"
        variant="fullscreen-mobile"
        panelClassName="users-confirm-modal"
        headerClassName=""
        showCloseButton={false}
        bareBody
        busy={actionLoading}
      >
        <p>
          Vas a desbloquear a <strong>{unblockCandidate?.email}</strong>.
        </p>
        <p>Se retirará el bloqueo por intentos fallidos y el usuario podrá volver a iniciar sesión.</p>
        <div className="users-form-actions">
          <button
            type="button"
            onClick={() => setUnblockCandidate(null)}
            disabled={actionLoading}
          >
            Cancelar
          </button>
          <button type="button" className="button-secondary" onClick={() => void unblock()} disabled={actionLoading}>
            {actionLoading ? 'Desbloqueando...' : 'Desbloquear'}
          </button>
        </div>
      </Modal>
    </section>
  );
}
