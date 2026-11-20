// Resumen legible de un User-Agent ("Chrome 157 · Windows"). Funcion pura, sin
// imports (los tests se compilan con NodeNext y no resuelven el alias '@/').

function detectarNavegador(ua: string): { nombre: string; version: string } | null {
  // El orden importa: Edge, Opera y Electron tambien dicen "Chrome" y "Safari".
  const reglas: Array<[string, RegExp]> = [
    ['Edge', /\b(?:Edg|Edge|EdgA|EdgiOS)\/(\d+)/],
    ['Opera', /\b(?:OPR|Opera)\/(\d+)/],
    ['Claude', /\bClaude\/(\d+)/],
    ['Electron', /\bElectron\/(\d+)/],
    ['Firefox', /\b(?:Firefox|FxiOS)\/(\d+)/],
    ['Chrome', /\b(?:Chrome|CriOS)\/(\d+)/],
    ['Safari', /\bVersion\/(\d+)[\d.]*.*\bSafari\//],
  ];
  for (const [nombre, regla] of reglas) {
    const m = regla.exec(ua);
    if (m) {
      return { nombre, version: m[1] };
    }
  }
  return null;
}

function detectarSistema(ua: string): string | null {
  // iOS y Android antes que macOS/Linux: sus UA contienen "like Mac OS X" / "Linux".
  if (/\b(iPhone|iPad|iPod)\b/.test(ua)) return 'iOS';
  if (/\bAndroid\b/.test(ua)) return 'Android';
  if (/\bWindows\b/.test(ua)) return 'Windows';
  if (/\bMac OS X\b|\bMacintosh\b/.test(ua)) return 'macOS';
  if (/\bCrOS\b/.test(ua)) return 'ChromeOS';
  if (/\bLinux\b|\bX11\b/.test(ua)) return 'Linux';
  return null;
}

/**
 * "Chrome 157 · Windows". Si no reconoce nada devuelve el texto recortado
 * (por ejemplo un resumen que ya sea legible) o '' si esta vacio.
 */
export function resumenUserAgent(userAgent: string | null | undefined): string {
  const ua = userAgent?.trim() ?? '';
  if (!ua) {
    return '';
  }

  const navegador = detectarNavegador(ua);
  const sistema = detectarSistema(ua);
  const partes = [navegador ? `${navegador.nombre} ${navegador.version}` : null, sistema].filter(
    (parte): parte is string => Boolean(parte),
  );

  return partes.length > 0 ? partes.join(' · ') : ua;
}
