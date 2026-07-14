import { useEffect } from 'react';
import { useUiStore } from '@/stores/uiStore';

export function useBlockingOverlay(open: boolean) {
  const registerBlockingOverlay = useUiStore((state) => state.registerBlockingOverlay);
  const unregisterBlockingOverlay = useUiStore((state) => state.unregisterBlockingOverlay);

  useEffect(() => {
    if (!open) {
      return undefined;
    }

    registerBlockingOverlay();
    return () => unregisterBlockingOverlay();
  }, [open, registerBlockingOverlay, unregisterBlockingOverlay]);
}
