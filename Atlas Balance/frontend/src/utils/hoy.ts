// "Hoy" en la zona horaria local del navegador, no en UTC: entre las 00:00 y
// las 02:00 de Espana la fecha UTC sigue siendo la del dia anterior. Es el
// mismo criterio que usan ConciliacionPage e ImportacionPage, aqui compartido
// para que no se repita el calculo en cada sitio.
export function hoy(ahora: Date = new Date()): string {
  const offsetMs = ahora.getTimezoneOffset() * 60_000;
  return new Date(ahora.getTime() - offsetMs).toISOString().slice(0, 10);
}
