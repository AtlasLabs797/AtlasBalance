import { useEffect, useRef } from 'react';
import { useBlockingOverlay } from '@/hooks/useBlockingOverlay';

const FOCUSABLE_SELECTOR =
  'button:not(:disabled), [href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])';

// Pila de dialogos abiertos: con modales apilados (variant 'stack-top') solo el
// ultimo procesa Escape y Tab; si no, un Escape cerraba los dos y el foco saltaba
// al modal inferior.
const openDialogStack: symbol[] = [];

interface DialogFocusOptions {
  onEscape?: () => void;
  initialFocus?: () => HTMLElement | null;
}

export function useDialogFocus<T extends HTMLElement>(
  open: boolean,
  options: DialogFocusOptions = {},
) {
  const dialogRef = useRef<T | null>(null);
  const triggerRef = useRef<Element | null>(null);
  const { initialFocus, onEscape } = options;
  const initialFocusRef = useRef(initialFocus);
  const onEscapeRef = useRef(onEscape);
  useBlockingOverlay(open);

  useEffect(() => {
    initialFocusRef.current = initialFocus;
    onEscapeRef.current = onEscape;
  }, [initialFocus, onEscape]);

  useEffect(() => {
    if (!open) {
      return undefined;
    }

    triggerRef.current = document.activeElement;
    const stackToken = Symbol('dialog');
    openDialogStack.push(stackToken);

    const focusTimeoutId = window.setTimeout(() => {
      const target =
        initialFocusRef.current?.() ??
        dialogRef.current?.querySelector<HTMLElement>(FOCUSABLE_SELECTOR) ??
        dialogRef.current;
      target?.focus();
    }, 0);

    const handleKeyDown = (event: KeyboardEvent) => {
      if (openDialogStack[openDialogStack.length - 1] !== stackToken) {
        return;
      }

      // Un Combobox o DatePicker interno que ya uso el Escape para cerrarse no
      // debe cerrar tambien el dialogo.
      if (event.key === 'Escape' && !event.defaultPrevented && onEscapeRef.current) {
        onEscapeRef.current();
        return;
      }

      if (event.key !== 'Tab') {
        return;
      }

      const focusable = Array.from(dialogRef.current?.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR) ?? []);
      if (focusable.length === 0) {
        event.preventDefault();
        dialogRef.current?.focus();
        return;
      }

      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => {
      // Cancela el foco inicial pendiente para no llamar .focus() sobre un nodo
      // ya desmontado si el dialogo se abre y cierra muy rapido.
      window.clearTimeout(focusTimeoutId);
      window.removeEventListener('keydown', handleKeyDown);
      const stackIndex = openDialogStack.indexOf(stackToken);
      if (stackIndex !== -1) {
        openDialogStack.splice(stackIndex, 1);
      }
      if (triggerRef.current instanceof HTMLElement) {
        triggerRef.current.focus();
      }
    };
  }, [open]);

  return dialogRef;
}
