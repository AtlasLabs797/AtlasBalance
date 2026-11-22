// F-2: una respuesta que llega tarde (el usuario cambia de pestaña o de
// pagina mientras la peticion esta en vuelo) no debe pisar la que si es la
// ultima. Cada peticion recibe un numero de secuencia y solo la ultima
// secuencia vigente puede escribir en el estado.
//
// El numero de secuencia vive en un objeto y no en un ref de React para que
// la regla sea testeable sin DOM.
export interface SecuenciaPeticion {
  /** Abre una peticion nueva y devuelve su numero de secuencia. */
  siguiente(): number;
  /** true solo para la ultima peticion abierta. */
  esActual(secuencia: number): boolean;
}

export function crearSecuenciaPeticion(): SecuenciaPeticion {
  let actual = 0;

  return {
    siguiente: () => {
      actual += 1;
      return actual;
    },
    esActual: (secuencia: number) => secuencia === actual,
  };
}
