import { FormEvent, KeyboardEvent, useEffect, useMemo, useRef, useState } from 'react';
import { ArrowUp, Link as LinkIcon, RotateCcw, SendHorizontal } from 'lucide-react';
import { AppSelect } from '@/components/common/AppSelect';
import { CloseIconButton } from '@/components/common/CloseIconButton';
import { EmptyState } from '@/components/common/EmptyState';
import { AiMessageContent } from '@/components/ia/AiMessageContent';
import { useAiChatStore, type ChatMessage } from '@/stores/aiChatStore';
import {
  getAiModelLabel,
  getThinkingModeOptions,
  normalizeAiProvider,
  type ThinkingMode,
} from '@/utils/aiModels';

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

function formatMessageTime(timestamp: number) {
  const date = new Date(timestamp);
  const hours = date.getHours().toString().padStart(2, '0');
  const minutes = date.getMinutes().toString().padStart(2, '0');
  return `${hours}:${minutes}`;
}

function isSameLocalDay(a: number, b: number) {
  const left = new Date(a);
  const right = new Date(b);
  return (
    left.getFullYear() === right.getFullYear() &&
    left.getMonth() === right.getMonth() &&
    left.getDate() === right.getDate()
  );
}

function humanizeThinkingMode(value: string | null | undefined) {
  switch (value) {
    case 'low':
      return 'Esfuerzo bajo';
    case 'medium':
      return 'Esfuerzo medio';
    case 'high':
      return 'Esfuerzo alto';
    case 'on':
      return 'Pensamiento activado';
    case 'off':
      return 'Pensamiento desactivado';
    case 'auto':
      return 'Esfuerzo automatico';
    default:
      return 'Esfuerzo automatico';
  }
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
  const config = useAiChatStore((state) => state.config);
  const ensureConfig = useAiChatStore((state) => state.ensureConfig);
  const thinkingMode = useAiChatStore((state) => state.thinkingMode);
  const setThinkingMode = useAiChatStore((state) => state.setThinkingMode);
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
  const activeModelLabel = getAiModelLabel(selectedProvider, configModel);
  const providerLabel = selectedProvider === 'OPENAI' ? 'OpenAI' : selectedProvider === 'MINIMAX' ? 'MiniMax' : 'OpenRouter';

  // V-02.09 (Fase UI): el backend publica los modos de pensamiento del provider;
  // si no llega la lista usamos el fallback local en `getThinkingModeOptions`.
  const thinkingModeOptions = useMemo(() => {
    const backend = config?.thinking_modes ?? [];
    if (backend.length === 0) {
      return getThinkingModeOptions(selectedProvider);
    }
    return backend.map((option) => ({ value: option.value, label: option.label }));
  }, [config?.thinking_modes, selectedProvider]);

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

  // V-02.09 (Fase UI): si el provider actual no admite el thinking_mode
  // seleccionado, degradamos a auto para no enviar valores no soportados.
  useEffect(() => {
    const allowed = thinkingModeOptions.map((option) => option.value);
    if (!allowed.includes(thinkingMode)) {
      setThinkingMode('auto');
    }
  }, [thinkingModeOptions, thinkingMode, setThinkingMode]);

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

  const showReset = messages.length > 0;
  const hasThinkingOptions = thinkingModeOptions.length > 1;

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
          <span className="ai-chat-provider" aria-label={`Proveedor activo: ${providerLabel}`}>
            {providerLabel}
          </span>
        </div>
        <div className="ai-chat-header-actions">
          {showReset ? (
            <button
              type="button"
              className="ai-chat-header-button"
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
          {onClose ? (
            <CloseIconButton className="ai-chat-header-button" onClick={onClose} ariaLabel="Cerrar chat IA" title="Cerrar" />
          ) : null}
        </div>
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
              messages.map((message, index) =>
                renderMessage(message, index, messages, activeModelLabel, loading),
              )
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

          <form className="ai-chat-composer" onSubmit={submit}>
            <label className="sr-only" htmlFor={compact ? 'ai-chat-floating-question' : 'ai-chat-page-question'}>
              Pregunta para la IA financiera
            </label>
            <textarea
              ref={inputRef}
              id={compact ? 'ai-chat-floating-question' : 'ai-chat-page-question'}
              className="ai-chat-composer-input"
              value={input}
              onChange={(event) => setInput(event.target.value)}
              onKeyDown={handleInputKeyDown}
              placeholder="Pregunta por movimientos, comisiones o saldos..."
              disabled={!canAsk || loading}
              maxLength={MAX_PROMPT_LENGTH}
              rows={1}
            />
            <div className="ai-chat-composer-footer">
              <div className="ai-chat-composer-footer-left">
                {hasThinkingOptions ? (
                  <AppSelect
                    className="ai-chat-composer-trigger"
                    value={thinkingMode}
                    options={thinkingModeOptions}
                    onChange={(value) => setThinkingMode(value as ThinkingMode)}
                    ariaLabel="Modo de pensamiento"
                    disabled={!canAsk || loading}
                  />
                ) : (
                  <span className="ai-chat-composer-static" aria-label="Modo de pensamiento">
                    {humanizeThinkingMode(thinkingMode)}
                  </span>
                )}
              </div>
              <div className="ai-chat-composer-footer-right">
                <span className="ai-chat-composer-model" aria-label={`Modelo activo: ${activeModelLabel}`}>
                  {activeModelLabel}
                </span>
                <button
                  type="submit"
                  className="ai-chat-composer-send"
                  disabled={!canAsk || loading || !input.trim()}
                  aria-label="Enviar pregunta a IA"
                  title="Enviar"
                >
                  {loading ? <SendHorizontal size={18} aria-hidden="true" /> : <ArrowUp size={18} aria-hidden="true" />}
                </button>
              </div>
            </div>
          </form>
        </>
      ) : null}
    </section>
  );
}

function renderMessage(
  message: ChatMessage,
  index: number,
  messages: ChatMessage[],
  activeModelLabel: string,
  loading: boolean,
) {
  const showDayDivider =
    index === 0 || !isSameLocalDay(messages[index - 1].timestamp, message.timestamp);
  const dayLabel = showDayDivider ? formatDayLabel(message.timestamp) : null;

  if (message.role === 'user') {
    return (
      <div key={`user-${index}`} className="ai-chat-message-group">
        {dayLabel ? (
          <div className="ai-chat-day-divider" role="separator">
            <span>{dayLabel}</span>
          </div>
        ) : null}
        <article className="ai-chat-message ai-chat-message--user">
          <p>{message.content}</p>
        </article>
      </div>
    );
  }

  if (message.role === 'system') {
    return (
      <div key={`system-${index}`} className="ai-chat-message-group">
        {dayLabel ? (
          <div className="ai-chat-day-divider" role="separator">
            <span>{dayLabel}</span>
          </div>
        ) : null}
        <article className="ai-chat-message ai-chat-message--system">
          <p>{message.content}</p>
        </article>
      </div>
    );
  }

  return (
    <div key={`assistant-${index}`} className="ai-chat-message-group">
      {dayLabel ? (
        <div className="ai-chat-day-divider" role="separator">
          <span>{dayLabel}</span>
        </div>
      ) : null}
      <article className="ai-chat-message ai-chat-message--assistant">
        <AiMessageContent content={message.content} />
        {message.meta?.opcionesAclaracion && message.meta.opcionesAclaracion.length > 0 ? (
          <div className="ai-chat-clarification">
            <p className="ai-chat-clarification-question">{message.content}</p>
            <ul>
              {message.meta.opcionesAclaracion.map((opcion) => (
                <li key={opcion.valor}>
                  <button
                    type="button"
                    onClick={() => useAiChatStore.getState().ask(opcion.etiqueta)}
                    disabled={loading}
                  >
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
        <p className="ai-chat-message-meta-inline">
          <span>{formatMessageTime(message.timestamp)}</span>
          <span aria-hidden="true">·</span>
          <span className="ai-chat-message-meta-model">
            {message.meta?.model ?? activeModelLabel}
          </span>
          {message.meta?.thinkingModeAplicado && message.meta.thinkingModeAplicado !== 'auto' ? (
            <>
              <span aria-hidden="true">·</span>
              <span className="ai-chat-message-meta-thinking">{humanizeThinkingMode(message.meta.thinkingModeAplicado)}</span>
            </>
          ) : null}
        </p>
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
    </div>
  );
}

function formatDayLabel(timestamp: number) {
  const date = new Date(timestamp);
  const today = new Date();
  if (isSameLocalDay(timestamp, today.getTime())) {
    return 'Hoy';
  }
  const yesterday = new Date(today);
  yesterday.setDate(today.getDate() - 1);
  if (isSameLocalDay(timestamp, yesterday.getTime())) {
    return 'Ayer';
  }
  return date.toLocaleDateString('es-ES', { day: '2-digit', month: 'short' });
}
