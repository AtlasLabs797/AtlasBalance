import { FormEvent, KeyboardEvent, useEffect, useMemo, useRef, useState } from 'react';
import { Link as LinkIcon, RotateCcw, SendHorizontal } from 'lucide-react';
import { AppSelect } from '@/components/common/AppSelect';
import { CloseIconButton } from '@/components/common/CloseIconButton';
import { EmptyState } from '@/components/common/EmptyState';
import { AiMessageContent } from '@/components/ia/AiMessageContent';
import api from '@/services/api';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
import type { IaChatResponse, IaConfig, IaModel } from '@/types';
import { getAiModelLabel, getAiModelOptions, normalizeAiModel, normalizeAiProvider } from '@/utils/aiModels';
import { friendlyIaError } from '@/utils/iaErrors';

interface AssistantMessageMeta {
  movimientosAnalizados: number;
  model: string;
  tokens: string;
  coste: string;
  aviso: string | null;
  // V-02.09 (Fase 10): indica si la respuesta viene del calculo
  // local (sin proveedor) o del modelo. Permite al usuario saber
  // cuando no se ha gastado cuota.
  origen?: 'local' | 'proveedor';
  // V-02.09 (Fase 10): periodo y divisa que el sistema ha usado,
  // para que el usuario entienda que limites se le han aplicado.
  periodo?: string;
  divisa?: string;
  // V-02.09 (Fase 10): pistas para profundizar la consulta (links
  // a Extractos, Revision, Conciliacion con filtros aplicados).
  enlaces?: AssistantLink[];
  // V-02.09 (Fase 10): opciones de aclaracion cuando la pregunta
  // es ambigua. Cada opcion es un boton que rellena el input.
  opcionesAclaracion?: AssistantClarificationOption[];
}

interface AssistantLink {
  etiqueta: string;
  ruta: string;
}

interface AssistantClarificationOption {
  etiqueta: string;
  valor: string;
}

interface ChatMessage {
  role: 'user' | 'assistant' | 'system';
  content: string;
  meta?: AssistantMessageMeta;
}

interface AiChatPanelProps {
  compact?: boolean;
  onClose?: () => void;
}

// V-02.09 (Fase 10): sugerencias agrupadas por categoria. Cada
// categoria tiene una cabecera y un par de ejemplos que disparan
// el camino local (Fase 4) o el semantico (Fase 2/3) segun el
// texto. La categoria sirve para que el usuario entienda donde
// encaja su pregunta antes de escribirla.
const SUGGESTED_PROMPTS: { categoria: string; ejemplos: string[] }[] = [
  {
    categoria: 'Movimientos',
    ejemplos: [
      'Cual fue el ultimo gasto?',
      'Cual es el saldo actual de mis cuentas?'
    ]
  },
  {
    categoria: 'Tendencias',
    ejemplos: [
      'Cuanto hemos gastado este trimestre?',
      'Tendencia de gastos del ultimo ano'
    ]
  },
  {
    categoria: 'Revision',
    ejemplos: [
      'Cuales son las comisiones pendientes?',
      'Que movimientos tienen importe atipico?'
    ]
  },
  {
    categoria: 'Pendientes',
    ejemplos: [
      'Que cobros o pagos tengo esperados?',
      'Hay conciliaciones abiertas?'
    ]
  }
];
const MAX_PROMPT_LENGTH = 500;

function getCompactModelLabel(label: string) {
  return label.replace(' (elige el mejor)', '').replace(' (gratis permitido)', '').replace(' (free)', '');
}

export function AiChatPanel({ compact = false, onClose }: AiChatPanelProps) {
  const [config, setConfig] = useState<IaConfig | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState('');
  const [selectedModel, setSelectedModel] = useState('');
  const [openRouterModels, setOpenRouterModels] = useState<IaModel[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [lastFailedPrompt, setLastFailedPrompt] = useState<string | null>(null);
  const scrollRef = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLTextAreaElement | null>(null);
  const selectedPaisId = usePaisScopeStore((state) => state.selectedPaisId);
  const configured = Boolean(config?.configurada);
  const disabledReason = config?.mensaje_estado || 'Falta configurar la IA en Ajustes.';
  const accessBlocked = Boolean(config && (!config.habilitada || !config.usuario_puede_usar));
  const canAsk = configured && !accessBlocked;
  const configProvider = config?.provider;
  const configModel = config?.model;
  const selectedProvider = normalizeAiProvider(configProvider);
  const modelOptions = useMemo(() => getAiModelOptions(selectedProvider), [selectedProvider]);
  const openRouterModelOptions = useMemo(
    () => (openRouterModels.length > 0 ? openRouterModels.map((model) => ({ value: model.id, label: model.nombre || model.id })) : modelOptions),
    [modelOptions, openRouterModels],
  );
  const chatModelOptions = useMemo(
    () => modelOptions.map((model) => ({ ...model, label: getCompactModelLabel(model.label) })),
    [modelOptions],
  );
  const activeModel = normalizeAiModel(selectedProvider, selectedModel || configModel);
  const providerLabel = selectedProvider === 'OPENAI' ? 'OpenAI' : selectedProvider === 'MINIMAX' ? 'MiniMax' : 'OpenRouter';

  useEffect(() => {
    let mounted = true;
    const load = async () => {
      try {
        const { data } = await api.get<IaConfig>('/ia/config');
        if (!mounted) return;
        setConfig(data);
        setSelectedModel(normalizeAiModel(data.provider, data.model));
        if (!data.configurada) {
          setMessages([
            {
              role: 'system',
              content: data.mensaje_estado || 'Falta configurar la IA en Ajustes.',
            },
          ]);
        }
      } catch (err) {
        if (mounted) {
          const friendly = friendlyIaError(err, 'No se pudo cargar la configuración de IA.');
          setError(friendly.texto);
        }
      }
    };

    void load();
    return () => {
      mounted = false;
    };
  }, []);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight });
  }, [messages, loading]);

  useEffect(() => {
    if (canAsk && !loading) {
      inputRef.current?.focus();
    }
  }, [canAsk, loading]);

  useEffect(() => {
    if (!configProvider) {
      return;
    }

    setSelectedModel((current) => normalizeAiModel(configProvider, current || configModel));
  }, [configProvider, configModel]);

  useEffect(() => {
    if (selectedProvider !== 'OPENROUTER') {
      return;
    }

    let mounted = true;
    const loadModels = async () => {
      try {
        const { data } = await api.get<IaModel[]>('/ia/modelos', {
          params: { provider: 'OPENROUTER', search: selectedModel || configModel || undefined },
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
  }, [selectedProvider, selectedModel, configModel]);

  const ask = async (question: string) => {
    const prompt = question.trim();
    if (!prompt || loading) {
      return;
    }

    if (!canAsk) {
      setError(disabledReason);
      return;
    }

    if (prompt.length > MAX_PROMPT_LENGTH) {
      setError(`La pregunta no puede superar ${MAX_PROMPT_LENGTH} caracteres.`);
      return;
    }

    setInput('');
    setError(null);
    setLastFailedPrompt(null);
    setMessages((current) => [...current, { role: 'user', content: prompt }]);
    setLoading(true);

    try {
      const { data } = await api.post<IaChatResponse>('/ia/chat', {
        pregunta: prompt,
        model: activeModel,
        pais_id: selectedPaisId || undefined,
      }, {
        // V-02.09 (Fase 1.3): el HttpClient del backend (openrouter/openai/minimax)
        // tiene timeout 45s; el default de axios es 15s, asi que cualquier consulta
        // medianamente larga se cancela antes de poder recibir respuesta. La
        // /ia/chat es la unica ruta con esta ventana amplia; el resto mantiene
        // el timeout defensivo de 15s.
        timeout: 45_000,
      });
      setMessages((current) => [
        ...current,
        {
          role: 'assistant',
          content: data.respuesta,
          meta: {
            movimientosAnalizados: data.movimientos_analizados,
            model: getAiModelLabel(data.provider, data.model),
            tokens: `${data.tokens_entrada_estimados}/${data.tokens_salida_estimados}`,
            coste: `${data.coste_estimado_eur.toFixed(6)} EUR`,
            aviso: data.aviso,
          },
        },
      ]);
    } catch (err) {
      // V-02.09 (Fase 10): el backend lanza excepciones con tipos
      // especificos (IaAccessDeniedException, IaOutOfScopeException,
      // IaLimitExceededException, IaConfigurationException,
      // IaProviderException). El helper las mapea a mensajes
      // amigables para el usuario final en vez de mostrar el texto
      // crudo del backend.
      const friendly = friendlyIaError(err, 'La IA no pudo responder con los datos actuales.');
      setError(friendly.texto);
      setLastFailedPrompt(prompt);
    } finally {
      setLoading(false);
    }
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    void ask(input);
  };

  const handleInputKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (
      event.key !== 'Enter' ||
      event.shiftKey ||
      event.ctrlKey ||
      event.altKey ||
      event.metaKey ||
      event.nativeEvent.isComposing
    ) {
      return;
    }

    event.preventDefault();
    void ask(input);
  };

  return (
    <section
      className={`ai-chat-panel${compact ? ' ai-chat-panel--compact' : ''}`}
      aria-label="Chat IA financiero"
      onKeyDown={(event) => {
        if (event.key === 'Escape' && onClose) {
          event.stopPropagation();
          onClose();
        }
      }}
    >
      <header className="ai-chat-header">
        <div className="ai-chat-heading">
          <h2>Análisis IA</h2>
          {canAsk ? (
            <div className="ai-chat-toolbar" aria-label="Opciones de consulta IA">
              <span className="ai-chat-provider">{providerLabel}</span>
              {selectedProvider !== 'OPENROUTER' ? (
                <AppSelect
                  value={activeModel}
                  options={chatModelOptions}
                  onChange={setSelectedModel}
                  ariaLabel={`Modelo de IA en ${providerLabel}`}
                  disabled={!canAsk || loading}
                />
              ) : (
                <>
                  <input
                    className="ai-chat-model-input"
                    list="ai-chat-openrouter-modelos"
                    value={selectedModel || activeModel}
                    onChange={(event) => setSelectedModel(event.target.value)}
                    aria-label={`Modelo de IA en ${providerLabel}`}
                    disabled={!canAsk || loading}
                  />
                  <datalist id="ai-chat-openrouter-modelos">
                    {openRouterModelOptions.map((model) => (
                      <option key={model.value} value={model.value}>
                        {getCompactModelLabel(model.label)}
                      </option>
                    ))}
                  </datalist>
                </>
              )}
            </div>
          ) : null}
        </div>
        {onClose ? (
          <CloseIconButton className="ai-chat-close" onClick={onClose} ariaLabel="Cerrar chat IA" title="Cerrar" />
        ) : null}
      </header>

      {!configured ? (
        <div className="ai-chat-config-warning">
          <strong>IA no disponible</strong>
          <p>{disabledReason}</p>
        </div>
      ) : null}

      {accessBlocked ? (
        <EmptyState
          variant="permission"
          title="IA no disponible para tu usuario."
          subtitle={disabledReason}
        />
      ) : null}

      {canAsk ? (
        <>
          <div ref={scrollRef} className="ai-chat-messages" aria-live="polite">
            {messages.length === 0 ? (
              <div className="ai-chat-empty">
                {SUGGESTED_PROMPTS.map((grupo) => (
                  <section key={grupo.categoria} className="ai-chat-suggestions">
                    <h3>{grupo.categoria}</h3>
                    <ul>
                      {grupo.ejemplos.map((prompt) => (
                        <li key={prompt}>
                          <button type="button" onClick={() => void ask(prompt)} disabled={!canAsk || loading}>
                            {prompt}
                          </button>
                        </li>
                      ))}
                    </ul>
                  </section>
                ))}
              </div>
            ) : (
              messages.map((message, index) => (
                <article key={`${message.role}-${index}`} className={`ai-chat-message ai-chat-message--${message.role}`}>
                  <span>{message.role === 'user' ? 'Tú' : message.role === 'assistant' ? 'IA' : 'Sistema'}</span>
                  <AiMessageContent content={message.content} />
                  {message.meta?.opcionesAclaracion && message.meta.opcionesAclaracion.length > 0 ? (
                    <div className="ai-chat-clarification">
                      <p className="ai-chat-clarification-question">{message.content}</p>
                      <ul>
                        {message.meta.opcionesAclaracion.map((opcion) => (
                          <li key={opcion.valor}>
                            <button type="button" onClick={() => void ask(opcion.etiqueta)} disabled={!canAsk || loading}>
                              {opcion.etiqueta}
                            </button>
                          </li>
                        ))}
                      </ul>
                    </div>
                  ) : null}
                  {message.meta?.enlaces && message.meta.enlaces.length > 0 ? (
                    <ul className="ai-chat-links">
                      {message.meta.enlaces.map((enlace) => (
                        <li key={enlace.ruta}>
                          <a href={enlace.ruta}>
                            <LinkIcon size={12} aria-hidden="true" /> {enlace.etiqueta}
                          </a>
                        </li>
                      ))}
                    </ul>
                  ) : null}
                  {message.meta ? (
                    <details className="ai-chat-message-meta">
                      <summary>Detalles de IA</summary>
                      <dl>
                        <div>
                          <dt>Origen</dt>
                          <dd>{message.meta.origen === 'local' ? 'Calculado localmente' : 'Proveedor externo'}</dd>
                        </div>
                        <div>
                          <dt>Movimientos</dt>
                          <dd>{message.meta.movimientosAnalizados}</dd>
                        </div>
                        <div>
                          <dt>Modelo</dt>
                          <dd>{message.meta.model}</dd>
                        </div>
                        {message.meta.periodo ? (
                          <div>
                            <dt>Periodo</dt>
                            <dd>{message.meta.periodo}</dd>
                          </div>
                        ) : null}
                        {message.meta.divisa ? (
                          <div>
                            <dt>Divisa</dt>
                            <dd>{message.meta.divisa}</dd>
                          </div>
                        ) : null}
                        <div>
                          <dt>Tokens</dt>
                          <dd>{message.meta.tokens}</dd>
                        </div>
                        <div>
                          <dt>Coste</dt>
                          <dd>{message.meta.coste}</dd>
                        </div>
                      </dl>
                      {message.meta.aviso ? <p>{message.meta.aviso}</p> : null}
                    </details>
                  ) : null}
                </article>
              ))
            )}
            {loading ? (
              <p className="ai-chat-loading" role="status" aria-label="Analizando datos reales">
                <span className="ai-chat-loading-dots" aria-hidden="true">
                  <span />
                  <span />
                  <span />
                </span>
                Analizando datos reales...
              </p>
            ) : null}
          </div>

          {error ? (
            <div className="auth-error" role="alert">
              <p>{error}</p>
              {lastFailedPrompt ? (
                <button type="button" className="button-secondary" onClick={() => void ask(lastFailedPrompt)} disabled={loading}>
                  Reintentar última pregunta
                </button>
              ) : null}
            </div>
          ) : null}

          <form className="ai-chat-form" onSubmit={submit}>
            <label className="sr-only" htmlFor={compact ? 'ai-chat-floating-question' : 'ai-chat-page-question'}>
              Pregunta para la IA financiera
            </label>
            <textarea
              ref={inputRef}
              id={compact ? 'ai-chat-floating-question' : 'ai-chat-page-question'}
              value={input}
              onChange={(event) => setInput(event.target.value)}
              onKeyDown={handleInputKeyDown}
              placeholder="Pregunta por movimientos, comisiones o saldos..."
              disabled={!canAsk || loading}
              maxLength={MAX_PROMPT_LENGTH}
              rows={1}
            />
            {messages.length > 0 ? (
              <button
                type="button"
                className="ai-chat-reset"
                onClick={() => {
                  setMessages([]);
                  setError(null);
                  setLastFailedPrompt(null);
                }}
                disabled={loading}
                aria-label="Nueva conversacion"
                title="Nueva conversacion"
              >
                <RotateCcw size={16} aria-hidden="true" />
              </button>
            ) : null}
            <button type="submit" disabled={!canAsk || loading || !input.trim()} aria-label="Enviar pregunta a IA">
              <SendHorizontal size={18} aria-hidden="true" />
            </button>
          </form>
        </>
      ) : null}
    </section>
  );
}
