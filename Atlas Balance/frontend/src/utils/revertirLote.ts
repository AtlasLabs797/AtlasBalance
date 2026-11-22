// V-03.02 (LOTE 6, seccion 8.1 del informe de revision integral): helpers puros
// de la accion "Revertir" del historial de importacion. Separados del
// componente para poder testearlos sin React.
//
// El backend (ImportacionService.RevertirLoteAsync) solo revierte un lote en
// estado 'confirmado'; el resto de estados son un 409 de dominio. Y desde
// S-3 exige el permiso `puede_eliminar_lineas` en la cuenta del lote (403 si
// no lo hay), porque revertir borra masivamente los extractos del lote.

export interface LoteParaRevertir {
  id: string;
  estado: string;
  cuenta_nombre?: string | null;
}

/** Un lote solo es reversible si esta confirmado (el unico estado que acepta el backend). */
export function esEstadoReversible(estado: string | null | undefined): boolean {
  return (estado ?? '').trim().toLowerCase() === 'confirmado';
}

/**
 * El boton "Revertir" solo se muestra si el lote es reversible por estado Y el
 * usuario puede eliminar lineas en la cuenta. El frontend solo oculta lo que
 * el backend ya deniega: el backend sigue siendo quien autoriza.
 */
export function puedeRevertirLote(
  lote: Pick<LoteParaRevertir, 'estado'>,
  tienePermisoEliminar: boolean
): boolean {
  return tienePermisoEliminar && esEstadoReversible(lote.estado);
}

/**
 * Texto de confirmacion: que se borra, que pasa con las conciliaciones y que
 * hace falta el permiso "eliminar lineas".
 */
export function textoConfirmacionRevertir(lote: LoteParaRevertir): string {
  const cuenta = lote.cuenta_nombre?.trim();
  const destino = cuenta ? ` de la cuenta ${cuenta}` : '';

  return (
    `Se va a revertir el lote de importación${destino}. ` +
    'Se borrarán (soft delete) los extractos importados por ese lote, ' +
    'las conciliaciones de esos extractos pasarán a estado "excepción" y el lote quedará revertido. ' +
    'La importación se puede volver a hacer. ' +
    'Esta acción necesita el permiso "eliminar líneas" en la cuenta.'
  );
}
