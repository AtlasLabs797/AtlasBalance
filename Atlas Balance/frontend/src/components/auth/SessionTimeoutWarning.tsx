import React from 'react';

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
  if (!open) return null;

  return (
    <div className="modal-backdrop">
      <div className="users-confirm-modal" role="dialog" aria-modal="true">
        <h2>Sesión a punto de expirar</h2>
        <p>Tu sesión expirará en <strong>{remainingSeconds}</strong> segundos por inactividad.</p>

        <div style={{ textAlign: 'center', margin: '20px 0' }}>
          <div style={{ fontSize: '48px', fontWeight: 'bold', color: 'var(--color-danger)' }}>
            {remainingSeconds}
          </div>
          <div style={{ fontSize: '14px', color: 'var(--color-text-secondary)' }}>
            segundos
          </div>
        </div>

        <div className="users-form-actions">
          <button type="button" onClick={onLogout}>
            Cerrar sesión
          </button>
          <button type="button" onClick={onContinue}>
            Continuar sesión
          </button>
        </div>
      </div>
    </div>
  );
};
