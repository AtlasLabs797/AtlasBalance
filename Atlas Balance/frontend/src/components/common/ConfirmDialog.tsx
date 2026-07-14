import { useRef } from 'react';
import { useDialogFocus } from '@/hooks/useDialogFocus';

interface ConfirmDialogProps {
  open: boolean;
  title: string;
  message: string;
  confirmLabel: string;
  cancelLabel?: string;
  loadingLabel?: string;
  loading?: boolean;
  onCancel: () => void;
  onConfirm: () => void | Promise<void>;
}

export default function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel,
  cancelLabel = 'Cancelar',
  loadingLabel = 'Procesando...',
  loading = false,
  onCancel,
  onConfirm,
}: ConfirmDialogProps) {
  const cancelButtonRef = useRef<HTMLButtonElement | null>(null);
  const dialogRef = useDialogFocus<HTMLDivElement>(open, {
    initialFocus: () => cancelButtonRef.current,
    onEscape: loading ? undefined : onCancel,
  });

  if (!open) {
    return null;
  }

  return (
    <div className="modal-backdrop" onClick={!loading ? onCancel : undefined}>
      <div
        ref={dialogRef}
        className="users-confirm-modal"
        onClick={(event) => event.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirm-dialog-title"
        aria-describedby="confirm-dialog-message"
      >
        <h2 id="confirm-dialog-title">{title}</h2>
        <p id="confirm-dialog-message">{message}</p>
        <div className="users-form-actions">
          <button ref={cancelButtonRef} type="button" onClick={onCancel} disabled={loading}>
            {cancelLabel}
          </button>
          <button type="button" className="button-danger" onClick={() => void onConfirm()} disabled={loading}>
            {loading ? loadingLabel : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
