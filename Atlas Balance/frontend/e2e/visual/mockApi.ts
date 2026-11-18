import type { Page, Route } from '@playwright/test';

// API mockeada para capturas visuales. Sin backend ni credenciales.
// - Endpoint desconocido: 200 con {} y se registra en `unknownPaths`.
// - VISUAL_FAIL=/dashboard/evolucion (varios separados por coma) fuerza 500 en ese path.

const ID = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

const USER_ID = ID(1);
const PAIS_ES = ID(11);
const PAIS_DO = ID(12);
const TIT = [ID(21), ID(22), ID(23)];
const CTA = [ID(31), ID(32), ID(33), ID(34), ID(35)];

const usuario = {
  id: USER_ID, email: 'admin@atlasbalance.local', nombre_completo: 'Marta Admin', rol: 'ADMIN',
  activo: true, primer_login: false, puede_usar_ia: true, mfa_enabled: true, mfa_required: true,
  fecha_creacion: '2025-01-10T09:00:00Z', fecha_ultima_login: '2026-11-15T08:30:00Z',
};

const paises = [
  { id: PAIS_ES, nombre: 'Espana', codigo_iso2: 'ES', activo: true, fecha_creacion: '2025-01-10T09:00:00Z', fecha_modificacion: null, deleted_at: null },
  { id: PAIS_DO, nombre: 'Republica Dominicana', codigo_iso2: 'DO', activo: true, fecha_creacion: '2025-01-10T09:00:00Z', fecha_modificacion: null, deleted_at: null },
];

const titulares = [
  { id: TIT[0], nombre: 'Empresa Demo SL', tipo: 'EMPRESA', notas: 'Sociedad operativa', fecha_creacion: '2025-02-01T10:00:00Z', cuentas_count: 3, deleted_at: null },
  { id: TIT[1], nombre: 'Autonomo Demo', tipo: 'AUTONOMO', notas: null, fecha_creacion: '2025-03-12T10:00:00Z', cuentas_count: 1, deleted_at: null },
  { id: TIT[2], nombre: 'Particular Demo', tipo: 'PARTICULAR', notas: 'Ahorro familiar', fecha_creacion: '2025-04-20T10:00:00Z', cuentas_count: 1, deleted_at: null },
];

const cuentasBase = [
  { id: CTA[0], titular_id: TIT[0], titular_nombre: titulares[0].nombre, nombre: 'Operativa BBVA', numero_cuenta: null, iban: 'ES9121000418450200051332', banco_nombre: 'BBVA', divisa: 'EUR', formato_id: null, pais_id: PAIS_ES, pais_nombre: 'Espana', es_efectivo: false, tipo_cuenta: 'NORMAL', activa: true, notas: null, fecha_creacion: '2025-02-02T10:00:00Z', saldo_actual: 48250.75, ingresos_mes: 21400.5, egresos_mes: 15870.2, plazo_fijo: null, deleted_at: null },
  { id: CTA[1], titular_id: TIT[0], titular_nombre: titulares[0].nombre, nombre: 'Nominas Santander', numero_cuenta: null, iban: 'ES7921000813610123456789', banco_nombre: 'Santander', divisa: 'EUR', formato_id: null, pais_id: PAIS_ES, pais_nombre: 'Espana', es_efectivo: false, tipo_cuenta: 'NORMAL', activa: true, notas: null, fecha_creacion: '2025-02-02T10:00:00Z', saldo_actual: 12980.1, ingresos_mes: 0, egresos_mes: 9120, plazo_fijo: null, deleted_at: null },
  { id: CTA[2], titular_id: TIT[0], titular_nombre: titulares[0].nombre, nombre: 'Caja Bavaro', numero_cuenta: null, iban: null, banco_nombre: null, divisa: 'DOP', formato_id: null, pais_id: PAIS_DO, pais_nombre: 'Republica Dominicana', es_efectivo: true, tipo_cuenta: 'EFECTIVO', activa: true, notas: null, fecha_creacion: '2025-05-02T10:00:00Z', saldo_actual: 385000, ingresos_mes: 120000, egresos_mes: 98000, plazo_fijo: null, deleted_at: null },
  { id: CTA[3], titular_id: TIT[1], titular_nombre: titulares[1].nombre, nombre: 'Autonoma CaixaBank', numero_cuenta: null, iban: 'ES1720852066623456789011', banco_nombre: 'CaixaBank', divisa: 'USD', formato_id: null, pais_id: PAIS_ES, pais_nombre: 'Espana', es_efectivo: false, tipo_cuenta: 'NORMAL', activa: true, notas: null, fecha_creacion: '2025-03-13T10:00:00Z', saldo_actual: 7340.9, ingresos_mes: 3100, egresos_mes: 2210.4, plazo_fijo: null, deleted_at: null },
  {
    id: CTA[4], titular_id: TIT[2], titular_nombre: titulares[2].nombre, nombre: 'Deposito 12 meses', numero_cuenta: null, iban: 'ES6621000418401234567891', banco_nombre: 'ING', divisa: 'EUR', formato_id: null, pais_id: PAIS_ES, pais_nombre: 'Espana', es_efectivo: false, tipo_cuenta: 'PLAZO_FIJO', activa: true, notas: null, fecha_creacion: '2025-04-21T10:00:00Z', saldo_actual: 30000, ingresos_mes: 0, egresos_mes: 0,
    plazo_fijo: { id: ID(41), cuenta_id: CTA[4], cuenta_referencia_id: CTA[0], cuenta_referencia_nombre: 'Operativa BBVA', fecha_inicio: '2026-04-21', fecha_vencimiento: '2027-04-21', interes_previsto: 840, renovable: true, estado: 'ACTIVO', fecha_ultima_notificacion: null, fecha_renovacion: null, notas: null },
    deleted_at: null,
  },
];

const resumenCuentas = (titularId: string) => cuentasBase.filter((c) => c.titular_id === titularId).map((c) => ({
  cuenta_id: c.id, cuenta_nombre: c.nombre, iban: c.iban, banco_nombre: c.banco_nombre, divisa: c.divisa,
  pais_id: c.pais_id, pais_nombre: c.pais_nombre, titular_id: c.titular_id, titular_nombre: c.titular_nombre,
  es_efectivo: c.es_efectivo, tipo_cuenta: c.tipo_cuenta, plazo_fijo: c.plazo_fijo, notas: c.notas,
  saldo_actual: c.saldo_actual, ingresos_mes: c.ingresos_mes, egresos_mes: c.egresos_mes, ultima_actualizacion: '2026-11-14T18:00:00Z',
}));
const titularesResumen = titulares.map((t) => ({ titular_id: t.id, titular_nombre: t.nombre, cuentas: resumenCuentas(t.id) }));

const chartColors = { ingresos: '#16a34a', egresos: '#dc2626', saldo: '#2563eb' };
const saldosPorCuenta = cuentasBase.map((c) => ({
  cuenta_id: c.id, cuenta_nombre: c.nombre, titular_id: c.titular_id, titular_nombre: c.titular_nombre,
  pais_id: c.pais_id, pais_nombre: c.pais_nombre, banco_nombre: c.banco_nombre, divisa: c.divisa,
  es_efectivo: c.es_efectivo, tipo_cuenta: c.tipo_cuenta, saldo_actual: c.saldo_actual,
  saldo_convertido: c.divisa === 'DOP' ? +(c.saldo_actual / 62.4).toFixed(2) : c.divisa === 'USD' ? +(c.saldo_actual * 0.92).toFixed(2) : c.saldo_actual,
}));
const dashboardPrincipal = {
  divisa_principal: 'EUR',
  saldos_por_divisa: { EUR: 91230.85, USD: 7340.9, DOP: 385000 },
  total_convertido: 104420.3, ingresos_mes: 38200.4, egresos_mes: 27350.1,
  plazos_fijos: { monto_total_convertido: 30000, intereses_previstos_convertidos: 840, dias_hasta_proximo_vencimiento: 156, proximo_vencimiento: '2027-04-21', total_cuentas: 1 },
  saldos_por_titular: titulares.map((t, i) => ({
    titular_id: t.id, titular_nombre: t.nombre, tipo_titular: t.tipo, saldos_por_divisa: { EUR: [61230, 6760, 30000][i] },
    total_convertido: [67380, 6760, 30280][i], saldo_inmovilizado_convertido: i === 2 ? 30000 : 0, saldo_disponible_convertido: [67380, 6760, 280][i],
  })),
  saldos_por_cuenta: saldosPorCuenta,
  saldos_por_pais: [
    { pais_id: PAIS_ES, pais_nombre: 'Espana', saldos_por_divisa: { EUR: 91230.85, USD: 7340.9 }, total_convertido: 98000.5, total_cuentas: 4 },
    { pais_id: PAIS_DO, pais_nombre: 'Republica Dominicana', saldos_por_divisa: { DOP: 385000 }, total_convertido: 6169.2, total_cuentas: 1 },
  ],
  concentracion_bancos: [
    { banco_nombre: 'BBVA', saldo_convertido: 48250.75, porcentaje: 46.2 },
    { banco_nombre: 'ING', saldo_convertido: 30000, porcentaje: 28.7 },
    { banco_nombre: 'Santander', saldo_convertido: 12980.1, porcentaje: 12.4 },
    { banco_nombre: 'Otros', saldo_convertido: 13189.45, porcentaje: 12.7 },
  ],
  chart_colors: chartColors,
};
const evolucion = {
  periodo: '6m', granularidad: 'semanal', divisa_principal: 'EUR', saldo_inicio_periodo: 82000,
  disponible_inicio_periodo: 52000, inmovilizado_inicio_periodo: 30000, ingresos_anterior: 30100, egresos_anterior: 25400,
  puntos: Array.from({ length: 26 }, (_, i) => {
    const d = new Date(Date.UTC(2026, 4, 18 + i * 7));
    const ingresos = 7000 + ((i * 937) % 4200);
    const egresos = 5200 + ((i * 611) % 3800);
    return { fecha: d.toISOString().slice(0, 10), ingresos, egresos, neto: ingresos - egresos, saldo: 82000 + i * 870 + ((i * 313) % 1500) };
  }),
};
const saldosDivisa = {
  divisa_principal: 'EUR', total_convertido: 104420.3,
  divisas: [
    { divisa: 'EUR', saldo: 91230.85, saldo_convertido: 91230.85, saldo_disponible: 61230.85, saldo_inmovilizado: 30000, saldo_total: 91230.85, saldo_total_convertido: 91230.85 },
    { divisa: 'USD', saldo: 7340.9, saldo_convertido: 6753.6, saldo_disponible: 7340.9, saldo_inmovilizado: 0, saldo_total: 7340.9, saldo_total_convertido: 6753.6 },
    { divisa: 'DOP', saldo: 385000, saldo_convertido: 6169.2, saldo_disponible: 385000, saldo_inmovilizado: 0, saldo_total: 385000, saldo_total_convertido: 6169.2 },
  ],
};

const conceptos = ['Alquiler oficina', 'Cobro cliente demo', 'Suministro electrico', 'Transferencia Cliente Demo SRL', 'Comision mantenimiento', 'Cuota seguro', 'Pago proveedor limpieza', 'Cobro oficina privada', 'Nomina equipo', 'Recibo internet', 'Compra material oficina', 'Devolucion comision'];
let saldoAcum = 48250.75;
const extractos = Array.from({ length: 60 }, (_, i) => {
  const monto = (i % 3 === 0 ? 1 : -1) * Math.round((120 + ((i * 7919) % 4800)) * 100) / 100;
  const saldo = saldoAcum;
  saldoAcum = Math.round((saldoAcum - monto) * 100) / 100;
  const d = new Date(Date.UTC(2026, 10, 14 - Math.floor(i / 2)));
  return {
    id: ID(1000 + i), cuenta_id: CTA[0], fecha: d.toISOString().slice(0, 10), concepto: conceptos[i % conceptos.length],
    comentarios: i % 7 === 0 ? 'Revisar con contabilidad' : null, monto, saldo, fila_numero: 60 - i,
    checked: i % 4 === 0, checked_at: null, checked_by_id: null, flagged: i % 11 === 0, flagged_nota: i % 11 === 0 ? 'Importe a confirmar' : null,
    flagged_at: null, flagged_by_id: null, desglose_count: 0, desglose_total: 0, desglose_estado: 'sin_desglose',
    fecha_creacion: d.toISOString(), cuenta_nombre: 'Operativa BBVA', banco_nombre: 'BBVA', titular_id: TIT[0], titular_nombre: titulares[0].nombre, pais_id: PAIS_ES, divisa: 'EUR',
  };
});

const usuariosRows = [
  usuario,
  { ...usuario, id: ID(2), email: 'gerente@atlasbalance.local', nombre_completo: 'Pablo Gerente', rol: 'GERENTE', mfa_enabled: false, mfa_required: false },
  { ...usuario, id: ID(3), email: 'empleada@atlasbalance.local', nombre_completo: 'Ana Empleada', rol: 'EMPLEADO', mfa_enabled: false, mfa_required: true, puede_usar_ia: false },
].map((u) => ({ ...u, deleted_at: null }));

const backups = Array.from({ length: 8 }, (_, i) => ({
  id: ID(500 + i), fecha_creacion: new Date(Date.UTC(2026, 10, 15 - i, 3, 0)).toISOString(), ruta_archivo: `backups/atlas-${20261115 - i}.zip`,
  tamanio_bytes: 18_400_000 + i * 120_000, estado: i === 3 ? 'FAILED' : 'SUCCESS', tipo: i % 3 === 0 ? 'MANUAL' : 'AUTO',
  iniciado_por_id: null, iniciado_por_nombre: i % 3 === 0 ? 'Marta Admin' : null, notas: null, destino: 'LOCAL_Y_GOOGLE_DRIVE',
  cloud_provider: 'GOOGLE_DRIVE', cloud_estado: i === 3 ? 'FAILED' : 'SUCCESS', cloud_uploaded_at: null, cloud_file_id: null, cloud_file_name: null, cloud_error_message: i === 3 ? 'Cuota excedida' : null,
}));
const backupConfig = {
  auto_enabled: true, frequency: 'DAILY', time_utc: '03:00', day_of_week: 1, day_of_month: 1, interval_hours: 6,
  destination: 'LOCAL_Y_GOOGLE_DRIVE', last_started_utc: '2026-11-15T03:00:00Z', last_result: 'SUCCESS',
  google_drive: { client_id: 'demo-client.apps.googleusercontent.com', client_secret_configured: true, connected: true, account_email: 'backups@corales.example', folder_id: 'folder-demo', last_validated_at: '2026-11-14T10:00:00Z', last_error: null, encryption_key_configured: true },
};

const iaConfig = {
  provider: 'openrouter', openrouter_api_key_configurada: true, openai_api_key_configurada: false, minimax_api_key_configurada: false,
  model: 'openai/gpt-4o-mini', habilitada: true, usuario_puede_usar: true, configurada: true, mensaje_estado: 'Asistente listo', permite_retencion_datos: false,
  modelos_permitidos: ['openai/gpt-4o-mini'], requests_por_minuto: 10, requests_por_hora: 60, requests_por_dia: 200, requests_globales_por_dia: 1000,
  presupuesto_mensual_eur: 20, presupuesto_mensual_usuario_eur: 5, presupuesto_total_eur: 100, coste_mes_estimado_eur: 2.4, coste_mes_usuario_estimado_eur: 0.8,
  coste_total_estimado_eur: 9.1, requests_mes_usuario: 34, tokens_entrada_mes_usuario: 52000, tokens_salida_mes_usuario: 14000, porcentaje_aviso_presupuesto: 80,
  input_cost_per_million_tokens_eur: 0.15, output_cost_per_million_tokens_eur: 0.6, max_input_tokens: 12000, max_output_tokens: 2000, max_context_rows: 200,
  thinking_modes: [{ value: 'auto', label: 'Automatico' }],
};
const configuracion = {
  smtp: { host: 'smtp.corales.example', port: 587, user: 'avisos@corales.example', password: '', from: 'Atlas Balance <avisos@corales.example>' },
  general: { app_base_url: 'https://atlas.corales.example', app_update_check_url: 'https://api.github.com/repos/AtlasLabs797/Atlas-Balance/releases/latest', app_update_auto_enabled: false, app_update_auto_hour_utc: 3, app_update_auto_last_checked_utc: '', app_update_auto_last_started_utc: '', app_update_auto_last_result: '', mfa_remember_device_enabled: true, mfa_remember_device_days: 30, require_mfa_for_non_admin_users: false, backup_path: 'C:\\AtlasBalance\\backups', export_path: 'C:\\AtlasBalance\\exports' },
  exchange: { api_key: '', api_key_configurada: true },
  dashboard: { color_ingresos: '#16a34a', color_egresos: '#dc2626', color_saldo: '#2563eb' },
  revision: { comisiones_importe_minimo: 1, saldo_bajo_cooldown_horas: 24 },
  ia: { ...iaConfig, openrouter_api_key: '', openai_api_key: '', minimax_api_key: '' },
};
const divisas = [
  { codigo: 'EUR', nombre: 'Euro', simbolo: 'EUR', activa: true, es_base: true },
  { codigo: 'USD', nombre: 'Dolar estadounidense', simbolo: '$', activa: true, es_base: false },
  { codigo: 'DOP', nombre: 'Peso dominicano', simbolo: 'RD$', activa: true, es_base: false },
];
const tiposCambio = [
  { id: ID(601), divisa_origen: 'EUR', divisa_destino: 'USD', tasa: 1.0872, fecha_actualizacion: '2026-11-15T06:00:00Z', fuente: 'API' },
  { id: ID(602), divisa_origen: 'EUR', divisa_destino: 'DOP', tasa: 62.4, fecha_actualizacion: '2026-11-15T06:00:00Z', fuente: 'MANUAL' },
];
const catalogos = {
  paises: paises.map((p) => ({ id: p.id, nombre: p.nombre })),
  titulares: titulares.map((t) => ({ id: t.id, nombre: t.nombre })),
  cuentas: cuentasBase.map((c) => ({ id: c.id, nombre: c.nombre, titular_id: c.titular_id, pais_id: c.pais_id })),
};
const importContexto = {
  cuentas: cuentasBase.filter((c) => c.tipo_cuenta !== 'PLAZO_FIJO').map((c) => ({
    id: c.id, nombre: c.nombre, titular_nombre: c.titular_nombre, divisa: c.divisa, pais_id: c.pais_id,
    es_efectivo: c.es_efectivo, tipo_cuenta: c.tipo_cuenta, formato_id: null, formato_predefinido: null,
  })),
};

const MS_HORA = 60 * 60 * 1000;
const MS_DIA = 24 * MS_HORA;
/** Fecha ISO relativa al instante de cargar el fixture (el aviso de vencimiento depende de "ahora"). */
const en = (ms: number) => new Date(Date.now() + ms).toISOString();

// V-03.02 (LOTE 6): dispositivos MFA de confianza. Forma exacta del DTO
// backend AuthDtos.TrustedMfaDeviceResponse (snake_case). Tres dispositivos
// representativos: el actual, uno que caduca pronto y uno ya revocado.
const mfaDispositivosFixture = [
  {
    id: ID(1201), created_at: en(-20 * MS_DIA), expires_at: en(70 * MS_DIA),
    last_used_at: en(-3 * MS_HORA), revoked_at: null,
    user_agent_summary: 'Chrome 141 - Windows 11', ip_address_summary: '192.168.1.24', current: true,
  },
  {
    id: ID(1202), created_at: en(-87 * MS_DIA), expires_at: en(3 * MS_DIA),
    last_used_at: en(-40 * MS_DIA), revoked_at: null,
    user_agent_summary: 'Safari 18 - iPhone 15', ip_address_summary: '203.0.113.45', current: false,
  },
  {
    id: ID(1203), created_at: en(-50 * MS_DIA), expires_at: en(40 * MS_DIA),
    last_used_at: en(-12 * MS_DIA), revoked_at: en(-2 * MS_DIA),
    user_agent_summary: 'Edge 140 - Windows 10', ip_address_summary: '10.0.0.15', current: false,
  },
];

// V-03.02 (LOTE 6): historial de lotes de importacion. Forma exacta de
// ImportacionLoteResponse (snake_case). Solo un lote 'confirmado' es
// reversible, asi que es el unico que muestra el boton "Revertir".
const lotesImportacionFixture = [
  {
    id: ID(1301), cuenta_id: CTA[0], cuenta_nombre: 'Operativa BBVA', usuario_creador_id: USER_ID,
    tipo_origen: 'ARCHIVO', nombre_archivo: 'movimientos-noviembre.csv', tamanio_bytes: 18_432,
    sha256: '3f9a1c7d2b5e84a6c0d1e2f3a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7',
    separador: ';', lote_hash: 'a1b2c3d4e5f60718', estado: 'confirmado',
    filas_total: 48, filas_validas: 46, filas_error: 2, filas_advertencia: 1, advertencias_aceptadas: true,
    fecha_creacion: en(-2 * MS_DIA), fecha_confirmacion: en(-2 * MS_DIA + MS_HORA), confirmado_por_id: USER_ID,
    fecha_reversion: null, revertido_por_id: null,
    divisa_mismatch: false, divisa_cuenta: 'EUR', divisa_esperada: null,
  },
  {
    id: ID(1302), cuenta_id: CTA[0], cuenta_nombre: 'Operativa BBVA', usuario_creador_id: USER_ID,
    tipo_origen: 'PEGADO', nombre_archivo: null, tamanio_bytes: 4_096,
    sha256: '9b2c4d6e8f0a1b3c5d7e9f0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5',
    separador: 'tab', lote_hash: 'b2c3d4e5f6071829', estado: 'validado_con_errores',
    filas_total: 30, filas_validas: 27, filas_error: 3, filas_advertencia: 0, advertencias_aceptadas: false,
    fecha_creacion: en(-6 * MS_HORA), fecha_confirmacion: null, confirmado_por_id: null,
    fecha_reversion: null, revertido_por_id: null,
    divisa_mismatch: false, divisa_cuenta: 'EUR', divisa_esperada: null,
  },
  {
    id: ID(1303), cuenta_id: CTA[0], cuenta_nombre: 'Operativa BBVA', usuario_creador_id: USER_ID,
    tipo_origen: 'ARCHIVO', nombre_archivo: 'movimientos-octubre.csv', tamanio_bytes: 21_880,
    sha256: 'c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2',
    separador: ';', lote_hash: 'c3d4e5f607182930', estado: 'revertido',
    filas_total: 52, filas_validas: 52, filas_error: 0, filas_advertencia: 0, advertencias_aceptadas: true,
    fecha_creacion: en(-14 * MS_DIA), fecha_confirmacion: en(-14 * MS_DIA + MS_HORA), confirmado_por_id: USER_ID,
    fecha_reversion: en(-9 * MS_DIA), revertido_por_id: USER_ID,
    divisa_mismatch: false, divisa_cuenta: 'EUR', divisa_esperada: null,
  },
];

const tokensFixture = [
  { id: ID(901), nombre: 'Token lectura demo', descripcion: 'Asistente de consulta', tipo: 'OPENCLAW', estado: 'activo', permiso_lectura: true, permiso_escritura: false, fecha_creacion: '2026-10-01T09:00:00Z', fecha_expiracion: '2027-10-01T23:59:59Z', fecha_ultima_uso: '2026-11-14T10:20:00Z', fecha_revocacion: null, rotated_from_token_id: null, scopes: ['titulares', 'saldos', 'extractos'], last_used_ip_address: '203.0.113.10', usuario_creador_id: USER_ID, deleted_at: null },
  { id: ID(902), nombre: 'Token informes demo', descripcion: null, tipo: 'OPENCLAW', estado: 'activo', permiso_lectura: true, permiso_escritura: false, fecha_creacion: '2026-10-05T09:00:00Z', fecha_expiracion: null, fecha_ultima_uso: null, fecha_revocacion: null, rotated_from_token_id: null, scopes: ['evolucion', 'alertas'], last_used_ip_address: null, usuario_creador_id: USER_ID, deleted_at: null },
];

const alertasFixture = [
  { id: ID(951), cuenta_id: null, tipo_titular: null, alcance: 'GLOBAL', cuenta_nombre: null, titular_id: null, titular_nombre: null, divisa: null, saldo_minimo: 5000, activa: true, fecha_creacion: '2026-09-01T09:00:00Z', fecha_ultima_alerta: null, destinatarios: [{ usuario_id: USER_ID, nombre_completo: 'Marta Admin', email_login: 'admin@atlasbalance.local' }] },
  { id: ID(952), cuenta_id: null, tipo_titular: 'AUTONOMO', alcance: 'TIPO_TITULAR', cuenta_nombre: null, titular_id: null, titular_nombre: null, divisa: null, saldo_minimo: 2000, activa: true, fecha_creacion: '2026-09-02T09:00:00Z', fecha_ultima_alerta: '2026-11-10T08:00:00Z', destinatarios: [{ usuario_id: USER_ID, nombre_completo: 'Marta Admin', email_login: 'admin@atlasbalance.local' }] },
  { id: ID(953), cuenta_id: CTA[1], tipo_titular: null, alcance: 'CUENTA', cuenta_nombre: 'Nominas Santander', titular_id: TIT[0], titular_nombre: titulares[0].nombre, divisa: 'EUR', saldo_minimo: 15000, activa: true, fecha_creacion: '2026-09-03T09:00:00Z', fecha_ultima_alerta: '2026-11-12T08:00:00Z', destinatarios: [{ usuario_id: USER_ID, nombre_completo: 'Marta Admin', email_login: 'admin@atlasbalance.local' }, { usuario_id: ID(2), nombre_completo: 'Pablo Gerente', email_login: 'gerente@atlasbalance.local' }] },
  { id: ID(954), cuenta_id: CTA[2], tipo_titular: null, alcance: 'CUENTA', cuenta_nombre: 'Caja Bavaro', titular_id: TIT[0], titular_nombre: titulares[0].nombre, divisa: 'DOP', saldo_minimo: 100000, activa: false, fecha_creacion: '2026-09-04T09:00:00Z', fecha_ultima_alerta: null, destinatarios: [] },
];

const exportacionesFixture = Array.from({ length: 4 }, (_, i) => ({
  id: ID(970 + i), cuenta_id: CTA[i % 3], cuenta_nombre: cuentasBase[i % 3].nombre, titular_nombre: titulares[0].nombre,
  fecha_exportacion: new Date(Date.UTC(2026, 10, 15 - i, 6, 0)).toISOString(), ruta_archivo: `exports/demo-${i}.xlsx`,
  tamanio_bytes: 240_000 + i * 18_000, estado: i === 2 ? 'FAILED' : 'SUCCESS', tipo: i % 2 === 0 ? 'AUTO' : 'MANUAL',
  iniciado_por_id: null, iniciado_por_nombre: i % 2 === 0 ? null : 'Marta Admin',
}));

const movimientosEsperadosFixture = [
  { id: ID(980), cuenta_id: CTA[0], cuenta_nombre: 'Operativa BBVA', fecha_esperada: '2026-11-14', monto: -1200, divisa: 'EUR', referencia: 'REF-DEMO-1', concepto: 'Alquiler oficina', estado: 'pendiente', origen: 'manual', usuario_creacion_id: USER_ID, fecha_creacion: '2026-11-10T09:00:00Z' },
];
const conciliacionesFixture = [
  {
    id: ID(985), cuenta_id: CTA[0], cuenta_nombre: 'Operativa BBVA', movimiento_esperado_id: ID(980), extracto_id: ID(1000), estado: 'sugerida', score: 92,
    regla: 'importe+concepto', diferencia_dias: 1, referencia_normalizada: null, concepto_normalizado: null, observacion: null,
    fecha_creacion: '2026-11-14T09:00:00Z', fecha_confirmacion: null, fecha_resolucion: null,
    movimiento_esperado: movimientosEsperadosFixture[0],
    extracto: { id: ID(1000), fecha: '2026-11-14', concepto: 'Alquiler oficina', monto: -1200, saldo: 48250.75, fila_numero: 60 },
  },
];

// Titulares eliminados (solo se devuelven con incluirEliminados=true; alimentan Papelera).
const titularesEliminados = [
  { id: ID(24), nombre: 'Sociedad Archivada Demo', tipo: 'EMPRESA', notas: null, fecha_creacion: '2024-02-01T10:00:00Z', cuentas_count: 0, deleted_at: '2026-11-01T10:00:00Z' },
  { id: ID(25), nombre: 'Autonomo Antiguo Demo', tipo: 'AUTONOMO', notas: null, fecha_creacion: '2024-03-01T10:00:00Z', cuentas_count: 0, deleted_at: '2026-11-05T10:00:00Z' },
];

/** Dashboard principal sin cuentas ni titulares (estado de configuracion inicial). */
export const dashboardPrincipalVacio = {
  ...dashboardPrincipal,
  saldos_por_divisa: {}, total_convertido: 0, ingresos_mes: 0, egresos_mes: 0,
  plazos_fijos: { monto_total_convertido: 0, intereses_previstos_convertidos: 0, dias_hasta_proximo_vencimiento: null, proximo_vencimiento: null, total_cuentas: 0 },
  saldos_por_titular: [], saldos_por_cuenta: [], saldos_por_pais: [], concentracion_bancos: [],
};

function paged<T>(data: T[], page = 1, pageSize = 50, extra: Record<string, unknown> = {}) {
  return {
    data: data.slice((page - 1) * pageSize, page * pageSize), total: data.length, page, page_size: pageSize,
    total_pages: Math.max(1, Math.ceil(data.length / pageSize)), ...extra,
  };
}

type Handler = (url: URL) => unknown;
const routes: Array<[RegExp, Handler]> = [
  [/^\/auth\/me$/, () => ({ usuario, permisos: [] })],
  [/^\/auth\/mfa\/trusted-devices$/, () => mfaDispositivosFixture],
  [/^\/auth\/(logout|refresh-token)$/, () => ({ csrf_token: 'x'.repeat(43) + '=', usuario, permisos: [] })],
  [/^\/alertas\/activas$/, () => [{ alerta_id: ID(701), cuenta_id: CTA[1], titular_id: TIT[0], cuenta_nombre: 'Nominas Santander', titular_nombre: titulares[0].nombre, saldo_actual: 12980.1, saldo_minimo: 15000, divisa: 'EUR' }]],
  [/^\/notificaciones-admin\/resumen$/, () => ({ exportaciones_pendientes: 1, total_pendientes: 1 })],
  [/^\/paises$/, (u) => paged(paises, 1, Number(u.searchParams.get('pageSize') ?? 500))],
  [/^\/sistema\/version-disponible$/, () => ({ version_actual: '3.2.0', version_disponible: null, actualizacion_disponible: false, instalable: false, bloqueos: [], asset_zip_nombre: null, asset_zip_detectado: false, firma_detectada: false, digest_presente: false, clave_publica_configurada: true, watchdog_disponible: true, mensaje: null })],
  [/^\/sistema\/estado$/, () => ({ estado: 'IDLE', operacion: null, mensaje: null, updated_at: null })],
  [/^\/ia\/config$/, () => iaConfig],
  [/^\/ia\/modelos$/, () => [{ id: 'openai/gpt-4o-mini', nombre: 'GPT-4o mini', context_length: 128000, permitido: true }]],
  [/^\/dashboard\/principal$/, () => dashboardPrincipal],
  [/^\/dashboard\/evolucion$/, (u) => ({ ...evolucion, periodo: u.searchParams.get('periodo') ?? '6m' })],
  [/^\/dashboard\/saldos-divisa$/, () => saldosDivisa],
  [/^\/dashboard\/titular\/.+/, () => ({ titular_id: TIT[0], titular_nombre: titulares[0].nombre, divisa_principal: 'EUR', saldos_por_divisa: dashboardPrincipal.saldos_por_divisa, ingresos_mes: 21400.5, egresos_mes: 15870.2, total_convertido: 67380, saldos_por_cuenta: saldosPorCuenta.slice(0, 3), saldos_por_pais: dashboardPrincipal.saldos_por_pais, chart_colors: chartColors })],
  [/^\/extractos\/titulares-resumen$/, () => titularesResumen],
  [/^\/extractos\/columnas-visibles$/, () => ({ columnas_visibles: null, columnas_restringidas: [] })],
  [/^\/extractos$/, (u) => paged(extractos, Number(u.searchParams.get('page') ?? 1), Number(u.searchParams.get('pageSize') ?? 200), { columnas_disponibles: ['fecha', 'concepto', 'comentarios', 'monto', 'saldo'] })],
  [/^\/extractos\/[^/]+\/desglose$/, () => ({ extracto_id: ID(1000), extracto_monto: 0, count: 0, total: 0, diferencia: 0, estado: 'sin_desglose', version: '1', lineas: [] })],
  [/^\/titulares$/, (u) => paged(u.searchParams.get('incluirEliminados') === 'true' ? [...titulares, ...titularesEliminados] : titulares, 1, Number(u.searchParams.get('pageSize') ?? 50))],
  [/^\/titulares\/[^/]+$/, () => titulares[0]],
  [/^\/cuentas\/divisas-activas$/, () => divisas.map((d) => ({ codigo: d.codigo, nombre: d.nombre }))],
  [/^\/cuentas$/, (u) => paged(cuentasBase, 1, Number(u.searchParams.get('pageSize') ?? 50))],
  [/^\/cuentas\/[^/]+$/, () => cuentasBase[0]],
  [/^\/formatos-importacion$/, () => paged([{ id: ID(801), nombre: 'BBVA CSV', banco_nombre: 'BBVA', divisa: 'EUR' }])],
  [/^\/formatos-importacion\/columnas-extra-sugeridas$/, () => ({ data: [] })],
  [/^\/importacion\/contexto$/, () => importContexto],
  [/^\/importacion\/lotes$/, (u) => {
    const cuentaId = u.searchParams.get('cuentaId');
    const filtrados = cuentaId ? lotesImportacionFixture.filter((l) => l.cuenta_id === cuentaId) : lotesImportacionFixture;
    return paged(filtrados, Number(u.searchParams.get('page') ?? 1), Number(u.searchParams.get('pageSize') ?? 20));
  }],
  [/^\/usuarios$/, () => paged(usuariosRows)],
  [/^\/usuarios\/catalogos-permisos$/, () => catalogos],
  [/^\/usuarios\/[^/]+$/, () => ({ ...usuariosRows[1], permisos: [] })],
  [/^\/backups$/, (u) => paged(backups, 1, Number(u.searchParams.get('pageSize') ?? 10))],
  [/^\/backups\/config$/, () => backupConfig],
  [/^\/backups\/google-drive\/files$/, () => ({ data: [] })],
  [/^\/configuracion$/, () => configuracion],
  [/^\/divisas$/, () => divisas],
  [/^\/tipos-cambio$/, () => tiposCambio],
  [/^\/integraciones\/tokens$/, () => paged(tokensFixture)],
  [/^\/integraciones\/tokens\/[^/]+\/metricas$/, () => ({ total_requests: 128, porcentaje_exito: 98.4, tiempo_promedio_ms: 142 })],
  [/^\/alertas$/, () => alertasFixture],
  [/^\/alertas\/contexto$/, () => ({
    cuentas: cuentasBase.map((c) => ({ id: c.id, nombre: c.nombre, titular_id: c.titular_id, titular_nombre: c.titular_nombre, divisa: c.divisa, pais_id: c.pais_id })),
    titulares: titulares.map((t) => ({ id: t.id, nombre: t.nombre, tipo: t.tipo })),
    usuarios: usuariosRows.map((u) => ({ id: u.id, nombre_completo: u.nombre_completo, email_login: u.email, email: u.email, rol: u.rol })),
  })],
  [/^\/exportaciones$/, () => paged(exportacionesFixture)],
  [/^\/conciliacion\/movimientos-esperados$/, () => movimientosEsperadosFixture],
  [/^\/conciliacion$/, () => conciliacionesFixture],
  [/^\/extractos\/[^/]+\/audit-celda$/, () => []],
];

export const unknownPaths = new Set<string>();

export interface MockApiOptions {
  /** Paths que devuelven 500 (se suman a VISUAL_FAIL). */
  fail?: string[];
  /** Respuesta fija para un path GET concreto (sustituye al fixture por defecto). */
  overrides?: Record<string, unknown>;
}

export async function installMockApi(page: Page, options: MockApiOptions = {}): Promise<void> {
  const failList = [
    ...(process.env.VISUAL_FAIL ?? '').split(',').map((s) => s.trim()).filter(Boolean),
    ...(options.fail ?? []),
  ];
  const overrides = options.overrides ?? {};

  await page.route('**/api/**', async (route: Route) => {
    const req = route.request();
    const url = new URL(req.url());
    const path = url.pathname.replace(/^\/api/, '') || '/';
    const method = req.method();

    if (failList.some((f) => path === f || path.startsWith(f + '/'))) {
      const body = JSON.stringify({ error: 'Fallo forzado por VISUAL_FAIL', message: 'Fallo forzado por VISUAL_FAIL' });
      await route.fulfill({ status: 500, contentType: 'application/json', body });
      return;
    }

    if (method !== 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '{}' });
      return;
    }

    const hit = routes.find(([re]) => re.test(path));
    let body: unknown;
    if (path in overrides) {
      body = overrides[path];
    } else if (hit) {
      body = hit[1](url);
    } else {
      unknownPaths.add(`${method} ${path}`);
      body = {};
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  });
}
