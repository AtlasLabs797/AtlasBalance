import { useEffect } from 'react';

const NOMBRE_APP = 'Atlas Balance';

// Asigna el titulo del documento como "<titulo> | Atlas Balance".
// Si no hay titulo o es el propio nombre de la app, deja solo "Atlas Balance"
// para no duplicar el sufijo. No restaura nada al desmontar: la siguiente
// pagina que use el hook sobrescribe el titulo.
export const useDocumentTitle = (title: string): void => {
  useEffect(() => {
    const limpio = title.trim();
    document.title = limpio && limpio !== NOMBRE_APP ? `${limpio} | ${NOMBRE_APP}` : NOMBRE_APP;
  }, [title]);
};
