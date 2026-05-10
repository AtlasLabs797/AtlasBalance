import { FormEvent, useEffect, useRef, useState } from 'react';
import { SendHorizontal } from 'lucide-react';
import api from '@/services/api';
import type { IaChatResponse, IaConfig } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';

interface ChatMessage {
  role: 'user' | 'assistant' | 'system';
  content: string;
}

interface AiChatPanelProps {
  compact?: boolean;
  onClose?: () => void;
}

const EXAMPLE_PROMPTS = [
  'Que comisiones bancarias estan pendientes de devolucion?',
  'Cuanto se ha pagado en seguros este ano?',
  'Que cuentas han tenido mas gastos este trimestre?',
];
const MAX_PROMPT_LENGTH = 500;

export function AiChatPanel({ compact = false, onClose }: AiChatPanelProps) {
  const [config, setConfig] = useState<IaConfig | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const scrollRef = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLTextAreaElement | null>(null);
  const configured = Boolean(config?.configurada);
  const disabledReason = config?.mensaje_estado || 'Falta configurar la IA en Ajustes.';
  const accessBlocked = Boolean(config && (!config.habilitada || !config.usuario_puede_usar));

  useEffect(() => {
    let mounted = true;
    const load = async () => {
      try {
        const { data } = await api.get<IaConfig>('/ia/config');
        if (!mounted) return;
        setConfig(data);
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
          setError(extractErrorMessage(err, 'No se pudo cargar la configuracion de IA.'));
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
    if (configured && !loading) {
      inputRef.current?.focus();
    }
  }, [configured, loading]);

  const ask = async (question: string) => {
    const prompt = question.trim();
    if (!prompt || loading) {
      return;
    }

    if (!configured) {
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
      const { data } = await api.post<IaChatResponse>('/ia/chat', { pregunta: prompt });
      setMessages((current) => [
        ...current,
        {
          role: 'assistant',
          content: `${data.respuesta}\n\nMovimientos analizados: ${data.movimientos_analizados}. Modelo: ${data.model}. Tokens aprox.: ${data.tokens_entrada_estimados}/${data.tokens_salida_estimados}. Coste estimado: ${data.coste_estimado_eur.toFixed(6)} EUR.${data.aviso ? `\n${data.aviso}` : ''}`,
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
        <h2>Análisis IA</h2>
        {onClose ? (
          <button type="button" className="ai-chat-close" onClick={onClose} aria-label="Cerrar chat IA" title="Cerrar">
            ✕
          </button>
        ) : null}
      </header>

      {!configured ? (
        <div className="ai-chat-config-warning">
          <strong>IA no disponible</strong>
          <p>{disabledReason}</p>
        </div>
      ) : null}

      {!accessBlocked ? (
        <>
          <div ref={scrollRef} className="ai-chat-messages" aria-live="polite">
            {messages.length === 0 ? (
              <div className="ai-chat-empty">
                {EXAMPLE_PROMPTS.map((prompt) => (
                  <button key={prompt} type="button" onClick={() => void ask(prompt)} disabled={!configured || loading}>
                    {prompt}
                  </button>
                ))}
              </div>
            ) : (
              messages.map((message, index) => (
                <article key={`${message.role}-${index}`} className={`ai-chat-message ai-chat-message--${message.role}`}>
                  <span>{message.role === 'user' ? 'Tu' : message.role === 'assistant' ? 'IA' : 'Sistema'}</span>
                  <p>{message.content}</p>
                </article>
              ))
            )}
            {loading ? (
              <p className="ai-chat-loading" role="status">
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
              placeholder="Haz una pregunta financiera..."
              disabled={!configured || loading}
              maxLength={MAX_PROMPT_LENGTH}
              rows={1}
            />
            <button type="submit" disabled={!configured || loading || !input.trim()} aria-label="Enviar pregunta a IA">
              <SendHorizontal size={18} aria-hidden="true" />
            </button>
          </form>
        </>
      ) : null}
    </section>
  );
}
