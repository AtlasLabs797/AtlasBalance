import { useEffect } from 'react';

/**
 * Avisa al usuario antes de cerrar/recargar/salir del navegador cuando hay
 * cambios sin guardar. Cubre el caso de perdida de datos por refresco o cierre
 * de pestana. La navegacion entre rutas internas no se intercepta aqui: el
 * router del proyecto usa <BrowserRouter> (no data router), por lo que useBlocker
 * no esta disponible; para cerrar modales/cambiar de pestana usa una confirmacion
 * explicita (ver useConfirmDialog).
 *
 * @param isDirty  true si el formulario tiene cambios sin guardar.
 */
export function useUnsavedChanges(isDirty: boolean): void {
  useEffect(() => {
    if (!isDirty) {
      return;
    }

    const handler = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      // Requerido por navegadores antiguos para disparar el dialogo nativo.
      event.returnValue = '';
    };

    window.addEventListener('beforeunload', handler);
    return () => window.removeEventListener('beforeunload', handler);
  }, [isDirty]);
}
