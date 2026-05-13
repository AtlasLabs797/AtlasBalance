import { FormEvent, KeyboardEvent, useEffect, useMemo, useRef, useState } from 'react';
import { SendHorizontal } from 'lucide-react';
import { AppSelect } from '@/components/common/AppSelect';
import { CloseIconButton } from '@/components/common/CloseIconButton';
import { EmptyState } from '@/components/common/EmptyState';
import { AiMessageContent } from '@/components/ia/AiMessageContent';
import api from '@/services/api';
import type { IaChatResponse, IaConfig } from '@/types';
import { getAiModelLabel, getAiModelOptions, normalizeAiModel, normalizeAiProvider } from '@/utils/aiModels';
import { extractErrorMessage } from '@/utils/errorMessage';

interface AssistantMessageMeta {
  movimientosAnalizados: number;
  model: string;
  tokens: string;
  coste: string;
  aviso: string | null;
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

const EXAMPLE_PROMPTS = [
  '¿Qué comisiones bancarias están pendientes de devolución?',
  '¿Cuánto se ha pagado en seguros este año?',
  '¿Qué cuentas han tenido más gastos este trimestre?',
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
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
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
  const chatModelOptions = useMemo(
    () => modelOptions.map((model) => ({ ...model, label: getCompactModelLabel(model.label) })),
    [modelOptions],
  );
  const activeModel = normalizeAiModel(selectedProvider, selectedModel || configModel);
  const providerLabel = selectedProvider === 'OPENAI' ? 'OpenAI' : 'OpenRouter';

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
          setError(extractErrorMessage(err, 'No se pudo cargar la configuración de IA.'));
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
    setMessages((current) => [...current, { role: 'user', content: prompt }]);
    setLoading(true);

    try {
      const { data } = await api.post<IaChatResponse>('/ia/chat', { pregunta: prompt, model: activeModel });
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
      setError(extractErrorMessage(err, 'La IA no pudo responder con los datos actuales.'));
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
              <AppSelect
                value={activeModel}
                options={chatModelOptions}
                onChange={setSelectedModel}
                ariaLabel={`Modelo de IA en ${providerLabel}`}
                disabled={!canAsk || loading}
              />
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
                {EXAMPLE_PROMPTS.map((prompt) => (
                  <button key={prompt} type="button" onClick={() => void ask(prompt)} disabled={!canAsk || loading}>
                    {prompt}
                  </button>
                ))}
              </div>
            ) : (
              messages.map((message, index) => (
                <article key={`${message.role}-${index}`} className={`ai-chat-message ai-chat-message--${message.role}`}>
                  <span>{message.role === 'user' ? 'Tú' : message.role === 'assistant' ? 'IA' : 'Sistema'}</span>
                  <AiMessageContent content={message.content} />
                  {message.meta ? (
                    <details className="ai-chat-message-meta">
                      <summary>Detalles de IA</summary>
                      <dl>
                        <div>
                          <dt>Movimientos</dt>
                          <dd>{message.meta.movimientosAnalizados}</dd>
                        </div>
                        <div>
                          <dt>Modelo</dt>
                          <dd>{message.meta.model}</dd>
                        </div>
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

          {error ? <p className="auth-error" role="alert">{error}</p> : null}

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
            <button type="submit" disabled={!canAsk || loading || !input.trim()} aria-label="Enviar pregunta a IA">
              <SendHorizontal size={18} aria-hidden="true" />
            </button>
          </form>
        </>
      ) : null}
    </section>
  );
}
