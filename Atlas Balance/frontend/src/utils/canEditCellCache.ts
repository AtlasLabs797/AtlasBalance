// Los permisos de celda se evaluaban tres veces por celda y en cada render de
// la tabla (un cambio de foco re-renderiza la tabla entera). Esta cache guarda
// el resultado por fila y columna, y se invalida sola cuando cambia el objeto
// fila o cuando se crea una cache nueva (es decir, cuando cambia la funcion de
// permisos que recibe el componente).
export interface CanEditCellCache<Row extends { id: string }> {
  (row: Row, column: string): boolean;
}

export function createCanEditCellCache<Row extends { id: string }>(
  canEditCell: (row: Row, column: string) => boolean,
): CanEditCellCache<Row> {
  const cache = new Map<string, { row: Row; value: boolean }>();

  return (row, column) => {
    const key = `${row.id}::${column}`;
    const cached = cache.get(key);
    if (cached && cached.row === row) {
      return cached.value;
    }

    const value = canEditCell(row, column);
    cache.set(key, { row, value });
    return value;
  };
}
