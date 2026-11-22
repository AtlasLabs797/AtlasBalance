// S-2: el backend separa la REGLA del administrador (columnas_restringidas)
// de la PREFERENCIA del usuario (columnas_visibles). El conjunto efectivo es
// la interseccion y el usuario solo puede estrechar: no puede volver a
// mostrar una columna que el administrador prohibio.
//
// Estas funciones son puras para poder testearlas sin React.

function normaliza(valor: string): string {
  return valor.trim().toLowerCase();
}

// Columnas que puede ofrecer el frontend: interseccion entre la whitelist del
// administrador y las columnas candidatas.
// - `restricciones` null = el administrador no restringe: se ofrecen todas las
//   candidatas.
// - El resultado no tiene duplicados y conserva el orden de las candidatas.
// - Una columna que no esta en las candidatas (unknown) se descarta.
export function columnasPermitidas(restricciones: string[] | null, candidatas: readonly string[]): string[] {
  const permitidas = restricciones === null
    ? null
    : new Set(restricciones.map(normaliza).filter(Boolean));

  const resultado: string[] = [];
  const vistas = new Set<string>();
  for (const candidata of candidatas) {
    const nombre = candidata.trim();
    if (!nombre) {
      continue;
    }
    const clave = normaliza(nombre);
    if (vistas.has(clave)) {
      continue;
    }
    if (permitidas && !permitidas.has(clave)) {
      continue;
    }
    vistas.add(clave);
    resultado.push(nombre);
  }
  return resultado;
}

// Columnas que hay que quitar del interruptor y de la tabla porque el
// administrador las prohibio. Devuelve la lista a EXCLUIR del conjunto
// ofrecido: vacia cuando el administrador no restringe.
export function columnasExcluidasPorAdmin(restricciones: string[] | null, columnas: readonly string[]): string[] {
  if (restricciones === null) {
    return [];
  }
  const permitidas = new Set(restricciones.map(normaliza).filter(Boolean));
  return columnas.filter((columna) => !permitidas.has(normaliza(columna)));
}
