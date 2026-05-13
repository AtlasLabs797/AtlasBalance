import { useEffect, useRef } from 'react';

const FOCUSABLE_SELECTOR =
  'button:not(:disabled), [href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])';

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

  useEffect(() => {
    initialFocusRef.current = initialFocus;
    onEscapeRef.current = onEscape;
  }, [initialFocus, onEscape]);

  useEffect(() => {
    if (!open) {
      return undefined;
    }

    triggerRef.current = document.activeElement;

    window.setTimeout(() => {
      const target =
        initialFocusRef.current?.() ??
        dialogRef.current?.querySelector<HTMLElement>(FOCUSABLE_SELECTOR) ??
        dialogRef.current;
      target?.focus();
    }, 0);

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && onEscapeRef.current) {
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
      window.removeEventListener('keydown', handleKeyDown);
      if (triggerRef.current instanceof HTMLElement) {
        triggerRef.current.focus();
      }
    };
  }, [open]);

  return dialogRef;
}
