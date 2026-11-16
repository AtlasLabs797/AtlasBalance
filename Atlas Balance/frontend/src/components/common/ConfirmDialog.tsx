import { useRef } from 'react';
import { AlertTriangle, Info, Trash2 } from 'lucide-react';
import Modal from '@/components/common/Modal';

export type ConfirmTone = 'normal' | 'reversible' | 'irreversible';

const TONE_CLASS: Record<ConfirmTone, string> = {
  normal: 'button-primary',
  reversible: 'button-warning confirm-action-reversible',
  irreversible: 'button-danger',
};

interface ConfirmDialogProps {
  open: boolean;
  title: string;
  message: string;
  confirmLabel: string;
  /**
   * Nivel de la accion: normal (rutina), reversible (cambia estado, se puede
   * deshacer) o irreversible (borrado/revocacion/perdida de datos). Sin valor
   * se trata como irreversible para conservar el estilo destructivo previo.
   */
  tone?: ConfirmTone;
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
  tone = 'normal',
  cancelLabel = 'Cancelar',
  loadingLabel = 'Procesando...',
  loading = false,
  onCancel,
  onConfirm,
}: ConfirmDialogProps) {
  const cancelButtonRef = useRef<HTMLButtonElement | null>(null);

  if (!open) {
    return null;
  }

  const ToneIcon = tone === 'irreversible' ? Trash2 : tone === 'reversible' ? AlertTriangle : Info;
  const describedBy =
    tone === 'irreversible' ? 'confirm-dialog-message confirm-dialog-irreversible' : 'confirm-dialog-message';

  return (
    <Modal
      open={open}
      onClose={onCancel}
      titleId="confirm-dialog-title"
      title={title}
      descriptionId="confirm-dialog-message"
      size="sm"
      busy={loading}
      showCloseButton={false}
      initialFocus={() => cancelButtonRef.current}
      footer={
        <>
          <button ref={cancelButtonRef} type="button" onClick={onCancel} disabled={loading}>
            {cancelLabel}
          </button>
          <button
            type="button"
            className={`${TONE_CLASS[tone]} confirm-action`}
            onClick={() => void onConfirm()}
            disabled={loading}
            aria-describedby={describedBy}
          >
            {!loading && <ToneIcon size={16} aria-hidden="true" />}
            {loading ? loadingLabel : confirmLabel}
          </button>
        </>
      }
    >
      <p id="confirm-dialog-message">{message}</p>
      {tone === 'irreversible' && (
        <p id="confirm-dialog-irreversible" className="confirm-dialog-irreversible" role="note">
          <AlertTriangle size={16} aria-hidden="true" />
          <strong>Esta acción no se puede deshacer.</strong>
        </p>
      )}
    </Modal>
  );
}
