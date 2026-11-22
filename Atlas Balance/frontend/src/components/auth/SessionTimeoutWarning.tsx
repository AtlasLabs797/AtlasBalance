import React, { useRef } from 'react';
import Modal from '@/components/common/Modal';

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

  return (
    <Modal
      open={open}
      // El aviso no se descarta cerrando: hay que elegir "Continuar sesion" o
      // "Cerrar sesion". Por eso el fondo y Escape no hacen nada.
      onClose={() => {}}
      titleId="session-timeout-title"
      title="Sesión a punto de expirar"
      descriptionId="session-timeout-message"
      panelClassName="users-confirm-modal"
      headerClassName=""
      showCloseButton={false}
      bareBody
      initialFocus={() => continueButtonRef.current}
    >
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
    </Modal>
  );
};
