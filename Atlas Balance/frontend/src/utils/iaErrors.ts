// V-02.09 (Fase 10): traduce las excepciones que el backend lanza
// cuando la IA no puede responder por motivos esperables (scope,
// permisos, rate limit, configuracion, proveedor) en mensajes
// amigables para el usuario final. Cada rama identifica el tipo de
// excepcion por nombre para no acoplar este modulo a las clases
// de error del backend.

interface FriendlyIaMessage {
  texto: string;
  accion?: 'reformular' | 'contactar_admin' | 'esperar' | 'revisar_permisos';
}

export function friendlyIaError(err: unknown, fallback: string): FriendlyIaMessage {
  const message = String(err instanceof Error ? err.message : err ?? '');
  const lower = message.toLowerCase();

  if (lower.includes('desactivada') || lower.includes('not configured') || lower.includes('configurar')) {
    return {
      texto: 'La IA esta desactivada o sin configurar. Un administrador debe habilitarla en Configuracion > IA.',
      accion: 'contactar_admin',
    };
  }

  if (lower.includes('no tiene permiso') || lower.includes('permisos') || lower.includes('forbidden') || lower.includes('access denied')) {
    return {
      texto: 'Tu usuario no tiene permiso para usar la IA. Pide acceso a un administrador desde Configuracion > Usuarios.',
      accion: 'contactar_admin',
    };
  }

  if (lower.includes('fuera de alcance') || lower.includes('solo puedo responder') || lower.includes('out of scope') || lower.includes('sobre atlas')) {
    return {
      texto: 'Solo puedo responder sobre Atlas Balance, su funcionamiento o los datos financieros disponibles. Reformula la pregunta con esos terminos (extracto, comision, saldo, titular...).',
      accion: 'reformular',
    };
  }

  if (lower.includes('limite') || lower.includes('demasiadas') || lower.includes('rate limit') || lower.includes('too many')) {
    return {
      texto: 'Has alcanzado el limite de consultas de IA (por minuto, hora o dia). Espera un poco y vuelve a intentarlo.',
      accion: 'esperar',
    };
  }

  if (lower.includes('proveedor') || lower.includes('provider') || lower.includes('openrouter') || lower.includes('openai')) {
    return {
      texto: 'El proveedor de IA no respondio correctamente. Reintenta en unos segundos o prueba con otro modelo.',
      accion: 'esperar',
    };
  }

  if (lower.includes('timeout') || lower.includes('tardo demasiado')) {
    return {
      texto: 'La IA tardo demasiado en responder. Reintenta en unos segundos.',
      accion: 'esperar',
    };
  }

  if (lower.includes('presupuesto')) {
    return {
      texto: 'Se ha agotado el presupuesto configurado para la IA. Un administrador debe revisar los limites en Configuracion > IA.',
      accion: 'contactar_admin',
    };
  }

  if (lower.includes('sin datos') || lower.includes('no hay') || lower.includes('insuficiente')) {
    return {
      texto: 'No hay datos suficientes para responder. Comprueba que tus cuentas tienen extractos cargados o amplia el periodo.',
      accion: 'reformular',
    };
  }

  return { texto: fallback };
}
