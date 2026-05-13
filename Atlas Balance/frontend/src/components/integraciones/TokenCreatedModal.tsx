import { useState } from 'react';
import { CloseIconButton } from '@/components/common/CloseIconButton';
import { useDialogFocus } from '@/hooks/useDialogFocus';

interface TokenCreatedModalProps {
  tokenPlano: string | null;
  onClose: () => void;
}

export function TokenCreatedModal({ tokenPlano, onClose }: TokenCreatedModalProps) {
  const [copied, setCopied] = useState(false);
  const dialogRef = useDialogFocus<HTMLDivElement>(Boolean(tokenPlano), {
    onEscape: onClose,
  });

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
    <div className="config-modal-backdrop" role="presentation" onClick={onClose}>
      <div
        ref={dialogRef}
        className="config-modal-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="token-created-modal-title"
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
      >
        <header className="config-modal-header">
          <h3 id="token-created-modal-title">Token generado (solo una vez)</h3>
          <CloseIconButton onClick={onClose} ariaLabel="Cerrar modal de token generado" />
        </header>
        <div className="config-token-plain-box">
          <code>{tokenPlano}</code>
        </div>
        <div className="import-actions">
          <button type="button" onClick={() => void copyToken()}>{copied ? 'Copiado' : 'Copiar'}</button>
        </div>
      </div>
    </div>
  );
}
