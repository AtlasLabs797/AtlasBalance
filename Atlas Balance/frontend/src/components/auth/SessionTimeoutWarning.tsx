import React, { useEffect, useRef } from 'react';

interface SessionTimeoutWarningProps {
  open: boolean;
  remainingSeconds: number;
  onContinue: () => void;
  onLogout: () => void;
}

export const SessionTimeoutWarning: React.FC<SessionTimeoutWarningProps> = ({
  open,
  remainingSeconds,
  onContinue,
  onLogout,
}) => {
  const continueButtonRef = useRef<HTMLButtonElement | null>(null);
  const dialogRef = useRef<HTMLDivElement | null>(null);
  const triggerRef = useRef<Element | null>(null);

  useEffect(() => {
    if (!open) {
      return undefined;
    }

    triggerRef.current = document.activeElement;
    window.setTimeout(() => continueButtonRef.current?.focus(), 0);

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Tab') {
        return;
      }

      const focusable = Array.from(
        dialogRef.current?.querySelectorAll<HTMLElement>(
          'button:not(:disabled), [href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])'
        ) ?? []
      );

      if (focusable.length === 0) {
        return;
      }

      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => {
      window.removeEventListener('keydown', handleKeyDown);
      if (triggerRef.current instanceof HTMLElement) {
        triggerRef.current.focus();
      }
    };
  }, [open]);

  if (!open) return null;

  return (
    <div className="modal-backdrop">
      <div
        ref={dialogRef}
        className="users-confirm-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="session-timeout-title"
        aria-describedby="session-timeout-message"
      >
        <h2 id="session-timeout-title">Sesión a punto de expirar</h2>
        <p id="session-timeout-message">
          Tu sesión expirará en <strong>{remainingSeconds}</strong> segundos por inactividad.
        </p>

        <div className="session-timeout-meter" aria-hidden="true">
          <div className="session-timeout-count">{remainingSeconds}</div>
          <div className="session-timeout-label">segundos</div>
        </div>

        <div className="users-form-actions">
          <button type="button" onClick={onLogout}>
            Cerrar sesión
          </button>
          <button ref={continueButtonRef} type="button" onClick={onContinue}>
            Continuar sesión
          </button>
        </div>
      </div>
    </div>
  );
};
