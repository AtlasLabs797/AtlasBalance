// R-3 (seccion 9): helpers puros de paginacion. El contrato paginado del
// backend ({ data, total, page, page_size, total_pages }) obliga a que las
// pantallas calculen sus controles de pagina. Se separan del componente para
// poder testearlos sin React (los tests unitarios son node:test sobre
// TypeScript compilado, sin entorno de navegador).

// Numero de paginas que cubren `total` filas con el tamano de pagina dado.
export function totalPaginas(total: number, pageSize: number): number {
  if (pageSize <= 0) {
    return 0;
  }
  return Math.ceil(Math.max(0, total) / pageSize);
}

export function hayPaginaAnterior(page: number): boolean {
  return page > 1;
}

export function hayPaginaSiguiente(page: number, totalPages: number): boolean {
  return page < totalPages;
}

interface RangoMostrado {
  desde: number;
  hasta: number;
}

// Rango de filas que se muestra en pantalla. Sin filas (total 0) el rango es
// 0-0: se muestra "Mostrando 0-0 de 0". Una pagina mas alla de la ultima se
// recorta a la ultima: no se inventan filas que no existen.
export function rangoMostrado(page: number, pageSize: number, total: number): RangoMostrado {
  if (total <= 0 || pageSize <= 0) {
    return { desde: 0, hasta: 0 };
  }
  const paginaActual = Math.min(Math.max(1, page), totalPaginas(total, pageSize));
  const desde = (paginaActual - 1) * pageSize + 1;
  const hasta = Math.min(paginaActual * pageSize, total);
  return { desde, hasta };
}

// Texto del rango con el formato que ya usan ExtractosPage y CuentaDetailPage.
export function textoMostrando(page: number, pageSize: number, total: number, etiqueta: string): string {
  const { desde, hasta } = rangoMostrado(page, pageSize, total);
  return `Mostrando ${desde}-${hasta} de ${total} ${etiqueta}`;
}
