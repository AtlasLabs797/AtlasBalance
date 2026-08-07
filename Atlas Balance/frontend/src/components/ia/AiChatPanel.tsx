import { FormEvent, KeyboardEvent, useEffect, useMemo, useRef, useState } from 'react';
import { Link as LinkIcon, RotateCcw, SendHorizontal } from 'lucide-react';
import { AppSelect } from '@/components/common/AppSelect';
import { CloseIconButton } from '@/components/common/CloseIconButton';
import { EmptyState } from '@/components/common/EmptyState';
import { AiMessageContent } from '@/components/ia/AiMessageContent';
import { useAiChatStore } from '@/stores/aiChatStore';
import { normalizeAiModel, normalizeAiProvider, getAiModelOptions } from '@/utils/aiModels';

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
  // V-02.09 (Fase 1.6): estado de conversacion (mensajes, modelo seleccionado,
  // errores, loading, config) vive en el store compartido entre el chat
  // flotante y la pagina /ia. Asi ambas instancias ven la misma conversacion
  // y la misma eleccion de modelo. El input (texto a medio escribir) sigue
  // siendo local por instancia: si los dos textareas estan visibles a la vez,
  // cada uno mantiene su propio borrador.
  const messages = useAiChatStore((state) => state.messages);
  const loading = useAiChatStore((state) => state.loading);
  const error = useAiChatStore((state) => state.error);
  const lastFailedPrompt = useAiChatStore((state) => state.lastFailedPrompt);
  const selectedModel = useAiChatStore((state) => state.selectedModel);
  const openRouterModels = useAiChatStore((state) => state.openRouterModels);
  const config = useAiChatStore((state) => state.config);
  const ensureConfig = useAiChatStore((state) => state.ensureConfig);
  const loadOpenRouterModels = useAiChatStore((state) => state.loadOpenRouterModels);
  const setSelectedModel = useAiChatStore((state) => state.setSelectedModel);
  const reset = useAiChatStore((state) => state.reset);

  const [input, setInput] = useState('');
  const scrollRef = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLTextAreaElement | null>(null);

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
    void ensureConfig();
  }, [ensureConfig]);

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
    const current = useAiChatStore.getState().selectedModel;
    const normalized = normalizeAiModel(configProvider, current || configModel);
    if (normalized !== current) {
      setSelectedModel(normalized);
    }
  }, [configProvider, configModel, setSelectedModel]);

  useEffect(() => {
    if (selectedProvider !== 'OPENROUTER') {
      return;
    }
    void loadOpenRouterModels(selectedModel || configModel);
  }, [selectedProvider, selectedModel, configModel, loadOpenRouterModels]);

  const handleQuickAsk = (prompt: string) => {
    void useAiChatStore.getState().ask(prompt);
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const prompt = input.trim();
    if (!prompt) {
      return;
    }
    setInput('');
    void useAiChatStore.getState().ask(prompt);
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
    const prompt = input.trim();
    if (!prompt) {
      return;
    }
    setInput('');
    void useAiChatStore.getState().ask(prompt);
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
                          <button type="button" onClick={() => handleQuickAsk(prompt)} disabled={!canAsk || loading}>
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
                            <button type="button" onClick={() => handleQuickAsk(opcion.etiqueta)} disabled={!canAsk || loading}>
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
                <button type="button" className="button-secondary" onClick={() => handleQuickAsk(lastFailedPrompt)} disabled={loading}>
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
                  void reset();
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
