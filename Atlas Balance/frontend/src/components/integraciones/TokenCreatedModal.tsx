import { useEffect, useState } from 'react';
import Modal from '@/components/common/Modal';

interface TokenCreatedModalProps {
  tokenPlano: string | null;
  onClose: () => void;
}

export function TokenCreatedModal({ tokenPlano, onClose }: TokenCreatedModalProps) {
  const [copied, setCopied] = useState(false);
  // V-02-05 (LOW-FE-9): por defecto el token se muestra enmascarado para evitar
  // que quede visible de reojo. El operador debe pulsar "Mostrar" explicitamente.
  const [revealed, setRevealed] = useState(false);

  // V-02-05 (LOW-FE-9): auto-close a los 60s para reducir la ventana en la que
  // el token esta visible aunque el operador se olvide de cerrar.
  useEffect(() => {
    if (!tokenPlano) {
      return;
    }
    const t = setTimeout(() => {
      onClose();
    }, 60_000);
    return () => clearTimeout(t);
  }, [tokenPlano, onClose]);

  if (!tokenPlano) {
    return null;
  }

  const copyToken = async () => {
    try {
      await navigator.clipboard.writeText(tokenPlano);
      setCopied(true);
    } catch {
      setCopied(false);
    }
  };

  const masked = `atlas_••••••${tokenPlano.slice(-6)}`;

  return (
    <Modal
      open
      onClose={onClose}
      titleId="token-created-modal-title"
      title="Token generado (solo una vez)"
      closeLabel="Cerrar modal de token generado"
      footer={
        <>
          <button
            type="button"
            onClick={() => setRevealed((v) => !v)}
          >
            {revealed ? 'Ocultar' : 'Mostrar'}
          </button>
          <button type="button" onClick={() => void copyToken()}>{copied ? 'Copiado' : 'Copiar'}</button>
        </>
      }
    >
      <p className="config-modal-description">
        Guarda este token en un gestor de secretos. No se mostrara de nuevo.
      </p>
      <div className="config-token-plain-box">
        <code aria-label="Token de integración">{revealed ? tokenPlano : masked}</code>
      </div>
    </Modal>
  );
}
