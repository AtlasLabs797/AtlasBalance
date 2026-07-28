export const queryKeys = {
  scope: () => ['scope'] as const,

  auth: {
    me: () => ['auth', 'me'] as const,
  },

  dashboard: {
    principal: (params: { usuarioId: string; paisId: string | null; divisaPrincipal: string }) =>
      ['dashboard', 'principal', params] as const,
    evolucion: (params: {
      usuarioId: string;
      paisId: string | null;
      divisaPrincipal: string;
      periodo: string;
      titularId?: string | null;
    }) => ['dashboard', 'evolucion', params] as const,
    saldosDivisa: (params: {
      usuarioId: string;
      paisId: string | null;
      divisaPrincipal: string;
      titularId?: string | null;
    }) => ['dashboard', 'saldos-divisa', params] as const,
    titular: (params: { usuarioId: string; titularId: string; divisaPrincipal: string; paisId: string | null }) =>
      ['dashboard', 'titular', params] as const,
    saldosTitular: (titularId: string) => ['dashboard', 'titular-saldos', titularId] as const,
  },

  extractos: {
    list: (params: {
      usuarioId: string;
      cuentaId?: string | null;
      titularId?: string | null;
      paisId?: string | null;
      fechaDesde?: string | null;
      fechaHasta?: string | null;
      search?: string | null;
      includeFlagged?: boolean;
      includeChecked?: boolean;
      onlyFlagged?: boolean;
      onlyChecked?: boolean;
      page: number;
      pageSize: number;
      sortBy?: string;
      sortDir?: 'asc' | 'desc';
      incluirEliminados?: boolean;
    }) => ['extractos', 'list', params] as const,
    titularesResumen: (params: { usuarioId: string; paisId?: string | null }) =>
      ['extractos', 'titulares-resumen', params] as const,
    cuentaResumen: (params: { usuarioId: string; cuentaId: string; periodo: string; paisId: string | null }) =>
      ['extractos', 'cuenta-resumen', params] as const,
    columnasVisibles: (params: { usuarioId: string; cuentaId?: string | null; titularId?: string | null; paisId?: string | null }) =>
      ['extractos', 'columnas-visibles', params] as const,
    auditCelda: (extractoId: string, columna: string | null) =>
      ['extractos', 'audit-celda', extractoId, columna] as const,
    desglose: (extractoId: string) => ['extractos', 'desglose', extractoId] as const,
  },

  cuentas: {
    list: (params: {
      usuarioId: string;
      page: number;
      pageSize: number;
      search?: string | null;
      titularId?: string | null;
      paisId?: string | null;
      tipoTitular?: string | null;
      tipoCuenta?: string | null;
      incluirEliminados?: boolean;
      sortBy?: string;
      sortDir?: 'asc' | 'desc';
    }) => ['cuentas', 'list', params] as const,
    detalle: (params: { usuarioId: string; cuentaId: string; incluirEliminados?: boolean }) =>
      ['cuentas', 'detalle', params] as const,
    activas: (params: { usuarioId: string; page: number; pageSize: number; sortBy?: string; sortDir?: 'asc' | 'desc' }) =>
      ['cuentas', 'activas', params] as const,
    resumen: (params: { usuarioId: string; cuentaId: string; periodo: string; paisId: string | null }) =>
      ['cuentas', 'resumen', params] as const,
  },

  titulares: {
    list: (params: {
      usuarioId: string;
      page: number;
      pageSize: number;
      search?: string | null;
      tipoTitular?: string | null;
      paisId?: string | null;
      incluirEliminados?: boolean;
      sortBy?: string;
      sortDir?: 'asc' | 'desc';
    }) => ['titulares', 'list', params] as const,
    detalle: (params: { usuarioId: string; titularId: string; incluirEliminados?: boolean }) =>
      ['titulares', 'detalle', params] as const,
  },

  catalogo: {
    paises: (params: { usuarioId: string; page: number; pageSize: number; activos: boolean }) =>
      ['catalogo', 'paises', params] as const,
    divisasActivas: (params: { usuarioId: string }) => ['catalogo', 'divisas-activas', params] as const,
    catalogosPermisos: (params: { usuarioId: string }) => ['catalogo', 'permisos', params] as const,
    importacionContexto: (params: { usuarioId: string; paisId: string | null }) =>
      ['catalogo', 'importacion-contexto', params] as const,
    importacionLotes: (params: { usuarioId: string; cuentaId: string; page: number; pageSize: number }) =>
      ['catalogo', 'importacion-lotes', params] as const,
    importacionLote: (loteId: string) => ['catalogo', 'importacion-lote', loteId] as const,
    formatosColumnasSugeridas: (params: { usuarioId: string }) =>
      ['catalogo', 'formatos-columnas-sugeridas', params] as const,
  },

  alertas: {
    activas: (params: { usuarioId: string; paisId: string | null }) => ['alertas', 'activas', params] as const,
    lista: (params: { usuarioId: string }) => ['alertas', 'lista', params] as const,
    contexto: (params: { usuarioId: string }) => ['alertas', 'contexto', params] as const,
  },

  notificaciones: {
    adminResumen: (params: { usuarioId: string }) => ['notificaciones', 'admin-resumen', params] as const,
  },

  ia: {
    config: (params: { usuarioId: string }) => ['ia', 'config', params] as const,
    modelos: (params: { usuarioId: string; provider: string; search?: string | null }) =>
      ['ia', 'modelos', params] as const,
  },

  sistema: {
    versionDisponible: (params: { usuarioId: string }) => ['sistema', 'version-disponible', params] as const,
  },

  revision: {
    comisiones: (params: { usuarioId: string; estado: string; page: number; pageSize: number; paisId: string | null }) =>
      ['revision', 'comisiones', params] as const,
    seguros: (params: { usuarioId: string; estado: string; page: number; pageSize: number; paisId: string | null }) =>
      ['revision', 'seguros', params] as const,
  },

  conciliacion: {
    movimientosEsperados: (params: { usuarioId: string; cuentaId?: string | null }) =>
      ['conciliacion', 'movimientos-esperados', params] as const,
    matches: (params: { usuarioId: string; cuentaId?: string | null }) =>
      ['conciliacion', 'matches', params] as const,
  },

  usuarios: {
    list: (params: { usuarioId: string; page: number; pageSize: number; search?: string | null; incluirEliminados?: boolean }) =>
      ['usuarios', 'list', params] as const,
    detalle: (params: { usuarioId: string; usuarioId2: string }) => ['usuarios', 'detalle', params] as const,
  },

  formatosImportacion: {
    list: (params: { usuarioId: string; page: number; pageSize: number; search?: string | null; incluirEliminados?: boolean; sortBy?: string; sortDir?: 'asc' | 'desc' }) =>
      ['formatos-importacion', 'list', params] as const,
    detalle: (params: { usuarioId: string; formatoId: string; incluirEliminados?: boolean }) =>
      ['formatos-importacion', 'detalle', params] as const,
  },

  auditoria: {
    filtros: (params: { usuarioId: string; paisId: string | null }) => ['auditoria', 'filtros', params] as const,
    list: (params: {
      usuarioId: string;
      page: number;
      pageSize: number;
      usuarioId2?: string | null;
      cuentaId?: string | null;
      paisId?: string | null;
      tipoAccion?: string | null;
      fechaDesde?: string | null;
      fechaHasta?: string | null;
      tab?: string;
    }) => ['auditoria', 'list', params] as const,
  },

  papelera: {
    titulares: (params: { usuarioId: string; page: number; pageSize: number }) => ['papelera', 'titulares', params] as const,
    cuentas: (params: { usuarioId: string; page: number; pageSize: number }) => ['papelera', 'cuentas', params] as const,
    extractos: (params: { usuarioId: string; page: number; pageSize: number }) => ['papelera', 'extractos', params] as const,
    usuarios: (params: { usuarioId: string; page: number; pageSize: number }) => ['papelera', 'usuarios', params] as const,
  },

  exportaciones: {
    list: (params: { usuarioId: string; page: number; pageSize: number; cuentaId?: string | null; paisId?: string | null }) =>
      ['exportaciones', 'list', params] as const,
  },

  backups: {
    list: (params: { usuarioId: string; page: number; pageSize: number }) => ['backups', 'list', params] as const,
    config: (params: { usuarioId: string }) => ['backups', 'config', params] as const,
    googleDriveFiles: (params: { usuarioId: string }) => ['backups', 'google-drive-files', params] as const,
  },

  integraciones: {
    tokens: (params: { usuarioId: string; page: number; pageSize: number }) => ['integraciones', 'tokens', params] as const,
    metricas: (params: { usuarioId: string; tokenId: string }) => ['integraciones', 'metricas', params] as const,
    auditoria: (params: { usuarioId: string; page: number; pageSize: number; tokenId?: string | null }) =>
      ['integraciones', 'auditoria', params] as const,
  },

  configuracion: {
    sistema: (params: { usuarioId: string }) => ['configuracion', 'sistema', params] as const,
    tiposCambio: (params: { usuarioId: string }) => ['configuracion', 'tipos-cambio', params] as const,
    divisas: (params: { usuarioId: string }) => ['configuracion', 'divisas', params] as const,
  },
} as const;

export type QueryKey = readonly unknown[];

export function normalizeQueryParams<T extends Record<string, unknown>>(params: T): T {
  const out: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') {
      continue;
    }
    out[key] = value;
  }
  return out as T;
}
