import { useState } from 'react';

interface TokenCreatedModalProps {
  tokenPlano: string | null;
  onClose: () => void;
}

export function TokenCreatedModal({ tokenPlano, onClose }: TokenCreatedModalProps) {
  const [copied, setCopied] = useState(false);

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

  return (
    <div className="config-modal-backdrop">
      <div className="config-modal-card">
        <header className="config-modal-header">
          <h3>Token generado (solo una vez)</h3>
        </header>
        <div className="config-token-plain-box">
          <code>{tokenPlano}</code>
        </div>
        <div className="import-actions">
          <button type="button" onClick={() => void copyToken()}>{copied ? 'Copiado' : 'Copiar'}</button>
          <button type="button" onClick={onClose}>Cerrar</button>
        </div>
      </div>
    </div>
  );
}
