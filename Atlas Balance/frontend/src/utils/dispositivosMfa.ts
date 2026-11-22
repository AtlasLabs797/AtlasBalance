// V-03.02 (LOTE 6, seccion 8.1 del informe de revision integral): helpers puros
// de la pantalla de dispositivos MFA de confianza. Separados del componente
// para poder testearlos sin React (el proyecto no tiene entorno de React en
// los tests unitarios).
//
// El backend (AuthService.GetTrustedMfaDevicesAsync) solo devuelve los
// dispositivos con `ExpiresAt > now`, asi que la lista nunca trae expirados:
// el resumen de vencimiento sirve para decir cuanto queda de confianza.
//
// El helper no importa nada: el proyecto compila los tests con NodeNext, donde
// el alias '@/' no resuelve. La forma completa de la respuesta de la API
// (TrustedMfaDevice) vive en @/types y es compatible con esta.

/** Datos del dispositivo que necesitan estos helpers. */
export interface DispositivoMfa {
  user_agent_summary: string | null;
  ip_address_summary: string | null;
  revoked_at: string | null;
  current: boolean;
}

const MS_HORA = 60 * 60 * 1000;
const MS_DIA = 24 * MS_HORA;

/** Resumen legible del dispositivo: el user agent que guardo el backend. */
export function etiquetaDispositivo(device: Pick<DispositivoMfa, 'user_agent_summary'>): string {
  const resumen = device.user_agent_summary?.trim();
  return resumen ? resumen : 'Dispositivo desconocido';
}

/** Direccion IP del dispositivo, o el marcador del proyecto si no se conoce. */
export function etiquetaIp(device: Pick<DispositivoMfa, 'ip_address_summary'>): string {
  const ip = device.ip_address_summary?.trim();
  return ip ? ip : 'IP no disponible';
}

/**
 * Un dispositivo revocado ya no es de confianza, pero el backend lo sigue
 * devolviendo mientras no expire (la consulta filtra por `ExpiresAt > now`,
 * no por `RevokedAt`). La UI lo marca como revocado en vez de ofrecer
 * revocarlo otra vez.
 */
export function estaRevocado(device: Pick<DispositivoMfa, 'revoked_at'>): boolean {
  return Boolean(device.revoked_at);
}

/**
 * Resumen de la confianza segun cuando expira:
 * - 'Expirado' cuando ya paso la fecha de expiracion.
 * - 'Expira en X dias' o 'Expira manana' cuando queda mas de un dia.
 * - 'Expira en X h' cuando queda menos de un dia.
 */
export function resumenVencimiento(expiresAt: string | null | undefined, ahora: Date = new Date()): string {
  if (!expiresAt) {
    return 'Sin fecha de expiración';
  }

  const expira = new Date(expiresAt);
  if (Number.isNaN(expira.getTime())) {
    return 'Sin fecha de expiración';
  }

  const restante = expira.getTime() - ahora.getTime();
  if (restante <= 0) {
    return 'Expirado';
  }

  const dias = Math.ceil(restante / MS_DIA);
  if (restante < MS_DIA) {
    // Queda menos de un dia: se informa en horas.
    const horas = Math.max(1, Math.ceil(restante / MS_HORA));
    return `Expira en ${horas} h`;
  }

  if (dias === 1) {
    return 'Expira mañana';
  }

  return `Expira en ${dias} días`;
}

/**
 * Texto de confirmacion al revocar un dispositivo de confianza. Avisa de lo
 * que pasa: ese dispositivo tendra que volver a pedir el codigo MFA.
 */
export function textoConfirmacionRevocar(device: DispositivoMfa): string {
  const dispositivo = etiquetaDispositivo(device);
  const ip = etiquetaIp(device);
  const actual = device.current ? ' Es el dispositivo desde el que estás viendo esta página.' : '';

  return (
    `Se va a revocar la confianza MFA de ${dispositivo} (${ip}).${actual} ` +
    'La próxima vez que se acceda a la aplicación desde ese dispositivo habrá que ' +
    'volver a introducir el código MFA. Esta acción no se puede deshacer.'
  );
}
