import { FormEvent, KeyboardEvent, useEffect, useMemo, useRef, useState } from 'react';
import {
  ArrowUp,
  Check,
  ChevronDown,
  Gauge,
  Link as LinkIcon,
  RotateCcw,
  Sparkles,
  Square,
  type LucideIcon,
} from 'lucide-react';
import { CloseIconButton } from '@/components/common/CloseIconButton';
import { EmptyState } from '@/components/common/EmptyState';
import { IconAiFace, type AiFaceState } from '@/components/Icons';
import { AiMessageContent } from '@/components/ia/AiMessageContent';
import { useAuthStore } from '@/stores/authStore';
import { useAiChatStore, type ChatMessage } from '@/stores/aiChatStore';
import {
  getAiModelLabel,
  getAiModelOptions,
  getThinkingModeOptions,
  normalizeAiModel,
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
const SUGGESTED_PROMPTS = [
  '¿Cuál fue el último gasto?',
  '¿Cuál es el saldo actual de mis cuentas?',
  '¿Cuánto hemos gastado este trimestre?',
  'Tendencia de gastos del último año',
  '¿Cuáles son las comisiones pendientes?',
  '¿Qué movimientos tienen importe atípico?',
  '¿Qué cobros o pagos tengo esperados?',
  '¿Hay conciliaciones abiertas?',
];
const COMPACT_SUGGESTED_PROMPTS = ['¿Qué cuenta baja del umbral?', 'Resumen de agosto'];
const MAX_PROMPT_LENGTH = 500;

function formatMessageTime(timestamp: number) {
  const date = new Date(timestamp);
  const hours = date.getHours().toString().padStart(2, '0');
  const minutes = date.getMinutes().toString().padStart(2, '0');
  return `${hours}:${minutes}`;
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
      return 'Esfuerzo automático';
    default:
      return 'Esfuerzo automático';
  }
}

interface AiMenuProps {
  value: string;
  options: { value: string; label: string }[];
  onChange: (value: string) => void;
  icon: LucideIcon;
  ariaLabel: string;
  buttonLabel: string;
  disabled?: boolean;
}

function AiMenu({ value, options, onChange, icon: MenuIcon, ariaLabel, buttonLabel, disabled = false }: AiMenuProps) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!open) {
      return undefined;
    }

    const handlePointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    };
    const handleKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false);
      }
    };

    document.addEventListener('pointerdown', handlePointerDown);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('pointerdown', handlePointerDown);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [open]);

  return (
    <div ref={rootRef} className="ai-chat-tool-menu">
      <button
        type="button"
        className="ai-chat-tool"
        aria-label={ariaLabel}
        aria-haspopup="menu"
        aria-expanded={open}
        disabled={disabled}
        onClick={() => setOpen((current) => !current)}
      >
        <MenuIcon size={14} strokeWidth={1.8} aria-hidden="true" />
        <span>{buttonLabel}</span>
        <ChevronDown size={12} strokeWidth={1.8} aria-hidden="true" />
      </button>
      {open ? (
        <div className="ai-chat-tool-popover" role="menu" aria-label={ariaLabel}>
          {options.map((option) => (
            <button
              key={option.value}
              type="button"
              className="ai-chat-tool-option"
              role="menuitemradio"
              aria-checked={option.value === value}
              onClick={() => {
                onChange(option.value);
                setOpen(false);
              }}
            >
              <span>{option.label}</span>
              {option.value === value ? <Check size={14} strokeWidth={2} aria-hidden="true" /> : null}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function getUserInitials(name: string | null | undefined) {
  const source = name?.trim() || '';
  if (!source) {
    return 'TÚ';
  }

  const words = source.split(/\s+/).filter(Boolean);
  if (words.length > 1) {
    return `${words[0][0]}${words[words.length - 1][0]}`.toUpperCase();
  }

  return source.slice(0, 2).toUpperCase();
}

function getThinkingControlLabel(value: string, label: string) {
  switch (value) {
    case 'auto':
      return 'Auto';
    case 'low':
      return 'Bajo';
    case 'medium':
      return 'Medio';
    case 'high':
      return 'Alto';
    default:
      return label;
  }
}

function getModelControlLabel(value: string, label: string) {
  if (value === 'openrouter/auto') {
    return 'Auto';
  }
  if (value.endsWith(':free')) {
    return 'Gratis';
  }
  return label;
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
  const selectedModel = useAiChatStore((state) => state.selectedModel);
  const setSelectedModel = useAiChatStore((state) => state.setSelectedModel);
  const reset = useAiChatStore((state) => state.reset);
  const usuario = useAuthStore((state) => state.usuario);

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
  const activeModel = normalizeAiModel(selectedProvider, selectedModel || configModel);
  const activeModelLabel = getAiModelLabel(selectedProvider, activeModel);
  const userInitials = useMemo(
    () => getUserInitials(usuario?.nombre_completo || usuario?.email),
    [usuario?.email, usuario?.nombre_completo],
  );

  const modelOptions = useMemo(() => {
    const options = getAiModelOptions(selectedProvider);
    return options.some((option) => option.value === activeModel)
      ? options
      : [{ value: activeModel, label: activeModelLabel }, ...options];
  }, [activeModel, activeModelLabel, selectedProvider]);

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

  useEffect(() => {
    const element = inputRef.current;
    if (!element) {
      return;
    }
    element.style.height = 'auto';
    element.style.height = `${Math.min(element.scrollHeight, 200)}px`;
  }, [input]);

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
  const faceState: AiFaceState = loading ? 'thinking' : input.trim() ? 'listening' : 'idle';
  const selectedThinkingLabel = thinkingModeOptions.find((option) => option.value === thinkingMode)?.label
    || humanizeThinkingMode(thinkingMode);
  const suggestedPrompts = compact ? COMPACT_SUGGESTED_PROMPTS : SUGGESTED_PROMPTS;

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
          <span
            className={`ai-chat-face${loading ? ' ai-chat-face--thinking' : ''}`}
            aria-hidden="true"
          >
            <IconAiFace state={faceState} />
          </span>
          <div className="ai-chat-heading-copy">
            <h2>Asistente</h2>
            <p>Solo ve lo que tú puedes ver</p>
          </div>
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
          {onClose && !compact ? (
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
              <div className="ai-chat-empty" aria-label="Preguntas sugeridas">
                {compact ? (
                  <div className="ai-chat-welcome">
                    <span className="ai-chat-message-face" aria-hidden="true">
                      <IconAiFace state="idle" size={36} />
                    </span>
                    <p>Puedo responder sobre saldos, alertas y movimientos de esta cuenta. ¿Qué necesitas revisar?</p>
                  </div>
                ) : null}
                <div className="ai-chat-suggestions">
                  {suggestedPrompts.map((prompt) => (
                    <button type="button" key={prompt} onClick={() => handleQuickAsk(prompt)} disabled={!canAsk || loading}>
                      {prompt}
                    </button>
                  ))}
                </div>
              </div>
            ) : (
              messages.map((message, index) =>
                renderMessage(message, index, messages, activeModelLabel, loading, userInitials),
              )
            )}
            {loading ? (
              <div className="ai-chat-message-group ai-chat-message-group--role-start">
                <div className="ai-chat-message-row ai-chat-message-row--assistant" role="status" aria-label="Pensando">
                  <span className="ai-chat-message-face ai-chat-message-face--thinking" aria-hidden="true">
                    <IconAiFace state="thinking" size={36} />
                  </span>
                  <div className="ai-chat-message-column">
                    <article className="ai-chat-message ai-chat-message--assistant ai-chat-message--thinking">
                      <span className="ai-chat-loading-label">Pensando</span>
                    </article>
                  </div>
                </div>
              </div>
            ) : null}
          </div>

          {error ? (
            <div className="ai-chat-error" role="alert">
              <p>{error}</p>
              {lastFailedPrompt ? (
                <button type="button" className="button-secondary" onClick={() => handleQuickAsk(lastFailedPrompt)} disabled={loading}>
                  Reintentar última pregunta
                </button>
              ) : null}
            </div>
          ) : null}

          <form className="ai-chat-composer" onSubmit={submit}>
            <div className="ai-chat-composer-box">
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
                placeholder="Pregunta sobre saldos, proyectos o registros…"
                disabled={!canAsk || loading}
                maxLength={MAX_PROMPT_LENGTH}
                rows={1}
              />
              <div className="ai-chat-composer-footer">
                <div className="ai-chat-composer-footer-left">
                  <AiMenu
                    value={activeModel}
                    options={modelOptions}
                    onChange={setSelectedModel}
                    icon={Sparkles}
                    ariaLabel="Modelo de IA"
                    buttonLabel={getModelControlLabel(activeModel, activeModelLabel)}
                    disabled={!canAsk || loading}
                  />
                  <AiMenu
                    value={thinkingMode}
                    options={thinkingModeOptions}
                    onChange={(value) => setThinkingMode(value as ThinkingMode)}
                    icon={Gauge}
                    ariaLabel="Modo de pensamiento"
                    buttonLabel={getThinkingControlLabel(thinkingMode, selectedThinkingLabel)}
                    disabled={!canAsk || loading}
                  />
                </div>
                <button
                  type="submit"
                  className={`ai-chat-composer-send${loading ? ' ai-chat-composer-send--loading' : ''}`}
                  disabled={!canAsk || loading || !input.trim()}
                  aria-label="Enviar pregunta a IA"
                  title="Enviar"
                >
                  {loading ? <Square size={14} fill="currentColor" aria-hidden="true" /> : <ArrowUp size={24} strokeWidth={2.5} aria-hidden="true" />}
                </button>
              </div>
            </div>
            <span className="ai-chat-composer-footnote">Las respuestas citan registros. Verifica antes de operar.</span>
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
  userInitials: string,
) {
  const previous = messages[index - 1];
  const next = messages[index + 1];
  const isFirstInRole = !previous || previous.role !== message.role;
  const isLastInRole = !next || next.role !== message.role;

  if (message.role === 'user') {
    return (
      <div
        key={`user-${index}`}
        className={`ai-chat-message-group${isFirstInRole ? ' ai-chat-message-group--role-start' : ''}`}
      >
        <div className="ai-chat-message-row ai-chat-message-row--user">
          <div className="ai-chat-message-column">
            <article className="ai-chat-message ai-chat-message--user">
              <p>{message.content}</p>
            </article>
            {isLastInRole ? <span className="ai-chat-message-meta-time">{formatMessageTime(message.timestamp)}</span> : null}
          </div>
          <span className={`ai-chat-user-avatar${isFirstInRole ? '' : ' ai-chat-avatar--hidden'}`} aria-hidden="true">
            {userInitials}
          </span>
        </div>
      </div>
    );
  }

  if (message.role === 'system') {
    return (
      <div
        key={`system-${index}`}
        className={`ai-chat-message-group${isFirstInRole ? ' ai-chat-message-group--role-start' : ''}`}
      >
        <article className="ai-chat-message ai-chat-message--system">
          <p>{message.content}</p>
        </article>
      </div>
    );
  }

  return (
    <div
      key={`assistant-${index}`}
      className={`ai-chat-message-group${isFirstInRole ? ' ai-chat-message-group--role-start' : ''}`}
    >
      <div className="ai-chat-message-row ai-chat-message-row--assistant">
        <span className={`ai-chat-message-face${isFirstInRole ? '' : ' ai-chat-avatar--hidden'}`} aria-hidden="true">
          {isFirstInRole ? <IconAiFace state="idle" size={36} /> : null}
        </span>
        <div className="ai-chat-message-column">
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
          {isLastInRole ? (
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
          ) : null}
        </div>
      </div>
    </div>
  );
}
