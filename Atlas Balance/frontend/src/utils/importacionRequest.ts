export const IMPORTACION_OPERATION_TIMEOUT_MS = 300_000;

export type ImportacionSeparator = 'tab' | 'comma' | 'semicolon';

export interface CreateImportacionLoteRequestInput {
  cuentaId: string;
  rawData: string;
  separator: ImportacionSeparator;
  mapeo: unknown;
  divisaEsperada: string | null | undefined;
  divisaCuenta: string | null | undefined;
  idempotencyKey: string;
}

export interface ConfirmImportacionLoteRequestInput {
  loteId: string;
  filasAImportar: number[];
  aceptaAdvertencias: boolean;
  forceConfirmDivisaMismatch: boolean;
  idempotencyKey: string;
}

interface ImportacionRequestConfig {
  timeout: typeof IMPORTACION_OPERATION_TIMEOUT_MS;
  headers: {
    'Idempotency-Key': string;
  };
}

function getRequestConfig(idempotencyKey: string): ImportacionRequestConfig {
  if (!idempotencyKey.trim()) {
    throw new Error('La clave de idempotencia es obligatoria.');
  }

  return {
    timeout: IMPORTACION_OPERATION_TIMEOUT_MS,
    headers: { 'Idempotency-Key': idempotencyKey },
  };
}

export function buildCreateImportacionLoteRequest(input: CreateImportacionLoteRequestInput) {
  return {
    url: '/importacion/lotes',
    body: {
      cuenta_id: input.cuentaId,
      raw_data: input.rawData,
      separador: input.separator,
      mapeo: input.mapeo,
      tipo_origen: 'PEGADO' as const,
      divisa_esperada: input.divisaEsperada || input.divisaCuenta || null,
    },
    config: getRequestConfig(input.idempotencyKey),
  };
}

export function buildConfirmImportacionLoteRequest(input: ConfirmImportacionLoteRequestInput) {
  return {
    url: `/importacion/lotes/${input.loteId}/confirmar`,
    body: {
      filas_a_importar: input.filasAImportar,
      acepta_advertencias: input.aceptaAdvertencias,
      force_confirm_divisa_mismatch: input.forceConfirmDivisaMismatch,
    },
    config: getRequestConfig(input.idempotencyKey),
  };
}
