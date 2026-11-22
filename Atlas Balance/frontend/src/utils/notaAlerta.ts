// La nota de alerta solo se guarda cuando el texto ha cambiado respecto al
// valor conocido (el que devolvio el servidor). Si el usuario entra y sale del
// campo sin escribir nada, no hay que lanzar un PATCH que no cambia nada.
export function notaAlertaCambiada(notaConocida: string | null | undefined, notaActual: string): boolean {
  return notaActual !== (notaConocida ?? '');
}
