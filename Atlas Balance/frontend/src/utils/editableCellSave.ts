// F-9: si el guardado de una celda falla, lo que el usuario ha escrito no debe
// descartarse. Esta regla vive aqui (sin dependencias) para que se pueda
// comprobar sin DOM: el componente EditableCell la aplica y el test la verifica.
export type EditableCellSaveState = 'idle' | 'saved' | 'error';

export interface EditableCellState {
  /** Texto que el usuario esta escribiendo (borrador). */
  draft: string;
  /** Si el campo editable esta abierto. */
  isEditing: boolean;
  /** Estado del aviso de guardado. */
  saveState: EditableCellSaveState;
}

export type EditableCellSaveResult = 'saved' | 'failed';

/**
 * Aplica el resultado de un intento de guardado a una celda editable.
 * - 'saved': se cierra el editor y se avisa de que se guardo (comportamiento actual).
 * - 'failed': el editor sigue abierto y el borrador se conserva intacto, sin
 *   sustituirlo por el valor del servidor: el usuario decide si reintenta
 *   (Enter o blur) o lo descarta (Escape).
 */
export function applyEditableCellSaveResult(
  state: EditableCellState,
  result: EditableCellSaveResult,
): EditableCellState {
  if (result === 'failed') {
    return { ...state, isEditing: true, saveState: 'error' };
  }

  return { ...state, isEditing: false, saveState: 'saved' };
}
