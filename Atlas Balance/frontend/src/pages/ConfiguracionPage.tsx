import { FormEvent, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Bot, Coins, Globe, KeyRound, Mail, ServerCog } from 'lucide-react';
import type { KeyboardEvent } from 'react';
import { Outlet, useLocation, useNavigate } from 'react-router';
import { AppSelect } from '@/components/common/AppSelect';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import { useConfirmDialog } from '@/hooks/useConfirmDialog';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { useUnsavedChanges } from '@/hooks/useUnsavedChanges';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import { useUiStore } from '@/stores/uiStore';
import { useUpdateStore } from '@/stores/updateStore';
import { CreateTokenModal } from '@/components/integraciones/CreateTokenModal';
import { TokenCreatedModal } from '@/components/integraciones/TokenCreatedModal';
import { TokenList } from '@/components/integraciones/TokenList';
import {
  OPENROUTER_DEFAULT_RUNTIME_MODEL,
  aiProviderOptions,
  getAiModelOptions,
  getDefaultAiModel,
  isOpenRouterFreeModel,
  normalizeAiModel,
  normalizeAiProvider,
} from '@/utils/aiModels';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatDateTime as formatDateTimeValue, parseStrictDecimal } from '@/utils/formatters';
import { parseOptionalNumberInput } from '@/utils/numberInput';
import { createPollLifecycle, type PollLifecycle } from '@/utils/pollLifecycle';
import type {
  ConfiguracionSistema,
  DivisaActiva,
  IaModel,
  IntegrationTokenListItem,
  PaginatedResponse,
  TipoCambio,
  WatchdogState,
} from '@/types';
import '@/styles/layout/routes/configuracion.css';

type TabKey = 'general' | 'revision-ia' | 'divisas' | 'sistema' | 'integraciones' | 'paises';
const STALE_THRESHOLD_MS = 24 * 60 * 60 * 1000;
const MFA_REMEMBER_DEVICE_DAYS = 90;

// S-17: modo de cifrado del SMTP. "required" = STARTTLS obligatorio,
// "auto" = se cifra si el servidor lo admite, "none" = sin cifrar.
// El tipo compartido ConfiguracionSistema todavia no declara tls_mode, asi que
// se amplia aqui sin tocar types/index.ts.
type SmtpTlsMode = 'required' | 'auto' | 'none';
type SmtpConfig = ConfiguracionSistema['smtp'] & { tls_mode?: SmtpTlsMode };
type ConfiguracionConTls = Omit<ConfiguracionSistema, 'smtp'> & { smtp: SmtpConfig };

function normalizarTlsMode(valor: string | null | undefined): SmtpTlsMode {
  return valor === 'required' || valor === 'auto' || valor === 'none' ? valor : 'auto';
}

const tabs: Array<{ key: TabKey; label: string; Icon: typeof Mail }> = [
  { key: 'general', label: 'General + SMTP', Icon: Mail },
  { key: 'revision-ia', label: 'Revisión + IA', Icon: Bot },
  { key: 'divisas', label: 'Divisas y tipos', Icon: Coins },
  { key: 'sistema', label: 'Sistema', Icon: ServerCog },
  { key: 'integraciones', label: 'Integraciones', Icon: KeyRound },
  { key: 'paises', label: 'Países', Icon: Globe },
];

interface CatalogoPermisos {
  paises: Array<{ id: string; nombre: string }>;
  titulares: Array<{ id: string; nombre: string }>;
  cuentas: Array<{ id: string; nombre: string; titular_id: string; pais_id: string | null }>;
}

function formatOptionalDateTime(value: string | null) {
  if (!value) {
    return 'Sin datos';
  }

  return formatDateTimeValue(value);
}

// Importe decimal no negativo: vacio = 0; invalido/negativo = null.
function parseDecimalInput(raw: string): number | null {
  if (!raw.trim()) {
    return 0;
  }
  const parsed = parseStrictDecimal(raw);
  return parsed === null || parsed < 0 ? null : parsed;
}

// toFixed evita la notacion cientifica (1e-7); se recortan ceros sobrantes.
function decimalToInput(value: number): string {
  if (!Number.isFinite(value)) {
    return '';
  }
  return value.toFixed(8).replace(/\.?0+$/, '').replace('.', ',');
}

interface DecimalFieldProps {
  fieldKey: string;
  value: number;
  onValue: (next: number) => void;
  onValidity: (fieldKey: string, valid: boolean) => void;
}

// Campo de importe con texto local (admite coma europea) que propaga el numero
// ya parseado al estado de configuracion; si el texto no es valido avisa por
// onValidity para bloquear el guardado.
function DecimalField({ fieldKey, value, onValue, onValidity }: DecimalFieldProps) {
  const [text, setText] = useState(() => decimalToInput(value));

  useEffect(() => {
    // Sincroniza cuando el valor externo cambia (carga/guardado) y ya no coincide con el texto
    if (parseDecimalInput(text) !== value) {
      setText(decimalToInput(value));
      onValidity(fieldKey, true);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- solo reacciona al valor externo
  }, [value]);

  useEffect(() => () => onValidity(fieldKey, true), [fieldKey, onValidity]);

  return (
    <input
      type="text"
      inputMode="decimal"
      autoComplete="off"
      value={text}
      aria-invalid={parseDecimalInput(text) === null}
      onChange={(e) => {
        const raw = e.target.value;
        setText(raw);
        const parsed = parseDecimalInput(raw);
        onValidity(fieldKey, parsed !== null);
        if (parsed !== null) {
          onValue(parsed);
        }
      }}
    />
  );
}

interface NumberFieldProps {
  fieldKey: string;
  value: number;
  min?: number;
  max?: number;
  step?: number;
  onValue: (next: number) => void;
}

// F-8: campo numerico con texto local. Se puede vaciar (el campo queda vacio,
// no 0) y un valor vacio no se convierte en 0 al guardar: si el campo esta
// vacio se conserva el ultimo valor valido de la configuracion.
function NumberField({ fieldKey, value, min, max, step, onValue }: NumberFieldProps) {
  const [text, setText] = useState(() => String(value));

  useEffect(() => {
    // Sincroniza cuando el valor externo cambia (carga/guardado) y el texto
    // local ya no representa ese valor. Un campo vacio se deja como esta.
    setText((prev) => (parseOptionalNumberInput(prev) === value ? prev : String(value)));
  }, [value]);

  return (
    <input
      id={fieldKey}
      type="number"
      min={min}
      max={max}
      step={step}
      value={text}
      onChange={(e) => {
        const raw = e.target.value;
        setText(raw);
        const parsed = parseOptionalNumberInput(raw);
        if (parsed !== null) {
          onValue(parsed);
        }
      }}
    />
  );
}

// D-8: los colores del grafico eran texto libre: se podia guardar cualquier
// cosa y el grafico se rompia en silencio. Solo se acepta un color
// hexadecimal (#RGB o #RRGGBB) o el valor vacio, que significa "usa el color
// del tema".
const COLOR_HEX_PATTERN = /^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/;

// D-8: tokens de grafico del design system (--chart-1..--chart-10) y sus
// aliases de variables.css. El color por defecto de cada campo sale del token
// (leido del CSS del tema activo), no de un hex escrito a mano, asi que se
// adapta al tema claro/oscuro.
const CHART_COLOR_TOKEN_NAMES = [
  '--chart-1',
  '--chart-2',
  '--chart-3',
  '--chart-4',
  '--chart-5',
  '--chart-6',
  '--chart-7',
  '--chart-8',
  '--chart-9',
  '--chart-10',
  '--chart-series-1',
  '--chart-series-2',
  '--chart-series-3',
  '--chart-series-4',
  '--chart-series-5',
  '--chart-series-6',
  '--chart-series-7',
  '--chart-series-8',
  '--chart-series-9',
  '--chart-series-10',
  '--chart-series-other',
  '--chart-ingresos',
  '--chart-egresos',
  '--chart-saldo',
  '--chart-color-ingresos',
  '--chart-color-egresos',
  '--chart-color-saldo',
] as const;

// Los tres colores del grafico y el token del tema que aporta cada uno.
const CHART_COLOR_KEYS = ['color_ingresos', 'color_egresos', 'color_saldo'] as const;

type ChartColorKey = (typeof CHART_COLOR_KEYS)[number];

const CHART_COLOR_FIELDS: Record<ChartColorKey, { label: string; campo: string; token: string; alias: string }> = {
  color_ingresos: { label: 'Color ingresos', campo: 'El color de ingresos', token: '--chart-5', alias: '--chart-ingresos' },
  color_egresos: { label: 'Color egresos', campo: 'El color de egresos', token: '--chart-7', alias: '--chart-egresos' },
  color_saldo: { label: 'Color saldo', campo: 'El color de saldo', token: '--chart-1', alias: '--chart-saldo' },
};

// Cache por tema: los tokens solo cambian cuando cambia [data-theme].
let chartTokenCache: { theme: string; tokens: Record<string, string> } | null = null;

// Lee el valor resuelto de cada token de grafico del tema activo.
function readChartColorTokens(theme: string): Record<string, string> {
  if (chartTokenCache?.theme === theme) {
    return chartTokenCache.tokens;
  }

  const tokens: Record<string, string> = {};
  if (typeof document !== 'undefined') {
    const styles = getComputedStyle(document.documentElement);
    for (const token of CHART_COLOR_TOKEN_NAMES) {
      const value = styles.getPropertyValue(token).trim();
      if (COLOR_HEX_PATTERN.test(value)) {
        tokens[token] = value;
      }
    }
  }

  chartTokenCache = { theme, tokens };
  return tokens;
}

interface ChartColorFieldProps {
  id: string;
  label: string;
  value: string;
  tokenLabel: string;
  tokenHex: string;
  listId: string;
  onChange: (next: string) => void;
}

// Campo de color del grafico: valida el hexadecimal, explica junto al campo
// cual es el formato esperado y muestra el color efectivo (el valor escrito o,
// si esta vacio, el del token del tema).
function ChartColorField({ id, label, value, tokenLabel, tokenHex, listId, onChange }: ChartColorFieldProps) {
  const trimmed = value.trim();
  const invalid = trimmed.length > 0 && !COLOR_HEX_PATTERN.test(trimmed);
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  const previewHex = invalid || trimmed.length === 0 ? tokenHex : trimmed;

  return (
    <label className="config-field config-field--color">
      <span>{label}</span>
      <div className="config-color-control">
        <input
          id={id}
          list={listId}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          aria-invalid={invalid}
          aria-describedby={invalid ? `${errorId} ${hintId}` : hintId}
          placeholder={tokenHex || '#1A2B3C'}
        />
        <span aria-hidden="true" className="config-color-dot" style={{ backgroundColor: previewHex || 'transparent' }} />
      </div>
      {invalid ? (
        <p className="auth-error" id={errorId} role="alert">
          El color no es válido. Escribe un hexadecimal como #1A2B3C (o #fff) y deja el campo vacío para usar el color del tema.
        </p>
      ) : null}
      <small id={hintId} className="import-muted">
        {`Vacío = color del tema (${tokenLabel}${tokenHex ? ` · ${tokenHex}` : ''}).`}
      </small>
    </label>
  );
}

export default function ConfiguracionPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const onPaisesSubroute = location.pathname.startsWith('/configuracion/paises');
  const [tab, setTab] = useState<TabKey>('general');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const { confirm, dialogProps: confirmDialogProps } = useConfirmDialog();
  const [error, setError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<string | null>(null);
  // D-8: los tokens de grafico se releen cuando cambia el tema activo.
  const theme = useUiStore((state) => state.theme);
  const chartTokens = readChartColorTokens(theme);
  // Paleta de graficos: los hex de los tokens, agrupados por color para no
  // repetir el mismo valor en el desplegable del campo.
  const chartPalette = useMemo(() => {
    const porHex = new Map<string, string[]>();
    for (const token of CHART_COLOR_TOKEN_NAMES) {
      const hex = chartTokens[token];
      if (!hex) {
        continue;
      }
      const labels = porHex.get(hex) ?? [];
      labels.push(token);
      porHex.set(hex, labels);
    }
    return [...porHex.entries()].map(([hex, labels]) => ({ hex, labels }));
  }, [chartTokens]);

  const [config, setConfig] = useState<ConfiguracionConTls>({
    smtp: { host: '', port: 587, user: '', password: '', from: '', tls_mode: 'auto' },
    general: {
      app_base_url: '',
      app_update_check_url: '',
      app_update_auto_enabled: false,
      app_update_auto_hour_utc: 3,
      app_update_auto_last_checked_utc: '',
      app_update_auto_last_started_utc: '',
      app_update_auto_last_result: '',
      mfa_remember_device_enabled: true,
      mfa_remember_device_days: MFA_REMEMBER_DEVICE_DAYS,
      require_mfa_for_non_admin_users: true,
      backup_path: '',
      export_path: '',
    },
    exchange: { api_key: '', api_key_configurada: false },
    // D-8: el color por defecto sale del token del tema, no de un hex escrito
    // a mano (--chart-5 / --chart-7 / --chart-1).
    dashboard: {
      color_ingresos: chartTokens[CHART_COLOR_FIELDS.color_ingresos.token] ?? '',
      color_egresos: chartTokens[CHART_COLOR_FIELDS.color_egresos.token] ?? '',
      color_saldo: chartTokens[CHART_COLOR_FIELDS.color_saldo.token] ?? '',
    },
    revision: { comisiones_importe_minimo: 1, saldo_bajo_cooldown_horas: 24 },
    ia: {
      provider: 'OPENROUTER',
      openrouter_api_key: '',
      openrouter_api_key_configurada: false,
      openai_api_key: '',
      openai_api_key_configurada: false,
      minimax_api_key: '',
      minimax_api_key_configurada: false,
      model: OPENROUTER_DEFAULT_RUNTIME_MODEL,
      habilitada: false,
      usuario_puede_usar: false,
      configurada: false,
      mensaje_estado: 'La IA está desactivada globalmente.',
      permite_retencion_datos: false,
      requests_por_minuto: 6,
      requests_por_hora: 30,
      requests_por_dia: 60,
      requests_globales_por_dia: 300,
      presupuesto_mensual_eur: 0,
      presupuesto_mensual_usuario_eur: 0,
      presupuesto_total_eur: 0,
      coste_mes_estimado_eur: 0,
      coste_mes_usuario_estimado_eur: 0,
      coste_total_estimado_eur: 0,
      requests_mes_usuario: 0,
      tokens_entrada_mes_usuario: 0,
      tokens_salida_mes_usuario: 0,
      porcentaje_aviso_presupuesto: 80,
      input_cost_per_million_tokens_eur: 0,
      output_cost_per_million_tokens_eur: 0,
      max_input_tokens: 6000,
      max_output_tokens: 700,
      max_context_rows: 80,
    },
  });
  const [smtpTo, setSmtpTo] = useState('');
  // Campos decimales con texto invalido (bloquean el guardado de configuracion)
  const invalidDecimalsRef = useRef<Set<string>>(new Set());
  const handleDecimalValidity = useCallback((fieldKey: string, valid: boolean) => {
    if (valid) {
      invalidDecimalsRef.current.delete(fieldKey);
    } else {
      invalidDecimalsRef.current.add(fieldKey);
    }
  }, []);
  // Snapshot de la configuracion cargada para avisar de cambios sin guardar al
  // refrescar/cerrar el navegador. Los tabs son render condicional sobre este
  // estado, asi que cambiar de pestana NO pierde datos (no necesita guardia).
  const configBaselineRef = useRef<string | null>(null);
  const isConfigDirty = configBaselineRef.current !== null && JSON.stringify(config) !== configBaselineRef.current;
  useUnsavedChanges(isConfigDirty);

  const [tipos, setTipos] = useState<TipoCambio[]>([]);
  const [divisas, setDivisas] = useState<DivisaActiva[]>([]);
  const [divisaPorDefecto, setDivisaPorDefecto] = useState('EUR');
  const [manualRate, setManualRate] = useState({ origen: 'EUR', destino: 'USD', tasa: '' });
  const [nuevaDivisa, setNuevaDivisa] = useState({ codigo: '', nombre: '', simbolo: '' });

  const [tokens, setTokens] = useState<IntegrationTokenListItem[]>([]);
  const [catalogos, setCatalogos] = useState<CatalogoPermisos>({ paises: [], titulares: [], cuentas: [] });
  const [openRouterModels, setOpenRouterModels] = useState<IaModel[]>([]);
  const [showCreateTokenModal, setShowCreateTokenModal] = useState(false);
  const [tokenPlano, setTokenPlano] = useState<string | null>(null);

  // R-6: la busqueda del campo "Modelo" pasa por el hook de debounce ya
  // existente: antes se pedia /ia/modelos en cada pulsacion de tecla.
  const debouncedModelSearch = useDebouncedValue(config.ia.model, 300);
  // F-4: sondeo de la actualizacion; se detiene al desmontar la pagina.
  const updatePollRef = useRef<PollLifecycle | null>(null);

  const logout = useAuthStore((state) => state.logout);
  const updateAvailable = useUpdateStore((state) => state.available);
  const updateInstallable = useUpdateStore((state) => state.installable);
  const updateBlockers = useUpdateStore((state) => state.blockers);
  const updatePreflight = useUpdateStore((state) => state.preflight);
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

  // A-8: un recargador por seccion. Ninguno activa el loading global (el
  // skeleton de pagina completa): las acciones menores recargan solo la
  // seccion que modifican, para no perder lo escrito sin guardar.
  const loadConfiguracion = async () => {
    const cfg = await api.get<ConfiguracionConTls>('/configuracion');
    const fallbackIa: ConfiguracionSistema['ia'] = {
      provider: 'OPENROUTER',
      openrouter_api_key: '',
      openrouter_api_key_configurada: false,
      openai_api_key: '',
      openai_api_key_configurada: false,
      minimax_api_key: '',
      minimax_api_key_configurada: false,
      model: OPENROUTER_DEFAULT_RUNTIME_MODEL,
      habilitada: false,
      usuario_puede_usar: false,
      configurada: false,
      mensaje_estado: 'La IA está desactivada globalmente.',
      permite_retencion_datos: false,
      requests_por_minuto: 6,
      requests_por_hora: 30,
      requests_por_dia: 60,
      requests_globales_por_dia: 300,
      presupuesto_mensual_eur: 0,
      presupuesto_mensual_usuario_eur: 0,
      presupuesto_total_eur: 0,
      coste_mes_estimado_eur: 0,
      coste_mes_usuario_estimado_eur: 0,
      coste_total_estimado_eur: 0,
      requests_mes_usuario: 0,
      tokens_entrada_mes_usuario: 0,
      tokens_salida_mes_usuario: 0,
      porcentaje_aviso_presupuesto: 80,
      input_cost_per_million_tokens_eur: 0,
      output_cost_per_million_tokens_eur: 0,
      max_input_tokens: 6000,
      max_output_tokens: 700,
      max_context_rows: 80,
    };
    const loadedIa = cfg.data.ia ?? fallbackIa;
    const loadedIaProvider = normalizeAiProvider(loadedIa.provider);
    const nextConfig: ConfiguracionConTls = {
      ...cfg.data,
      general: {
        ...cfg.data.general,
        mfa_remember_device_enabled: cfg.data.general?.mfa_remember_device_enabled ?? true,
        mfa_remember_device_days: cfg.data.general?.mfa_remember_device_days ?? MFA_REMEMBER_DEVICE_DAYS,
        require_mfa_for_non_admin_users: cfg.data.general?.require_mfa_for_non_admin_users ?? true,
      },
      // V-02.07: el backend ya no envia api_key ni smtp.password en el GET
      // (los DTO de respuesta no declaran esos campos). Los inicializamos
      // aqui a '' igual que las claves de IA, porque son estado del
      // formulario: sin esto el input pasaria a no controlado.
      // S-17: tls_mode se conserva si el backend lo devuelve; si no, "auto".
      smtp: { ...cfg.data.smtp, password: '', tls_mode: normalizarTlsMode(cfg.data.smtp?.tls_mode) },
      exchange: {
        api_key: '',
        api_key_configurada: cfg.data.exchange?.api_key_configurada ?? false,
      },
      revision: cfg.data.revision ?? { comisiones_importe_minimo: 1, saldo_bajo_cooldown_horas: 24 },
      ia: {
        ...loadedIa,
        provider: loadedIaProvider,
        model: normalizeAiModel(loadedIaProvider, loadedIa.model),
        openrouter_api_key: '',
        openai_api_key: '',
        minimax_api_key: '',
      },
    };
    setConfig(nextConfig);
    configBaselineRef.current = JSON.stringify(nextConfig);
    setSmtpTo(cfg.data.smtp.from);
  };

  // A-8: recarga solo los tipos de cambio.
  const loadTiposCambio = async () => {
    const tiposRes = await api.get<TipoCambio[]>('/tipos-cambio');
    setTipos(tiposRes.data ?? []);
  };

  // A-8: recarga solo las divisas; la divisa base se deriva de es_base.
  const loadDivisas = async () => {
    const divisasRes = await api.get<DivisaActiva[]>('/divisas');
    const nextDivisas = divisasRes.data ?? [];
    const activeCodes = nextDivisas.filter((item) => item.activa).map((item) => item.codigo);
    const baseDivisa = nextDivisas.find((d) => d.es_base)?.codigo ?? activeCodes[0] ?? 'EUR';
    setDivisas(nextDivisas);
    setDivisaPorDefecto(baseDivisa);
    setManualRate((prev) => {
      const origen = activeCodes.includes(prev.origen) ? prev.origen : (activeCodes[0] ?? '');
      const destinosDisponibles = activeCodes.filter((code) => code !== origen);
      const destino = destinosDisponibles.includes(prev.destino) ? prev.destino : (destinosDisponibles[0] ?? '');
      return { ...prev, origen, destino };
    });
  };

  // A-8: recarga solo los tokens de integracion.
  const loadTokens = async () => {
    const tokensRes = await api.get<PaginatedResponse<IntegrationTokenListItem>>('/integraciones/tokens', { params: { page: 1, pageSize: 100 } });
    setTokens(tokensRes.data.data ?? []);
  };

  // A-8: el catalogo de permisos solo se necesita en la carga inicial.
  const loadCatalogos = async () => {
    const catalogosRes = await api.get<CatalogoPermisos>('/usuarios/catalogos-permisos');
    setCatalogos(catalogosRes.data);
  };

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      await Promise.all([
        loadConfiguracion(),
        loadTiposCambio(),
        loadDivisas(),
        loadTokens(),
        loadCatalogos(),
      ]);
      await checkUpdate(true);
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo cargar la configuración. Revisa la conexión con el servidor e inténtalo de nuevo.'));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- carga inicial explícita de configuración
  }, []);

  // F-4: al desmontar la pagina el sondeo de la actualizacion se detiene:
  // limpia el timer y no vuelve a consultar ni a tocar el estado.
  useEffect(() => () => {
    updatePollRef.current?.stop();
  }, []);

  useEffect(() => {
    if (normalizeAiProvider(config.ia.provider) !== 'OPENROUTER') {
      return;
    }

    let mounted = true;
    const loadModels = async () => {
      try {
        const { data } = await api.get<IaModel[]>('/ia/modelos', {
          params: { provider: 'OPENROUTER', search: debouncedModelSearch || undefined },
        });
        if (mounted) {
          setOpenRouterModels(data ?? []);
        }
      } catch {
        if (mounted) {
          setOpenRouterModels([]);
        }
      }
    };

    void loadModels();
    return () => {
      mounted = false;
    };
  }, [config.ia.provider, debouncedModelSearch]);

  const saveConfig = async (message: string) => {
    if (invalidDecimalsRef.current.size > 0) {
      throw new Error('Hay importes con formato inválido (presupuestos, costes o importe mínimo). Corrígelos antes de guardar.');
    }
    // D-8: un color que no sea un hexadecimal no se guarda. Sin esta
    // comprobacion el grafico se rompia en silencio y nadie sabia por que.
    const colorInvalido = CHART_COLOR_KEYS.find((key) => {
      const valor = config.dashboard[key].trim();
      return valor.length > 0 && !COLOR_HEX_PATTERN.test(valor);
    });
    if (colorInvalido) {
      throw new Error(`${CHART_COLOR_FIELDS[colorInvalido].campo} no es válido. Escribe un hexadecimal como #1A2B3C (o #fff) o deja el campo vacío para usar el color del tema.`);
    }
    const aiProvider = normalizeAiProvider(config.ia.provider);
    const payload: ConfiguracionConTls = {
      ...config,
      // S-17: el modo TLS viaja siempre en el objeto smtp del guardado.
      smtp: { ...config.smtp, tls_mode: normalizarTlsMode(config.smtp.tls_mode) },
      // D-8: se guarda el color tal cual, siempre como hexadecimal valido (o
      // vacio, que el grafico resuelve con el color del tema).
      dashboard: {
        ...config.dashboard,
        color_ingresos: config.dashboard.color_ingresos.trim(),
        color_egresos: config.dashboard.color_egresos.trim(),
        color_saldo: config.dashboard.color_saldo.trim(),
      },
      ia: {
        ...config.ia,
        provider: aiProvider,
        model: normalizeAiModel(aiProvider, config.ia.model),
      },
    };
    await api.put('/configuracion', payload);
    const refreshed = await api.get<ConfiguracionConTls>('/configuracion');
    const merged: ConfiguracionConTls = {
      ...refreshed.data,
      general: {
        ...(refreshed.data.general ?? config.general),
        mfa_remember_device_enabled: refreshed.data.general?.mfa_remember_device_enabled ?? config.general.mfa_remember_device_enabled,
        mfa_remember_device_days: refreshed.data.general?.mfa_remember_device_days ?? config.general.mfa_remember_device_days,
        require_mfa_for_non_admin_users: refreshed.data.general?.require_mfa_for_non_admin_users ?? config.general.require_mfa_for_non_admin_users,
      },
      exchange: {
        api_key: '',
        api_key_configurada:
          refreshed.data.exchange?.api_key_configurada ?? config.exchange.api_key_configurada,
      },
      ia: {
        ...(refreshed.data.ia ?? config.ia),
        provider: normalizeAiProvider(refreshed.data.ia?.provider ?? config.ia.provider),
        model: normalizeAiModel(refreshed.data.ia?.provider ?? config.ia.provider, refreshed.data.ia?.model ?? config.ia.model),
        openrouter_api_key: '',
        openai_api_key: '',
        minimax_api_key: '',
      },
      smtp: {
        ...(refreshed.data.smtp ?? config.smtp),
        password: '',
        // S-17: si el backend todavia no devuelve tls_mode, se mantiene el
        // valor elegido en el formulario.
        tls_mode: normalizarTlsMode(refreshed.data.smtp?.tls_mode ?? config.smtp.tls_mode),
      },
    };
    setConfig(merged);
    // Tras guardar, la configuracion vuelve a estar limpia: reseteamos la linea base.
    configBaselineRef.current = JSON.stringify(merged);
    setFeedback(message);
  };

  const handleSaveConfig = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setFeedback(null);
    try {
      await saveConfig('Configuración guardada.');
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo guardar la configuración. Revisa los campos marcados e inténtalo de nuevo.'));
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
      await saveConfig('Ajustes de actualizaciones guardados.');
      await checkUpdate(true);
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudieron guardar los ajustes de actualización. Revisa los campos marcados e inténtalo de nuevo.'));
    } finally {
      setBusy(false);
    }
  };

  const handleRequireMfaToggle = async (next: boolean) => {
    const previous = config.general.require_mfa_for_non_admin_users;
    if (next === previous) {
      return;
    }
    if (!next) {
      const confirmed = await confirm({
        title: 'Desactivar Authenticator para gerentes y empleados',
        message:
          'Los gerentes y empleados podrán iniciar sesión solo con contraseña. Los administradores seguirán obligados a usar Authenticator. El cambio se aplica al siguiente inicio de sesión y queda registrado en auditoría.',
        confirmLabel: 'Desactivar para no administradores',
        tone: 'reversible',
      });
      if (!confirmed) {
        return;
      }
    }
    setConfig((p) => ({
      ...p,
      general: { ...p.general, require_mfa_for_non_admin_users: next },
    }));
  };

  const sendTestEmail = async () => {
    setBusy(true);
    setError(null);
    setFeedback(null);
    try {
      await api.post('/configuracion/smtp/test', { to: smtpTo || null });
      setFeedback('Correo de prueba enviado.');
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo enviar el correo de prueba. Revisa el servidor SMTP y la dirección de destino e inténtalo de nuevo.'));
    } finally {
      setBusy(false);
    }
  };

  const updateNow = async () => {
    const confirmed = await confirm({
      title: 'Actualizar ahora',
      message: 'Se instalará la nueva versión y la aplicación se reiniciará. Tu sesión se cerrará y el servicio no estará disponible unos minutos. Esta acción no se puede deshacer. ¿Continuar?',
      confirmLabel: 'Actualizar ahora',
      tone: 'reversible',
    });
    if (!confirmed) {
      return;
    }

    setBusy(true);
    setError(null);
    // F-4: el sondeo se cancela al desmontar la pagina y pide el estado sin
    // toast: durante una actualizacion real la API esta caida a proposito.
    const poll = createPollLifecycle();
    updatePollRef.current = poll;
    try {
      await api.post('/sistema/actualizar', {});
      const timeoutAt = Date.now() + 10 * 60 * 1000;
      while (Date.now() < timeoutAt) {
        if (!poll.active) {
          return;
        }
        try {
          const { data } = await api.get<WatchdogState>('/sistema/estado', { skipErrorToast: true });
          if (!poll.active) {
            return;
          }
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
            setError(data.mensaje || 'La actualización falló.');
            return;
          }
        } catch {
          // Durante una actualizacion real la API se detiene y vuelve a arrancar.
          // No es fallo final mientras el timeout del watchdog siga abierto.
        }
        const continuar = await poll.wait(2500);
        if (!continuar) {
          return;
        }
      }
      if (poll.active) {
        setError('La actualización no confirmó el resultado dentro del tiempo esperado. Revisa el estado del servicio e inténtalo de nuevo.');
      }
    } catch (err) {
      if (poll.active) {
        setError(extractErrorMessage(err, 'No se pudo actualizar la aplicación. Revisa el estado del servicio e inténtalo de nuevo.'));
      }
    } finally {
      const cancelado = !poll.active;
      poll.stop();
      if (updatePollRef.current === poll) {
        updatePollRef.current = null;
      }
      if (!cancelado) {
        setBusy(false);
      }
    }
  };

  const saveManualRate = async (event: FormEvent) => {
    event.preventDefault();
    if (!manualRate.origen || !manualRate.destino || manualRate.origen === manualRate.destino) {
      setError('Selecciona dos divisas activas distintas: una de origen y otra de destino.');
      return;
    }
    const tasa = parseStrictDecimal(manualRate.tasa);
    if (tasa === null || tasa <= 0) {
      setError('La tasa no es válida. Escribe un número mayor que 0 (por ejemplo, 1,25) e inténtalo de nuevo.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await api.put(`/tipos-cambio/${manualRate.origen}/${manualRate.destino}`, { tasa });
      setFeedback('Tipo de cambio actualizado.');
      await loadTiposCambio();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo actualizar el tipo de cambio. Revisa la tasa y las divisas seleccionadas e inténtalo de nuevo.'));
    } finally {
      setBusy(false);
    }
  };

  const syncRates = async () => {
    setBusy(true);
    setError(null);
    setFeedback(null);
    try {
      const response = await api.post('/tipos-cambio/sincronizar');
      // B-2 (LOTE 5): las tasas manuales no se sobrescriben; el servidor
      // dice cuantas ha omitido y deja aviso al administrador.
      const manualOmitidas = Number(response?.data?.manual_omitidas ?? 0);
      setFeedback(
        manualOmitidas > 0
          ? `Tipos de cambio sincronizados. ${manualOmitidas} tasa(s) marcada(s) como manual no se han sobrescrito: se ha avisado al administrador.`
          : 'Tipos de cambio sincronizados.'
      );
      await loadTiposCambio();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudieron sincronizar los tipos de cambio. Revisa la clave API de ExchangeRate y la conexión e inténtalo de nuevo.'));
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

    // Confirmar solo los cambios con impacto global: fijar una nueva divisa base
    // (recalcula todas las conversiones) o desactivar una divisa (deja de estar
    // disponible en toda la aplicacion).
    if (divisa.es_base || !divisa.activa) {
      const confirmed = await confirm({
        title: divisa.es_base ? 'Cambiar divisa base' : 'Desactivar divisa',
        message: divisa.es_base
          ? `Vas a fijar ${codigo} como divisa base. Todas las conversiones y totales pasarán a calcularse en ${codigo}. ¿Continuar?`
          : `Vas a desactivar la divisa ${codigo}. Dejará de estar disponible en la aplicación. ¿Continuar?`,
        confirmLabel: divisa.es_base ? 'Fijar como base' : 'Desactivar divisa',
        tone: 'reversible',
      });
      if (!confirmed) {
        return;
      }
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
      await loadDivisas();
    } catch (err) {
      setError(extractErrorMessage(err, `No se pudo actualizar la divisa ${codigo}. Revisa su nombre, su símbolo y si está activa, e inténtalo de nuevo.`));
    } finally {
      setBusy(false);
    }
  };

  const createDivisa = async (event: FormEvent) => {
    event.preventDefault();
    if (!nuevaDivisa.codigo.trim()) {
      setError('Escribe el código de la divisa (por ejemplo, USD) para poder crearla.');
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
      await loadDivisas();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo crear la divisa. Revisa el código (por ejemplo, USD) e inténtalo de nuevo.'));
    } finally {
      setBusy(false);
    }
  };

  const revokeToken = async (id: string) => {
    // La confirmacion (nivel irreversible) la pide TokenList antes de llamar aqui.
    setBusy(true);
    setError(null);
    setFeedback(null);
    try {
      await api.post(`/integraciones/tokens/${id}/revocar`);
      setFeedback('Token revocado.');
      await loadTokens();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo revocar el token. Recarga la lista de tokens e inténtalo de nuevo.'));
    } finally {
      setBusy(false);
    }
  };

  const rotateToken = async (id: string) => {
    const confirmed = await confirm({
      title: 'Rotar token',
      message: 'Se generará un token nuevo y el actual dejará de funcionar. Las integraciones que usen el valor anterior deberán actualizarse. ¿Rotar?',
      confirmLabel: 'Rotar token',
      tone: 'irreversible',
    });
    if (!confirmed) {
      return;
    }

    setBusy(true);
    setError(null);
    setFeedback(null);
    try {
      const { data } = await api.post<{ token_plano: string }>(`/integraciones/tokens/${id}/rotar`, {});
      setTokenPlano(data.token_plano);
      setFeedback('Token rotado. Copia el nuevo valor ahora.');
      await loadTokens();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo rotar el token. Recarga la lista de tokens e inténtalo de nuevo.'));
    } finally {
      setBusy(false);
    }
  };

  const deleteToken = async (id: string) => {
    // La confirmacion (nivel irreversible) la pide TokenList antes de llamar aqui.
    setBusy(true);
    setError(null);
    setFeedback(null);
    try {
      await api.delete(`/integraciones/tokens/${id}`);
      setFeedback('Token eliminado.');
      await loadTokens();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo eliminar el token. Recarga la lista de tokens e inténtalo de nuevo.'));
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
      // A-8: la divisa base solo se expone en GET /divisas (es_base); GET
      // /configuracion no la devuelve y recargarla tiraria lo sin guardar.
      await loadDivisas();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo guardar la divisa por defecto. Recarga la lista de divisas e inténtalo de nuevo.'));
    } finally {
      setBusy(false);
    }
  };

  const focusConfigTab = (nextTab: TabKey) => {
    window.setTimeout(() => document.getElementById(`config-tab-${nextTab}`)?.focus(), 0);
  };

  // Activa una pestana: 'paises' es una subruta; las demas vuelven a /configuracion si se esta en ella
  const activateTab = (key: TabKey) => {
    if (key === 'paises') {
      if (!onPaisesSubroute) {
        navigate('/configuracion/paises', { replace: true });
      }
      return;
    }
    setTab(key);
    if (onPaisesSubroute) {
      navigate('/configuracion', { replace: true });
    }
  };

  const handleTabKeyDown = (event: KeyboardEvent<HTMLButtonElement>, key: TabKey) => {
    const currentIndex = tabs.findIndex((item) => item.key === key);
    if (currentIndex < 0) {
      return;
    }

    const moveTo = (nextIndex: number) => {
      const nextTab = tabs[(nextIndex + tabs.length) % tabs.length].key;
      event.preventDefault();
      activateTab(nextTab);
      focusConfigTab(nextTab);
    };

    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
      moveTo(currentIndex + 1);
    } else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
      moveTo(currentIndex - 1);
    } else if (event.key === 'Home') {
      moveTo(0);
    } else if (event.key === 'End') {
      moveTo(tabs.length - 1);
    }
  };

  if (loading) return <PageSkeleton rows={5} />;

  const selectedAiProvider = normalizeAiProvider(config.ia.provider);
  const selectedAiModel = normalizeAiModel(selectedAiProvider, config.ia.model);
  const aiModelOptions = getAiModelOptions(selectedAiProvider);
  const openRouterModelOptions = openRouterModels.length > 0
    ? openRouterModels.map((model) => ({ value: model.id, label: model.nombre || model.id }))
    : aiModelOptions;
  const aiUsesOpenAi = selectedAiProvider === 'OPENAI';
  const aiUsesMiniMax = selectedAiProvider === 'MINIMAX';
  const aiUsesOpenRouter = selectedAiProvider === 'OPENROUTER';
  const aiProviderLabel = aiUsesOpenAi ? 'OpenAI' : aiUsesMiniMax ? 'MiniMax' : 'OpenRouter';
  const aiApiKeyValue = aiUsesOpenAi
    ? config.ia.openai_api_key
    : aiUsesMiniMax
      ? config.ia.minimax_api_key
      : config.ia.openrouter_api_key;
  const aiApiKeyConfigured = aiUsesOpenAi
    ? config.ia.openai_api_key_configurada
    : aiUsesMiniMax
      ? config.ia.minimax_api_key_configurada
      : config.ia.openrouter_api_key_configurada;

  return (
    <section className="config-page page">
      <header className="config-header page-header">
        <div className="page-header__lead">
          <h1 className="page-header__title">Configuración</h1>
          <p className="page-header__subtitle">Ajustes generales, IA, divisas, sistema, integraciones y países.</p>
        </div>
      </header>

      <div className="config-tabs config-tabs--settings" role="tablist" aria-label="Secciones de configuración">
        {tabs.map(({ key, label, Icon }) => {
          const isActive = key === 'paises' ? onPaisesSubroute : !onPaisesSubroute && tab === key;
          return (
            <button
              key={key}
              id={`config-tab-${key}`}
              type="button"
              className={isActive ? 'config-tab config-tab--active' : 'config-tab'}
              role="tab"
              aria-selected={isActive}
              tabIndex={isActive ? 0 : -1}
              onClick={() => activateTab(key)}
              onKeyDown={(event) => handleTabKeyDown(event, key)}
            >
              <span className="config-tab-icon" aria-hidden="true">
                <Icon size={18} strokeWidth={1.9} />
              </span>
              <span>{label}</span>
            </button>
          );
        })}
      </div>

      {error ? <p className="auth-error" role="alert">{error}</p> : null}
      {feedback ? <p className="config-feedback" role="status">{feedback}</p> : null}

      {!onPaisesSubroute ? (
        <>
      {tab === 'general' && (
        <form
          id="config-panel-general"
          className="config-card config-card--general"
          role="tabpanel"
          aria-labelledby="config-tab-general"
          onSubmit={handleSaveConfig}
        >
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
              <h3>Autenticación</h3>
              <p className="config-note">
                Los administradores siempre deben usar Authenticator. El siguiente interruptor aplica
                la obligatoriedad solo a gerentes y empleados.
              </p>
              <label className="config-check">
                <input
                  type="checkbox"
                  checked={config.general.require_mfa_for_non_admin_users}
                  onChange={(e) => void handleRequireMfaToggle(e.target.checked)}
                />
                Exigir Authenticator a gerentes y empleados
              </label>
              <label className="config-check">
                <input
                  type="checkbox"
                  checked={config.general.mfa_remember_device_enabled}
                  onChange={(e) =>
                    setConfig((p) => ({
                      ...p,
                      general: { ...p.general, mfa_remember_device_enabled: e.target.checked },
                    }))
                  }
                />
                Permitir recordar dispositivos MFA durante {config.general.mfa_remember_device_days || MFA_REMEMBER_DEVICE_DAYS} días
              </label>
              <p className="config-note config-note--warning">
                Cerrar sesión mantiene el dispositivo recordado. Revoca dispositivos desde MFA o cambia la contraseña para invalidarlos.
              </p>
              <p className="config-note config-note--warning">
                Los administradores nunca pueden desactivar su propio Authenticator. La política por rol se
                evalúa en cada inicio de sesión y refresh; cambiar el interruptor no afecta a sesiones
                administrativas ya iniciadas.
              </p>
            </article>

            <article className="config-section-panel">
              <h3>Servidor SMTP</h3>
              <div className="config-field-grid">
                <label className="config-field">
                  <span>Servidor SMTP</span>
                  <input value={config.smtp.host} onChange={(e) => setConfig((p) => ({ ...p, smtp: { ...p.smtp, host: e.target.value } }))} />
                </label>
                <label className="config-field">
                  <span>Puerto SMTP</span>
                  <NumberField
                    fieldKey="smtp_port"
                    value={config.smtp.port}
                    onValue={(next) => setConfig((p) => ({ ...p, smtp: { ...p.smtp, port: next } }))}
                  />
                </label>
                <label className="config-field">
                  <span>Usuario SMTP</span>
                  <input value={config.smtp.user} onChange={(e) => setConfig((p) => ({ ...p, smtp: { ...p.smtp, user: e.target.value } }))} />
                </label>
                <label className="config-field">
                  <span>Contraseña SMTP</span>
                  <input type="password" placeholder="Dejar en blanco para conservar" value={config.smtp.password} onChange={(e) => setConfig((p) => ({ ...p, smtp: { ...p.smtp, password: e.target.value } }))} />
                </label>
                <label className="config-field">
                  <span>Remitente</span>
                  <input value={config.smtp.from} onChange={(e) => setConfig((p) => ({ ...p, smtp: { ...p.smtp, from: e.target.value } }))} />
                </label>
                <AppSelect
                  label="Modo TLS"
                  value={normalizarTlsMode(config.smtp.tls_mode)}
                  options={[
                    { value: 'required', label: 'Obligatorio (STARTTLS)' },
                    { value: 'auto', label: 'Automático (si el servidor lo admite)' },
                    { value: 'none', label: 'Sin cifrar (servidor sin TLS)' },
                  ]}
                  onChange={(next) => setConfig((p) => ({ ...p, smtp: { ...p.smtp, tls_mode: normalizarTlsMode(next) } }))}
                />
              </div>

              <p className="config-note">
                Si tu servidor SMTP no admite TLS, elige «Sin cifrar»: las alertas con saldos saldrán sin cifrar.
              </p>

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
              <datalist id="config-colores-grafico">
                {chartPalette.map((opcion) => (
                  <option key={opcion.hex} value={opcion.hex} label={opcion.labels.join(' · ')} />
                ))}
              </datalist>
              <div className="config-field-grid">
                <label className="config-field">
                  <span>Clave API de ExchangeRate</span>
                  <input
                    type="password"
                    placeholder={config.exchange.api_key_configurada ? 'Dejar en blanco para conservar' : 'Pega la clave API de ExchangeRate-API'}
                    value={config.exchange.api_key}
                    onChange={(e) => setConfig((p) => ({ ...p, exchange: { ...p.exchange, api_key: e.target.value } }))}
                  />
                </label>
                <ChartColorField
                  id="config-color-ingresos"
                  label={CHART_COLOR_FIELDS.color_ingresos.label}
                  listId="config-colores-grafico"
                  value={config.dashboard.color_ingresos}
                  tokenLabel={CHART_COLOR_FIELDS.color_ingresos.alias}
                  tokenHex={chartTokens[CHART_COLOR_FIELDS.color_ingresos.token] ?? ''}
                  onChange={(valor) => setConfig((p) => ({ ...p, dashboard: { ...p.dashboard, color_ingresos: valor } }))}
                />
                <ChartColorField
                  id="config-color-egresos"
                  label={CHART_COLOR_FIELDS.color_egresos.label}
                  listId="config-colores-grafico"
                  value={config.dashboard.color_egresos}
                  tokenLabel={CHART_COLOR_FIELDS.color_egresos.alias}
                  tokenHex={chartTokens[CHART_COLOR_FIELDS.color_egresos.token] ?? ''}
                  onChange={(valor) => setConfig((p) => ({ ...p, dashboard: { ...p.dashboard, color_egresos: valor } }))}
                />
                <ChartColorField
                  id="config-color-saldo"
                  label={CHART_COLOR_FIELDS.color_saldo.label}
                  listId="config-colores-grafico"
                  value={config.dashboard.color_saldo}
                  tokenLabel={CHART_COLOR_FIELDS.color_saldo.alias}
                  tokenHex={chartTokens[CHART_COLOR_FIELDS.color_saldo.token] ?? ''}
                  onChange={(valor) => setConfig((p) => ({ ...p, dashboard: { ...p.dashboard, color_saldo: valor } }))}
                />
              </div>
            </article>
          </div>

          {!config.exchange.api_key_configurada ? <p className="config-note config-note--warning">Sin clave API configurada: la sincronización automática de tipos de cambio quedará bloqueada.</p> : null}

          <div className="config-general-actions">
            <button className="button-primary" type="submit" disabled={busy}>
              Guardar configuración
            </button>
          </div>
        </form>
      )}

      {tab === 'revision-ia' && (
        <form
          id="config-panel-revision-ia"
          className="config-card config-card--general"
          role="tabpanel"
          aria-labelledby="config-tab-revision-ia"
          onSubmit={handleSaveConfig}
        >
          <header className="config-card-headline">
            <h2>Revisión e IA</h2>
            <p className="config-subtitle">Umbrales operativos, antiduplicados de alertas y proveedor de IA financiera.</p>
          </header>

          <div className="config-general-layout">
            <article className="config-section-panel">
              <h3>Revisión bancaria</h3>
              <div className="config-field-grid">
                <label className="config-field">
                  <span>Importe mínimo comisión</span>
                  <DecimalField
                    fieldKey="comisiones_importe_minimo"
                    value={config.revision.comisiones_importe_minimo}
                    onValidity={handleDecimalValidity}
                    onValue={(next) =>
                      setConfig((p) => ({
                        ...p,
                        revision: { ...p.revision, comisiones_importe_minimo: next },
                      }))
                    }
                  />
                </label>
                <label className="config-field">
                  <span>Horas sin duplicar saldo bajo</span>
                  <NumberField
                    fieldKey="saldo_bajo_cooldown_horas"
                    value={config.revision.saldo_bajo_cooldown_horas}
                    min={1}
                    step={1}
                    onValue={(next) =>
                      setConfig((p) => ({
                        ...p,
                        revision: { ...p.revision, saldo_bajo_cooldown_horas: next },
                      }))
                    }
                  />
                </label>
              </div>
              <p className="config-note">
                El aviso de saldo bajo se evalúa al crear o editar extractos. Se envía si el último saldo queda por debajo del umbral aplicable y no se ha avisado dentro de esta ventana.
              </p>
            </article>

            <article className="config-section-panel">
              <h3>IA financiera</h3>
              <label className="config-check">
                <input
                  type="checkbox"
                  checked={config.ia.habilitada}
                  onChange={(e) => setConfig((p) => ({ ...p, ia: { ...p.ia, habilitada: e.target.checked } }))}
                />
                IA activada globalmente
              </label>
              <div className="config-field-grid">
                <AppSelect
                  className="config-inline-select"
                  label="Proveedor"
                  value={config.ia.provider}
                  options={aiProviderOptions}
                  onChange={(value) => setConfig((p) => ({ ...p, ia: { ...p.ia, provider: value, model: getDefaultAiModel(value), max_input_tokens: value === 'MINIMAX' ? 1000000 - p.ia.max_output_tokens : p.ia.max_input_tokens } }))}
                />
                {aiUsesOpenRouter ? (
                  <>
                    <p className="import-muted">
                      El modelo predeterminado es <code>openrouter/auto</code>, que exige retención
                      cero de datos (ZDR) al proveedor. Atlas Balance seudonimiza el contexto antes
                      de enviarlo.
                    </p>
                    <label className="config-check">
                      <input
                        type="checkbox"
                        checked={config.ia.permite_retencion_datos}
                        onChange={(e) =>
                          setConfig((p) => ({ ...p, ia: { ...p.ia, permite_retencion_datos: e.target.checked } }))
                        }
                      />
                      Permitir modelos gratuitos de OpenRouter
                    </label>
                    <p className="auth-error" role="status">
                      Los modelos gratuitos de OpenRouter pueden guardar y usar para entrenamiento
                      los datos enviados (importes, saldos y conceptos). Actívalo solo si tu empresa
                      lo acepta.
                    </p>
                  </>
                ) : (
                  <p className="auth-error" role="status">
                    Aviso: con {aiProviderLabel} la aplicación no puede exigir retención cero por
                    consulta (solo OpenRouter lo soporta). El contexto financiero se envía a {aiProviderLabel}
                    y su conservación depende de la configuración/contrato de tu cuenta con ese proveedor.
                    Para datos financieros reales on-premise, usa OpenRouter o confirma la retención cero
                    a nivel de cuenta antes de activarlo.
                  </p>
                )}
                <label className="config-field">
                  <span>Clave API de {aiProviderLabel}</span>
                  <input
                    type="password"
                    placeholder={aiApiKeyConfigured ? 'Dejar en blanco para conservar' : `Pega la clave API de ${aiProviderLabel}`}
                    value={aiApiKeyValue}
                    onChange={(e) =>
                      setConfig((p) => ({
                        ...p,
                        ia: aiUsesOpenAi
                          ? { ...p.ia, openai_api_key: e.target.value }
                          : aiUsesMiniMax
                            ? { ...p.ia, minimax_api_key: e.target.value }
                            : { ...p.ia, openrouter_api_key: e.target.value },
                      }))
                    }
                  />
                </label>
                {aiUsesOpenRouter ? (
                  <label className="config-field">
                    <span>Modelo</span>
                    <input
                      list="openrouter-modelos"
                      value={config.ia.model}
                      placeholder="openrouter/auto o proveedor/modelo"
                      onChange={(e) => setConfig((p) => ({ ...p, ia: { ...p.ia, model: e.target.value } }))}
                    />
                    <datalist id="openrouter-modelos">
                      {openRouterModelOptions.map((model) => (
                        <option key={model.value} value={model.value}>
                          {isOpenRouterFreeModel(model.value) && !config.ia.permite_retencion_datos
                            ? `${model.label} (requiere permitir modelos gratuitos)`
                            : model.label}
                        </option>
                      ))}
                    </datalist>
                  </label>
                ) : (
                  <AppSelect
                    className="config-inline-select"
                    label="Modelo"
                    value={selectedAiModel}
                    options={aiModelOptions}
                    onChange={(value) => setConfig((p) => ({ ...p, ia: { ...p.ia, model: value, max_input_tokens: aiUsesMiniMax && value === 'MiniMax-M3.1-Flash' ? 1000000 - p.ia.max_output_tokens : p.ia.max_input_tokens } }))}
                  />
                )}
              </div>
              <p className={config.ia.configurada ? 'config-note' : 'config-note config-note--warning'}>
                {config.ia.mensaje_estado || 'Configura IA antes de permitir consultas.'}
              </p>
              {!aiApiKeyConfigured && !aiApiKeyValue ? (
                <p className="config-note config-note--warning">Sin clave API: el chat mostrará un aviso y no llamará al proveedor.</p>
              ) : null}
            </article>

            <article className="config-section-panel">
              <h3>Límites y coste</h3>
              <div className="config-field-grid">
                <label className="config-field">
                  <span>Peticiones/min por usuario</span>
                  <NumberField
                    fieldKey="requests_por_minuto"
                    value={config.ia.requests_por_minuto}
                    min={0}
                    onValue={(next) => setConfig((p) => ({ ...p, ia: { ...p.ia, requests_por_minuto: next } }))}
                  />
                </label>
                <label className="config-field">
                  <span>Peticiones/hora por usuario</span>
                  <NumberField
                    fieldKey="requests_por_hora"
                    value={config.ia.requests_por_hora}
                    min={0}
                    onValue={(next) => setConfig((p) => ({ ...p, ia: { ...p.ia, requests_por_hora: next } }))}
                  />
                </label>
                <label className="config-field">
                  <span>Peticiones/día por usuario</span>
                  <NumberField
                    fieldKey="requests_por_dia"
                    value={config.ia.requests_por_dia}
                    min={0}
                    onValue={(next) => setConfig((p) => ({ ...p, ia: { ...p.ia, requests_por_dia: next } }))}
                  />
                </label>
                <label className="config-field">
                  <span>Peticiones/día de la app</span>
                  <NumberField
                    fieldKey="requests_globales_por_dia"
                    value={config.ia.requests_globales_por_dia}
                    min={0}
                    onValue={(next) => setConfig((p) => ({ ...p, ia: { ...p.ia, requests_globales_por_dia: next } }))}
                  />
                </label>
                <label className="config-field">
                  <span>Presupuesto mensual EUR</span>
                  <DecimalField fieldKey="presupuesto_mensual_eur" value={config.ia.presupuesto_mensual_eur} onValidity={handleDecimalValidity} onValue={(next) => setConfig((p) => ({ ...p, ia: { ...p.ia, presupuesto_mensual_eur: next } }))} />
                </label>
                <label className="config-field">
                  <span>Presupuesto mensual usuario EUR</span>
                  <DecimalField fieldKey="presupuesto_mensual_usuario_eur" value={config.ia.presupuesto_mensual_usuario_eur} onValidity={handleDecimalValidity} onValue={(next) => setConfig((p) => ({ ...p, ia: { ...p.ia, presupuesto_mensual_usuario_eur: next } }))} />
                </label>
                <label className="config-field">
                  <span>Presupuesto total EUR</span>
                  <DecimalField fieldKey="presupuesto_total_eur" value={config.ia.presupuesto_total_eur} onValidity={handleDecimalValidity} onValue={(next) => setConfig((p) => ({ ...p, ia: { ...p.ia, presupuesto_total_eur: next } }))} />
                </label>
                <label className="config-field">
                  <span>Aviso presupuesto %</span>
                  <NumberField
                    fieldKey="porcentaje_aviso_presupuesto"
                    value={config.ia.porcentaje_aviso_presupuesto}
                    min={1}
                    max={100}
                    onValue={(next) => setConfig((p) => ({ ...p, ia: { ...p.ia, porcentaje_aviso_presupuesto: next } }))}
                  />
                </label>
                <label className="config-field">
                  <span>Coste entrada / 1 M tokens</span>
                  <DecimalField fieldKey="input_cost_per_million_tokens_eur" value={config.ia.input_cost_per_million_tokens_eur} onValidity={handleDecimalValidity} onValue={(next) => setConfig((p) => ({ ...p, ia: { ...p.ia, input_cost_per_million_tokens_eur: next } }))} />
                </label>
                <label className="config-field">
                  <span>Coste salida / 1 M tokens</span>
                  <DecimalField fieldKey="output_cost_per_million_tokens_eur" value={config.ia.output_cost_per_million_tokens_eur} onValidity={handleDecimalValidity} onValue={(next) => setConfig((p) => ({ ...p, ia: { ...p.ia, output_cost_per_million_tokens_eur: next } }))} />
                </label>
              </div>
              <p className="config-note">
                Coste estimado: app este mes {config.ia.coste_mes_estimado_eur.toFixed(4)} EUR · usuario este mes {config.ia.coste_mes_usuario_estimado_eur.toFixed(4)} EUR · total {config.ia.coste_total_estimado_eur.toFixed(4)} EUR. Uso del usuario este mes: {config.ia.requests_mes_usuario} peticiones y {config.ia.tokens_entrada_mes_usuario + config.ia.tokens_salida_mes_usuario} tokens. Si no defines coste por modelo, se aplican los límites de peticiones y tokens.
              </p>
            </article>

            <article className="config-section-panel">
              <h3>Tokens y privacidad</h3>
              <div className="config-field-grid">
                <label className="config-field">
                  <span>Tokens entrada máx.</span>
                  <NumberField
                    fieldKey="max_input_tokens"
                    value={config.ia.max_input_tokens}
                    min={1000}
                    max={1000000}
                    onValue={(next) => setConfig((p) => ({ ...p, ia: { ...p.ia, max_input_tokens: next } }))}
                  />
                </label>
                <label className="config-field">
                  <span>Tokens salida máx.</span>
                  <NumberField
                    fieldKey="max_output_tokens"
                    value={config.ia.max_output_tokens}
                    min={64}
                    max={4000}
                    onValue={(next) => setConfig((p) => ({ ...p, ia: { ...p.ia, max_output_tokens: next } }))}
                  />
                </label>
                <label className="config-field">
                  <span>Movimientos enviados máx.</span>
                  <NumberField
                    fieldKey="max_context_rows"
                    value={config.ia.max_context_rows}
                    min={0}
                    max={500}
                    onValue={(next) => setConfig((p) => ({ ...p, ia: { ...p.ia, max_context_rows: next } }))}
                  />
                </label>
              </div>
              <p className="config-note config-note--warning">
                Las consultas usan un proveedor externo. Atlas Balance envía contexto financiero minimizado y no guarda prompts ni respuestas completas en auditoría.
              </p>
            </article>

          </div>

          <div className="config-general-actions">
            <button className="button-primary" type="submit" disabled={busy}>
              Guardar revisión e IA
            </button>
          </div>
        </form>
      )}

      {tab === 'divisas' && (
        <div
          id="config-panel-divisas"
          className="config-tab-panel"
          role="tabpanel"
          aria-labelledby="config-tab-divisas"
        >
          <section className="config-card">
            <h2>Sincronización</h2>
            <div className="config-status-grid">
              <article><h3>Última actualización</h3><p>{formatOptionalDateTime(lastSync?.fecha_actualizacion ?? null)}</p></article>
              <article><h3>Estado</h3><p className={isStale ? 'config-badge config-badge--stale' : 'config-badge config-badge--ok'}>{isStale ? 'Desactualizado' : 'Actualizado'}</p></article>
              <article><h3>Total tasas</h3><p>{tipos.length}</p></article>
            </div>
            {!exchangeApiConfigured ? <p className="auth-error" role="alert">Configura la clave API en la pestaña General para habilitar la sincronización.</p> : null}
            <div className="import-actions"><button type="button" onClick={() => void syncRates()} disabled={busy || !exchangeApiConfigured}>Sincronizar ahora</button></div>

            <div className="config-default-currency-section">
              <h3>Divisa por defecto</h3>
              <p>
                Selecciona la divisa base que se usará para las conversiones y sincronizaciones
              </p>
              <div className="config-default-currency-actions">
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
                      Símbolo
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
              <label>Tasa<input type="text" inputMode="decimal" autoComplete="off" value={manualRate.tasa} onChange={(e) => setManualRate((p) => ({ ...p, tasa: e.target.value }))} /></label>
              <button type="submit" className="button-primary" disabled={busy || !canEditRates}>Guardar tasa</button>
            </form>
            {!canEditRates ? <p className="import-muted">Necesitas al menos dos divisas activas para editar una tasa manual.</p> : null}
          </section>

          <section className="config-card">
            <h2>Nueva divisa</h2>
            <form className="config-manual-form" onSubmit={createDivisa}>
              <label>Código<input value={nuevaDivisa.codigo} onChange={(e) => setNuevaDivisa((p) => ({ ...p, codigo: e.target.value.toUpperCase() }))} /></label>
              <label>Nombre<input value={nuevaDivisa.nombre} onChange={(e) => setNuevaDivisa((p) => ({ ...p, nombre: e.target.value }))} /></label>
              <label>Símbolo<input value={nuevaDivisa.simbolo} onChange={(e) => setNuevaDivisa((p) => ({ ...p, simbolo: e.target.value }))} /></label>
              <button type="submit" className="button-primary" disabled={busy}>Crear divisa</button>
            </form>
          </section>

          <section className="config-card">
            <h2>Tipos vigentes</h2>
            {tiposOrdenados.length === 0 ? <p>No hay tipos de cambio cargados.</p> : (
              <div className="config-table-wrap">
                <table>
                  <caption className="sr-only">Tipos de cambio vigentes</caption>
                  <thead>
                    <tr>
                      <th scope="col">Origen</th>
                      <th scope="col">Destino</th>
                      <th scope="col">Tasa</th>
                      <th scope="col">Fuente</th>
                      <th scope="col">Actualización</th>
                    </tr>
                  </thead>
                  <tbody>
                    {tiposOrdenados.map((tipo) => (
                      <tr key={tipo.id}>
                        <td>{tipo.divisa_origen}</td>
                        <td>{tipo.divisa_destino}</td>
                        <td>{tipo.tasa}</td>
                        <td>{tipo.fuente}</td>
                        <td>{formatOptionalDateTime(tipo.fecha_actualizacion)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </div>
      )}

      {tab === 'sistema' && (
        <form
          id="config-panel-sistema"
          className="config-card"
          role="tabpanel"
          aria-labelledby="config-tab-sistema"
          onSubmit={handleSaveSystemConfig}
        >
          <h2>Sistema y actualización</h2>
          <div className="config-grid-3">
            <label>
              Repositorio GitHub de actualizaciones
              <input
                type="url"
                placeholder="https://github.com/AtlasLabs797/AtlasBalance"
                autoCapitalize="none"
                autoCorrect="off"
                spellCheck={false}
                value={config.general.app_update_check_url}
                onChange={(e) => setConfig((p) => ({ ...p, general: { ...p.general, app_update_check_url: e.target.value } }))}
              />
            </label>
            <label>
              Hora UTC automática
              <NumberField
                fieldKey="app_update_auto_hour_utc"
                value={config.general.app_update_auto_hour_utc}
                min={0}
                max={23}
                onValue={(next) =>
                  setConfig((p) => ({
                    ...p,
                    general: {
                      ...p.general,
                      app_update_auto_hour_utc: Math.max(0, Math.min(23, next)),
                    },
                  }))
                }
              />
            </label>
          </div>
          <label className="config-check">
            <input
              type="checkbox"
              checked={config.general.app_update_auto_enabled}
              onChange={(e) =>
                setConfig((p) => ({
                  ...p,
                  general: { ...p.general, app_update_auto_enabled: e.target.checked },
                }))
              }
            />
            Actualizar automáticamente desde GitHub
          </label>
          <p className="import-muted">Usa el repositorio oficial por HTTPS. Atlas Balance consulta el último GitHub Release, descarga el ZIP win-x64 y lo prepara en la carpeta segura de actualizaciones.</p>
          <div className="config-status-grid">
            <article><h3>Versión actual</h3><p title={currentVersion ?? undefined}>{currentVersion ?? 'Sin dato'}</p></article>
            <article><h3>Versión disponible</h3><p>{availableVersion ?? 'Ninguna'}</p></article>
            <article><h3>Estado</h3><p className={updateAvailable ? 'config-badge config-badge--stale' : 'config-badge config-badge--ok'}>{updateAvailable ? 'Actualización disponible' : 'Actualizado'}</p></article>
            <article><h3>Instalable</h3><p className={updateInstallable ? 'config-badge config-badge--ok' : 'config-badge config-badge--stale'}>{updateInstallable ? 'Listo' : 'Bloqueado'}</p></article>
            <article><h3>ZIP</h3><p>{updatePreflight.assetZipName ?? (updatePreflight.assetZipDetected ? 'Detectado' : 'Sin detectar')}</p></article>
            <article><h3>Firma/digest</h3><p>{updatePreflight.signatureDetected && updatePreflight.digestPresent ? 'OK' : 'Pendiente'}</p></article>
            <article><h3>Watchdog</h3><p className={updatePreflight.watchdogAvailable ? 'config-badge config-badge--ok' : 'config-badge config-badge--stale'}>{updatePreflight.watchdogAvailable ? 'Disponible' : 'No disponible'}</p></article>
            <article><h3>Auto</h3><p className={config.general.app_update_auto_enabled ? 'config-badge config-badge--ok' : 'config-badge'}>{config.general.app_update_auto_enabled ? 'Activo' : 'Inactivo'}</p></article>
            <article><h3>Última comprobación auto</h3><p>{formatOptionalDateTime(config.general.app_update_auto_last_checked_utc || null)}</p></article>
            <article><h3>Último inicio auto</h3><p>{formatOptionalDateTime(config.general.app_update_auto_last_started_utc || null)}</p></article>
          </div>
          {updateAvailable && !updateInstallable && updateBlockers.length > 0 ? (
            <ul className="config-note config-note--warning">
              {updateBlockers.map((blocker) => (
                <li key={blocker}>{blocker}</li>
              ))}
            </ul>
          ) : null}
          {config.general.app_update_auto_last_result ? <p className="config-note">{config.general.app_update_auto_last_result}</p> : null}
          {updateMessage ? <p className="config-note" role="status">{updateMessage}</p> : null}
          <div className="import-actions">
            <button type="submit" className="button-primary" disabled={busy}>Guardar actualizaciones</button>
            <button type="button" className="button-secondary" onClick={() => void checkUpdate(true)} disabled={busy}>Verificar actualización</button>
            <button type="button" className="button-warning" onClick={updateNow} disabled={!updateAvailable || !updateInstallable || busy}>Actualizar ahora</button>
          </div>
        </form>
      )}

      {tab === 'integraciones' && (
        <div
          id="config-panel-integraciones"
          className="config-tab-panel"
          role="tabpanel"
          aria-labelledby="config-tab-integraciones"
        >
          <section className="config-card">
            <h2>Tokens OpenClaw</h2>
            <div className="import-actions">
              <button type="button" className="button-primary" onClick={() => setShowCreateTokenModal(true)} disabled={busy}>Crear token</button>
            </div>
          </section>

          <section className="config-card">
            <h2>Tokens existentes</h2>
            <TokenList tokens={tokens} busy={busy} onRevocar={revokeToken} onRotar={rotateToken} onEliminar={deleteToken} />
          </section>
        </div>
      )}
        </>
      ) : null}

      <Outlet />

      <CreateTokenModal
        open={showCreateTokenModal}
        busy={busy}
        catalogos={catalogos}
        onClose={() => setShowCreateTokenModal(false)}
        onCreated={async (plain) => {
          setTokenPlano(plain);
          await loadTokens();
        }}
        onError={setError}
      />
      <TokenCreatedModal tokenPlano={tokenPlano} onClose={() => setTokenPlano(null)} />
      <ConfirmDialog {...confirmDialogProps} />
    </section>
  );
}
