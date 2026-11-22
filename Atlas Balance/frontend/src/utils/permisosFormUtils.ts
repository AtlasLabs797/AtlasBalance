// V-02.08: helpers puros para la matriz de permisos del modal de Usuarios.
// Separados del componente para poder testearlos sin React.
//
// La jerarquia es Pais > Titular > Cuenta. Cada dimension null = "todas" en esa
// dimension; un valor concreto limita el alcance a esa dimension.

interface CatalogCuenta {
  id: string;
  nombre: string;
  titular_id: string;
  titular_nombre: string | null;
  pais_id: string | null;
  pais_nombre: string | null;
  activa?: boolean;
}

interface CatalogTitular {
  id: string;
  nombre: string;
}

interface PermisoDraft {
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

type CoherenceState = 'coherent' | 'partial' | 'dangling';

// V-03.02: fila editable de la matriz de permisos y su equivalente en API.
// Se movieron aqui desde UsuarioModal para poder testear el payload sin React.

export interface PermisoFormRow {
  key: string;
  pais_id: string;
  titular_id: string;
  cuenta_id: string;
  puede_ver_cuentas: boolean;
  puede_agregar_lineas: boolean;
  puede_editar_lineas: boolean;
  puede_eliminar_lineas: boolean;
  puede_importar: boolean;
  puede_ver_dashboard: boolean;
  puede_revisar_lineas: boolean;
  puede_aprobar_importaciones: boolean;
  puede_conciliar: boolean;
  puede_cerrar_conciliacion: boolean;
  columnas_visibles: string;
  columnas_editables: string;
}

// Fila tal y como la devuelve la API. Los flags son opcionales porque el
// backend puede omitir los que no aplica.
export interface PermisoApiRow {
  pais_id?: string | null;
  titular_id?: string | null;
  cuenta_id?: string | null;
  puede_ver_cuentas?: boolean;
  puede_agregar_lineas?: boolean;
  puede_editar_lineas?: boolean;
  puede_eliminar_lineas?: boolean;
  puede_importar?: boolean;
  puede_ver_dashboard?: boolean;
  puede_revisar_lineas?: boolean;
  puede_aprobar_importaciones?: boolean;
  puede_conciliar?: boolean;
  puede_cerrar_conciliacion?: boolean;
  columnas_visibles?: string[];
  columnas_editables?: string[];
}

// Payload de una fila de permisos. El backend reemplaza la matriz entera con
// lo que llega, asi que todos los flags se envian siempre.
export interface PermisoPayloadRow {
  cuenta_id: string | null;
  titular_id: string | null;
  pais_id: string | null;
  puede_ver_cuentas: boolean;
  puede_agregar_lineas: boolean;
  puede_editar_lineas: boolean;
  puede_eliminar_lineas: boolean;
  puede_importar: boolean;
  puede_ver_dashboard: boolean;
  puede_revisar_lineas: boolean;
  puede_aprobar_importaciones: boolean;
  puede_conciliar: boolean;
  puede_cerrar_conciliacion: boolean;
  columnas_visibles: string[] | undefined;
  columnas_editables: string[] | undefined;
}

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

// Convierte una fila de la API en la fila editable del formulario. Los flags
// que no llegan se leen como false.
export function mapPermisoApiRow(permiso: PermisoApiRow, key: string): PermisoFormRow {
  return {
    key,
    pais_id: permiso.pais_id ?? '',
    titular_id: permiso.titular_id ?? '',
    cuenta_id: permiso.cuenta_id ?? '',
    puede_ver_cuentas: permiso.puede_ver_cuentas ?? false,
    puede_agregar_lineas: permiso.puede_agregar_lineas ?? false,
    puede_editar_lineas: permiso.puede_editar_lineas ?? false,
    puede_eliminar_lineas: permiso.puede_eliminar_lineas ?? false,
    puede_importar: permiso.puede_importar ?? false,
    puede_ver_dashboard: permiso.puede_ver_dashboard ?? false,
    puede_revisar_lineas: permiso.puede_revisar_lineas ?? false,
    puede_aprobar_importaciones: permiso.puede_aprobar_importaciones ?? false,
    puede_conciliar: permiso.puede_conciliar ?? false,
    puede_cerrar_conciliacion: permiso.puede_cerrar_conciliacion ?? false,
    columnas_visibles: (permiso.columnas_visibles ?? []).join(', '),
    columnas_editables: (permiso.columnas_editables ?? []).join(', '),
  };
}

// Parsea las columnas separadas por coma. Devuelve undefined si no hay
// ninguna, para no mandar listas vacias.
function parsePermisoColumns(value: string): string[] | undefined {
  const parsed = value
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean);
  return parsed.length > 0 ? parsed : undefined;
}

// Indica si la fila tiene algun flag de permiso activo.
function permisoHasFlags(permiso: PermisoFormRow): boolean {
  return (
    permiso.puede_ver_cuentas ||
    permiso.puede_agregar_lineas ||
    permiso.puede_editar_lineas ||
    permiso.puede_eliminar_lineas ||
    permiso.puede_importar ||
    permiso.puede_ver_dashboard ||
    permiso.puede_revisar_lineas ||
    permiso.puede_aprobar_importaciones ||
    permiso.puede_conciliar ||
    permiso.puede_cerrar_conciliacion
  );
}

// Construye el payload de permisos. Descarta las filas vacias (sin alcance,
// sin flags y sin columnas) porque no conceden nada.
export function buildPermisosPayload(permisos: PermisoFormRow[]): PermisoPayloadRow[] {
  return permisos
    .map((permiso) => {
      const columnasVisibles = parsePermisoColumns(permiso.columnas_visibles);
      const columnasEditables = parsePermisoColumns(permiso.columnas_editables);
      const hasScope = !!permiso.cuenta_id || !!permiso.titular_id || !!permiso.pais_id;

      if (!permisoHasFlags(permiso) && !hasScope && !columnasVisibles && !columnasEditables) {
        return null;
      }

      return {
        cuenta_id: permiso.cuenta_id || null,
        titular_id: permiso.titular_id || null,
        pais_id: permiso.pais_id || null,
        puede_ver_cuentas: permiso.puede_ver_cuentas,
        puede_agregar_lineas: permiso.puede_agregar_lineas,
        puede_editar_lineas: permiso.puede_editar_lineas,
        puede_eliminar_lineas: permiso.puede_eliminar_lineas,
        puede_importar: permiso.puede_importar,
        puede_ver_dashboard: permiso.puede_ver_dashboard,
        puede_revisar_lineas: permiso.puede_revisar_lineas,
        puede_aprobar_importaciones: permiso.puede_aprobar_importaciones,
        puede_conciliar: permiso.puede_conciliar,
        puede_cerrar_conciliacion: permiso.puede_cerrar_conciliacion,
        columnas_visibles: columnasVisibles,
        columnas_editables: columnasEditables,
      };
    })
    .filter((item): item is PermisoPayloadRow => !!item);
}

// Fila totalmente vacia: sin alcance, sin flags y sin columnas.
export function isPermisoRowBlank(permiso: PermisoFormRow): boolean {
  return (
    !permiso.pais_id &&
    !permiso.titular_id &&
    !permiso.cuenta_id &&
    !permisoHasFlags(permiso) &&
    !permiso.columnas_visibles &&
    !permiso.columnas_editables
  );
}
