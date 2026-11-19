// F-7: el borrado y el marcado en loteaban las peticiones de una en una (una
// peticion por fila y en serie). Este helper ejecuta la lista con un limite
// de concurrencia, espera a TODAS las operaciones (no deja ninguna colgada) y
// devuelve el resultado de cada una para que la pagina pueda avisar de las
// filas que fallaron.

// Pool por defecto: cuatro peticiones simultaneas. Suficiente para que el
// borrado o el marcado de una tanda no tarden lo que tardaria la serie, sin
// saturar la API ni el navegador.
export const LIMITE_CONCURRENCIA_LOTE = 4;

interface ResultadoOperacion<T> {
  indice: number;
  exito: boolean;
  valor?: T;
  error?: unknown;
}

export async function ejecutarEnLote<T>(
  operaciones: Array<() => Promise<T>>,
  limiteConcurrencia: number = LIMITE_CONCURRENCIA_LOTE
): Promise<ResultadoOperacion<T>[]> {
  const resultados = new Array<ResultadoOperacion<T>>(operaciones.length);
  if (operaciones.length === 0) {
    return resultados;
  }

  const limite = Math.max(1, Math.min(Math.floor(limiteConcurrencia) || 1, operaciones.length));
  let siguiente = 0;

  // Cada consumidor va sacando operaciones de la cola hasta que se agotan.
  // Un fallo se guarda en su resultado y NO corta el lote: el resto sigue.
  const consumir = async (): Promise<void> => {
    for (;;) {
      const indice = siguiente;
      siguiente += 1;
      if (indice >= operaciones.length) {
        return;
      }

      try {
        const valor = await operaciones[indice]();
        resultados[indice] = { indice, exito: true, valor };
      } catch (error) {
        resultados[indice] = { indice, exito: false, error };
      }
    }
  };

  await Promise.all(Array.from({ length: limite }, () => consumir()));

  return resultados;
}
