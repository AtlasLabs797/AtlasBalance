import { FormEvent, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { AppSelect } from '@/components/common/AppSelect';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { SignedAmount } from '@/components/common/SignedAmount';
import api from '@/services/api';
import { useAlertasStore } from '@/stores/alertasStore';
import { useAuthStore } from '@/stores/authStore';
import type { TipoTitular } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatCurrency, formatDateTime } from '@/utils/formatters';

interface AlertaContextoCuenta {
  id: string;
  nombre: string;
  titular_id: string;
  titular_nombre: string;
  divisa: string;
}

interface AlertaContextoUsuario {
  id: string;
  nombre_completo: string;
  email: string;
}

interface AlertaDestinatario {
  usuario_id: string;
  nombre_completo: string;
  email_login: string;
}

interface AlertaItem {
  id: string;
  cuenta_id: string | null;
  tipo_titular: TipoTitular | null;
  alcance: 'GLOBAL' | 'TIPO_TITULAR' | 'CUENTA';
  cuenta_nombre: string | null;
  titular_id: string | null;
  titular_nombre: string | null;
  divisa: string | null;
  saldo_minimo: number;
  activa: boolean;
  fecha_creacion: string;
  fecha_ultima_alerta: string | null;
  destinatarios: AlertaDestinatario[];
}

interface SaveAlertaPayload {
  cuenta_id: string | null;
  tipo_titular: TipoTitular | null;
  saldo_minimo: number;
  activa: boolean;
  destinatario_usuario_ids: string[];
}

const EMPTY_FORM: SaveAlertaPayload = {
  cuenta_id: null,
  tipo_titular: null,
  saldo_minimo: 0,
  activa: true,
  destinatario_usuario_ids: [],
};

function getErrorMessage(error: unknown, fallback: string) {
  return extractErrorMessage(error, fallback);
}

export default function AlertasPage() {
  const usuario = useAuthStore((state) => state.usuario);
  const isAdmin = usuario?.rol === 'ADMIN';
  const alertasActivas = useAlertasStore((state) => state.alertasActivas);
  const activeLoading = useAlertasStore((state) => state.loading);
  const activeError = useAlertasStore((state) => state.lastError);
  const loadAlertasActivas = useAlertasStore((state) => state.loadAlertasActivas);

  const [configLoading, setConfigLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<string | null>(null);
  const [alertas, setAlertas] = useState<AlertaItem[]>([]);
  const [cuentas, setCuentas] = useState<AlertaContextoCuenta[]>([]);
  const [usuarios, setUsuarios] = useState<AlertaContextoUsuario[]>([]);
  const [globalForm, setGlobalForm] = useState<SaveAlertaPayload>(EMPTY_FORM);
  const [cuentaForm, setCuentaForm] = useState<SaveAlertaPayload>(EMPTY_FORM);
  const [tipoForm, setTipoForm] = useState<SaveAlertaPayload>({ ...EMPTY_FORM, tipo_titular: 'AUTONOMO' });
  const [editingCuentaAlertId, setEditingCuentaAlertId] = useState<string | null>(null);
  const [editingTipoAlertId, setEditingTipoAlertId] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<AlertaItem | null>(null);

  const cuentaAlerts = useMemo(() => alertas.filter((item) => item.cuenta_id !== null), [alertas]);
  const tipoAlerts = useMemo(() => alertas.filter((item) => item.cuenta_id === null && item.tipo_titular !== null), [alertas]);
  const onlyGlobalAlert = useMemo(() => alertas.find((item) => item.cuenta_id === null && item.tipo_titular === null) ?? null, [alertas]);
  const tipoTitularOptions: Array<{ value: TipoTitular; label: string }> = [
    { value: 'EMPRESA', label: 'Empresa' },
    { value: 'AUTONOMO', label: 'Autónomo' },
    { value: 'PARTICULAR', label: 'Particular' },
  ];
  const availableCuentas = useMemo(() => {
    const occupied = new Set(
      cuentaAlerts
        .filter((item) => item.id !== editingCuentaAlertId && item.cuenta_id)
        .map((item) => item.cuenta_id as string)
    );

    return cuentas.filter((cuenta) => cuenta.id === cuentaForm.cuenta_id || !occupied.has(cuenta.id));
  }, [cuentaAlerts, cuentas, cuentaForm.cuenta_id, editingCuentaAlertId]);

  const hydrateForms = (items: AlertaItem[]) => {
    const global = items.find((item) => item.cuenta_id === null && item.tipo_titular === null) ?? null;
    if (global) {
      setGlobalForm({
        cuenta_id: null,
        tipo_titular: null,
        saldo_minimo: global.saldo_minimo,
        activa: global.activa,
        destinatario_usuario_ids: global.destinatarios.map((d) => d.usuario_id),
      });
    } else {
      setGlobalForm({ ...EMPTY_FORM, cuenta_id: null });
    }
  };

  const loadConfiguracion = async () => {
    if (!isAdmin) {
      return;
    }

    setConfigLoading(true);
    setError(null);

    try {
      const [contextoRes, alertasRes] = await Promise.all([
        api.get<{ cuentas: AlertaContextoCuenta[]; usuarios: AlertaContextoUsuario[] }>('/alertas/contexto'),
        api.get<AlertaItem[]>('/alertas'),
      ]);

      setCuentas(contextoRes.data.cuentas);
      setUsuarios(contextoRes.data.usuarios);
      setAlertas(alertasRes.data);
      hydrateForms(alertasRes.data);
    } catch (loadError: unknown) {
      setError(getErrorMessage(loadError, 'No se pudo cargar la configuración de alertas.'));
    } finally {
      setConfigLoading(false);
    }
  };

  useEffect(() => {
    void loadAlertasActivas();
  }, [loadAlertasActivas]);

  useEffect(() => {
    if (!isAdmin) {
      setConfigLoading(false);
      setAlertas([]);
      setCuentas([]);
      setUsuarios([]);
      return;
    }

    void loadConfiguracion();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- se ejecuta al cambiar rol admin
  }, [isAdmin]);

  const saveGlobalAlert = async (event: FormEvent) => {
    event.preventDefault();
    setSaving(true);
    setError(null);
    setFeedback(null);

    try {
      if (onlyGlobalAlert) {
        await api.put(`/alertas/${onlyGlobalAlert.id}`, globalForm);
      } else {
        await api.post('/alertas', globalForm);
      }

      setFeedback('Alerta global guardada.');
      await Promise.all([loadConfiguracion(), loadAlertasActivas()]);
    } catch (saveError: unknown) {
      setError(getErrorMessage(saveError, 'No se pudo guardar la alerta global.'));
    } finally {
      setSaving(false);
    }
  };

  const saveCuentaAlert = async (event: FormEvent) => {
    event.preventDefault();
    if (!cuentaForm.cuenta_id) {
      setError('Selecciona una cuenta.');
      return;
    }

    setSaving(true);
    setError(null);
    setFeedback(null);

    try {
      const payload = { ...cuentaForm, tipo_titular: null };
      if (editingCuentaAlertId) {
        await api.put(`/alertas/${editingCuentaAlertId}`, payload);
      } else {
        await api.post('/alertas', payload);
      }

      setFeedback(editingCuentaAlertId ? 'Alerta de cuenta actualizada.' : 'Alerta de cuenta creada.');
      setEditingCuentaAlertId(null);
      setCuentaForm(EMPTY_FORM);
      await Promise.all([loadConfiguracion(), loadAlertasActivas()]);
    } catch (saveError: unknown) {
      setError(getErrorMessage(saveError, 'No se pudo guardar la alerta de cuenta.'));
    } finally {
      setSaving(false);
    }
  };

  const saveTipoAlert = async (event: FormEvent) => {
    event.preventDefault();
    if (!tipoForm.tipo_titular) {
      setError('Selecciona un tipo de titular.');
      return;
    }

    setSaving(true);
    setError(null);
    setFeedback(null);

    try {
      const payload = { ...tipoForm, cuenta_id: null };
      if (editingTipoAlertId) {
        await api.put(`/alertas/${editingTipoAlertId}`, payload);
      } else {
        await api.post('/alertas', payload);
      }

      setFeedback(editingTipoAlertId ? 'Alerta por tipo actualizada.' : 'Alerta por tipo creada.');
      setEditingTipoAlertId(null);
      setTipoForm({ ...EMPTY_FORM, tipo_titular: 'AUTONOMO' });
      await Promise.all([loadConfiguracion(), loadAlertasActivas()]);
    } catch (saveError: unknown) {
      setError(getErrorMessage(saveError, 'No se pudo guardar la alerta por tipo.'));
    } finally {
      setSaving(false);
    }
  };

  const editCuentaAlert = (item: AlertaItem) => {
    setEditingCuentaAlertId(item.id);
    setCuentaForm({
      cuenta_id: item.cuenta_id,
      tipo_titular: null,
      saldo_minimo: item.saldo_minimo,
      activa: item.activa,
      destinatario_usuario_ids: item.destinatarios.map((d) => d.usuario_id),
    });
    setFeedback(null);
    setError(null);
  };

  const editTipoAlert = (item: AlertaItem) => {
    setEditingTipoAlertId(item.id);
    setTipoForm({
      cuenta_id: null,
      tipo_titular: item.tipo_titular ?? 'AUTONOMO',
      saldo_minimo: item.saldo_minimo,
      activa: item.activa,
      destinatario_usuario_ids: item.destinatarios.map((d) => d.usuario_id),
    });
    setFeedback(null);
    setError(null);
  };

  const deleteAlert = async (alertId: string) => {
    setSaving(true);
    setError(null);
    setFeedback(null);

    try {
      await api.delete(`/alertas/${alertId}`);
      if (editingCuentaAlertId === alertId) {
        setEditingCuentaAlertId(null);
        setCuentaForm(EMPTY_FORM);
      }
      if (editingTipoAlertId === alertId) {
        setEditingTipoAlertId(null);
        setTipoForm({ ...EMPTY_FORM, tipo_titular: 'AUTONOMO' });
      }

      setFeedback('Alerta eliminada.');
      await Promise.all([loadConfiguracion(), loadAlertasActivas()]);
    } catch (deleteError: unknown) {
      setError(getErrorMessage(deleteError, 'No se pudo eliminar la alerta.'));
    } finally {
      setSaving(false);
    }
  };

  const toggleDestinatario = (
    userId: string,
    current: SaveAlertaPayload,
    setter: (value: SaveAlertaPayload) => void
  ) => {
    const exists = current.destinatario_usuario_ids.includes(userId);
    const next = exists
      ? current.destinatario_usuario_ids.filter((id) => id !== userId)
      : [...current.destinatario_usuario_ids, userId];

    setter({ ...current, destinatario_usuario_ids: next });
  };

  return (
    <section className="alertas-page">
      <header>
        <h1>Alertas de Saldo Bajo</h1>
        <p className="users-subtitle">
          {isAdmin
            ? 'Configuración y seguimiento de cuentas por debajo del mínimo.'
            : 'Estado actual de las cuentas por debajo del mínimo dentro de tu alcance.'}
        </p>
      </header>

      {error ? <p className="auth-error">{error}</p> : null}
      {feedback ? <p className="config-feedback">{feedback}</p> : null}

      <article className="alertas-card">
        <div className="users-section-header">
          <div>
            <h2>Alertas Activas</h2>
            <p>
              Se evalúan al crear o editar extractos. El banner superior y este listado usan la misma fuente.
            </p>
          </div>
          <button type="button" onClick={() => void loadAlertasActivas()} disabled={activeLoading}>
            {activeLoading ? 'Actualizando...' : 'Recargar'}
          </button>
        </div>

        {activeError ? <p className="auth-error">{activeError}</p> : null}

        {!activeError && activeLoading && alertasActivas.length === 0 ? (
          <p className="import-muted">Cargando alertas activas...</p>
        ) : null}

        {!activeError && !activeLoading && alertasActivas.length === 0 ? (
          <p className="import-muted">No hay cuentas por debajo del mínimo en este momento.</p>
        ) : null}

        {alertasActivas.length > 0 ? (
          <div className="config-table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Titular</th>
                  <th>Cuenta</th>
                  <th>Divisa</th>
                  <th>Saldo actual</th>
                  <th>Saldo mínimo</th>
                  <th>Acción</th>
                </tr>
              </thead>
              <tbody>
                {alertasActivas.map((item) => (
                  <tr key={`${item.alerta_id}-${item.cuenta_id}`}>
                    <td>{item.titular_nombre}</td>
                    <td>{item.cuenta_nombre}</td>
                    <td>{item.divisa}</td>
                    <td>
                      <SignedAmount value={item.saldo_actual}>{formatCurrency(item.saldo_actual, item.divisa ?? 'EUR')}</SignedAmount>
                    </td>
                    <td>
                      <SignedAmount value={item.saldo_minimo}>{formatCurrency(item.saldo_minimo, item.divisa ?? 'EUR')}</SignedAmount>
                    </td>
                    <td>
                      <Link to={`/cuentas/${item.cuenta_id}`}>Abrir cuenta</Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </article>

      {isAdmin ? (
        <>
          <article className="alertas-card">
            <div className="users-section-header">
              <div>
                <h2>Alerta Global</h2>
                <p>Aplica cuando una cuenta no tiene alerta específica.</p>
              </div>
            </div>

            {configLoading ? <p className="import-muted">Cargando configuración...</p> : null}

            <form className="alertas-form" onSubmit={saveGlobalAlert}>
              <label>
                Saldo mínimo global
                <input
                  type="number"
                  step="0.01"
                  min="0"
                  value={globalForm.saldo_minimo}
                  onChange={(event) =>
                    setGlobalForm({ ...globalForm, saldo_minimo: Number(event.target.value || '0') })
                  }
                />
              </label>
              <label className="config-check">
                <input
                  type="checkbox"
                  checked={globalForm.activa}
                  onChange={(event) => setGlobalForm({ ...globalForm, activa: event.target.checked })}
                />
                Activa
              </label>
              <div className="alertas-destinatarios">
                <h3>Destinatarios</h3>
                {usuarios.map((user) => (
                  <label key={user.id} className="users-check-row-item">
                    <input
                      type="checkbox"
                      checked={globalForm.destinatario_usuario_ids.includes(user.id)}
                      onChange={() => toggleDestinatario(user.id, globalForm, setGlobalForm)}
                    />
                    {user.nombre_completo} ({user.email})
                  </label>
                ))}
              </div>
              <button type="submit" disabled={saving || configLoading}>
                {onlyGlobalAlert ? 'Actualizar global' : 'Crear global'}
              </button>
            </form>
          </article>

          <article className="alertas-card">
            <h2>{editingTipoAlertId ? 'Editar alerta por tipo de titular' : 'Nueva alerta por tipo de titular'}</h2>

            <form className="alertas-form" onSubmit={saveTipoAlert}>
              <AppSelect
                label="Tipo de titular"
                value={tipoForm.tipo_titular ?? 'AUTONOMO'}
                options={tipoTitularOptions}
                onChange={(next) => setTipoForm({ ...tipoForm, tipo_titular: next as TipoTitular })}
              />
              <label>
                Saldo mínimo
                <input
                  type="number"
                  step="0.01"
                  min="0"
                  value={tipoForm.saldo_minimo}
                  onChange={(event) =>
                    setTipoForm({ ...tipoForm, saldo_minimo: Number(event.target.value || '0') })
                  }
                />
              </label>
              <label className="config-check">
                <input
                  type="checkbox"
                  checked={tipoForm.activa}
                  onChange={(event) => setTipoForm({ ...tipoForm, activa: event.target.checked })}
                />
                Activa
              </label>
              <div className="alertas-destinatarios">
                <h3>Destinatarios</h3>
                {usuarios.map((user) => (
                  <label key={user.id} className="users-check-row-item">
                    <input
                      type="checkbox"
                      checked={tipoForm.destinatario_usuario_ids.includes(user.id)}
                      onChange={() => toggleDestinatario(user.id, tipoForm, setTipoForm)}
                    />
                    {user.nombre_completo} ({user.email})
                  </label>
                ))}
              </div>
              <div className="users-row-actions">
                <button type="submit" disabled={saving || configLoading}>
                  {editingTipoAlertId ? 'Guardar cambios' : 'Crear alerta'}
                </button>
                {editingTipoAlertId ? (
                  <button
                    type="button"
                    onClick={() => {
                      setEditingTipoAlertId(null);
                      setTipoForm({ ...EMPTY_FORM, tipo_titular: 'AUTONOMO' });
                    }}
                  >
                    Cancelar edición
                  </button>
                ) : null}
              </div>
            </form>
          </article>

          <article className="alertas-card">
            <h2>{editingCuentaAlertId ? 'Editar alerta por cuenta' : 'Nueva alerta por cuenta'}</h2>

            <form className="alertas-form" onSubmit={saveCuentaAlert}>
              <AppSelect
                label="Cuenta"
                value={cuentaForm.cuenta_id ?? ''}
                options={[
                  { value: '', label: 'Seleccionar cuenta' },
                  ...availableCuentas.map((cuenta) => ({
                    value: cuenta.id,
                    label: `${cuenta.titular_nombre} - ${cuenta.nombre} (${cuenta.divisa})`,
                  })),
                ]}
                onChange={(next) =>
                  setCuentaForm({
                    ...cuentaForm,
                    cuenta_id: next || null,
                  })
                }
              />
              <label>
                Saldo mínimo
                <input
                  type="number"
                  step="0.01"
                  min="0"
                  value={cuentaForm.saldo_minimo}
                  onChange={(event) =>
                    setCuentaForm({ ...cuentaForm, saldo_minimo: Number(event.target.value || '0') })
                  }
                />
              </label>
              <label className="config-check">
                <input
                  type="checkbox"
                  checked={cuentaForm.activa}
                  onChange={(event) => setCuentaForm({ ...cuentaForm, activa: event.target.checked })}
                />
                Activa
              </label>
              <div className="alertas-destinatarios">
                <h3>Destinatarios</h3>
                {usuarios.map((user) => (
                  <label key={user.id} className="users-check-row-item">
                    <input
                      type="checkbox"
                      checked={cuentaForm.destinatario_usuario_ids.includes(user.id)}
                      onChange={() => toggleDestinatario(user.id, cuentaForm, setCuentaForm)}
                    />
                    {user.nombre_completo} ({user.email})
                  </label>
                ))}
              </div>
              <div className="users-row-actions">
                <button type="submit" disabled={saving || configLoading || availableCuentas.length === 0}>
                  {editingCuentaAlertId ? 'Guardar cambios' : 'Crear alerta'}
                </button>
                {editingCuentaAlertId ? (
                  <button
                    type="button"
                    onClick={() => {
                      setEditingCuentaAlertId(null);
                      setCuentaForm(EMPTY_FORM);
                    }}
                  >
                    Cancelar edición
                  </button>
                ) : null}
              </div>
            </form>
          </article>

          <article className="alertas-card">
            <h2>Alertas por Tipo de Titular</h2>
            {configLoading ? <p className="import-muted">Cargando alertas configuradas...</p> : null}

            {!configLoading && tipoAlerts.length === 0 ? (
              <p className="import-muted">No hay alertas por tipo configuradas.</p>
            ) : null}

            {tipoAlerts.length > 0 ? (
              <div className="config-table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Alcance</th>
                      <th>Mínimo</th>
                      <th>Activa</th>
                      <th>Destinatarios</th>
                      <th>Última alerta</th>
                      <th>Acciones</th>
                    </tr>
                  </thead>
                  <tbody>
                    {tipoAlerts.map((item) => (
                      <tr key={item.id}>
                        <td>Tipo: {item.tipo_titular}</td>
                        <td><SignedAmount value={item.saldo_minimo}>{formatCurrency(item.saldo_minimo, item.divisa ?? 'EUR')}</SignedAmount></td>
                        <td>{item.activa ? 'Sí' : 'No'}</td>
                        <td>{item.destinatarios.map((d) => d.nombre_completo).join(', ') || '—'}</td>
                        <td>{item.fecha_ultima_alerta ? formatDateTime(item.fecha_ultima_alerta) : '—'}</td>
                        <td>
                          <div className="users-row-actions">
                            <button type="button" onClick={() => editTipoAlert(item)}>
                              Editar
                            </button>
                            <button type="button" onClick={() => setDeleteTarget(item)} disabled={saving}>
                              Eliminar alerta
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : null}
          </article>

          <article className="alertas-card">
            <h2>Alertas por Cuenta</h2>
            {configLoading ? <p className="import-muted">Cargando alertas configuradas...</p> : null}

            {!configLoading && cuentaAlerts.length === 0 ? (
              <p className="import-muted">No hay alertas por cuenta configuradas.</p>
            ) : null}

            {cuentaAlerts.length > 0 ? (
              <div className="config-table-wrap">
                <table>
                  <thead>
                  <tr>
                    <th>Titular</th>
                    <th>Cuenta</th>
                    <th>Divisa</th>
                    <th>Mínimo</th>
                    <th>Activa</th>
                    <th>Destinatarios</th>
                      <th>Última alerta</th>
                    <th>Acciones</th>
                  </tr>
                </thead>
                <tbody>
                  {cuentaAlerts.map((item) => (
                    <tr key={item.id}>
                        <td>{item.titular_nombre ?? '—'}</td>
                        <td>{item.cuenta_nombre ?? '—'}</td>
                        <td>{item.divisa ?? '—'}</td>
                        <td><SignedAmount value={item.saldo_minimo}>{formatCurrency(item.saldo_minimo, item.divisa ?? 'EUR')}</SignedAmount></td>
                        <td>{item.activa ? 'Sí' : 'No'}</td>
                        <td>{item.destinatarios.map((d) => d.nombre_completo).join(', ') || '—'}</td>
                        <td>{item.fecha_ultima_alerta ? formatDateTime(item.fecha_ultima_alerta) : '—'}</td>
                        <td>
                          <div className="users-row-actions">
                            <button type="button" onClick={() => editCuentaAlert(item)}>
                              Editar
                            </button>
                            <button type="button" onClick={() => setDeleteTarget(item)} disabled={saving}>
                              Eliminar alerta
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : null}
          </article>
        </>
      ) : null}

      <ConfirmDialog
        open={deleteTarget !== null}
        title="Eliminar alerta"
        message={
          deleteTarget?.cuenta_nombre
            ? `Vas a eliminar la alerta de ${deleteTarget.cuenta_nombre}.`
            : deleteTarget?.tipo_titular
              ? `Vas a eliminar la alerta para titulares de tipo ${deleteTarget.tipo_titular}.`
              : 'Vas a eliminar la alerta global.'
        }
        confirmLabel="Eliminar alerta"
        loadingLabel="Eliminando..."
        loading={saving}
        onCancel={() => setDeleteTarget(null)}
        onConfirm={async () => {
          if (!deleteTarget) return;
          await deleteAlert(deleteTarget.id);
          setDeleteTarget(null);
        }}
      />
    </section>
  );
}
