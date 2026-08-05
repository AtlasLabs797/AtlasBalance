// V-02.08: helpers puros para la matriz de permisos del modal de Usuarios.
// Separados del componente para poder testearlos sin React.
//
// La jerarquia es Pais > Titular > Cuenta. Cada dimension null = "todas" en esa
// dimension; un valor concreto limita el alcance a esa dimension.

export interface CatalogCuenta {
  id: string;
  nombre: string;
  titular_id: string;
  titular_nombre: string | null;
  pais_id: string | null;
  pais_nombre: string | null;
  activa?: boolean;
}

export interface CatalogTitular {
  id: string;
  nombre: string;
}

export interface CatalogPais {
  id: string;
  nombre: string;
  codigo_iso2?: string | null;
}

export interface PermisoDraft {
  pais_id: string;
  titular_id: string;
  cuenta_id: string;
  puede_ver_cuentas?: boolean;
  puede_agregar_lineas?: boolean;
  puede_editar_lineas?: boolean;
  puede_eliminar_lineas?: boolean;
  puede_importar?: boolean;
  puede_ver_dashboard?: boolean;
}

export type CoherenceState = 'coherent' | 'partial' | 'dangling';

// Estado de coherencia entre pais/titular/cuenta seleccionados en una fila.
// - coherent: vacio todo, o las tres dimensiones son compatibles.
// - partial: titular o pais no encajan con la cuenta seleccionada.
// - dangling: la cuenta seleccionada ya no existe en el catalogo.
export function computeCoherence(
  permiso: Pick<PermisoDraft, 'pais_id' | 'titular_id' | 'cuenta_id'>,
  cuentas: CatalogCuenta[]
): CoherenceState {
  if (!permiso.cuenta_id) {
    return 'coherent';
  }
  const cuenta = cuentas.find((c) => c.id === permiso.cuenta_id);
  if (!cuenta) {
    return 'dangling';
  }
  const titularOk = !permiso.titular_id || permiso.titular_id === cuenta.titular_id;
  const paisOk = !permiso.pais_id || permiso.pais_id === cuenta.pais_id;
  return titularOk && paisOk ? 'coherent' : 'partial';
}

// Titulares que tienen al menos una cuenta (activa si se sabe) en el pais
// seleccionado. Si no hay pais, devuelve todos los titulares.
export function computeTitularesParaPermiso(
  permiso: Pick<PermisoDraft, 'pais_id'>,
  titulares: CatalogTitular[],
  cuentas: CatalogCuenta[]
): CatalogTitular[] {
  if (!permiso.pais_id) {
    return titulares;
  }
  const ids = new Set<string>();
  for (const c of cuentas) {
    if (c.pais_id === permiso.pais_id) {
      ids.add(c.titular_id);
    }
  }
  return titulares.filter((t) => ids.has(t.id));
}

// Cuentas compatibles con el pais y titular seleccionados. Si la dimension
// correspondiente esta vacia, no se filtra por ella.
export function computeCuentasParaPermiso(
  permiso: Pick<PermisoDraft, 'pais_id' | 'titular_id'>,
  cuentas: CatalogCuenta[]
): CatalogCuenta[] {
  return cuentas.filter((c) => {
    if (permiso.pais_id && c.pais_id !== permiso.pais_id) return false;
    if (permiso.titular_id && c.titular_id !== permiso.titular_id) return false;
    return true;
  });
}

// Numero de cuentas a las que esta fila concederia acceso.
// Una fila sin pais y sin titular afecta a todas las cuentas.
// Una fila con pais solo afecta a las cuentas de ese pais.
// Una fila con titular solo afecta a las cuentas de ese titular.
// Una fila con ambos afecta a las cuentas del titular en ese pais.
// Una fila con cuenta afecta solo a esa cuenta (sea lo demas lo que sea).
export function computeAlcance(
  permiso: Pick<PermisoDraft, 'pais_id' | 'titular_id' | 'cuenta_id'>,
  cuentas: CatalogCuenta[]
): number {
  if (permiso.cuenta_id) {
    return cuentas.some((c) => c.id === permiso.cuenta_id) ? 1 : 0;
  }
  return cuentas.filter((c) => {
    if (permiso.pais_id && c.pais_id !== permiso.pais_id) return false;
    if (permiso.titular_id && c.titular_id !== permiso.titular_id) return false;
    return true;
  }).length;
}

// Cuando la cuenta seleccionada pertenece a un titular o pais distintos a los
// declarados, devuelve los valores correctos que el admin puede aplicar.
export function corregirTitularYPaisDesdeCuenta(
  permiso: Pick<PermisoDraft, 'pais_id' | 'titular_id' | 'cuenta_id'>,
  cuentas: CatalogCuenta[]
): { titular_id: string; pais_id: string } | null {
  if (!permiso.cuenta_id) return null;
  const cuenta = cuentas.find((c) => c.id === permiso.cuenta_id);
  if (!cuenta) return null;
  return {
    titular_id: cuenta.titular_id,
    pais_id: cuenta.pais_id ?? '',
  };
}

// Etiqueta del dropdown Titular. Cambia segun si hay pais seleccionado:
// sin pais: "Todos los titulares"; con pais: "Todos los titulares del pais".
export function titularDropdownPlaceholder(tienePais: boolean): string {
  return tienePais ? 'Todos los titulares del país' : 'Todos los titulares';
}

// Etiqueta del dropdown Cuenta. Antes era "Sin cuenta especifica".
// Se mantiene porque la semantica no cambio: null = "no aplicar a una sola".
export const cuentaDropdownPlaceholder = 'Sin cuenta específica';

export const paisDropdownPlaceholder = 'Todos los países';
