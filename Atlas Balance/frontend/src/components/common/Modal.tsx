import type { ReactNode } from 'react';
import { CloseIconButton } from '@/components/common/CloseIconButton';
import { useDialogFocus } from '@/hooks/useDialogFocus';

/**
 * Variante del fondo del modal.
 * - `default`: fondo simple.
 * - `fullscreen-mobile`: el panel ocupa toda la pantalla en movil.
 * - `stack-top`: el fondo sube por encima de otro modal ya abierto.
 */
export type ModalVariant = 'default' | 'fullscreen-mobile' | 'stack-top';

interface ModalProps {
  open: boolean;
  onClose: () => void;
  titleId: string;
  title: ReactNode;
  subtitle?: ReactNode;
  /** id del subtitulo, para poder referenciarlo desde `aria-describedby`. */
  subtitleId?: string;
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
  /** Variante del fondo. */
  variant?: ModalVariant;
  /**
   * Clases del panel. Sustituyen a `modal-panel modal-panel--{size}` para que los
   * modales con estructura propia conserven su aspecto (ancho, rejilla y scroll).
   */
  panelClassName?: string;
  /**
   * Clases de la cabecera. Sustituyen a `modal-panel__header`. Al fijarlas, el
   * modal entra en modo "cabecera propia": el titulo deja de llevar
   * `modal-panel__title` para que manden las reglas de la cabecera existente.
   */
  headerClassName?: string;
  /**
   * Clases del subtitulo. Sustituyen a `modal-panel__subtitle`. Sin esta prop y
   * con `headerClassName`, el subtitulo se queda sin clase.
   */
  subtitleClassName?: string;
  /**
   * Renderiza el cuerpo sin el `<div>` contenedor, para los modales cuyo cuerpo
   * es un `<form>` propio (el formulario necesita ser hijo directo del panel).
   */
  bareBody?: boolean;
  /** Clases extra del boton de cierre de la cabecera. */
  closeButtonClassName?: string;
}

export default function Modal({
  open,
  onClose,
  titleId,
  title,
  subtitle,
  subtitleId,
  descriptionId,
  size = 'md',
  busy = false,
  initialFocus,
  closeLabel = 'Cerrar',
  showCloseButton = true,
  children,
  footer,
  variant = 'default',
  panelClassName,
  headerClassName,
  subtitleClassName,
  bareBody = false,
  closeButtonClassName,
}: ModalProps) {
  const dialogRef = useDialogFocus<HTMLDivElement>(open, {
    initialFocus,
    onEscape: busy ? undefined : onClose,
  });

  if (!open) {
    return null;
  }

  const backdropClass = variant === 'default' ? 'modal-backdrop' : `modal-backdrop modal-backdrop--${variant}`;
  const panelClass = panelClassName ?? `modal-panel modal-panel--${size}`;
  const headerClass = headerClassName ?? 'modal-panel__header';
  // Cabecera propia: el titulo y el subtitulo se dejan sin clase para que
  // manden las reglas de estilo de esa cabecera.
  const ownHeader = headerClassName !== undefined;
  const titleClass = ownHeader ? undefined : 'modal-panel__title';
  const subtitleClass = subtitleClassName ?? (ownHeader ? undefined : 'modal-panel__subtitle');

  return (
    <div className={backdropClass} role="presentation" onClick={busy ? undefined : onClose}>
      <div
        ref={dialogRef}
        className={panelClass}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={descriptionId}
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
      >
        <header className={headerClass}>
          <div>
            <h2 id={titleId} className={titleClass}>{title}</h2>
            {subtitle ? <p id={subtitleId} className={subtitleClass}>{subtitle}</p> : null}
          </div>
          {showCloseButton && (
            <CloseIconButton
              className={closeButtonClassName}
              onClick={onClose}
              ariaLabel={closeLabel}
              disabled={busy}
            />
          )}
        </header>
        {bareBody ? children : <div className="modal-panel__body">{children}</div>}
        {footer && <div className="modal-panel__footer">{footer}</div>}
      </div>
    </div>
  );
}
