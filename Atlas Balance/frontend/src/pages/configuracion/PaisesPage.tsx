import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { EmptyState } from '@/components/common/EmptyState';
import Modal from '@/components/common/Modal';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import { useConfirmDialog } from '@/hooks/useConfirmDialog';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
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
      setError(extractErrorMessage(err, 'No se pudieron cargar los países. Revisa la conexión con el servidor e inténtalo de nuevo.'));
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
        message: 'Tienes cambios sin guardar en este país. Si cierras, se perderán. ¿Descartar?',
        confirmLabel: 'Descartar',
        tone: 'reversible',
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
      setFormError('Escribe el nombre del país.');
      return;
    }
    const codigoIso2 = form.codigoIso2.trim().toUpperCase();
    if (codigoIso2 && (codigoIso2.length !== 2 || !/^[A-Z]{2}$/.test(codigoIso2))) {
      setFormError('El código ISO2 debe tener exactamente dos letras.');
      return;
    }

    setSaving(true);
    setFormError(null);
    const payload = {
      nombre,
      codigo_iso2: codigoIso2 || null,
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
      setFormError(extractErrorMessage(err, 'No se pudo guardar el país. Revisa el nombre y el código ISO2 e inténtalo de nuevo.'));
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
      setError(extractErrorMessage(err, 'No se pudo enviar el país a la papelera. Recarga la lista e inténtalo de nuevo.'));
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
      setError(extractErrorMessage(err, 'No se pudo restaurar el país. Recarga la lista e inténtalo de nuevo.'));
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
    <section className="entity-page paises-page page">
      <header className="entity-header page-header">
        <div className="page-header__lead">
          <p className="page-header__eyebrow"><Link to="/configuracion">Configuración</Link> {' / '} Países</p>
          <h1 className="page-header__title">Países</h1>
          <p className="page-header__subtitle">
            Catálogo de países disponibles para asignar a cuentas y permisos por país.
          </p>
        </div>
        {isAdmin && (
          <button type="button" className="button-primary" onClick={openCreateModal}>
            Nuevo país
          </button>
        )}
      </header>

      <div className="entity-filters">
        <input
          type="search"
          autoComplete="off"
          autoCapitalize="none"
          autoCorrect="off"
          spellCheck={false}
          enterKeyHint="search"
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

      <div className="entity-grid">
        <div className="users-table-card paises-table-card">
          {loading ? (
            <div className="import-muted">
              <PageSkeleton variant="table" rows={5} columns={4} label="Cargando países..." />
            </div>
          ) : null}
          {!loading && items.length === 0 ? (
            <EmptyState
              headingLevel="h2"
              title="No hay países con estos filtros."
              subtitle="Ajusta la búsqueda o crea un país nuevo."
            />
          ) : null}
          {!loading && items.length > 0 ? (
            <div className="users-table-scroll" role="region" tabIndex={0} aria-label="Tabla de países">
              <table className="users-table paises-table">
                <caption className="sr-only">Países</caption>
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
                      <td className="users-row-actions paises-actions-col">
                        {isAdmin && !row.deleted_at ? (
                          <>
                            <button
                              type="button"
                              onClick={() => startEdit(row)}
                              disabled={saving}
                              aria-label={`Editar país ${row.nombre}`}
                            >
                              Editar
                            </button>
                            <button
                              type="button"
                              className="button-danger"
                              onClick={() => setDeleteCandidate({ id: row.id, nombre: row.nombre })}
                              disabled={saving}
                              aria-label={`Eliminar país ${row.nombre}`}
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
                            aria-label={`Restaurar país ${row.nombre}`}
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

      {isAdmin ? (
        <Modal
          open={isFormModalOpen}
          onClose={() => void closeFormModal()}
          titleId="paises-modal-title"
          title={editingId ? 'Editar país' : 'Nuevo país'}
          subtitle="Alta y edición del catálogo de países. Solo accesible para administradores."
          variant="fullscreen-mobile"
          panelClassName="users-modal modal-form"
          headerClassName="users-modal-header"
          bareBody
          busy={saving}
          closeButtonClassName="users-modal-close"
          closeLabel="Cerrar modal de país"
        >
          <form
            className="users-modal-body modal-form-body"
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
                    Dos letras en mayúsculas. Opcional pero recomendado para el selector de organización.
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

            <div className="users-form-actions modal-form-actions">
              <button type="button" className="button-secondary" onClick={() => void closeFormModal()} disabled={saving}>
                Cancelar
              </button>
              <button type="submit" className="button-primary" disabled={saving}>
                {saving ? 'Guardando...' : 'Guardar'}
              </button>
            </div>
          </form>
        </Modal>
      ) : null}

      <ConfirmDialog
        open={!!deleteCandidate}
        title="Eliminar país"
        message={
          deleteCandidate
            ? `Vas a enviar a papelera ${deleteCandidate.nombre}. Las cuentas con este país conservarán su etiqueta y la acción quedará auditada.`
            : ''
        }
        confirmLabel="Confirmar eliminación"
        tone="reversible"
        loadingLabel="Enviando..."
        loading={saving}
        onCancel={() => setDeleteCandidate(null)}
        onConfirm={remove}
      />
      <ConfirmDialog {...discardDialogProps} />
    </section>
  );
}