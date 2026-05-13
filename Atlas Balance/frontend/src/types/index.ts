// Mirror of DB schema + API response types

export type RolUsuario = 'ADMIN' | 'GERENTE' | 'EMPLEADO_ULTRA' | 'EMPLEADO_PLUS' | 'EMPLEADO';
export type TipoTitular = 'EMPRESA' | 'AUTONOMO' | 'PARTICULAR';
export type TipoCuenta = 'NORMAL' | 'EFECTIVO' | 'PLAZO_FIJO';
export type EstadoPlazoFijo = 'ACTIVO' | 'PROXIMO_VENCER' | 'VENCIDO' | 'RENOVADO' | 'CANCELADO';
export type EstadoToken = 'activo' | 'revocado';
export type FuenteTipoCambio = 'API' | 'MANUAL';
export type EstadoBackup = 'PENDING' | 'SUCCESS' | 'FAILED';
export type TipoBackup = 'AUTO' | 'MANUAL';

export interface Usuario {
  id: string;
  email: string;
  nombre_completo: string;
  rol: RolUsuario;
  activo: boolean;
  primer_login: boolean;
  puede_usar_ia: boolean;
  mfa_enabled: boolean;
  fecha_creacion: string;
  fecha_ultima_login: string | null;
}

export interface Titular {
  id: string;
  nombre: string;
  tipo: TipoTitular;
  identificacion: string | null;
  contacto_email: string | null;
  contacto_telefono: string | null;
  notas: string | null;
  fecha_creacion: string;
}

export interface Cuenta {
  id: string;
  titular_id: string;
  titular?: Titular;
  nombre: string;
  numero_cuenta: string | null;
  iban: string | null;
  banco_nombre: string | null;
  divisa: string;
  formato_id: string | null;
  es_efectivo: boolean;
  tipo_cuenta: TipoCuenta;
  titular_tipo?: TipoTitular;
  plazo_fijo?: PlazoFijo | null;
  activa: boolean;
  notas: string | null;
  fecha_creacion: string;
  // Computed fields from API
  saldo_actual?: number;
  ingresos_mes?: number;
  egresos_mes?: number;
}

export interface PlazoFijo {
  id: string;
  cuenta_id: string;
  cuenta_referencia_id: string | null;
  cuenta_referencia_nombre: string | null;
  fecha_inicio: string;
  fecha_vencimiento: string;
  interes_previsto: number | null;
  renovable: boolean;
  estado: EstadoPlazoFijo;
  fecha_ultima_notificacion: string | null;
  fecha_renovacion: string | null;
  notas: string | null;
}

export interface Extracto {
  id: string;
  cuenta_id: string;
  fecha: string;
  concepto: string | null;
  comentarios: string | null;
  monto: number;
  saldo: number;
  fila_numero: number;
  checked: boolean;
  checked_at: string | null;
  checked_by_id: string | null;
  flagged: boolean;
  flagged_nota: string | null;
  flagged_at: string | null;
  flagged_by_id: string | null;
  columnas_extra?: Record<string, string>;
  fecha_creacion: string;
  fecha_modificacion?: string | null;
  deleted_at?: string | null;
  cuenta_nombre?: string;
  titular_id?: string;
  titular_nombre?: string;
  divisa?: string;
}

export interface CuentaResumenKpi {
  cuenta_id: string;
  cuenta_nombre: string;
  iban: string | null;
  banco_nombre: string | null;
  divisa: string;
  titular_id: string;
  titular_nombre: string;
  es_efectivo: boolean;
  tipo_cuenta: TipoCuenta;
  plazo_fijo: PlazoFijo | null;
  notas: string | null;
  saldo_actual: number;
  ingresos_mes: number;
  egresos_mes: number;
  ultima_actualizacion: string | null;
}

export interface TitularConCuentas {
  titular_id: string;
  titular_nombre: string;
  cuentas: CuentaResumenKpi[];
}

export interface AuditCellEntry {
  id: string;
  tipo_accion: string;
  celda_referencia: string | null;
  columna_nombre: string | null;
  valor_anterior: string | null;
  valor_nuevo: string | null;
  timestamp: string;
  usuario_id: string | null;
}

export interface FormatoImportacion {
  id: string;
  nombre: string;
  banco_nombre: string | null;
  divisa: string | null;
  mapeo_json: MapeoColumnas;
  activo: boolean;
}

export type TipoMontoImportacion = 'una_columna' | 'dos_columnas' | 'tres_columnas';

export interface MapeoColumnas {
  tipo_monto?: TipoMontoImportacion;
  fecha: number;
  concepto: number;
  monto?: number | null;
  ingreso?: number | null;
  egreso?: number | null;
  saldo: number;
  columnas_extra?: { nombre: string; indice: number }[];
}

export interface PermisoUsuario {
  id: string;
  usuario_id: string;
  cuenta_id: string | null;
  titular_id: string | null;
  puede_ver_cuentas: boolean;
  puede_agregar_lineas: boolean;
  puede_editar_lineas: boolean;
  puede_eliminar_lineas: boolean;
  puede_importar: boolean;
  puede_ver_dashboard: boolean;
  columnas_visibles: string[] | null;
  columnas_editables: string[] | null;
}

export interface AlertaSaldo {
  id: string;
  cuenta_id: string | null;
  tipo_titular: TipoTitular | null;
  alcance: 'GLOBAL' | 'TIPO_TITULAR' | 'CUENTA';
  saldo_minimo: number;
  activa: boolean;
  fecha_creacion: string;
  fecha_ultima_alerta: string | null;
  destinatarios?: AlertaDestinatario[];
}

export interface AlertaDestinatario {
  id: string;
  alerta_id: string;
  usuario_id: string;
  usuario?: Usuario;
}

export interface Auditoria {
  id: string;
  usuario_id: string | null;
  tipo_accion: string;
  entidad_tipo: string | null;
  entidad_id: string | null;
  celda_referencia: string | null;
  columna_nombre: string | null;
  valor_anterior: string | null;
  valor_nuevo: string | null;
  timestamp: string;
  ip_address: string | null;
  detalles_json: Record<string, unknown> | null;
}

export interface AuditoriaListItem {
  id: string;
  timestamp: string;
  usuario_id: string | null;
  usuario_nombre: string | null;
  tipo_accion: string;
  entidad_tipo: string | null;
  entidad_id: string | null;
  cuenta_id: string | null;
  cuenta_nombre: string | null;
  titular_id: string | null;
  titular_nombre: string | null;
  celda_referencia: string | null;
  columna_nombre: string | null;
  valor_anterior: string | null;
  valor_nuevo: string | null;
  ip_address: string | null;
  detalles_json: string | null;
}

export interface AuditoriaFiltroUsuario {
  id: string;
  nombre: string;
}

export interface AuditoriaFiltroCuenta {
  id: string;
  nombre: string;
  titular_id: string;
  titular_nombre: string;
}

export interface AuditoriaFiltros {
  usuarios: AuditoriaFiltroUsuario[];
  cuentas: AuditoriaFiltroCuenta[];
  tipos_accion: string[];
}

export interface DivisaActiva {
  codigo: string;
  nombre: string | null;
  simbolo: string | null;
  activa: boolean;
  es_base: boolean;
}

export interface TipoCambio {
  id: string;
  divisa_origen: string;
  divisa_destino: string;
  tasa: number;
  fecha_actualizacion: string;
  fuente: FuenteTipoCambio;
}

export interface Configuracion {
  clave: string;
  valor: string;
  tipo: string | null;
  descripcion: string | null;
}

export interface ConfiguracionSistema {
  smtp: {
    host: string;
    port: number;
    user: string;
    password: string;
    from: string;
  };
  general: {
    app_base_url: string;
    app_update_check_url: string;
    backup_path: string;
    export_path: string;
  };
  exchange: {
    api_key: string;
    api_key_configurada: boolean;
  };
  dashboard: {
    color_ingresos: string;
    color_egresos: string;
    color_saldo: string;
  };
  revision: {
    comisiones_importe_minimo: number;
    saldo_bajo_cooldown_horas: number;
  };
  ia: {
    provider: string;
    openrouter_api_key: string;
    openrouter_api_key_configurada: boolean;
    openai_api_key: string;
    openai_api_key_configurada: boolean;
    model: string;
    habilitada: boolean;
    usuario_puede_usar: boolean;
    configurada: boolean;
    mensaje_estado: string;
    requests_por_minuto: number;
    requests_por_hora: number;
    requests_por_dia: number;
    requests_globales_por_dia: number;
    presupuesto_mensual_eur: number;
    presupuesto_mensual_usuario_eur: number;
    presupuesto_total_eur: number;
    coste_mes_estimado_eur: number;
    coste_mes_usuario_estimado_eur: number;
    coste_total_estimado_eur: number;
    requests_mes_usuario: number;
    tokens_entrada_mes_usuario: number;
    tokens_salida_mes_usuario: number;
    porcentaje_aviso_presupuesto: number;
    input_cost_per_million_tokens_eur: number;
    output_cost_per_million_tokens_eur: number;
    max_input_tokens: number;
    max_output_tokens: number;
    max_context_rows: number;
  };
}

export interface SaveConfiguracionSistemaRequest {
  smtp: {
    host: string;
    port: number;
    user: string;
    password: string;
    from: string;
  };
  general: {
    app_base_url: string;
    app_update_check_url: string;
    backup_path: string;
    export_path: string;
  };
  exchange: {
    api_key: string;
  };
  dashboard: {
    color_ingresos: string;
    color_egresos: string;
    color_saldo: string;
  };
  revision: {
    comisiones_importe_minimo: number;
    saldo_bajo_cooldown_horas: number;
  };
  ia: {
    provider: string;
    openrouter_api_key: string;
    openai_api_key: string;
    model: string;
    habilitada: boolean;
    requests_por_minuto: number;
    requests_por_hora: number;
    requests_por_dia: number;
    requests_globales_por_dia: number;
    presupuesto_mensual_eur: number;
    presupuesto_mensual_usuario_eur: number;
    presupuesto_total_eur: number;
    porcentaje_aviso_presupuesto: number;
    input_cost_per_million_tokens_eur: number;
    output_cost_per_million_tokens_eur: number;
    max_input_tokens: number;
    max_output_tokens: number;
    max_context_rows: number;
  };
}

export type RevisionEstadoComision = 'PENDIENTE' | 'DEVUELTA' | 'DESCARTADA';
export type RevisionEstadoSeguro = 'PENDIENTE' | 'CORRECTO' | 'DESCARTADA';

export interface RevisionComisionItem {
  extracto_id: string;
  cuenta_id: string;
  titular_id: string;
  titular: string;
  cuenta: string;
  fecha: string;
  monto: number;
  concepto: string;
  estado_devolucion: RevisionEstadoComision;
  divisa: string;
}

export interface RevisionSeguroItem {
  extracto_id: string;
  cuenta_id: string;
  titular_id: string;
  titular: string;
  cuenta: string;
  fecha: string;
  importe: number;
  concepto: string;
  estado: RevisionEstadoSeguro;
  divisa: string;
}

export interface IaConfig {
  provider: string;
  openrouter_api_key_configurada: boolean;
  openai_api_key_configurada: boolean;
  model: string;
  habilitada: boolean;
  usuario_puede_usar: boolean;
  configurada: boolean;
  mensaje_estado: string;
  requests_por_minuto: number;
  requests_por_hora: number;
  requests_por_dia: number;
  requests_globales_por_dia: number;
  presupuesto_mensual_eur: number;
  presupuesto_mensual_usuario_eur: number;
  presupuesto_total_eur: number;
  coste_mes_estimado_eur: number;
  coste_mes_usuario_estimado_eur: number;
  coste_total_estimado_eur: number;
  requests_mes_usuario: number;
  tokens_entrada_mes_usuario: number;
  tokens_salida_mes_usuario: number;
  porcentaje_aviso_presupuesto: number;
  input_cost_per_million_tokens_eur: number;
  output_cost_per_million_tokens_eur: number;
  max_input_tokens: number;
  max_output_tokens: number;
  max_context_rows: number;
}

export interface IaChatResponse {
  respuesta: string;
  provider: string;
  model: string;
  movimientos_analizados: number;
  tokens_entrada_estimados: number;
  tokens_salida_estimados: number;
  coste_estimado_eur: number;
  aviso_presupuesto: boolean;
  aviso: string | null;
}

export interface BackupItem {
  id: string;
  fecha_creacion: string;
  ruta_archivo: string;
  tamanio_bytes: number | null;
  estado: EstadoBackup;
  tipo: TipoBackup;
  iniciado_por_id: string | null;
  iniciado_por_nombre: string | null;
  notas: string | null;
}

export interface ExportacionItem {
  id: string;
  cuenta_id: string;
  cuenta_nombre: string;
  titular_nombre: string;
  fecha_exportacion: string;
  ruta_archivo: string | null;
  tamanio_bytes: number | null;
  estado: EstadoBackup;
  tipo: TipoBackup;
  iniciado_por_id: string | null;
  iniciado_por_nombre: string | null;
}

export interface WatchdogState {
  estado: 'IDLE' | 'RUNNING' | 'SUCCESS' | 'FAILED' | string;
  operacion: string | null;
  mensaje: string | null;
  updated_at: string | null;
}

export interface VersionActualResponse {
  version_actual: string;
}

export interface VersionDisponibleResponse {
  version_actual: string;
  version_disponible: string | null;
  actualizacion_disponible: boolean;
  mensaje: string | null;
}

export interface IntegrationPermissionItem {
  id: string;
  titular_id: string | null;
  cuenta_id: string | null;
  acceso_tipo: string;
}

export interface IntegrationTokenListItem {
  id: string;
  nombre: string;
  descripcion: string | null;
  tipo: string;
  estado: string;
  permiso_lectura: boolean;
  permiso_escritura: boolean;
  fecha_creacion: string;
  fecha_ultima_uso: string | null;
  fecha_revocacion: string | null;
  usuario_creador_id: string;
  deleted_at: string | null;
}

export interface IntegrationTokenDetail {
  token: IntegrationTokenListItem;
  permisos: IntegrationPermissionItem[];
}

export interface CreateIntegrationTokenRequest {
  nombre: string;
  descripcion?: string;
  permiso_lectura: boolean;
  permiso_escritura: boolean;
  permisos: Array<{
    titular_id: string | null;
    cuenta_id: string | null;
    acceso_tipo: string;
  }>;
}

export interface SaveIntegrationTokenRequest extends CreateIntegrationTokenRequest {}

export interface CreateIntegrationTokenResponse {
  token: IntegrationTokenDetail;
  token_plano: string;
}

export interface IntegrationTokenMetrics {
  total_requests: number;
  porcentaje_exito: number;
  tiempo_promedio_ms: number;
}

export interface IntegrationAuditItem {
  id: string;
  token_id: string;
  token_nombre: string | null;
  endpoint: string;
  metodo: string;
  codigo_respuesta: number | null;
  timestamp: string;
  tiempo_ejecucion_ms: number | null;
  ip_address: string | null;
}

export interface PaginatedResponse<T> {
  data: T[];
  total: number;
  page: number;
  page_size: number;
  total_pages: number;
}

export interface ApiResponse<T> {
  data: T;
}

export interface LoginResponse {
  csrf_token: string;
  usuario?: Usuario;
  permisos?: PermisoUsuario[];
  mfa_required?: boolean;
  mfa_setup_required?: boolean;
  mfa_challenge_id?: string;
  mfa_secret?: string | null;
  mfa_otp_auth_uri?: string | null;
}

export interface DashboardConcentracionBanco {
  banco_nombre: string;
  saldo_convertido: number;
  porcentaje: number;
}

export interface DashboardPrincipal {
  divisa_principal: string;
  saldos_por_divisa: Record<string, number>;
  total_convertido: number;
  ingresos_mes: number;
  egresos_mes: number;
  plazos_fijos: DashboardPlazosFijosResumen;
  saldos_por_titular: DashboardSaldoTitular[];
  saldos_por_cuenta: DashboardSaldoCuenta[];
  concentracion_bancos: DashboardConcentracionBanco[];
  chart_colors: DashboardChartColors;
}

export interface DashboardPlazosFijosResumen {
  monto_total_convertido: number;
  intereses_previstos_convertidos: number;
  dias_hasta_proximo_vencimiento: number | null;
  proximo_vencimiento: string | null;
  total_cuentas: number;
}

export interface DashboardTitular {
  titular_id: string;
  titular_nombre: string;
  divisa_principal: string;
  saldos_por_divisa: Record<string, number>;
  ingresos_mes: number;
  egresos_mes: number;
  total_convertido: number;
  saldos_por_cuenta: DashboardSaldoCuenta[];
  chart_colors: DashboardChartColors;
}

export interface DashboardPuntoEvolucion {
  fecha: string;
  ingresos: number;
  egresos: number;
  neto: number;
  saldo: number;
}

export type PeriodoDashboard = '1m' | '3m' | '6m' | '9m' | '12m' | '18m' | '24m';

export interface DashboardEvolucion {
  periodo: PeriodoDashboard;
  granularidad: 'diaria' | 'semanal';
  divisa_principal: string;
  saldo_inicio_periodo: number;
  disponible_inicio_periodo: number;
  inmovilizado_inicio_periodo: number;
  ingresos_anterior: number;
  egresos_anterior: number;
  puntos: DashboardPuntoEvolucion[];
}

export interface DashboardChartColors {
  ingresos: string;
  egresos: string;
  saldo: string;
}

export interface DashboardSaldoTitular {
  titular_id: string;
  titular_nombre: string;
  tipo_titular: TipoTitular;
  saldos_por_divisa: Record<string, number>;
  total_convertido: number;
  saldo_inmovilizado_convertido: number;
  saldo_disponible_convertido: number;
}

export interface DashboardSaldoCuenta {
  cuenta_id: string;
  cuenta_nombre: string;
  titular_id: string;
  titular_nombre: string;
  banco_nombre?: string | null;
  divisa: string;
  es_efectivo: boolean;
  tipo_cuenta: TipoCuenta;
  saldo_actual: number;
  saldo_convertido: number;
}

export interface DashboardSaldoDivisa {
  divisa: string;
  saldo: number;
  saldo_convertido: number;
  saldo_disponible: number;
  saldo_inmovilizado: number;
  saldo_total: number;
  saldo_total_convertido: number;
}

export interface DashboardSaldosDivisa {
  divisa_principal: string;
  divisas: DashboardSaldoDivisa[];
  total_convertido: number;
}

export interface ImportValidationResult {
  filas_ok: number;
  filas_error: number;
  separador_detectado: string;
  filas: ImportRowResult[];
  errores: {
    fila_indice: number;
    mensajes: string[];
  }[];
}

export interface ImportRowResult {
  indice: number;
  valida: boolean;
  datos: Record<string, string | null>;
  errores: string[];
  advertencias: string[];
}

export interface ImportMapExtraColumn {
  nombre: string;
  indice: number;
}

export interface ImportMapColumns {
  tipo_monto?: TipoMontoImportacion;
  fecha: number;
  concepto: number;
  monto?: number | null;
  ingreso?: number | null;
  egreso?: number | null;
  saldo: number;
  columnas_extra: ImportMapExtraColumn[];
}

export interface ImportCuentaContexto {
  id: string;
  nombre: string;
  titular_nombre: string;
  divisa: string;
  es_efectivo: boolean;
  tipo_cuenta: TipoCuenta;
  formato_id: string | null;
  formato_predefinido: ImportMapColumns | null;
}

export interface ImportContextoResponse {
  cuentas: ImportCuentaContexto[];
}

export interface ImportConfirmResult {
  filas_procesadas: number;
  filas_importadas: number;
  filas_con_error: number;
  errores: {
    fila_indice: number;
    mensajes: string[];
  }[];
}

export interface ImportPlazoFijoMovimientoResult {
  extracto_id: string;
  fila_numero: number;
  monto: number;
  saldo_anterior: number;
  saldo_actual: number;
}
