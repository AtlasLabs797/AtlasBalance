import type { ReactNode } from 'react';
import { CloseIconButton } from '@/components/common/CloseIconButton';
import { useDialogFocus } from '@/hooks/useDialogFocus';

interface ModalProps {
  open: boolean;
  onClose: () => void;
  titleId: string;
  title: ReactNode;
  subtitle?: ReactNode;
  descriptionId?: string;
  size?: 'sm' | 'md' | 'lg';
  /** Bloquea el cierre por Escape, clic en fondo y boton de cabecera (p. ej. mientras se guarda). */
  busy?: boolean;
  initialFocus?: () => HTMLElement | null;
  /** Etiqueta accesible del boton de cierre de la cabecera. */
  closeLabel?: string;
  showCloseButton?: boolean;
  children: ReactNode;
  footer?: ReactNode;
}

export default function Modal({
  open,
  onClose,
  titleId,
  title,
  subtitle,
  descriptionId,
  size = 'md',
  busy = false,
  initialFocus,
  closeLabel = 'Cerrar',
  showCloseButton = true,
  children,
  footer,
}: ModalProps) {
  const dialogRef = useDialogFocus<HTMLDivElement>(open, {
    initialFocus,
    onEscape: busy ? undefined : onClose,
  });

  if (!open) {
    return null;
  }

  return (
    <div className="modal-backdrop" role="presentation" onClick={busy ? undefined : onClose}>
      <div
        ref={dialogRef}
        className={`modal-panel modal-panel--${size}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={descriptionId}
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
      >
        <header className="modal-panel__header">
          <div>
            <h2 id={titleId} className="modal-panel__title">{title}</h2>
            {subtitle ? <p className="modal-panel__subtitle">{subtitle}</p> : null}
          </div>
          {showCloseButton && <CloseIconButton onClick={onClose} ariaLabel={closeLabel} disabled={busy} />}
        </header>
        <div className="modal-panel__body">{children}</div>
        {footer && <div className="modal-panel__footer">{footer}</div>}
      </div>
    </div>
  );
}
