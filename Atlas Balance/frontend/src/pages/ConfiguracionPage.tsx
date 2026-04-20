import { FormEvent, useEffect, useMemo, useState } from 'react';
import { AppSelect } from '@/components/common/AppSelect';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import { useUpdateStore } from '@/stores/updateStore';
import { CreateTokenModal } from '@/components/integraciones/CreateTokenModal';
import { TokenCreatedModal } from '@/components/integraciones/TokenCreatedModal';
import { TokenList } from '@/components/integraciones/TokenList';
import { extractErrorMessage } from '@/utils/errorMessage';
import type {
  ConfiguracionSistema,
  DivisaActiva,
  IntegrationTokenListItem,
  PaginatedResponse,
  TipoCambio,
  WatchdogState,
} from '@/types';

type TabKey = 'general' | 'divisas' | 'sistema' | 'integraciones';
const STALE_THRESHOLD_MS = 24 * 60 * 60 * 1000;

const tabs: Array<{ key: TabKey; label: string }> = [
  { key: 'general', label: 'General + SMTP' },
  { key: 'divisas', label: 'Divisas y Tipos' },
  { key: 'sistema', label: 'Sistema' },
  { key: 'integraciones', label: 'Integraciones' },
];

interface CatalogoPermisos {
  titulares: Array<{ id: string; nombre: string }>;
  cuentas: Array<{ id: string; nombre: string; titular_id: string }>;
}

function formatDateTime(value: string | null) {
  if (!value) {
    return 'Sin datos';
  }

  return new Date(value).toLocaleString('es-ES');
}

export default function ConfiguracionPage() {
  const [tab, setTab] = useState<TabKey>('general');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<string | null>(null);

  const [config, setConfig] = useState<ConfiguracionSistema>({
    smtp: { host: '', port: 587, user: '', password: '', from: '' },
    general: { app_base_url: '', app_update_check_url: '', backup_path: '', export_path: '' },
    exchange: { api_key: '', api_key_configurada: false },
    dashboard: { color_ingresos: '#43B430', color_egresos: '#FF4757', color_saldo: '#7B7B7B' },
  });
  const [smtpTo, setSmtpTo] = useState('');

  const [tipos, setTipos] = useState<TipoCambio[]>([]);
  const [divisas, setDivisas] = useState<DivisaActiva[]>([]);
  const [divisaPorDefecto, setDivisaPorDefecto] = useState('EUR');
  const [manualRate, setManualRate] = useState({ origen: 'EUR', destino: 'USD', tasa: '' });
  const [nuevaDivisa, setNuevaDivisa] = useState({ codigo: '', nombre: '', simbolo: '' });

  const [tokens, setTokens] = useState<IntegrationTokenListItem[]>([]);
  const [catalogos, setCatalogos] = useState<CatalogoPermisos>({ titulares: [], cuentas: [] });
  const [showCreateTokenModal, setShowCreateTokenModal] = useState(false);
  const [tokenPlano, setTokenPlano] = useState<string | null>(null);

  const logout = useAuthStore((state) => state.logout);
  const updateAvailable = useUpdateStore((state) => state.available);
  const currentVersion = useUpdateStore((state) => state.currentVersion);
  const availableVersion = useUpdateStore((state) => state.availableVersion);
  const checkUpdate = useUpdateStore((state) => state.check);
  const updateMessage = useUpdateStore((state) => state.message);

  const lastSync = useMemo(() => {
    if (tipos.length === 0) return null;
    return tipos.reduce((latest, current) =>
      new Date(current.fecha_actualizacion) > new Date(latest.fecha_actualizacion) ? current : latest);
  }, [tipos]);

  const isStale = useMemo(() => {
    if (!lastSync) return true;
    return Date.now() - new Date(lastSync.fecha_actualizacion).getTime() > STALE_THRESHOLD_MS;
  }, [lastSync]);

  const divisasActivas = useMemo(() => divisas.filter((item) => item.activa).map((item) => item.codigo), [divisas]);
  const tiposOrdenados = useMemo(
    () => [...tipos].sort((left, right) => left.divisa_origen.localeCompare(right.divisa_origen) || left.divisa_destino.localeCompare(right.divisa_destino)),
    [tipos]
  );
  const canEditRates = divisasActivas.length >= 2;
  const exchangeApiConfigured = config.exchange.api_key_configurada;

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const [cfg, tiposRes, divisasRes, tokensRes, catalogosRes] = await Promise.all([
        api.get<ConfiguracionSistema>('/configuracion'),
        api.get<TipoCambio[]>('/tipos-cambio'),
        api.get<DivisaActiva[]>('/divisas'),
        api.get<PaginatedResponse<IntegrationTokenListItem>>('/integraciones/tokens', { params: { page: 1, pageSize: 100 } }),
        api.get<CatalogoPermisos>('/usuarios/catalogos-permisos'),
      ]);
      const nextDivisas = divisasRes.data ?? [];
      const activeCodes = nextDivisas.filter((item) => item.activa).map((item) => item.codigo);
      const baseDivisa = nextDivisas.find((d) => d.es_base)?.codigo ?? activeCodes[0] ?? 'EUR';
      setConfig({
        ...cfg.data,
        exchange: cfg.data.exchange ?? { api_key: '', api_key_configurada: false },
      });
      setSmtpTo(cfg.data.smtp.from);
      setTipos(tiposRes.data ?? []);
      setDivisas(nextDivisas);
      setDivisaPorDefecto(baseDivisa);
      setTokens(tokensRes.data.data ?? []);
      setCatalogos(catalogosRes.data);
      setManualRate((prev) => {
        const origen = activeCodes.includes(prev.origen) ? prev.origen : (activeCodes[0] ?? '');
        const destinosDisponibles = activeCodes.filter((code) => code !== origen);
        const destino = destinosDisponibles.includes(prev.destino) ? prev.destino : (destinosDisponibles[0] ?? '');
        return { ...prev, origen, destino };
      });
      await checkUpdate(true);
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo cargar configuracion.'));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- carga inicial explícita de configuración
  }, []);

  const saveConfig = async (message: string) => {
    await api.put('/configuracion', config);
    setConfig((prev) => ({
      ...prev,
      smtp: { ...prev.smtp, password: '' },
      exchange: { ...prev.exchange, api_key: '', api_key_configurada: prev.exchange.api_key_configurada || prev.exchange.api_key.trim().length > 0 },
    }));
    setFeedback(message);
  };

  const handleSaveConfig = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setFeedback(null);
    try {
      await saveConfig('Configuracion guardada.');
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo guardar.'));
    } finally {
      setBusy(false);
    }
  };

  const handleSaveSystemConfig = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setFeedback(null);
    try {
      await saveConfig('URL de actualizaciones guardada.');
      await checkUpdate(true);
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo guardar la URL de actualizaciones.'));
    } finally {
      setBusy(false);
    }
  };

  const sendTestEmail = async () => {
    setBusy(true);
    setError(null);
    setFeedback(null);
    try {
      await api.post('/configuracion/smtp/test', { to: smtpTo || null });
      setFeedback('Correo de prueba enviado.');
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo enviar correo de prueba.'));
    } finally {
      setBusy(false);
    }
  };

  const updateNow = async () => {
    setBusy(true);
    setError(null);
    try {
      await api.post('/sistema/actualizar', {});
      const timeoutAt = Date.now() + 10 * 60 * 1000;
      while (Date.now() < timeoutAt) {
        const { data } = await api.get<WatchdogState>('/sistema/estado');
        const state = (data.estado ?? '').toUpperCase();
        if (state === 'SUCCESS') {
          sessionStorage.setItem('atlas_balance_update_message', 'Aplicación actualizada correctamente.');
          try {
            await api.post('/auth/logout');
          } catch {
            // Si el watchdog ya reinicio la API, al menos limpiamos el estado local.
          }
          logout();
          window.location.href = '/login';
          return;
        }
        if (state === 'FAILED') {
          setError(data.mensaje || 'La actualizacion fallo.');
          break;
        }
        await new Promise((resolve) => setTimeout(resolve, 2500));
      }
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo actualizar.'));
    } finally {
      setBusy(false);
    }
  };

  const saveManualRate = async (event: FormEvent) => {
    event.preventDefault();
    if (!manualRate.origen || !manualRate.destino || manualRate.origen === manualRate.destino) {
      setError('Selecciona dos divisas activas distintas.');
      return;
    }
    const tasa = Number(manualRate.tasa);
    if (!Number.isFinite(tasa) || tasa <= 0) {
      setError('Tasa invalida.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await api.put(`/tipos-cambio/${manualRate.origen}/${manualRate.destino}`, { tasa });
      setFeedback('Tipo de cambio actualizado.');
      await load();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo actualizar tipo de cambio.'));
    } finally {
      setBusy(false);
    }
  };

  const syncRates = async () => {
    setBusy(true);
    setError(null);
    setFeedback(null);
    try {
      await api.post('/tipos-cambio/sincronizar');
      setFeedback('Tipos de cambio sincronizados.');
      await load();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo sincronizar tipos de cambio.'));
    } finally {
      setBusy(false);
    }
  };

  const updateDivisaField = (codigo: string, patch: Partial<DivisaActiva>) => {
    setDivisas((prev) =>
      prev.map((divisa) => {
        if (divisa.codigo !== codigo) {
          return patch.es_base ? { ...divisa, es_base: false } : divisa;
        }

        return { ...divisa, ...patch };
      })
    );
  };

  const saveDivisa = async (codigo: string) => {
    const divisa = divisas.find((item) => item.codigo === codigo);
    if (!divisa) {
      return;
    }

    setBusy(true);
    setError(null);
    setFeedback(null);
    try {
      await api.put(`/divisas/${codigo}`, {
        nombre: divisa.nombre || null,
        simbolo: divisa.simbolo || null,
        activa: divisa.activa,
        es_base: divisa.es_base,
      });
      setFeedback(`Divisa ${codigo} actualizada.`);
      await load();
    } catch (err) {
      setError(extractErrorMessage(err, `No se pudo actualizar la divisa ${codigo}.`));
    } finally {
      setBusy(false);
    }
  };

  const createDivisa = async (event: FormEvent) => {
    event.preventDefault();
    if (!nuevaDivisa.codigo.trim()) {
      setError('Codigo obligatorio.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await api.post('/divisas', {
        codigo: nuevaDivisa.codigo.trim().toUpperCase(),
        nombre: nuevaDivisa.nombre || null,
        simbolo: nuevaDivisa.simbolo || null,
        activa: true,
        es_base: false,
      });
      setFeedback('Divisa creada.');
      setNuevaDivisa({ codigo: '', nombre: '', simbolo: '' });
      await load();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo crear divisa.'));
    } finally {
      setBusy(false);
    }
  };

  const revokeToken = async (id: string) => {
    setBusy(true);
    setError(null);
    setFeedback(null);
    try {
      await api.post(`/integraciones/tokens/${id}/revocar`);
      setFeedback('Token revocado.');
      await load();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo revocar token.'));
    } finally {
      setBusy(false);
    }
  };

  const deleteToken = async (id: string) => {
    setBusy(true);
    setError(null);
    setFeedback(null);
    try {
      await api.delete(`/integraciones/tokens/${id}`);
      setFeedback('Token eliminado.');
      await load();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo eliminar token.'));
    } finally {
      setBusy(false);
    }
  };

  const guardarDivisaPorDefecto = async () => {
    setBusy(true);
    setError(null);
    setFeedback(null);
    try {
      await api.post('/divisas/establecer-por-defecto', { codigo: divisaPorDefecto });
      setFeedback(`Divisa por defecto establecida en ${divisaPorDefecto}.`);
      await load();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo guardar divisa por defecto.'));
    } finally {
      setBusy(false);
    }
  };

  if (loading) return <PageSkeleton rows={5} />;

  return (
    <section className="config-page">
      <header className="config-header">
        <h1>Configuracion</h1>
      </header>

      <div className="config-tabs">
        {tabs.map((item) => (
          <button key={item.key} type="button" className={tab === item.key ? 'config-tab config-tab--active' : 'config-tab'} onClick={() => setTab(item.key)}>
            {item.label}
          </button>
        ))}
      </div>

      {error ? <p className="auth-error">{error}</p> : null}
      {feedback ? <p className="config-feedback">{feedback}</p> : null}

      {tab === 'general' && (
        <form className="config-card config-card--general" onSubmit={handleSaveConfig}>
          <header className="config-card-headline">
            <h2>General y SMTP</h2>
            <p className="config-subtitle">Configura rutas, servidor de correo y estilo de dashboard en una sola vista clara.</p>
          </header>

          <div className="config-general-layout">
            <article className="config-section-panel">
              <h3>Rutas del sistema</h3>
              <div className="config-field-grid">
                <label className="config-field">
                  <span>App URL</span>
                  <input value={config.general.app_base_url} onChange={(e) => setConfig((p) => ({ ...p, general: { ...p.general, app_base_url: e.target.value } }))} />
                </label>
                <label className="config-field">
                  <span>Backup path</span>
                  <input value={config.general.backup_path} onChange={(e) => setConfig((p) => ({ ...p, general: { ...p.general, backup_path: e.target.value } }))} />
                </label>
                <label className="config-field">
                  <span>Export path</span>
                  <input value={config.general.export_path} onChange={(e) => setConfig((p) => ({ ...p, general: { ...p.general, export_path: e.target.value } }))} />
                </label>
              </div>
            </article>

            <article className="config-section-panel">
              <h3>Servidor SMTP</h3>
              <div className="config-field-grid">
                <label className="config-field">
                  <span>SMTP host</span>
                  <input value={config.smtp.host} onChange={(e) => setConfig((p) => ({ ...p, smtp: { ...p.smtp, host: e.target.value } }))} />
                </label>
                <label className="config-field">
                  <span>SMTP port</span>
                  <input type="number" value={config.smtp.port} onChange={(e) => setConfig((p) => ({ ...p, smtp: { ...p.smtp, port: Number(e.target.value) || 0 } }))} />
                </label>
                <label className="config-field">
                  <span>SMTP user</span>
                  <input value={config.smtp.user} onChange={(e) => setConfig((p) => ({ ...p, smtp: { ...p.smtp, user: e.target.value } }))} />
                </label>
                <label className="config-field">
                  <span>SMTP password</span>
                  <input type="password" placeholder="Dejar en blanco para conservar" value={config.smtp.password} onChange={(e) => setConfig((p) => ({ ...p, smtp: { ...p.smtp, password: e.target.value } }))} />
                </label>
                <label className="config-field">
                  <span>SMTP from</span>
                  <input value={config.smtp.from} onChange={(e) => setConfig((p) => ({ ...p, smtp: { ...p.smtp, from: e.target.value } }))} />
                </label>
              </div>

              <div className="config-inline-action">
                <label className="config-field">
                  <span>Email de prueba</span>
                  <input value={smtpTo} onChange={(e) => setSmtpTo(e.target.value)} />
                </label>
                <button className="button-secondary config-inline-button" type="button" onClick={sendTestEmail} disabled={busy}>
                  Enviar email de prueba
                </button>
              </div>
            </article>

            <article className="config-section-panel">
              <h3>Exchange y dashboard</h3>
              <div className="config-field-grid">
                <label className="config-field">
                  <span>Exchange API key</span>
                  <input
                    type="password"
                    placeholder={config.exchange.api_key_configurada ? 'Dejar en blanco para conservar' : 'Pega la API key de ExchangeRate-API'}
                    value={config.exchange.api_key}
                    onChange={(e) => setConfig((p) => ({ ...p, exchange: { ...p.exchange, api_key: e.target.value } }))}
                  />
                </label>
                <label className="config-field config-field--color">
                  <span>Color ingresos</span>
                  <div className="config-color-control">
                    <input value={config.dashboard.color_ingresos} onChange={(e) => setConfig((p) => ({ ...p, dashboard: { ...p.dashboard, color_ingresos: e.target.value } }))} />
                    <span aria-hidden="true" className="config-color-dot" style={{ backgroundColor: config.dashboard.color_ingresos }} />
                  </div>
                </label>
                <label className="config-field config-field--color">
                  <span>Color egresos</span>
                  <div className="config-color-control">
                    <input value={config.dashboard.color_egresos} onChange={(e) => setConfig((p) => ({ ...p, dashboard: { ...p.dashboard, color_egresos: e.target.value } }))} />
                    <span aria-hidden="true" className="config-color-dot" style={{ backgroundColor: config.dashboard.color_egresos }} />
                  </div>
                </label>
                <label className="config-field config-field--color">
                  <span>Color saldo</span>
                  <div className="config-color-control">
                    <input value={config.dashboard.color_saldo} onChange={(e) => setConfig((p) => ({ ...p, dashboard: { ...p.dashboard, color_saldo: e.target.value } }))} />
                    <span aria-hidden="true" className="config-color-dot" style={{ backgroundColor: config.dashboard.color_saldo }} />
                  </div>
                </label>
              </div>
            </article>
          </div>

          {!config.exchange.api_key_configurada ? <p className="config-note config-note--warning">Sin API key configurada: la sincronizacion automatica de tipos de cambio quedara bloqueada.</p> : null}

          <div className="config-general-actions">
            <button className="button-primary" type="submit" disabled={busy}>
              Guardar configuracion
            </button>
          </div>
        </form>
      )}

      {tab === 'divisas' && (
        <>
          <section className="config-card">
            <h2>Sincronizacion</h2>
            <div className="config-status-grid">
              <article><h3>Ultima actualizacion</h3><p>{formatDateTime(lastSync?.fecha_actualizacion ?? null)}</p></article>
              <article><h3>Estado</h3><p className={isStale ? 'config-badge config-badge--stale' : 'config-badge config-badge--ok'}>{isStale ? 'Desactualizado' : 'Actualizado'}</p></article>
              <article><h3>Total tasas</h3><p>{tipos.length}</p></article>
            </div>
            {!exchangeApiConfigured ? <p className="auth-error">Configura la API key en la pestaña General para habilitar la sincronizacion.</p> : null}
            <div className="import-actions"><button type="button" onClick={() => void syncRates()} disabled={busy || !exchangeApiConfigured}>Sincronizar ahora</button></div>

            <div style={{ marginTop: 'var(--space-3)', paddingTop: 'var(--space-3)', borderTop: '1px solid var(--color-border-primary)' }}>
              <h3 style={{ marginBottom: 'var(--space-2)' }}>Divisa por defecto</h3>
              <p style={{ fontSize: 'var(--font-size-sm)', color: 'var(--color-text-secondary)', marginBottom: 'var(--space-2)' }}>
                Selecciona la divisa base que se usará para las conversiones y sincronizaciones
              </p>
              <div style={{ display: 'flex', gap: 'var(--space-2)', alignItems: 'flex-end' }}>
                <AppSelect
                  className="config-inline-select"
                  label="Divisa base"
                  value={divisas.filter((d) => d.activa).length === 0 ? '' : divisaPorDefecto}
                  disabled={divisas.filter((d) => d.activa).length === 0}
                  options={
                    divisas.filter((d) => d.activa).length === 0
                      ? [{ value: '', label: 'Sin divisas activas' }]
                      : divisas
                          .filter((d) => d.activa)
                          .map((d) => ({
                            value: d.codigo,
                            label: `${d.codigo} ${d.nombre ? `- ${d.nombre}` : ''}`,
                          }))
                  }
                  onChange={setDivisaPorDefecto}
                />
                <button
                  type="button"
                  onClick={() => void guardarDivisaPorDefecto()}
                  disabled={busy || divisas.filter(d => d.activa).length === 0}
                >
                  Guardar
                </button>
              </div>
            </div>
          </section>

          <section className="config-card">
            <h2>Divisas registradas</h2>
            {divisas.length === 0 ? <p>No hay divisas registradas.</p> : (
              <div className="config-divisas-grid">
                {divisas.map((divisa) => (
                  <article className="config-divisa-card" key={divisa.codigo}>
                    <div>
                      <h3>{divisa.codigo}</h3>
                      <p className="import-muted">
                        {divisa.es_base ? 'Divisa base' : 'Divisa secundaria'} · {divisa.activa ? 'Activa' : 'Inactiva'}
                      </p>
                    </div>
                    <label>
                      Nombre
                      <input value={divisa.nombre ?? ''} onChange={(event) => updateDivisaField(divisa.codigo, { nombre: event.target.value })} />
                    </label>
                    <label>
                      Simbolo
                      <input value={divisa.simbolo ?? ''} onChange={(event) => updateDivisaField(divisa.codigo, { simbolo: event.target.value })} />
                    </label>
                    <label className="config-check">
                      <input type="checkbox" checked={divisa.activa} onChange={(event) => updateDivisaField(divisa.codigo, { activa: event.target.checked })} />
                      Activa
                    </label>
                    <label className="config-check">
                      <input type="checkbox" checked={divisa.es_base} onChange={(event) => updateDivisaField(divisa.codigo, { es_base: event.target.checked })} />
                      Divisa base
                    </label>
                    <div className="import-actions">
                      <button type="button" onClick={() => void saveDivisa(divisa.codigo)} disabled={busy}>
                        Guardar {divisa.codigo}
                      </button>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </section>

          <section className="config-card">
            <h2>Tasa manual</h2>
            <form className="config-manual-form" onSubmit={saveManualRate}>
              <AppSelect
                label="Origen"
                value={manualRate.origen}
                disabled={!canEditRates}
                options={divisasActivas.length === 0
                  ? [{ value: '', label: 'Sin divisas activas' }]
                  : divisasActivas.map((code) => ({ value: code, label: code }))}
                onChange={(next) => setManualRate((p) => ({ ...p, origen: next }))}
              />
              <AppSelect
                label="Destino"
                value={manualRate.destino}
                disabled={!canEditRates}
                options={divisasActivas.filter((code) => code !== manualRate.origen).length === 0
                  ? [{ value: '', label: 'Sin destino' }]
                  : divisasActivas
                      .filter((code) => code !== manualRate.origen)
                      .map((code) => ({ value: code, label: code }))}
                onChange={(next) => setManualRate((p) => ({ ...p, destino: next }))}
              />
              <label>Tasa<input type="number" step="0.00000001" value={manualRate.tasa} onChange={(e) => setManualRate((p) => ({ ...p, tasa: e.target.value }))} /></label>
              <button type="submit" disabled={busy || !canEditRates}>Guardar tasa</button>
            </form>
            {!canEditRates ? <p className="import-muted">Necesitas al menos dos divisas activas para editar una tasa manual.</p> : null}
          </section>

          <section className="config-card">
            <h2>Nueva divisa</h2>
            <form className="config-manual-form" onSubmit={createDivisa}>
              <label>Codigo<input value={nuevaDivisa.codigo} onChange={(e) => setNuevaDivisa((p) => ({ ...p, codigo: e.target.value.toUpperCase() }))} /></label>
              <label>Nombre<input value={nuevaDivisa.nombre} onChange={(e) => setNuevaDivisa((p) => ({ ...p, nombre: e.target.value }))} /></label>
              <label>Simbolo<input value={nuevaDivisa.simbolo} onChange={(e) => setNuevaDivisa((p) => ({ ...p, simbolo: e.target.value }))} /></label>
              <button type="submit" disabled={busy}>Crear divisa</button>
            </form>
          </section>

          <section className="config-card">
            <h2>Tipos vigentes</h2>
            {tiposOrdenados.length === 0 ? <p>No hay tipos de cambio cargados.</p> : (
              <div className="config-table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Origen</th>
                      <th>Destino</th>
                      <th>Tasa</th>
                      <th>Fuente</th>
                      <th>Actualizacion</th>
                    </tr>
                  </thead>
                  <tbody>
                    {tiposOrdenados.map((tipo) => (
                      <tr key={tipo.id}>
                        <td>{tipo.divisa_origen}</td>
                        <td>{tipo.divisa_destino}</td>
                        <td>{tipo.tasa}</td>
                        <td>{tipo.fuente}</td>
                        <td>{formatDateTime(tipo.fecha_actualizacion)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </>
      )}

      {tab === 'sistema' && (
        <form className="config-card" onSubmit={handleSaveSystemConfig}>
          <h2>Sistema y actualizacion</h2>
          <div className="config-grid-3">
            <label>
              URL de actualizaciones
              <input
                type="url"
                placeholder="https://servidor/atlas-balance/version.json"
                value={config.general.app_update_check_url}
                onChange={(e) => setConfig((p) => ({ ...p, general: { ...p.general, app_update_check_url: e.target.value } }))}
              />
            </label>
          </div>
          <p className="import-muted">Endpoint JSON con version, source_path y target_path.</p>
          <div className="config-status-grid">
            <article><h3>Version actual</h3><p>{currentVersion ?? 'N/D'}</p></article>
            <article><h3>Version disponible</h3><p>{availableVersion ?? 'Ninguna'}</p></article>
            <article><h3>Estado</h3><p className={updateAvailable ? 'config-badge config-badge--stale' : 'config-badge config-badge--ok'}>{updateAvailable ? 'Update disponible' : 'Actualizado'}</p></article>
          </div>
          {updateMessage ? <p>{updateMessage}</p> : null}
          <div className="import-actions">
            <button type="submit" disabled={busy}>Guardar URL</button>
            <button type="button" onClick={() => void checkUpdate(true)} disabled={busy}>Verificar actualizacion</button>
            <button type="button" onClick={updateNow} disabled={!updateAvailable || busy}>Actualizar ahora</button>
          </div>
        </form>
      )}

      {tab === 'integraciones' && (
        <>
          <section className="config-card">
            <h2>Tokens OpenClaw</h2>
            <div className="import-actions">
              <button type="button" onClick={() => setShowCreateTokenModal(true)} disabled={busy}>Crear token</button>
            </div>
          </section>

          <section className="config-card">
            <h2>Tokens existentes</h2>
            <TokenList tokens={tokens} busy={busy} onRevocar={revokeToken} onEliminar={deleteToken} />
          </section>
        </>
      )}

      <CreateTokenModal
        open={showCreateTokenModal}
        busy={busy}
        catalogos={catalogos}
        onClose={() => setShowCreateTokenModal(false)}
        onCreated={async (plain) => {
          setTokenPlano(plain);
          await load();
        }}
        onError={setError}
      />
      <TokenCreatedModal tokenPlano={tokenPlano} onClose={() => setTokenPlano(null)} />
    </section>
  );
}
