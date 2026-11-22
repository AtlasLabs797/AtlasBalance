// V-03.02 (F-8): los campos numericos de Configuracion usaban
// `Number(valor) || 0`, asi que al vaciar el campo se guardaba un 0 silencioso
// (y con el fallback `|| 24` un 24). Aqui un campo vacio es un campo vacio:
// devuelve null y el formulario conserva el ultimo valor valido.
//
// Helper puro y sin dependencias: lo usa el campo numerico de ConfiguracionPage.

/**
 * Convierte el texto de un campo numerico en numero.
 * - '' (o solo espacios) = campo vacio: devuelve null, NUNCA 0.
 * - texto no numerico = null.
 * - '0' es un valor valido y se devuelve como 0.
 */
export const parseOptionalNumberInput = (raw: string): number | null => {
  const trimmed = raw.trim();

  if (trimmed === '') {
    return null;
  }

  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
};
