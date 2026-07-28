import { useQueryClient } from '@tanstack/react-query';
import { queryKeys } from './queryKeys.js';

const dashboardPrefixes = ['dashboard'] as const;
const extractosPrefixes = ['extractos'] as const;
const cuentasPrefixes = ['cuentas'] as const;
const titularesPrefixes = ['titulares'] as const;
const catalogoPrefixes = ['catalogo'] as const;
const alertasPrefixes = ['alertas'] as const;
const revisionPrefixes = ['revision'] as const;
const conciliacionPrefixes = ['conciliacion'] as const;
const notificacionesPrefixes = ['notificaciones'] as const;
const exportacionesPrefixes = ['exportaciones'] as const;
const backupsPrefixes = ['backups'] as const;
const integracionesPrefixes = ['integraciones'] as const;
const configuracionPrefixes = ['configuracion'] as const;
const usuariosPrefixes = ['usuarios'] as const;
const formatosPrefixes = ['formatos-importacion'] as const;
const papeleraPrefixes = ['papelera'] as const;
const iaPrefixes = ['ia'] as const;
const sistemaPrefixes = ['sistema'] as const;

export type QueryClientApi = ReturnType<typeof useQueryClient>;

export function invalidateExtractosFamilia(qc: QueryClientApi): Promise<void> {
  return qc.invalidateQueries({ queryKey: extractosPrefixes });
}

export function invalidateDashboardFamilia(qc: QueryClientApi): Promise<void> {
  return qc.invalidateQueries({ queryKey: dashboardPrefixes });
}

export function invalidateAlertasFamilia(qc: QueryClientApi): Promise<void> {
  return qc.invalidateQueries({ queryKey: alertasPrefixes });
}

export function invalidateCuentasFamilia(qc: QueryClientApi): Promise<void> {
  return Promise.all([
    qc.invalidateQueries({ queryKey: cuentasPrefixes }),
    qc.invalidateQueries({ queryKey: catalogoPrefixes }),
  ]).then(() => undefined);
}

export function invalidateTitularesFamilia(qc: QueryClientApi): Promise<void> {
  return qc.invalidateQueries({ queryKey: titularesPrefixes });
}

export function invalidateConfiguracionFamilia(qc: QueryClientApi): Promise<void> {
  return Promise.all([
    qc.invalidateQueries({ queryKey: configuracionPrefixes }),
    qc.invalidateQueries({ queryKey: dashboardPrefixes }),
    qc.invalidateQueries({ queryKey: iaPrefixes }),
    qc.invalidateQueries({ queryKey: sistemaPrefixes }),
  ]).then(() => undefined);
}

export function invalidateFullCatalog(qc: QueryClientApi): Promise<void> {
  return qc.invalidateQueries();
}

export const mutationInvalidation = {
  // Importaciones y extractos: la operacion toca casi todo.
  importarConfirmar: invalidateExtractosFamilia,
  importarRevertir: invalidateExtractosFamilia,
  extractoCreate: invalidateExtractosFamilia,
  extractoUpdate: invalidateExtractosFamilia,
  extractoDelete: invalidateExtractosFamilia,
  extractoRestaurar: invalidateExtractosFamilia,
  extractoCheck: invalidateExtractosFamilia,
  extractoFlag: invalidateExtractosFamilia,
  extractoDesglose: (qc: QueryClientApi) => invalidateExtractosFamilia(qc),
  extractoColumnasVisibles: (qc: QueryClientApi) => qc.invalidateQueries({ queryKey: extractosPrefixes }),

  // Cuentas y titulares.
  cuentaCreate: invalidateCuentasFamilia,
  cuentaUpdate: invalidateCuentasFamilia,
  cuentaDelete: invalidateCuentasFamilia,
  cuentaRestaurar: invalidateCuentasFamilia,
  cuentaNotas: invalidateCuentasFamilia,
  cuentaPlazoRenovar: invalidateCuentasFamilia,
  titularCreate: invalidateTitularesFamilia,
  titularUpdate: invalidateTitularesFamilia,
  titularDelete: invalidateTitularesFamilia,
  titularRestaurar: invalidateTitularesFamilia,

  // Catalogos y configuracion.
  divisa: invalidateConfiguracionFamilia,
  tipoCambio: invalidateConfiguracionFamilia,
  formato: (qc: QueryClientApi) => Promise.all([
    qc.invalidateQueries({ queryKey: formatosPrefixes }),
    qc.invalidateQueries({ queryKey: catalogoPrefixes }),
  ]).then(() => undefined),
  alerta: invalidateAlertasFamilia,
  pais: (qc: QueryClientApi) => Promise.all([
    qc.invalidateQueries({ queryKey: catalogoPrefixes }),
    qc.invalidateQueries({ queryKey: dashboardPrefixes }),
  ]).then(() => undefined),

  // Conciliacion y revision.
  conciliacion: (qc: QueryClientApi) => Promise.all([
    qc.invalidateQueries({ queryKey: conciliacionPrefixes }),
    qc.invalidateQueries({ queryKey: notificacionesPrefixes }),
  ]).then(() => undefined),
  revision: (qc: QueryClientApi) => qc.invalidateQueries({ queryKey: revisionPrefixes }),

  // Usuarios y permisos (afectan a cualquier GET sensible al scope).
  usuario: (qc: QueryClientApi) => Promise.all([
    qc.invalidateQueries({ queryKey: usuariosPrefixes }),
    qc.invalidateQueries({ queryKey: catalogoPrefixes }),
    qc.invalidateQueries({ queryKey: papeleraPrefixes }),
  ]).then(() => undefined),

  // Exportaciones, backups, integraciones.
  exportacion: (qc: QueryClientApi) => Promise.all([
    qc.invalidateQueries({ queryKey: exportacionesPrefixes }),
    qc.invalidateQueries({ queryKey: notificacionesPrefixes }),
  ]).then(() => undefined),
  backup: (qc: QueryClientApi) => qc.invalidateQueries({ queryKey: backupsPrefixes }),
  integracionToken: (qc: QueryClientApi) => qc.invalidateQueries({ queryKey: integracionesPrefixes }),
} as const;

export { queryKeys };
