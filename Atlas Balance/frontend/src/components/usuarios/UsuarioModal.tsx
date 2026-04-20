import { useEffect, useMemo, useState } from 'react';
import { AppSelect } from '@/components/common/AppSelect';
import api from '@/services/api';
import { extractErrorMessage } from '@/utils/errorMessage';

export interface CatalogTitular {
  id: string;
  nombre: string;
}

export interface CatalogCuenta {
  id: string;
  nombre: string;
  titular_id: string;
  titular_nombre: string | null;
}

interface PermisoFormRow {
  key: string;
  titular_id: string;
  cuenta_id: string;
  puede_agregar_lineas: boolean;
  puede_editar_lineas: boolean;
  puede_eliminar_lineas: boolean;
  puede_importar: boolean;
  puede_ver_dashboard: boolean;
  columnas_visibles: string;
  columnas_editables: string;
}

interface UserFormState {
  email: string;
  nombre_completo: string;
  rol: 'ADMIN' | 'GERENTE' | 'EMPLEADO_ULTRA' | 'EMPLEADO_PLUS' | 'EMPLEADO';
  activo: boolean;
  primer_login: boolean;
  password: string;
  emails: string;
  permisos: PermisoFormRow[];
}

interface PermisoApiRow {
  titular_id?: string | null;
  cuenta_id?: string | null;
  puede_agregar_lineas?: boolean;
  puede_editar_lineas?: boolean;
  puede_eliminar_lineas?: boolean;
  puede_importar?: boolean;
  puede_ver_dashboard?: boolean;
  columnas_visibles?: string[];
  columnas_editables?: string[];
}

interface UsuarioDetalleResponse {
  usuario: {
    email: string;
    nombre_completo: string;
    rol: UserFormState['rol'];
    activo: boolean;
    primer_login: boolean;
  };
  emails?: string[];
  permisos?: PermisoApiRow[];
}

interface UsuarioModalProps {
  open: boolean;
  editingId: string | null;
  titulares: CatalogTitular[];
  cuentas: CatalogCuenta[];
  onClose: () => void;
  onSaved: () => Promise<void> | void;
}

const emptyPermiso = (): PermisoFormRow => ({
  key: crypto.randomUUID(),
  titular_id: '',
  cuenta_id: '',
  puede_agregar_lineas: false,
  puede_editar_lineas: false,
  puede_eliminar_lineas: false,
  puede_importar: false,
  puede_ver_dashboard: false,
  columnas_visibles: '',
  columnas_editables: '',
});

const emptyForm = (): UserFormState => ({
  email: '',
  nombre_completo: '',
  rol: 'EMPLEADO',
  activo: true,
  primer_login: true,
  password: '',
  emails: '',
  permisos: [emptyPermiso()],
});

const getPermisoScopeLabel = (
  permiso: PermisoFormRow,
  titulares: CatalogTitular[],
  cuentas: CatalogCuenta[]
) => {
  if (permiso.cuenta_id) {
    const cuenta = cuentas.find((item) => item.id === permiso.cuenta_id);
    if (cuenta) {
      return cuenta.titular_nombre
        ? `Cuenta: ${cuenta.nombre} (${cuenta.titular_nombre})`
        : `Cuenta: ${cuenta.nombre}`;
    }

    return 'Cuenta específica';
  }

  if (permiso.titular_id) {
    const titular = titulares.find((item) => item.id === permiso.titular_id);
    return titular ? `Titular: ${titular.nombre}` : 'Titular específico';
  }

  return 'Permiso global';
};

export default function UsuarioModal({
  open,
  editingId,
  titulares,
  cuentas,
  onClose,
  onSaved,
}: UsuarioModalProps) {
  const [form, setForm] = useState<UserFormState>(emptyForm);
  const [loading, setLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const title = useMemo(
    () => (editingId ? 'Editar Usuario' : 'Nuevo Usuario'),
    [editingId]
  );

  useEffect(() => {
    if (!open) {
      setError(null);
      setLoading(false);
      setSubmitting(false);
      return;
    }

    if (!editingId) {
      setForm(emptyForm());
      setError(null);
      return;
    }

    let cancelled = false;

    const loadUsuario = async () => {
      setLoading(true);
      setError(null);
      try {
        const { data } = await api.get<UsuarioDetalleResponse>(`/usuarios/${editingId}`, {
          params: { incluirEliminados: true },
        });

        if (cancelled) return;

        const mappedPermisos: PermisoFormRow[] = (data.permisos ?? []).map((permiso) => ({
          key: crypto.randomUUID(),
          titular_id: permiso.titular_id ?? '',
          cuenta_id: permiso.cuenta_id ?? '',
          puede_agregar_lineas: permiso.puede_agregar_lineas ?? false,
          puede_editar_lineas: permiso.puede_editar_lineas ?? false,
          puede_eliminar_lineas: permiso.puede_eliminar_lineas ?? false,
          puede_importar: permiso.puede_importar ?? false,
          puede_ver_dashboard: permiso.puede_ver_dashboard ?? false,
          columnas_visibles: (permiso.columnas_visibles ?? []).join(', '),
          columnas_editables: (permiso.columnas_editables ?? []).join(', '),
        }));

        setForm({
          email: data.usuario.email,
          nombre_completo: data.usuario.nombre_completo,
          rol: data.usuario.rol,
          activo: data.usuario.activo,
          primer_login: data.usuario.primer_login,
          password: '',
          emails: (data.emails ?? []).join('\n'),
          permisos: mappedPermisos.length > 0 ? mappedPermisos : [emptyPermiso()],
        });
      } catch (err) {
        if (!cancelled) {
          setError(extractErrorMessage(err, 'No se pudo cargar el usuario'));
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    };

    void loadUsuario();

    return () => {
      cancelled = true;
    };
  }, [editingId, open]);

  useEffect(() => {
    if (!open) return;

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !submitting) {
        onClose();
      }
    };

    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [onClose, open, submitting]);

  const parseEmails = (value: string): string[] =>
    value
      .split(/[\n,;]/)
      .map((item) => item.trim())
      .filter(Boolean);

  const parseColumns = (value: string): string[] | undefined => {
    const parsed = value
      .split(',')
      .map((item) => item.trim())
      .filter(Boolean);
    return parsed.length > 0 ? parsed : undefined;
  };

  const buildPermisosPayload = () =>
    form.permisos
      .map((permiso) => {
        const columnasVisibles = parseColumns(permiso.columnas_visibles);
        const columnasEditables = parseColumns(permiso.columnas_editables);
        const hasFlags =
          permiso.puede_agregar_lineas ||
          permiso.puede_editar_lineas ||
          permiso.puede_eliminar_lineas ||
          permiso.puede_importar ||
          permiso.puede_ver_dashboard;
        const hasScope = !!permiso.cuenta_id || !!permiso.titular_id;

        if (!hasFlags && !hasScope && !columnasVisibles && !columnasEditables) {
          return null;
        }

        return {
          cuenta_id: permiso.cuenta_id || null,
          titular_id: permiso.titular_id || null,
          puede_agregar_lineas: permiso.puede_agregar_lineas,
          puede_editar_lineas: permiso.puede_editar_lineas,
          puede_eliminar_lineas: permiso.puede_eliminar_lineas,
          puede_importar: permiso.puede_importar,
          puede_ver_dashboard: permiso.puede_ver_dashboard,
          columnas_visibles: columnasVisibles,
          columnas_editables: columnasEditables,
        };
      })
      .filter((item): item is NonNullable<typeof item> => !!item);

  const updatePermiso = (key: string, patch: Partial<PermisoFormRow>) => {
    setForm((prev) => ({
      ...prev,
      permisos: prev.permisos.map((permiso) =>
        permiso.key === key ? { ...permiso, ...patch } : permiso
      ),
    }));
  };

  const addPermiso = () => {
    setForm((prev) => ({
      ...prev,
      permisos: [...prev.permisos, emptyPermiso()],
    }));
  };

  const removePermiso = (key: string) => {
    setForm((prev) => {
      const next = prev.permisos.filter((permiso) => permiso.key !== key);
      return {
        ...prev,
        permisos: next.length > 0 ? next : [emptyPermiso()],
      };
    });
  };

  const closeModal = () => {
    if (submitting) return;
    onClose();
  };

  const save = async () => {
    if (!form.nombre_completo.trim() || !form.email.trim()) {
      setError('Nombre y email son obligatorios');
      return;
    }

    if (!editingId && form.password.length < 8) {
      setError('Password mínimo 8 caracteres para crear usuario');
      return;
    }

    setSubmitting(true);
    setError(null);

    const payload = {
      email: form.email.trim().toLowerCase(),
      nombre_completo: form.nombre_completo.trim(),
      rol: form.rol,
      activo: form.activo,
      primer_login: form.primer_login,
      password: form.password,
      password_nueva: form.password || undefined,
      emails: parseEmails(form.emails),
      permisos: buildPermisosPayload(),
    };

    try {
      if (editingId) {
        await api.put(`/usuarios/${editingId}`, payload);
      } else {
        await api.post('/usuarios', payload);
      }

      await onSaved();
      onClose();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo guardar'));
    } finally {
      setSubmitting(false);
    }
  };

  if (!open) {
    return null;
  }

  return (
    <div className="modal-backdrop users-modal-backdrop" onClick={closeModal}>
      <div
        className="users-modal"
        onClick={(event) => event.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-labelledby="usuarios-modal-title"
      >
        <div className="users-modal-header">
          <div>
            <h2 id="usuarios-modal-title">{title}</h2>
            <p>Datos base, emails de notificación y permisos granulares.</p>
          </div>
          <button
            type="button"
            className="users-modal-close"
            onClick={closeModal}
            disabled={submitting}
          >
            Cerrar
          </button>
        </div>

        {loading ? (
          <div className="users-modal-loading">Cargando usuario...</div>
        ) : (
          <form
            className="users-modal-body"
            onSubmit={(event) => {
              event.preventDefault();
              void save();
            }}
          >
            {error && <p className="auth-error">{error}</p>}

            <section className="users-modal-section">
              <h3>Identidad</h3>
              <div className="users-form-grid">
                <label>
                  <span>Email</span>
                  <input
                    type="email"
                    placeholder="usuario@atlasbalance.local"
                    value={form.email}
                    onChange={(event) =>
                      setForm((prev) => ({ ...prev, email: event.target.value }))
                    }
                  />
                </label>

                <label>
                  <span>Nombre Completo</span>
                  <input
                    placeholder="Nombre y apellidos"
                    value={form.nombre_completo}
                    onChange={(event) =>
                      setForm((prev) => ({
                        ...prev,
                        nombre_completo: event.target.value,
                      }))
                    }
                  />
                </label>

                <AppSelect
                  label="Rol"
                  value={form.rol}
                  options={[
                    { value: 'ADMIN', label: 'ADMIN' },
                    { value: 'GERENTE', label: 'GERENTE' },
                    { value: 'EMPLEADO_ULTRA', label: 'EMPLEADO_ULTRA' },
                    { value: 'EMPLEADO_PLUS', label: 'EMPLEADO_PLUS' },
                    { value: 'EMPLEADO', label: 'EMPLEADO' },
                  ]}
                  onChange={(next) =>
                    setForm((prev) => ({
                      ...prev,
                      rol: next as UserFormState['rol'],
                    }))
                  }
                />

                <label>
                  <span>{editingId ? 'Nueva contraseña (opcional)' : 'Contraseña inicial'}</span>
                  <input
                    type="password"
                    placeholder={editingId ? 'Solo si la quieres cambiar' : 'Mínimo 8 caracteres'}
                    value={form.password}
                    onChange={(event) =>
                      setForm((prev) => ({ ...prev, password: event.target.value }))
                    }
                  />
                </label>
              </div>

              <div className="users-check-row">
                <label>
                  <input
                    type="checkbox"
                    className="users-check-input"
                    checked={form.activo}
                    onChange={(event) =>
                      setForm((prev) => ({ ...prev, activo: event.target.checked }))
                    }
                  />
                  Activo
                </label>
                <label>
                  <input
                    type="checkbox"
                    className="users-check-input"
                    checked={form.primer_login}
                    onChange={(event) =>
                      setForm((prev) => ({
                        ...prev,
                        primer_login: event.target.checked,
                      }))
                    }
                  />
                  Forzar primer login
                </label>
              </div>
            </section>

            <section className="users-modal-section">
              <h3>Emails de notificación</h3>
              <label>
                <span>Uno por línea o separados por coma</span>
                <textarea
                  rows={4}
                  placeholder={'alertas@atlasbalance.local\nsupervisor@atlasbalance.local'}
                  value={form.emails}
                  onChange={(event) =>
                    setForm((prev) => ({ ...prev, emails: event.target.value }))
                  }
                />
              </label>
            </section>

            <section className="users-modal-section">
              <div className="users-section-header">
                <div>
                  <h3>Permisos</h3>
                  <p>Globales, por titular o por cuenta específica.</p>
                </div>
                <button type="button" onClick={addPermiso}>
                  Añadir permiso
                </button>
              </div>

              <div className="users-permisos-list">
                {form.permisos.map((permiso, index) => {
                  const cuentasFiltradas = permiso.titular_id
                    ? cuentas.filter((cuenta) => cuenta.titular_id === permiso.titular_id)
                    : cuentas;
                  const scopeLabel = getPermisoScopeLabel(permiso, titulares, cuentas);

                  return (
                    <div key={permiso.key} className="permiso-row">
                      <div className="users-section-header permiso-row-header">
                        <div className="permiso-row-title">
                          <strong>Permiso #{index + 1}</strong>
                          <p className="permiso-scope">{scopeLabel}</p>
                        </div>
                        <button
                          type="button"
                          className="remove-permiso"
                          onClick={() => removePermiso(permiso.key)}
                        >
                          Quitar
                        </button>
                      </div>

                      <div className="permiso-grid">
                        <AppSelect
                          label="Titular"
                          value={permiso.titular_id}
                          options={[
                            { value: '', label: 'Global o por cuenta' },
                            ...titulares.map((titular) => ({ value: titular.id, label: titular.nombre })),
                          ]}
                          onChange={(next) =>
                            updatePermiso(permiso.key, {
                              titular_id: next,
                              cuenta_id: '',
                            })
                          }
                        />

                        <AppSelect
                          label="Cuenta"
                          value={permiso.cuenta_id}
                          options={[
                            { value: '', label: 'Sin cuenta especifica' },
                            ...cuentasFiltradas.map((cuenta) => ({
                              value: cuenta.id,
                              label: `${cuenta.nombre}${cuenta.titular_nombre ? ` (${cuenta.titular_nombre})` : ''}`,
                            })),
                          ]}
                          onChange={(next) =>
                            updatePermiso(permiso.key, {
                              cuenta_id: next,
                            })
                          }
                        />

                        <label>
                          <span>Columnas visibles</span>
                          <input
                            placeholder="fecha, concepto, monto"
                            value={permiso.columnas_visibles}
                            onChange={(event) =>
                              updatePermiso(permiso.key, {
                                columnas_visibles: event.target.value,
                              })
                            }
                          />
                        </label>

                        <label>
                          <span>Columnas editables</span>
                          <input
                            placeholder="monto, nota"
                            value={permiso.columnas_editables}
                            onChange={(event) =>
                              updatePermiso(permiso.key, {
                                columnas_editables: event.target.value,
                              })
                            }
                          />
                        </label>
                      </div>

                      <div className="users-check-grid">
                        <label>
                          <input
                            type="checkbox"
                            className="users-check-input"
                            checked={permiso.puede_agregar_lineas}
                            onChange={(event) =>
                              updatePermiso(permiso.key, {
                                puede_agregar_lineas: event.target.checked,
                              })
                            }
                          />
                          Puede Agregar
                        </label>
                        <label>
                          <input
                            type="checkbox"
                            className="users-check-input"
                            checked={permiso.puede_editar_lineas}
                            onChange={(event) =>
                              updatePermiso(permiso.key, {
                                puede_editar_lineas: event.target.checked,
                              })
                            }
                          />
                          Puede Editar
                        </label>
                        <label>
                          <input
                            type="checkbox"
                            className="users-check-input"
                            checked={permiso.puede_eliminar_lineas}
                            onChange={(event) =>
                              updatePermiso(permiso.key, {
                                puede_eliminar_lineas: event.target.checked,
                              })
                            }
                          />
                          Puede Eliminar
                        </label>
                        <label>
                          <input
                            type="checkbox"
                            className="users-check-input"
                            checked={permiso.puede_importar}
                            onChange={(event) =>
                              updatePermiso(permiso.key, {
                                puede_importar: event.target.checked,
                              })
                            }
                          />
                          Puede Importar
                        </label>
                        <label>
                          <input
                            type="checkbox"
                            className="users-check-input"
                            checked={permiso.puede_ver_dashboard}
                            onChange={(event) =>
                              updatePermiso(permiso.key, {
                                puede_ver_dashboard: event.target.checked,
                              })
                            }
                          />
                          Puede Ver Dashboard
                        </label>
                      </div>
                    </div>
                  );
                })}
              </div>
            </section>

            <div className="users-form-actions users-form-actions--sticky">
              <button type="button" onClick={closeModal} disabled={submitting}>
                Cancelar
              </button>
              <button type="submit" disabled={submitting}>
                {submitting ? 'Guardando...' : editingId ? 'Guardar cambios' : 'Crear usuario'}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}
