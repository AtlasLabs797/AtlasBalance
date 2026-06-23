import React, { useRef } from 'react';
import { useDialogFocus } from '@/hooks/useDialogFocus';

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
  const dialogRef = useDialogFocus<HTMLDivElement>(open, {
    initialFocus: () => continueButtonRef.current,
  });

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
