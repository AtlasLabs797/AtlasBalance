// A-2 (LOTE 2): helpers puros del estado de bloqueo de usuarios.
// Sin imports a proposito: el test unitario los compila de forma aislada
// (tsconfig.test.v2.json) sin arrastrar tipos, stores ni servicios.

/**
 * Un usuario esta bloqueado mientras la fecha de desbloqueo es futura.
 * `ahora` se recibe como parametro para que el calculo sea testeable.
 */
export function isUsuarioBloqueado(bloqueadoHasta: string | null | undefined, ahora: Date): boolean {
  if (!bloqueadoHasta) {
    return false;
  }

  const hasta = new Date(bloqueadoHasta).getTime();
  if (Number.isNaN(hasta)) {
    return false;
  }

  return hasta > ahora.getTime();
}

/**
 * Tiempo restante de bloqueo en formato corto: "29 min", "1 h 5 min", "2 d 3 h".
 * Si ya ha expirado devuelve "ahora".
 */
export function formatearTiempoRestante(bloqueadoHasta: string, ahora: Date): string {
  const restante = new Date(bloqueadoHasta).getTime() - ahora.getTime();
  if (!Number.isFinite(restante) || restante <= 0) {
    return 'ahora';
  }

  const minutosTotales = Math.ceil(restante / 60000);
  if (minutosTotales < 60) {
    return `${minutosTotales} min`;
  }

  const horas = Math.floor(minutosTotales / 60);
  const minutosRestantes = minutosTotales % 60;
  if (horas < 24) {
    return minutosRestantes > 0 ? `${horas} h ${minutosRestantes} min` : `${horas} h`;
  }

  const dias = Math.floor(horas / 24);
  const horasRestantes = horas % 24;
  return horasRestantes > 0 ? `${dias} d ${horasRestantes} h` : `${dias} d`;
}
