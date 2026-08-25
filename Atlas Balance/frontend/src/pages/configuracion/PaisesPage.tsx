import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { CloseIconButton } from '@/components/common/CloseIconButton';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import { useConfirmDialog } from '@/hooks/useConfirmDialog';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { useDialogFocus } from '@/hooks/useDialogFocus';
import { useUnsavedChanges } from '@/hooks/useUnsavedChanges';
import { useInvalidateAfterMutation } from '@/hooks/queries/useInvalidateAfterMutation';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import type { Pais, PaginatedResponse } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';

interface PaisRow extends Pais {
  deleted_at: string | null;
}

interface PaisFormState {
  nombre: string;
  codigoIso2: string;
  activo: boolean;
}

interface DeleteCandidate {
  id: string;
  nombre: string;
}

const emptyForm: PaisFormState = {
  nombre: '',
  codigoIso2: '',
  activo: true,
};

export default function PaisesPage() {
  const usuario = useAuthStore((state) => state.usuario);
  const isAdmin = usuario?.rol === 'ADMIN';

  const [items, setItems] = useState<PaisRow[]>([]);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [totalPages, setTotalPages] = useState(1);
  const [search, setSearch] = useState('');
  const [incluirEliminados, setIncluirEliminados] = useState(false);
  const debouncedSearch = useDebouncedValue(search);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  const [editingId, setEditingId] = useState<string | null>(null);
  const [form, setForm] = useState<PaisFormState>(emptyForm);
  const [isFormModalOpen, setIsFormModalOpen] = useState(false);
  const formBaselineRef = useRef<string | null>(null);
  const { confirm: confirmDiscard, dialogProps: discardDialogProps } = useConfirmDialog();
  const isFormDirty = isFormModalOpen && formBaselineRef.current !== null && JSON.stringify(form) !== formBaselineRef.current;
  useUnsavedChanges(isFormDirty);
  const [saving, setSaving] = useState(false);
  const [deleteCandidate, setDeleteCandidate] = useState<DeleteCandidate | null>(null);

  const invalidate = useInvalidateAfterMutation();

  const loadPaises = async () => {
    setLoading(true);
    setError(null);
    try {
      const { data } = await api.get<PaginatedResponse<PaisRow>>('/paises', {
        params: {
          page,
          pageSize,
          search: debouncedSearch || undefined,
          incluirEliminados,
          // PaisesController.Listar filtra por x.Activo salvo que incluirInactivos
          // sea true, y Eliminar pone Activo=false. Sin esto, "Ver eliminados"
          // pide las filas con IgnoreQueryFilters() pero el filtro de Activo las
          // vuelve a excluir: la lista sale vacia y no se puede restaurar nada.
          incluirInactivos: incluirEliminados,
          sortBy: 'nombre',
          sortDir: 'asc',
        },
      });
      setItems(data.data ?? []);
      setTotalPages(Math.max(data.total_pages ?? 1, 1));
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudieron cargar paises.'));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadPaises();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- recarga controlada por filtros y paginacion
  }, [page, pageSize, debouncedSearch, incluirEliminados]);

  const resetForm = () => {
    setEditingId(null);
    setForm(emptyForm);
  };

  const openCreateModal = () => {
    setEditingId(null);
    setForm(emptyForm);
    formBaselineRef.current = JSON.stringify(emptyForm);
    setFormError(null);
    setIsFormModalOpen(true);
  };

  const closeFormModal = async () => {
    if (saving) {
      return;
    }

    if (isFormDirty) {
      const discard = await confirmDiscard({
        title: 'Descartar cambios',
        message: 'Tienes cambios sin guardar en este pais. Si cierras, se perderan. ¿Descartar?',
        confirmLabel: 'Descartar',
        cancelLabel: 'Seguir editando',
      });
      if (!discard) {
        return;
      }
    }

    formBaselineRef.current = null;
    setIsFormModalOpen(false);
    setFormError(null);
    resetForm();
  };

  const formDialogRef = useDialogFocus<HTMLDivElement>(isFormModalOpen, {
    onEscape: saving ? undefined : () => void closeFormModal(),
  });

  const startEdit = (row: PaisRow) => {
    setEditingId(row.id);
    const loadedForm: PaisFormState = {
      nombre: row.nombre,
      codigoIso2: row.codigo_iso2 ?? '',
      activo: row.activo,
    };
    setForm(loadedForm);
    formBaselineRef.current = JSON.stringify(loadedForm);
    setFormError(null);
    setIsFormModalOpen(true);
  };

  const save = async () => {
    if (!isAdmin) return;
    const nombre = form.nombre.trim();
    if (!nombre) {
      setFormError('Escribe el nombre del pais.');
      return;
    }
    const codigoIso2 = form.codigoIso2.trim().toUpperCase();
    if (codigoIso2 && (codigoIso2.length !== 2 || !/^[A-Z]{2}$/.test(codigoIso2))) {
      setFormError('El codigo ISO2 debe tener exactamente dos letras.');
      return;
    }

    setSaving(true);
    setFormError(null);
    const payload = {
      nombre,
      codigoIso2: codigoIso2 || null,
      activo: form.activo,
    };

    try {
      if (editingId) {
        await api.put(`/paises/${editingId}`, payload);
      } else {
        await api.post('/paises', payload);
      }
      resetForm();
      setIsFormModalOpen(false);
      await loadPaises();
      await invalidate('pais');
    } catch (err) {
      setFormError(extractErrorMessage(err, 'No se pudo guardar país'));
    } finally {
      setSaving(false);
    }
  };

  const remove = async () => {
    if (!isAdmin || !deleteCandidate) return;
    setSaving(true);
    setError(null);
    try {
      await api.delete(`/paises/${deleteCandidate.id}`);
      setDeleteCandidate(null);
      await loadPaises();
      await invalidate('pais');
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo eliminar pais'));
    } finally {
      setSaving(false);
    }
  };

  const restore = async (id: string) => {
    if (!isAdmin) return;
    try {
      await api.post(`/paises/${id}/restaurar`);
      await loadPaises();
      await invalidate('pais');
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo restaurar país'));
    }
  };

  const estadoLabel = (row: PaisRow) => {
    if (row.deleted_at) return 'Eliminado';
    if (!row.activo) return 'Inactivo';
    return 'Activo';
  };

  const estadoClass = (row: PaisRow) => {
    if (row.deleted_at) return 'pill pill--danger';
    if (!row.activo) return 'pill pill--muted';
    return 'pill pill--ok';
  };

  return (
    <section className="phase2-page paises-page">
      <header className="phase2-header">
        <div>
          <p className="dashboard-subtitle"><Link to="/configuracion">Configuracion</Link> {' / '} Paises</p>
          <h1>Países</h1>
          <p className="dashboard-subtitle">
            Catalogo de paises disponibles para asignar a cuentas y permisos por pais.
          </p>
        </div>
        {isAdmin && (
          <button type="button" className="button-primary" onClick={openCreateModal}>
            Nuevo pais
          </button>
        )}
      </header>

      <div className="phase2-filters">
        <input
          type="search"
          aria-label="Buscar país"
          placeholder="Buscar por nombre o código ISO2"
          value={search}
          onChange={(e) => {
            setPage(1);
            setSearch(e.target.value);
          }}
        />
        <PageSizeSelect
          value={pageSize}
          options={[10, 20, 50, 100]}
          onChange={(next) => {
            setPage(1);
            setPageSize(next);
          }}
        />
        {isAdmin && (
          <label className="paises-filters-incluir">
            <input
              type="checkbox"
              checked={incluirEliminados}
              onChange={(e) => {
                setPage(1);
                setIncluirEliminados(e.target.checked);
              }}
            />
            Ver eliminados
          </label>
        )}
      </div>

      {error ? <p className="auth-error" role="alert">{error}</p> : null}

      <div className="phase2-grid">
        <div className="phase2-cards paises-table-card">
          {loading ? <p className="import-muted">Cargando paises...</p> : null}
          {!loading && items.length === 0 ? (
            <EmptyState
              title="No hay paises con estos filtros."
              subtitle="Ajusta la busqueda o crea un pais nuevo."
            />
          ) : null}
          {!loading && items.length > 0 ? (
            <div className="users-table-scroll">
              <table className="users-table paises-table">
                <thead>
                  <tr>
                    <th scope="col">Nombre</th>
                    <th scope="col">ISO2</th>
                    <th scope="col">Estado</th>
                    <th scope="col" className="paises-actions-col">Acciones</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((row) => (
                    <tr key={row.id} className={row.deleted_at ? 'paises-row paises-row--deleted' : 'paises-row'}>
                      <td>{row.nombre}</td>
                      <td className="paises-iso2">{row.codigo_iso2 ?? '—'}</td>
                      <td><span className={estadoClass(row)}>{estadoLabel(row)}</span></td>
                      <td className="paises-actions-col">
                        {isAdmin && !row.deleted_at ? (
                          <>
                            <button
                              type="button"
                              onClick={() => startEdit(row)}
                              disabled={saving}
                              aria-label={`Editar pais ${row.nombre}`}
                            >
                              Editar
                            </button>
                            <button
                              type="button"
                              className="button-danger"
                              onClick={() => setDeleteCandidate({ id: row.id, nombre: row.nombre })}
                              disabled={saving}
                              aria-label={`Eliminar pais ${row.nombre}`}
                            >
                              Eliminar
                            </button>
                          </>
                        ) : null}
                        {isAdmin && row.deleted_at ? (
                          <button
                            type="button"
                            onClick={() => restore(row.id)}
                            disabled={saving}
                            aria-label={`Restaurar pais ${row.nombre}`}
                          >
                            Restaurar
                          </button>
                        ) : null}
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
              disabled={page <= 1}
            >
              Anterior
            </button>
            <span>Página {page} / {totalPages}</span>
            <button
              type="button"
              onClick={() => setPage((current) => Math.min(totalPages, current + 1))}
              disabled={page >= totalPages}
            >
              Siguiente
            </button>
          </div>
        </div>
      </div>

      {isAdmin && isFormModalOpen ? (
        <div className="modal-backdrop users-modal-backdrop" onClick={() => void closeFormModal()}>
          <div
            ref={formDialogRef}
            className="users-modal phase2-form-modal"
            onClick={(event) => event.stopPropagation()}
            role="dialog"
            aria-modal="true"
            aria-labelledby="paises-modal-title"
            tabIndex={-1}
          >
            <div className="users-modal-header">
              <div>
                <h2 id="paises-modal-title">{editingId ? 'Editar país' : 'Nuevo país'}</h2>
                <p>Alta y edición del catálogo de países. Solo accesible para administradores.</p>
              </div>
              <CloseIconButton
                className="users-modal-close"
                onClick={() => void closeFormModal()}
                disabled={saving}
                ariaLabel="Cerrar modal de país"
              />
            </div>

            <form
              className="users-modal-body phase2-modal-form"
              onSubmit={(event) => {
                event.preventDefault();
                void save();
              }}
            >
              {formError ? <p className="auth-error" role="alert">{formError}</p> : null}

              <section className="users-modal-section">
                <h3>Datos del país</h3>
                <div className="users-form-grid">
                  <label>
                    <span>Nombre</span>
                    <input
                      value={form.nombre}
                      onChange={(e) => setForm((f) => ({ ...f, nombre: e.target.value }))}
                      maxLength={128}
                      required
                    />
                  </label>

                  <label>
                    <span>Código ISO2</span>
                    <input
                      value={form.codigoIso2}
                      onChange={(e) => setForm((f) => ({ ...f, codigoIso2: e.target.value.toUpperCase().slice(0, 2) }))}
                      maxLength={2}
                      placeholder="ES"
                      aria-describedby="paises-iso2-hint"
                    />
                    <small id="paises-iso2-hint" className="import-muted">
                      Dos letras en mayusculas. Opcional pero recomendado para el selector de organizacion.
                    </small>
                  </label>

                  <label className="paises-activo-field">
                    <input
                      type="checkbox"
                      checked={form.activo}
                      onChange={(e) => setForm((f) => ({ ...f, activo: e.target.checked }))}
                    />
                    <span>Activo</span>
                  </label>
                </div>
              </section>

              <div className="users-form-actions phase2-modal-actions">
                <button type="button" onClick={() => void closeFormModal()} disabled={saving}>
                  Cancelar
                </button>
                <button type="submit" disabled={saving}>
                  {saving ? 'Guardando...' : 'Guardar'}
                </button>
              </div>
            </form>
          </div>
        </div>
      ) : null}

      <ConfirmDialog
        open={!!deleteCandidate}
        title="Eliminar pais"
        message={
          deleteCandidate
            ? `Vas a enviar a papelera ${deleteCandidate.nombre}. Las cuentas con este pais conservaran su etiqueta y la accion quedara auditada.`
            : ''
        }
        confirmLabel="Confirmar eliminacion"
        loadingLabel="Enviando..."
        loading={saving}
        onCancel={() => setDeleteCandidate(null)}
        onConfirm={remove}
      />
      <ConfirmDialog {...discardDialogProps} />
    </section>
  );
}