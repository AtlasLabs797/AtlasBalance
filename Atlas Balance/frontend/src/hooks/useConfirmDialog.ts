import { useCallback, useRef, useState } from 'react';

export interface ConfirmOptions {
  title: string;
  message: string;
  confirmLabel: string;
  cancelLabel?: string;
}

interface DialogState extends ConfirmOptions {
  open: boolean;
}

const INITIAL: DialogState = {
  open: false,
  title: '',
  message: '',
  confirmLabel: '',
};

/**
 * Confirmacion imperativa reutilizable sobre el ConfirmDialog controlado ya
 * existente. Evita repetir estado (open + labels + handlers) en cada pagina.
 *
 * Uso:
 *   const { confirm, dialogProps } = useConfirmDialog();
 *   ...
 *   if (!(await confirm({ title, message, confirmLabel }))) return;
 *   // ...accion...
 *   // y en el JSX, una sola vez:  <ConfirmDialog {...dialogProps} />
 */
export function useConfirmDialog() {
  const [state, setState] = useState<DialogState>(INITIAL);
  const resolverRef = useRef<((value: boolean) => void) | null>(null);

  const settle = useCallback((result: boolean) => {
    setState((prev) => ({ ...prev, open: false }));
    const resolve = resolverRef.current;
    resolverRef.current = null;
    resolve?.(result);
  }, []);

  const confirm = useCallback((options: ConfirmOptions) => {
    // Si habia una confirmacion pendiente, la resolvemos como cancelada.
    resolverRef.current?.(false);
    setState({ ...options, open: true });
    return new Promise<boolean>((resolve) => {
      resolverRef.current = resolve;
    });
  }, []);

  const dialogProps = {
    open: state.open,
    title: state.title,
    message: state.message,
    confirmLabel: state.confirmLabel,
    cancelLabel: state.cancelLabel,
    onConfirm: () => settle(true),
    onCancel: () => settle(false),
  };

  return { confirm, dialogProps };
}
