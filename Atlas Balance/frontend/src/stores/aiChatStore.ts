import { create } from 'zustand';
import api from '@/services/api';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
import type { IaChatResponse, IaConfig } from '@/types';
import { getAiModelLabel, normalizeAiModel, normalizeThinkingMode, type ThinkingMode } from '@/utils/aiModels';
import { friendlyIaError } from '@/utils/iaErrors';

// V-02.09 (Fase 1.6): tipos del chat. Antes vivian dentro de AiChatPanel.tsx;
// se mueven al store (y se reexportan desde @/types) para que el store pueda
// importarlos sin acoplamiento circular con el componente.

export interface AssistantLink {
  etiqueta: string;
  ruta: string;
}

export interface AssistantClarificationOption {
  etiqueta: string;
  valor: string;
}

export interface AssistantMessageMeta {
  movimientosAnalizados: number;
  model: string;
  tokens: string;
  coste: string;
  aviso: string | null;
  origen?: 'local' | 'proveedor';
  periodo?: string;
  divisa?: string;
  enlaces?: AssistantLink[];
  opcionesAclaracion?: AssistantClarificationOption[];
  thinkingModeAplicado?: string | null;
}

export interface ChatMessage {
  role: 'user' | 'assistant' | 'system';
  content: string;
  // V-02.09 (Fase UI): timestamp generado en cliente al añadir el mensaje
  // (ms epoch). Se usa para el divisor "Today" y para el pie "HH:mm · modelo".
  timestamp: number;
  meta?: AssistantMessageMeta;
}

interface AiChatState {
  messages: ChatMessage[];
  loading: boolean;
  error: string | null;
  lastFailedPrompt: string | null;
  config: IaConfig | null;
  configCheckedAt: number | null;
  configLoading: boolean;
  // V-02.09 (Fase UI): modo de pensamiento seleccionado por el usuario. Se
  // persiste entre mensajes pero no se borra con `reset()` (es preferencia).
  thinkingMode: ThinkingMode;

  ensureConfig: () => Promise<void>;
  setThinkingMode: (mode: ThinkingMode) => void;
  ask: (prompt: string) => Promise<void>;
  reset: () => Promise<void>;
  clear: () => void;
}

const CONFIG_TTL_MS = 30 * 1000;
const MAX_PROMPT_LENGTH = 500;

export const useAiChatStore = create<AiChatState>((set, get) => ({
  messages: [],
  loading: false,
  error: null,
  lastFailedPrompt: null,
  config: null,
  configCheckedAt: null,
  configLoading: false,
  thinkingMode: 'auto',

  ensureConfig: async () => {
    const { config, configCheckedAt, configLoading } = get();
    const now = Date.now();
    if (config && configCheckedAt !== null && now - configCheckedAt < CONFIG_TTL_MS) {
      return;
    }
    if (configLoading) {
      return;
    }

    set({ configLoading: true });
    try {
      const { data } = await api.get<IaConfig>('/ia/config');
      const currentThinkingMode = get().thinkingMode;
      set({
        config: data,
        configCheckedAt: now,
        configLoading: false,
        // Si el provider no admite el thinking_mode actual, degradamos a auto
        // para no enviar un valor que el backend rechazaria.
        thinkingMode: currentThinkingMode === 'auto'
          ? 'auto'
          : normalizeThinkingMode(data.provider, currentThinkingMode),
        messages: data.configurada
          ? get().messages
          : [
              {
                role: 'system',
                content: data.mensaje_estado || 'Falta configurar la IA en Ajustes.',
                timestamp: Date.now(),
              },
            ],
      });
    } catch (err) {
      const friendly = friendlyIaError(err, 'No se pudo cargar la configuración de IA.');
      set({
        configLoading: false,
        error: friendly.texto,
      });
    }
  },

  setThinkingMode: (mode) => set({ thinkingMode: mode }),

  ask: async (rawPrompt) => {
    const prompt = rawPrompt.trim();
    if (!prompt || get().loading) {
      return;
    }

    const { config } = get();
    const configured = Boolean(config?.configurada);
    const accessBlocked = Boolean(config && (!config.habilitada || !config.usuario_puede_usar));
    const canAsk = configured && !accessBlocked;

    if (!canAsk) {
      set({
        error: config?.mensaje_estado || 'Falta configurar la IA en Ajustes.',
      });
      return;
    }

    if (prompt.length > MAX_PROMPT_LENGTH) {
      set({ error: `La pregunta no puede superar ${MAX_PROMPT_LENGTH} caracteres.` });
      return;
    }

    const { thinkingMode, config: cfg } = get();
    const provider = cfg?.provider;
    const activeModel = normalizeAiModel(provider, cfg?.model);
    const selectedPaisId = usePaisScopeStore.getState().selectedPaisId;
    const askedAt = Date.now();

    set({
      error: null,
      lastFailedPrompt: null,
      loading: true,
      messages: [...get().messages, { role: 'user', content: prompt, timestamp: askedAt }],
    });

    try {
      const { data } = await api.post<IaChatResponse>('/ia/chat', {
        pregunta: prompt,
        model: activeModel,
        pais_id: selectedPaisId || undefined,
        thinking_mode: thinkingMode,
      }, {
        // V-02.09 (Fase 1.3): el HttpClient del backend (openrouter/openai/minimax)
        // tiene timeout 45s; el default de axios es 15s, asi que cualquier consulta
        // medianamente larga se cancela antes de poder recibir respuesta. La
        // /ia/chat es la unica ruta con esta ventana amplia; el resto mantiene
        // el timeout defensivo de 15s.
        timeout: 45_000,
      });
      set({
        messages: [
          ...get().messages,
          {
            role: 'assistant',
            content: data.respuesta,
            timestamp: Date.now(),
            meta: {
              movimientosAnalizados: data.movimientos_analizados,
              model: getAiModelLabel(data.provider, data.model),
              tokens: `${data.tokens_entrada_estimados}/${data.tokens_salida_estimados}`,
              coste: `${data.coste_estimado_eur.toFixed(6)} EUR`,
              aviso: data.aviso,
              origen: data.origen,
              opcionesAclaracion: data.opciones_aclaracion ?? undefined,
              thinkingModeAplicado: data.thinking_mode_aplicado ?? null,
            },
          },
        ],
        loading: false,
      });
    } catch (err) {
      // V-02.09 (Fase 10): el backend lanza excepciones con tipos
      // especificos (IaAccessDeniedException, IaOutOfScopeException,
      // IaLimitExceededException, IaConfigurationException,
      // IaProviderException). El helper las mapea a mensajes
      // amigables para el usuario final en vez de mostrar el texto
      // crudo del backend.
      const friendly = friendlyIaError(err, 'La IA no pudo responder con los datos actuales.');
      set({
        error: friendly.texto,
        lastFailedPrompt: prompt,
        loading: false,
      });
    }
  },

  reset: async () => {
    // Invalida el ConversationContext estructurado del backend para que la
    // siguiente pregunta arranque limpia en el servidor (memoria de intencion).
    // Si el endpoint falla, limpiamos la UI igualmente: el siguiente mensaje
    // creara un nuevo contexto implicitamente al no encontrar nada en cache.
    // NO tocamos thinkingMode: es preferencia del usuario y debe sobrevivir
    // a un "Nueva conversacion".
    try {
      await api.post('/ia/conversacion/nueva');
    } catch {
      // No bloqueamos el reset de UI por un error de red aqui.
    }
    set({
      messages: [],
      error: null,
      lastFailedPrompt: null,
    });
  },

  clear: () => {
    set({
      messages: [],
      loading: false,
      error: null,
      lastFailedPrompt: null,
      config: null,
      configCheckedAt: null,
      configLoading: false,
      thinkingMode: 'auto',
    });
  },
}));
