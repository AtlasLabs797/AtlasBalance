import { useEffect, useMemo, useRef, useState } from 'react';
import { AppSelect } from '@/components/common/AppSelect';
import { CloseIconButton } from '@/components/common/CloseIconButton';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { useConfirmDialog } from '@/hooks/useConfirmDialog';
import { useDialogFocus } from '@/hooks/useDialogFocus';
import { useUnsavedChanges } from '@/hooks/useUnsavedChanges';
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
  pais_id: string | null;
  pais_nombre: string | null;
}

export interface CatalogPais {
  id: string;
  nombre: string;
  codigo_iso2: string | null;
}

interface PermisoFormRow {
  key: string;
  pais_id: string;
  titular_id: string;
  cuenta_id: string;
  puede_ver_cuentas: boolean;
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
  rol: 'ADMIN' | 'GERENTE' | 'EMPLEADO';
  activo: boolean;
  primer_login: boolean;
  puede_usar_ia: boolean;
  password: string;
  emails: string;
  permisos: PermisoFormRow[];
}

interface PermisoApiRow {
  pais_id?: string | null;
  titular_id?: string | null;
  cuenta_id?: string | null;
  puede_ver_cuentas?: boolean;
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
    puede_usar_ia: boolean;
  };
  emails?: string[];
  permisos?: PermisoApiRow[];
}

interface UsuarioModalProps {
  open: boolean;
  editingId: string | null;
  titulares: CatalogTitular[];
  cuentas: CatalogCuenta[];
  paises: CatalogPais[];
  onClose: () => void;
  onSaved: () => Promise<void> | void;
}

const emptyPermiso = (): PermisoFormRow => ({
  key: crypto.randomUUID(),
  pais_id: '',
  titular_id: '',
  cuenta_id: '',
  puede_ver_cuentas: false,
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
  puede_usar_ia: false,
  password: '',
  emails: '',
  permisos: [emptyPermiso()],
});

const getPermisoScopeLabel = (
  permiso: PermisoFormRow,
  paises: CatalogPais[],
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

  if (permiso.pais_id) {
    const pais = paises.find((item) => item.id === permiso.pais_id);
    return pais ? `País: ${pais.nombre}` : 'País específico';
  }

  return 'Permiso global';
};

const globalAccessPermiso = (): PermisoFormRow => ({
  ...emptyPermiso(),
  puede_ver_cuentas: true,
  puede_ver_dashboard: true,
});

export default function UsuarioModal({
  open,
  editingId,
  titulares,
  cuentas,
  paises,
  onClose,
  onSaved,
}: UsuarioModalProps) {
  const [form, setForm] = useState<UserFormState>(emptyForm);
  const [loading, setLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Snapshot del formulario base para detectar cambios sin guardar.
  const baselineRef = useRef<string | null>(null);
  const { confirm, dialogProps: discardDialogProps } = useConfirmDialog();
  const isDirty = open && baselineRef.current !== null && JSON.stringify(form) !== baselineRef.current;
  useUnsavedChanges(isDirty);
  const dialogRef = useDialogFocus<HTMLDivElement>(open, {
    onEscape: submitting ? undefined : () => void closeModal(),
  });

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
      const fresh = emptyForm();
      setForm(fresh);
      baselineRef.current = JSON.stringify(fresh);
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
          pais_id: permiso.pais_id ?? '',
          titular_id: permiso.titular_id ?? '',
          cuenta_id: permiso.cuenta_id ?? '',
          puede_ver_cuentas: permiso.puede_ver_cuentas ?? false,
          puede_agregar_lineas: permiso.puede_agregar_lineas ?? false,
          puede_editar_lineas: permiso.puede_editar_lineas ?? false,
          puede_eliminar_lineas: permiso.puede_eliminar_lineas ?? false,
          puede_importar: permiso.puede_importar ?? false,
          puede_ver_dashboard: permiso.puede_ver_dashboard ?? false,
          columnas_visibles: (permiso.columnas_visibles ?? []).join(', '),
          columnas_editables: (permiso.columnas_editables ?? []).join(', '),
        }));

        const loadedForm: UserFormState = {
          email: data.usuario.email,
          nombre_completo: data.usuario.nombre_completo,
          rol: data.usuario.rol,
          activo: data.usuario.activo,
          primer_login: data.usuario.primer_login,
          puede_usar_ia: data.usuario.puede_usar_ia,
          password: '',
          emails: (data.emails ?? []).join('\n'),
          permisos: mappedPermisos.length > 0 ? mappedPermisos : [emptyPermiso()],
        };
        setForm(loadedForm);
        baselineRef.current = JSON.stringify(loadedForm);
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
          permiso.puede_ver_cuentas ||
          permiso.puede_agregar_lineas ||
          permiso.puede_editar_lineas ||
          permiso.puede_eliminar_lineas ||
          permiso.puede_importar ||
          permiso.puede_ver_dashboard;
        const hasScope = !!permiso.cuenta_id || !!permiso.titular_id || !!permiso.pais_id;

        if (!hasFlags && !hasScope && !columnasVisibles && !columnasEditables) {
          return null;
        }

        return {
          cuenta_id: permiso.cuenta_id || null,
          titular_id: permiso.titular_id || null,
          pais_id: permiso.pais_id || null,
          puede_ver_cuentas: permiso.puede_ver_cuentas,
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

  const grantAllAccounts = () => {
    setForm((prev) => {
      const globalIndex = prev.permisos.findIndex(
        (permiso) => !permiso.pais_id && !permiso.titular_id && !permiso.cuenta_id
      );

      if (globalIndex >= 0) {
        return {
          ...prev,
          permisos: prev.permisos.map((permiso, index) =>
            index === globalIndex
              ? { ...permiso, puede_ver_cuentas: true, puede_ver_dashboard: true }
              : permiso
          ),
        };
      }

      const hasOnlyBlankRow =
        prev.permisos.length === 1 &&
        !prev.permisos[0].titular_id &&
        !prev.permisos[0].pais_id &&
        !prev.permisos[0].cuenta_id &&
        !prev.permisos[0].puede_ver_cuentas &&
        !prev.permisos[0].puede_agregar_lineas &&
        !prev.permisos[0].puede_editar_lineas &&
        !prev.permisos[0].puede_eliminar_lineas &&
        !prev.permisos[0].puede_importar &&
        !prev.permisos[0].puede_ver_dashboard &&
        !prev.permisos[0].columnas_visibles &&
        !prev.permisos[0].columnas_editables;

      return {
        ...prev,
        permisos: hasOnlyBlankRow
          ? [globalAccessPermiso()]
          : [globalAccessPermiso(), ...prev.permisos],
      };
    });
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

  const closeModal = async () => {
    if (submitting) return;
    if (isDirty) {
      const discard = await confirm({
        title: 'Descartar cambios',
        message: 'Tienes cambios sin guardar en este usuario. Si cierras, se perderán. ¿Descartar?',
        confirmLabel: 'Descartar',
        cancelLabel: 'Seguir editando',
      });
      if (!discard) {
        return;
      }
    }
    onClose();
  };

  const save = async () => {
    if (!form.nombre_completo.trim() || !form.email.trim()) {
      setError('Escribe el nombre y el email del usuario.');
      return;
    }

    if (!editingId && form.password.length < 12) {
      setError('La contraseña debe tener al menos 12 caracteres para crear el usuario.');
      return;
    }

    if (editingId && form.password.length > 0 && form.password.length < 12) {
      setError('La contraseña debe tener al menos 12 caracteres para cambiarla.');
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
      puede_usar_ia: form.puede_usar_ia,
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
      setError(extractErrorMessage(err, 'No se pudo guardar el usuario. Revisa los datos y vuelve a intentarlo.'));
    } finally {
      setSubmitting(false);
    }
  };

  if (!open) {
    return null;
  }

  return (
    <div className="modal-backdrop users-modal-backdrop" onClick={() => void closeModal()}>
      <div
        ref={dialogRef}
        className="users-modal"
        onClick={(event) => event.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-labelledby="usuarios-modal-title"
        tabIndex={-1}
      >
        <div className="users-modal-header">
          <div>
            <h2 id="usuarios-modal-title">{title}</h2>
            <p>Datos base, emails de notificación y permisos granulares.</p>
          </div>
          <CloseIconButton
            className="users-modal-close"
            onClick={() => void closeModal()}
            disabled={submitting}
            ariaLabel="Cerrar modal de usuario"
          />
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
            {error && <p className="auth-error" role="alert">{error}</p>}

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
                    placeholder={editingId ? 'Solo si la quieres cambiar' : 'Mínimo 12 caracteres'}
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
                  Forzar cambio en primer acceso
                </label>
                <label>
                  <input
                    type="checkbox"
                    className="users-check-input"
                    checked={form.puede_usar_ia}
                    onChange={(event) =>
                      setForm((prev) => ({
                        ...prev,
                        puede_usar_ia: event.target.checked,
                      }))
                    }
                  />
                  Puede usar IA
                </label>
              </div>
            </section>

            <section className="users-modal-section users-notifications-section">
              <h3>Emails de notificación</h3>
              <label className="users-notification-field">
                <span>Destinatarios</span>
                <textarea
                  aria-describedby="notification-emails-help"
                  rows={4}
                  placeholder={'alertas@atlasbalance.local\nsupervisor@atlasbalance.local'}
                  value={form.emails}
                  onChange={(event) =>
                    setForm((prev) => ({ ...prev, emails: event.target.value }))
                  }
                />
              </label>
              <p id="notification-emails-help" className="users-field-help">
                Uno por línea o separados por coma.
              </p>
            </section>

            <section className="users-modal-section">
              <div className="users-section-header">
                <div>
                  <h3>Permisos</h3>
                  <p>Usa acceso global para leer todas las cuentas sin regalar edición.</p>
                </div>
                <div className="users-section-actions">
                  <button
                    type="button"
                    className="button-warning"
                    onClick={grantAllAccounts}
                    aria-label="Conceder lectura global a todas las cuentas"
                  >
                    Conceder lectura global
                  </button>
                  <button type="button" className="button-primary" onClick={addPermiso}>
                    Añadir permiso
                  </button>
                </div>
              </div>

              <div className="users-permisos-list">
                {form.permisos.map((permiso, index) => {
                  const cuentasFiltradas = cuentas
                    .filter((cuenta) => !permiso.pais_id || cuenta.pais_id === permiso.pais_id)
                    .filter((cuenta) => !permiso.titular_id || cuenta.titular_id === permiso.titular_id);
                  const titularesFiltrados = permiso.pais_id
                    ? titulares.filter((titular) =>
                        cuentas.some((cuenta) => cuenta.pais_id === permiso.pais_id && cuenta.titular_id === titular.id)
                      )
                    : titulares;
                  const scopeLabel = getPermisoScopeLabel(permiso, paises, titulares, cuentas);

                  return (
                    <div key={permiso.key} className="permiso-row">
                      <div className="users-section-header permiso-row-header">
                        <div className="permiso-row-title">
                          <strong>Permiso #{index + 1}</strong>
                          <p className="permiso-scope">{scopeLabel}</p>
                        </div>
                        <button
                          type="button"
                          className="remove-permiso button-danger"
                          onClick={() => removePermiso(permiso.key)}
                        >
                          Quitar
                        </button>
                      </div>

                      <div className="permiso-grid">
                        <AppSelect
                          label="País"
                          value={permiso.pais_id}
                          options={[
                            { value: '', label: 'Todos los países' },
                            ...paises.map((pais) => ({ value: pais.id, label: pais.nombre })),
                          ]}
                          onChange={(next) =>
                            updatePermiso(permiso.key, {
                              pais_id: next,
                              titular_id: '',
                              cuenta_id: '',
                            })
                          }
                        />

                        <AppSelect
                          label="Titular"
                          value={permiso.titular_id}
                          options={[
                            { value: '', label: 'Global o por cuenta' },
                            ...titularesFiltrados.map((titular) => ({ value: titular.id, label: titular.nombre })),
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
                        <label className="users-check-danger">
                          <input
                            type="checkbox"
                            className="users-check-input"
                            checked={permiso.puede_ver_cuentas}
                            onChange={(event) =>
                              updatePermiso(permiso.key, {
                                puede_ver_cuentas: event.target.checked,
                              })
                            }
                          />
                          Ver cuentas
                        </label>
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
                          Añadir movimientos
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
                          Editar movimientos
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
                          Eliminar movimientos
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
                          Importar extractos
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
                          Ver dashboard
                        </label>
                      </div>
                    </div>
                  );
                })}
              </div>
            </section>

            <div className="users-form-actions users-form-actions--sticky">
              <button type="button" className="button-secondary" onClick={() => void closeModal()} disabled={submitting}>
                Cancelar
              </button>
              <button type="submit" className="button-primary" disabled={submitting}>
                {submitting ? 'Guardando...' : editingId ? 'Guardar cambios' : 'Crear usuario'}
              </button>
            </div>
          </form>
        )}
      </div>
      <ConfirmDialog {...discardDialogProps} />
    </div>
  );
}
