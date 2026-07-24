import { AxiosError } from 'axios';
import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { AppSelect } from '@/components/common/AppSelect';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { DatePickerField } from '@/components/common/DatePickerField';
import { EmptyState } from '@/components/common/EmptyState';
import { SignedAmount } from '@/components/common/SignedAmount';
import { useConfirmDialog } from '@/hooks/useConfirmDialog';
import { useUnsavedChanges } from '@/hooks/useUnsavedChanges';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
import { IMPORTACION_COMPLETADA_EVENT } from '@/utils/appEvents';
import { extractErrorMessage } from '@/utils/errorMessage';
import {
  buildConfirmImportacionLoteRequest,
  buildCreateImportacionLoteRequest,
} from '@/utils/importacionRequest';
import type {
  ImportConfirmResult,
  ImportContextoResponse,
  ImportCuentaContexto,
  ImportacionLote,
  ImportacionLoteDetalle,
  ImportMapColumns,
  ImportPlazoFijoMovimientoResult,
  ImportValidationResult,
  PaginatedResponse,
} from '@/types';
import { formatCurrency, parseEuropeanNumber } from '@/utils/formatters';

const EFFECTIVO_MARKER = '\u2022 Efectivo';
const EMPTY_MARKER = '\u2014';
const PLAZO_FIJO_MARKER = '\u2022 Plazo fijo';
const DEFAULT_RETURN_TO = '/dashboard';
const PREVIEW_ROW_LIMIT = 3;
const SEPARATOR_SAMPLE_LIMIT = 5;
const VALIDATION_PAGE_SIZE = 200;

type ImportStep = 1 | 2;
type ImportTab = 'nueva' | 'historial' | 'lote';
type PlazoFijoMovimiento = 'INGRESO' | 'EGRESO';
type ImportValidationRow = ImportValidationResult['filas'][number];

function getTodayInputValue(): string {
  const now = new Date();
  const offsetMs = now.getTimezoneOffset() * 60_000;
  return new Date(now.getTime() - offsetMs).toISOString().slice(0, 10);
}

function getApiErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof AxiosError) {
    return extractErrorMessage(error, fallback);
  }

  return extractErrorMessage(error, fallback);
}

function getValidationStatusLabel(row: ImportValidationRow): string {
  if (!row.valida) {
    return 'Error bloqueante';
  }

  if (row.advertencias.length > 0) {
    return 'Aviso importable';
  }

  return 'Valida';
}

function normalizeReturnTo(value: string | null): string {
  const candidate = value?.trim();
  if (!candidate || !candidate.startsWith('/') || candidate.startsWith('//') || candidate.includes('\\')) {
    return DEFAULT_RETURN_TO;
  }

  return candidate;
}

function detectSeparator(lines: string[]): 'tab' | 'comma' | 'semicolon' {
  const sample = lines.slice(0, 5);
  const candidates: Array<{ key: 'tab' | 'comma' | 'semicolon'; char: string }> = [
    { key: 'tab', char: '\t' },
    { key: 'semicolon', char: ';' },
    { key: 'comma', char: ',' },
  ];

  let best = candidates[0];
  let bestScore = -1;
  for (const candidate of candidates) {
    const score = sample.reduce((acc, line) => acc + line.split(candidate.char).length - 1, 0);
    if (score > bestScore) {
      best = candidate;
      bestScore = score;
    }
  }

  return best.key;
}

function getNonEmptySampleLines(value: string, limit: number): string[] {
  const lines: string[] = [];
  let current = '';

  for (let index = 0; index <= value.length && lines.length < limit; index++) {
    const char = index < value.length ? value[index] : '\n';
    if (char === '\r') {
      continue;
    }

    if (char === '\n') {
      const trimmed = current.trim();
      if (trimmed.length > 0) {
        lines.push(trimmed);
      }
      current = '';
      continue;
    }

    current += char;
  }

  return lines;
}

function splitLine(line: string, separator: 'tab' | 'comma' | 'semicolon'): string[] {
  const char = separator === 'tab' ? '\t' : separator === 'semicolon' ? ';' : ',';
  const cells: string[] = [];
  let current = '';
  let inQuotes = false;

  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (ch === '"') {
      if (inQuotes && i + 1 < line.length && line[i + 1] === '"') {
        current += '"';
        i += 1;
      } else {
        inQuotes = !inQuotes;
      }
      continue;
    }

    if (!inQuotes && ch === char) {
      cells.push(current.trim());
      current = '';
      continue;
    }

    current += ch;
  }

  cells.push(current.trim());
  return cells;
}

export default function ImportacionPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const preselectedCuentaId = searchParams.get('cuentaId');
  const autoCloseOnSuccess = searchParams.get('autoClose') === '1';
  const isEmbedded = searchParams.get('embedded') === '1';
  const returnTo = normalizeReturnTo(searchParams.get('returnTo'));
  const usuario = useAuthStore((state) => state.usuario);
  const selectedPaisId = usePaisScopeStore((state) => state.selectedPaisId);
  const rawDataId = useId();
  const [step, setStep] = useState<ImportStep>(1);
  const [activeTab, setActiveTab] = useState<ImportTab>('nueva');
  const [contexto, setContexto] = useState<ImportCuentaContexto[]>([]);
  const [loadingContext, setLoadingContext] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const [cuentaId, setCuentaId] = useState('');
  const [rawData, setRawData] = useState('');
  const [separator, setSeparator] = useState<'tab' | 'comma' | 'semicolon'>('tab');
  const [validacion, setValidacion] = useState<ImportValidationResult | null>(null);
  const [currentLote, setCurrentLote] = useState<ImportacionLote | null>(null);
  const [lotes, setLotes] = useState<ImportacionLote[]>([]);
  const [loadingLotes, setLoadingLotes] = useState(false);
  const [acceptWarnings, setAcceptWarnings] = useState(false);
  // V-02.06 (HIGH-1, bloqueante): aceptacion explicita de que se quiere
  // importar un archivo cuya divisa declarada no coincide con la divisa
  // de la cuenta. Sin esto el backend rechaza con 400.
  const [forceConfirmDivisaMismatch, setForceConfirmDivisaMismatch] = useState(false);
  // V-02.06 (HIGH-1, bug 18): el POST de creacion de lote debe enviar la
  // `divisa_esperada` declarada por el operador. Antes se omitia y el
  // backend asumia la divisa de la cuenta, perdiendo la validacion contra
  // pegados accidentales de otra divisa.
  const [divisaEsperada, setDivisaEsperada] = useState('');
  const [divisasDisponibles, setDivisasDisponibles] = useState<string[]>([]);
  const [selectedRows, setSelectedRows] = useState<number[]>([]);
  const [validationPage, setValidationPage] = useState(1);
  const [confirmResult, setConfirmResult] = useState<ImportConfirmResult | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const submittingRef = useRef(false);
  const createIdempotencyKeyRef = useRef(crypto.randomUUID());
  const confirmIdempotencyKeyRef = useRef(crypto.randomUUID());
  const { confirm, dialogProps: confirmDialogProps } = useConfirmDialog();
  const [closeAttempted, setCloseAttempted] = useState(false);
  const [plazoTipoMovimiento, setPlazoTipoMovimiento] = useState<PlazoFijoMovimiento>('INGRESO');
  const [plazoMonto, setPlazoMonto] = useState('');
  const [plazoFecha, setPlazoFecha] = useState(getTodayInputValue);
  const [plazoConcepto, setPlazoConcepto] = useState('');

  useEffect(() => {
    let mounted = true;
    const load = async () => {
      setLoadingContext(true);
      setError(null);
      setStep(1);
      setValidacion(null);
      setConfirmResult(null);
      setSelectedRows([]);
      try {
        const { data } = await api.get<ImportContextoResponse>('/importacion/contexto', {
          params: { paisId: selectedPaisId || undefined },
        });
        if (!mounted) {
          return;
        }

        const cuentas = data.cuentas ?? [];
        setContexto(cuentas);
        const requestedCuenta = preselectedCuentaId
          ? cuentas.find((cuenta) => cuenta.id === preselectedCuentaId)
          : null;
        const initialCuenta = requestedCuenta ?? cuentas[0];
        if (initialCuenta) {
          setCuentaId(initialCuenta.id);
          setDivisaEsperada(initialCuenta.divisa);
        }

        // V-02.06 (HIGH-1, bug 18): cargar la lista de monedas activas para
        // alimentar el selector `Divisa de los importes`. Si el endpoint no
        // existe o falla, se cae a las divisas presentes en las cuentas
        // cargadas para no bloquear al operador.
        try {
          const { data: monedasResp } = await api.get<{ codigos?: string[] }>('/cuentas/divisas-activas');
          const payload: { codigo?: string }[] | undefined = (() => {
            if (!monedasResp || typeof monedasResp !== 'object') return undefined;
            if ('data' in monedasResp && Array.isArray(monedasResp.data)) return monedasResp.data;
            if ('codigos' in monedasResp && Array.isArray(monedasResp.codigos)) {
              return monedasResp.codigos.map((codigo) => ({ codigo }));
            }
            return undefined;
          })();
          const codigos = payload
            ?.map((entry) => entry.codigo)
            .filter((codigo): codigo is string => typeof codigo === 'string' && codigo.length > 0)
            ?? [];
          if (codigos.length > 0) {
            setDivisasDisponibles(codigos);
          } else {
            throw new Error('endpoint sin monedas');
          }
        } catch {
          const fallback = Array.from(
            new Set(
              cuentas
                .map((cuenta) => cuenta.divisa)
                .filter((codigo): codigo is string => typeof codigo === 'string' && codigo.length > 0)
            )
          );
          if (fallback.length === 0) {
            fallback.push('EUR', 'USD', 'GBP', 'ARS');
          }
          setDivisasDisponibles(fallback);
        }
      } catch (err: unknown) {
        if (!mounted) {
          return;
        }

        setError(getApiErrorMessage(err, 'No se pudo cargar el contexto de importación'));
      } finally {
        if (mounted) {
          setLoadingContext(false);
        }
      }
    };

    void load();
    return () => {
      mounted = false;
    };
  }, [preselectedCuentaId, selectedPaisId]);

  const loadLotes = useCallback(async (targetCuentaId = cuentaId) => {
    if (!targetCuentaId) {
      setLotes([]);
      return;
    }

    setLoadingLotes(true);
    try {
      const { data } = await api.get<PaginatedResponse<ImportacionLote>>('/importacion/lotes', {
        params: { cuentaId: targetCuentaId, page: 1, pageSize: 20 },
      });
      setLotes(data.data ?? []);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo cargar el historial de lotes'));
    } finally {
      setLoadingLotes(false);
    }
  }, [cuentaId]);

  useEffect(() => {
    if (!cuentaId) {
      setLotes([]);
      return;
    }

    void loadLotes(cuentaId);
  }, [cuentaId, loadLotes]);

  const selectedCuenta = useMemo(
    () => contexto.find((cuenta) => cuenta.id === cuentaId) ?? null,
    [contexto, cuentaId]
  );
  const isPlazoFijo = selectedCuenta?.tipo_cuenta === 'PLAZO_FIJO';

  const selectedMapeo = useMemo<ImportMapColumns | null>(() => {
    if (!selectedCuenta?.formato_predefinido) {
      return null;
    }

    const tipoMonto = selectedCuenta.formato_predefinido.tipo_monto ?? 'una_columna';
    return {
      ...selectedCuenta.formato_predefinido,
      tipo_monto: tipoMonto,
      columnas_extra: selectedCuenta.formato_predefinido.columnas_extra ?? [],
    };
  }, [selectedCuenta]);

  const hasRequiredMapeo = useMemo(() => {
    if (!selectedMapeo) {
      return false;
    }

    if (selectedMapeo.tipo_monto === 'dos_columnas') {
      return selectedMapeo.ingreso !== null && selectedMapeo.ingreso !== undefined
        && selectedMapeo.egreso !== null && selectedMapeo.egreso !== undefined;
    }

    if (selectedMapeo.tipo_monto === 'tres_columnas') {
      return selectedMapeo.ingreso !== null && selectedMapeo.ingreso !== undefined
        && selectedMapeo.egreso !== null && selectedMapeo.egreso !== undefined
        && selectedMapeo.monto !== null && selectedMapeo.monto !== undefined;
    }

    return selectedMapeo.monto !== null && selectedMapeo.monto !== undefined;
  }, [selectedMapeo]);

  const previewRows = useMemo(() => {
    const lines = getNonEmptySampleLines(rawData, PREVIEW_ROW_LIMIT);
    return lines.map((line) => splitLine(line, separator));
  }, [rawData, separator]);

  const canValidate = Boolean(!isPlazoFijo && cuentaId && rawData.trim().length > 0 && selectedMapeo && hasRequiredMapeo);
  const plazoMontoNumber = useMemo(() => parseEuropeanNumber(plazoMonto), [plazoMonto]);
  const canSubmitPlazoFijo = Boolean(isPlazoFijo && cuentaId && plazoFecha && plazoMontoNumber !== null && plazoMontoNumber > 0);
  const selectedValidRowsCount = selectedRows.length;
  const selectedRowsSet = useMemo(() => new Set(selectedRows), [selectedRows]);
  const validationWarningsCount = useMemo(
    () => validacion?.filas.reduce((total, row) => total + (row.advertencias.length > 0 ? 1 : 0), 0) ?? 0,
    [validacion],
  );
  const selectedWarningRowsCount = useMemo(
    () => validacion?.filas.reduce(
      (total, row) => total + (selectedRowsSet.has(row.indice) && row.advertencias.length > 0 ? 1 : 0),
      0,
    ) ?? 0,
    [selectedRowsSet, validacion],
  );
  const validationTotalPages = validacion
    ? Math.max(1, Math.ceil(validacion.filas.length / VALIDATION_PAGE_SIZE))
    : 1;
  const validationPageRows = useMemo(() => {
    if (!validacion) {
      return [];
    }

    const safePage = Math.min(Math.max(validationPage, 1), validationTotalPages);
    const startIndex = (safePage - 1) * VALIDATION_PAGE_SIZE;
    return validacion.filas.slice(startIndex, startIndex + VALIDATION_PAGE_SIZE);
  }, [validacion, validationPage, validationTotalPages]);
  const validationPageStart = validacion && validacion.filas.length > 0
    ? (Math.min(Math.max(validationPage, 1), validationTotalPages) - 1) * VALIDATION_PAGE_SIZE + 1
    : 0;
  const validationPageEnd = validacion
    ? Math.min(validationPageStart + validationPageRows.length - 1, validacion.filas.length)
    : 0;
  const canManageFormatos = usuario?.rol === 'ADMIN';
  const importAlreadyConfirmed = confirmResult !== null;
  // Aviso al refrescar/cerrar el navegador con datos pegados sin confirmar.
  useUnsavedChanges(rawData.trim().length > 0 && !importAlreadyConfirmed);

  useEffect(() => {
    if (!autoCloseOnSuccess || !importAlreadyConfirmed || !cuentaId) {
      return;
    }

    const payload = {
      type: IMPORTACION_COMPLETADA_EVENT,
      cuentaId,
    };

    if (isEmbedded && window.parent && window.parent !== window) {
      window.parent.postMessage(payload, window.location.origin);
      return;
    }

    if (window.opener && !window.opener.closed) {
      window.opener.postMessage(payload, window.location.origin);
    }

    setCloseAttempted(true);
    const closeTimer = window.setTimeout(() => {
      window.close();
    }, 1000);

    return () => {
      window.clearTimeout(closeTimer);
    };
  }, [autoCloseOnSuccess, cuentaId, importAlreadyConfirmed, isEmbedded]);

  const resetValidationState = () => {
    // Una edicion crea una operacion distinta. Conservamos la clave solo
    // para reintentar exactamente la misma peticion tras un timeout.
    createIdempotencyKeyRef.current = crypto.randomUUID();
    confirmIdempotencyKeyRef.current = crypto.randomUUID();
    setValidacion(null);
    setCurrentLote(null);
    setAcceptWarnings(false);
    setForceConfirmDivisaMismatch(false);
    setSelectedRows([]);
    setValidationPage(1);
    setConfirmResult(null);
    setSuccess(null);
  };

  const setCuenta = (nextId: string) => {
    setCuentaId(nextId);
    const nextCuenta = contexto.find((cuenta) => cuenta.id === nextId);
    if (nextCuenta) {
      setDivisaEsperada(nextCuenta.divisa);
    } else {
      setDivisaEsperada('');
    }
    resetValidationState();
    setStep(1);
    const nextParams = new URLSearchParams(searchParams);
    nextParams.set('cuentaId', nextId);
    setSearchParams(nextParams, { replace: true });
  };

  const validateImport = async () => {
    if (!cuentaId) {
      setError('Selecciona una cuenta antes de validar.');
      return;
    }

    if (!rawData.trim()) {
      setError('Pega datos para validar.');
      return;
    }

    if (!selectedMapeo) {
      setError('La cuenta seleccionada no tiene un formato de importación activo. Asígnalo en Cuentas antes de importar.');
      return;
    }

    if (!hasRequiredMapeo) {
      setError('El formato de importación no tiene las columnas de importe requeridas. Revísalo en Formatos.');
      return;
    }

    setSubmitting(true);
    setError(null);
    setSuccess(null);
    setConfirmResult(null);

    try {
      const request = buildCreateImportacionLoteRequest({
        cuentaId,
        rawData,
        separator,
        mapeo: selectedMapeo,
        divisaEsperada,
        divisaCuenta: selectedCuenta?.divisa,
        idempotencyKey: createIdempotencyKeyRef.current,
      });
      const { data } = await api.post<ImportacionLoteDetalle>(request.url, request.body, request.config);

      setCurrentLote(data.lote);
      setValidacion(data.validacion);
      setSelectedRows(data.validacion.filas.filter((row) => row.valida && row.advertencias.length === 0).map((row) => row.indice));
      setAcceptWarnings(false);
      setForceConfirmDivisaMismatch(false);
      setValidationPage(1);
      setActiveTab('lote');
      setStep(2);
      await loadLotes(cuentaId);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo validar la importación'));
    } finally {
      setSubmitting(false);
    }
  };

  const confirmImport = async () => {
    if (!validacion || !currentLote || importAlreadyConfirmed || submittingRef.current) {
      return;
    }

    if (selectedWarningRowsCount > 0 && !acceptWarnings) {
      setError('Hay filas seleccionadas con avisos. Marca la aceptación explícita para confirmarlas.');
      return;
    }

    // V-02.06 (HIGH-1, bloqueante): si el backend marco el lote con
    // `divisa_mismatch`, exigimos aceptacion explicita via checkbox
    // antes de enviar `force_confirm_divisa_mismatch: true`.
    if (currentLote.divisa_mismatch && !forceConfirmDivisaMismatch) {
      setError(
        `La divisa declarada (${currentLote.divisa_esperada ?? '?'}) no coincide con la divisa de la cuenta (${currentLote.divisa_cuenta}). Marca la confirmación explícita para importar de todas formas.`,
      );
      return;
    }

    const confirmed = await confirm({
      title: 'Confirmar importación',
      message: `Se importarán ${selectedValidRowsCount} ${selectedValidRowsCount === 1 ? 'fila' : 'filas'} a la cuenta seleccionada. Esta acción escribe movimientos en el extracto. ¿Continuar?`,
      confirmLabel: 'Importar',
    });
    if (!confirmed) {
      return;
    }

    submittingRef.current = true;
    setSubmitting(true);
    setError(null);
    setSuccess(null);

    try {
      const request = buildConfirmImportacionLoteRequest({
        loteId: currentLote.id,
        filasAImportar: selectedRows,
        aceptaAdvertencias: acceptWarnings,
        forceConfirmDivisaMismatch,
        idempotencyKey: confirmIdempotencyKeyRef.current,
      });
      const { data } = await api.post<ImportConfirmResult>(request.url, request.body, request.config);

      setConfirmResult(data);
      setSuccess(`Importación completada: ${data.filas_importadas} filas importadas.`);
      await loadLotes(cuentaId);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo confirmar la importación'));
    } finally {
      submittingRef.current = false;
      setSubmitting(false);
    }
  };

  const startNextImport = () => {
    setRawData('');
    setSeparator('tab');
    setValidacion(null);
    setCurrentLote(null);
    setAcceptWarnings(false);
    setForceConfirmDivisaMismatch(false);
    setSelectedRows([]);
    setValidationPage(1);
    setConfirmResult(null);
    setSuccess(null);
    setError(null);
    setStep(1);
    setActiveTab('nueva');
    createIdempotencyKeyRef.current = crypto.randomUUID();
    confirmIdempotencyKeyRef.current = crypto.randomUUID();
    // V-02.06 (HIGH-1, bug 18): al iniciar una nueva importacion se
    // reinicia el selector de divisa a la divisa de la cuenta actual.
    setDivisaEsperada(selectedCuenta?.divisa ?? '');
  };

  const submitPlazoFijoMovimiento = async () => {
    if (!selectedCuenta || !isPlazoFijo) {
      return;
    }

    const monto = plazoMontoNumber;
    if (monto === null || monto <= 0) {
      setError('Introduce un monto mayor que cero.');
      return;
    }

    setSubmitting(true);
    setError(null);
    setSuccess(null);
    setConfirmResult(null);

    try {
      const { data } = await api.post<ImportPlazoFijoMovimientoResult>('/importacion/plazo-fijo/movimiento', {
        cuenta_id: cuentaId,
        tipo_movimiento: plazoTipoMovimiento,
        fecha: plazoFecha,
        monto,
        concepto: plazoConcepto,
      });

      setSuccess(
        `Movimiento registrado. Saldo actual: ${formatCurrency(data.saldo_actual, selectedCuenta.divisa)}.`
      );
      setPlazoMonto('');
      setPlazoConcepto('');

      const payload = {
        type: IMPORTACION_COMPLETADA_EVENT,
        cuentaId,
      };
      if (isEmbedded && window.parent && window.parent !== window) {
        window.parent.postMessage(payload, window.location.origin);
      } else if (window.opener && !window.opener.closed) {
        window.opener.postMessage(payload, window.location.origin);
      }
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo registrar el movimiento del plazo fijo'));
    } finally {
      setSubmitting(false);
    }
  };

  if (loadingContext) {
    return (
      <section className="import-page">
        <p>Cargando configuración de importación...</p>
      </section>
    );
  }

  if (error && contexto.length === 0) {
    return (
      <section className="import-page">
        <header className="import-header">
          <h1>Importación de extractos</h1>
        </header>
        <EmptyState
          variant="error"
          title="No se pudo cargar la importación."
          subtitle={error}
          primaryAction={<button type="button" onClick={() => window.location.reload()}>Reintentar</button>}
        />
      </section>
    );
  }

  if (contexto.length === 0) {
    return (
      <section className="import-page">
        <header className="import-header">
          <h1>Importación de extractos</h1>
          <p>No tienes cuentas habilitadas para importar.</p>
        </header>
        {canManageFormatos && (
          <div className="import-actions">
            <Link to="/formatos-importacion">Gestionar formatos de importación</Link>
          </div>
        )}
      </section>
    );
  }

  return (
    <section className="import-page">
      <header className="import-header">
        <h1>Importación de extractos</h1>
        <p>Las cuentas normales y de efectivo usan formato de importación. Las de plazo fijo solo permiten añadir o sacar dinero.</p>
        {canManageFormatos && (
          <div className="import-actions">
            <Link to="/formatos-importacion">Gestionar formatos de importación</Link>
          </div>
        )}
      </header>

      <div className="import-tabs" role="tablist" aria-label="Secciones de importacion">
        <button
          type="button"
          role="tab"
          aria-selected={activeTab === 'nueva'}
          className={activeTab === 'nueva' ? 'active' : ''}
          onClick={() => setActiveTab('nueva')}
        >
          Nueva
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={activeTab === 'historial'}
          className={activeTab === 'historial' ? 'active' : ''}
          onClick={() => setActiveTab('historial')}
        >
          Historial
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={activeTab === 'lote'}
          className={activeTab === 'lote' ? 'active' : ''}
          disabled={!currentLote}
          onClick={() => setActiveTab('lote')}
        >
          Lote
        </button>
      </div>

      {activeTab !== 'historial' && (
      <ol className="import-steps">
        <li className={step === 1 ? 'active' : ''}>1. Pegar</li>
        <li className={step === 2 ? 'active' : ''}>2. Validar y confirmar</li>
      </ol>
      )}

      {error && <p className="auth-error" role="alert">{error}</p>}
      {success && <p className="import-success" role="status">{success}</p>}
      {autoCloseOnSuccess && importAlreadyConfirmed && !isEmbedded && (
        <p className="import-muted" role="status">Importación confirmada. Esta pestaña se cerrará automáticamente.</p>
      )}
      {autoCloseOnSuccess && importAlreadyConfirmed && !isEmbedded && closeAttempted && (
        <p className="import-muted">
          Si no se cierra sola, vuelve a <Link to={returnTo}>la cuenta</Link> y cierra esta pestaña manualmente.
        </p>
      )}

      {activeTab === 'historial' ? (
        <div className="import-card">
          <div className="import-history-header">
            <h3>Historial de lotes</h3>
            <button type="button" className="button-secondary" onClick={() => void loadLotes()}>
              {loadingLotes ? 'Actualizando...' : 'Actualizar'}
            </button>
          </div>
          {lotes.length === 0 ? (
            <p className="import-muted">{loadingLotes ? 'Cargando lotes...' : 'No hay lotes para esta cuenta.'}</p>
          ) : (
            <div className="import-validation-table-wrap">
              <table className="import-validation-table">
                <thead>
                  <tr>
                    <th>Fecha</th>
                    <th>Cuenta</th>
                    <th>Origen</th>
                    <th>Estado</th>
                    <th>Filas</th>
                    <th>SHA-256</th>
                    <th>Accion</th>
                  </tr>
                </thead>
                <tbody>
                  {lotes.map((lote) => (
                    <tr key={lote.id}>
                      <td>{new Date(lote.fecha_creacion).toLocaleString()}</td>
                      <td>{lote.cuenta_nombre ?? EMPTY_MARKER}</td>
                      <td>{lote.tipo_origen}{lote.nombre_archivo ? ` / ${lote.nombre_archivo}` : ''}</td>
                      <td>{lote.estado}</td>
                      <td>{lote.filas_validas}/{lote.filas_total}</td>
                      <td><code>{lote.sha256.slice(0, 12)}</code></td>
                      <td>
                        <button
                          type="button"
                          className="button-secondary"
                          onClick={async () => {
                            setSubmitting(true);
                            setError(null);
                            try {
                              const { data } = await api.get<ImportacionLoteDetalle>(`/importacion/lotes/${lote.id}`);
                              setCurrentLote(data.lote);
                              setValidacion(data.validacion);
                              setSelectedRows(data.validacion.filas.filter((row) => row.valida && row.advertencias.length === 0).map((row) => row.indice));
                              setAcceptWarnings(false);
                              setForceConfirmDivisaMismatch(false);
                              setConfirmResult(null);
                              setStep(2);
                              setActiveTab('lote');
                            } catch (err: unknown) {
                              setError(getApiErrorMessage(err, 'No se pudo abrir el lote'));
                            } finally {
                              setSubmitting(false);
                            }
                          }}
                        >
                          Abrir
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      ) : (
      <div className="import-card">
        {step === 1 && (
          <>
            <AppSelect
              label="Cuenta destino"
              value={cuentaId}
              options={contexto.map((cuenta) => ({
                value: cuenta.id,
                label: `${cuenta.titular_nombre} / ${cuenta.nombre} (${cuenta.divisa}) ${cuenta.tipo_cuenta === 'PLAZO_FIJO' ? PLAZO_FIJO_MARKER : cuenta.es_efectivo ? EFFECTIVO_MARKER : ''}`,
              }))}
              onChange={setCuenta}
            />

            {isPlazoFijo ? (
              <>
                <p className="import-muted">
                  Esta cuenta es de plazo fijo: registra solo entradas o salidas de dinero, sin CSV, Excel ni formato de banco.
                </p>

                <div className="import-plazo-grid">
                  <AppSelect
                    label="Movimiento"
                    value={plazoTipoMovimiento}
                    options={[
                      { value: 'INGRESO', label: 'Anadir dinero' },
                      { value: 'EGRESO', label: 'Sacar dinero' },
                    ]}
                    onChange={(next) => setPlazoTipoMovimiento(next as PlazoFijoMovimiento)}
                  />

                  <div className="date-field">
                    <span>Fecha</span>
                    <DatePickerField
                      ariaLabel="Fecha del movimiento"
                      value={plazoFecha}
                      onChange={setPlazoFecha}
                    />
                  </div>

                  <label>
                    Monto
                    <input
                      inputMode="decimal"
                      value={plazoMonto}
                      onChange={(event) => setPlazoMonto(event.target.value)}
                      placeholder="0,00"
                    />
                  </label>
                </div>

                <label>
                  Concepto
                  <input
                    type="text"
                    value={plazoConcepto}
                    onChange={(event) => setPlazoConcepto(event.target.value)}
                    placeholder={plazoTipoMovimiento === 'INGRESO' ? 'Entrada plazo fijo' : 'Salida plazo fijo'}
                  />
                </label>

                <div className="import-actions">
                  <button
                    type="button"
                    className="button-primary"
                    disabled={!canSubmitPlazoFijo || submitting}
                    onClick={() => void submitPlazoFijoMovimiento()}
                  >
                    {submitting ? 'Guardando...' : 'Registrar movimiento'}
                  </button>
                </div>
              </>
            ) : (
              <>
            <p className={selectedMapeo ? 'import-muted' : 'auth-error'} role={selectedMapeo ? 'status' : 'alert'}>
              {selectedMapeo
                ? `Formato automatico aplicado: ${selectedMapeo.tipo_monto === 'tres_columnas' ? 'ingreso/egreso + monto de control' : selectedMapeo.tipo_monto === 'dos_columnas' ? 'ingreso/egreso separados' : 'monto firmado'} (${selectedMapeo.columnas_extra.length} columnas extra).`
                : 'Esta cuenta no tiene formato de importación activo. Asígnalo en la ficha de cuenta antes de importar.'}
            </p>

            <AppSelect
              label="Separador detectado/seleccionado"
              value={separator}
              options={[
                { value: 'tab', label: 'Tabulador' },
                { value: 'comma', label: 'Coma' },
                { value: 'semicolon', label: 'Punto y coma' },
              ]}
              onChange={(next) => {
                setSeparator(next as 'tab' | 'comma' | 'semicolon');
                resetValidationState();
              }}
            />

            <AppSelect
              label="Divisa de los importes"
              value={divisaEsperada || selectedCuenta?.divisa || ''}
              options={divisasDisponibles.map((codigo) => ({
                value: codigo,
                label: `${codigo}${selectedCuenta?.divisa === codigo ? ' (divisa de la cuenta)' : ''}`,
              }))}
              onChange={(next) => {
                setDivisaEsperada(next);
                resetValidationState();
              }}
            />

            <label htmlFor={rawDataId}>Datos (pegar desde Excel/CSV)</label>
            <textarea
              id={rawDataId}
              rows={10}
              value={rawData}
              onChange={(e) => {
                const nextRaw = e.target.value;
                setRawData(nextRaw);
                const lines = getNonEmptySampleLines(nextRaw, SEPARATOR_SAMPLE_LIMIT);
                if (lines.length > 0) {
                  setSeparator(detectSeparator(lines));
                }
                resetValidationState();
              }}
              placeholder={
                selectedMapeo?.tipo_monto === 'tres_columnas'
                  ? 'Ejemplo:\n01/04/2026\tVenta factura 123\t1200,50\t\t1200,50\t3000,25\n02/04/2026\tPago proveedor\t\t250,00\t250,00\t2750,25'
                  : selectedMapeo?.tipo_monto === 'dos_columnas'
                    ? 'Ejemplo:\n01/04/2026\tVenta factura 123\t1200,50\t\t3000,25\n02/04/2026\tPago proveedor\t\t250,00\t2750,25'
                  : 'Ejemplo:\n01/04/2026\tVenta factura 123\t1200,50\t3000,25'
              }
            />

            <h3>Preview (primeras 3 filas)</h3>
            <div className="import-preview-grid">
              {previewRows.length === 0 ? (
                <p className="import-muted">Aun no hay datos pegados.</p>
              ) : (
                <table>
                  <tbody>
                    {previewRows.map((row, index) => (
                      <tr key={`preview-${index}`}>
                        {row.map((cell, idx) => (
                          <td key={`preview-${index}-${idx}`}>{cell}</td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>

            <div className="import-actions">
              <button type="button" className="button-primary" disabled={!canValidate || submitting} onClick={() => void validateImport()}>
                {submitting ? 'Validando...' : 'Validar datos'}
              </button>
            </div>
              </>
            )}
          </>
        )}

        {step === 2 && validacion && (
          <>
            <h3>Validar y confirmar</h3>
            <div className="import-result-box import-validation-summary" role="status">
              <div>
                <strong>{validacion.filas_ok}</strong>
                <span>Filas validas</span>
              </div>
              <div>
                <strong>{validacion.filas_error}</strong>
                <span>Errores bloqueantes</span>
              </div>
              <div>
                <strong>{validationWarningsCount}</strong>
                <span>Avisos</span>
              </div>
              <div>
                <strong>{selectedValidRowsCount}</strong>
                <span>Seleccionadas</span>
              </div>
            </div>
            <p className="import-muted">
              Cuenta: {selectedCuenta ? `${selectedCuenta.titular_nombre} / ${selectedCuenta.nombre}` : EMPTY_MARKER}. Separador: {validacion.separador_detectado}.
            </p>
            {currentLote && (
              <p className="import-muted">
                Lote: <code>{currentLote.lote_hash.slice(0, 12)}</code> · SHA-256 <code>{currentLote.sha256.slice(0, 12)}</code> · {currentLote.estado}
              </p>
            )}

            <div className="import-validation-table-wrap">
              <table className="import-validation-table">
                <thead>
                  <tr>
                    <th>Importar</th>
                    <th>Fila</th>
                    <th>Estado</th>
                    <th>Fecha</th>
                    <th>Concepto</th>
                    {(selectedMapeo?.tipo_monto === 'dos_columnas' || selectedMapeo?.tipo_monto === 'tres_columnas') && <th>Ingreso</th>}
                    {(selectedMapeo?.tipo_monto === 'dos_columnas' || selectedMapeo?.tipo_monto === 'tres_columnas') && <th>Egreso</th>}
                    {selectedMapeo?.tipo_monto === 'tres_columnas' && <th>Monto banco</th>}
                    <th>Monto</th>
                    <th>Saldo</th>
                    <th>Errores / avisos</th>
                  </tr>
                </thead>
                <tbody>
                  {validationPageRows.map((row) => (
                    <tr
                      key={`valid-${row.indice}`}
                      className={!row.valida ? 'invalid' : row.advertencias.length > 0 ? 'warning' : ''}
                    >
                      <td>
                        {row.valida ? (
                          <input
                            type="checkbox"
                            aria-label={`Importar fila ${row.indice}`}
                            disabled={importAlreadyConfirmed}
                            checked={selectedRowsSet.has(row.indice)}
                            onChange={(e) => {
                              setSelectedRows((prev) => {
                                if (e.target.checked) {
                                  const next = new Set(prev);
                                  next.add(row.indice);
                                  return [...next];
                                }

                                return prev.filter((value) => value !== row.indice);
                              });
                            }}
                          />
                        ) : EMPTY_MARKER}
                      </td>
                      <td>{row.indice}</td>
                      <td>{getValidationStatusLabel(row)}</td>
                      <td>{row.datos.fecha ?? ''}</td>
                      <td>{row.datos.concepto ?? ''}</td>
                      {(selectedMapeo?.tipo_monto === 'dos_columnas' || selectedMapeo?.tipo_monto === 'tres_columnas') && <td>{row.datos.ingreso ?? ''}</td>}
                      {(selectedMapeo?.tipo_monto === 'dos_columnas' || selectedMapeo?.tipo_monto === 'tres_columnas') && <td>{row.datos.egreso ?? ''}</td>}
                      {selectedMapeo?.tipo_monto === 'tres_columnas' && <td>{row.datos.monto_banco ?? ''}</td>}
                      <td>
                        {row.datos.monto !== null && row.datos.monto !== undefined && row.datos.monto !== '' ? (
                          <SignedAmount value={row.datos.monto}>{String(row.datos.monto)}</SignedAmount>
                        ) : (
                          ''
                        )}
                      </td>
                      <td>
                        {row.datos.saldo !== null && row.datos.saldo !== undefined && row.datos.saldo !== '' ? (
                          <SignedAmount value={row.datos.saldo}>{String(row.datos.saldo)}</SignedAmount>
                        ) : (
                          ''
                        )}
                      </td>
                      <td>{[...row.errores, ...row.advertencias].join(' | ')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {validacion.filas.length > VALIDATION_PAGE_SIZE && (
              <div className="users-pagination">
                <button
                  type="button"
                  onClick={() => setValidationPage((page) => Math.max(1, page - 1))}
                  disabled={validationPage <= 1}
                >
                  Anterior
                </button>
                <span>
                  Filas {validationPageStart}-{validationPageEnd} / {validacion.filas.length}
                </span>
                <button
                  type="button"
                  onClick={() => setValidationPage((page) => Math.min(validationTotalPages, page + 1))}
                  disabled={validationPage >= validationTotalPages}
                >
                  Siguiente
                </button>
              </div>
            )}

            {selectedWarningRowsCount > 0 && !importAlreadyConfirmed && (
              <label className="import-warning-accept">
                <input
                  type="checkbox"
                  checked={acceptWarnings}
                  onChange={(event) => setAcceptWarnings(event.target.checked)}
                />
                Acepto importar {selectedWarningRowsCount} fila{selectedWarningRowsCount === 1 ? '' : 's'} con avisos.
              </label>
            )}

            {currentLote?.divisa_mismatch === true && !importAlreadyConfirmed && (
              <div className="import-warning-accept" role="alert">
                <p className="auth-error" style={{ margin: '0 0 0.5rem 0' }}>
                  Aviso de divisa: la cuenta destino opera en{' '}
                  <strong>{currentLote.divisa_cuenta}</strong> pero declaraste pegar datos en{' '}
                  <strong>{currentLote.divisa_esperada ?? '?'}</strong>. Si confirmas, los importes quedaran registrados con tu declaracion.
                </p>
                <label style={{ display: 'block', marginTop: '0.25rem' }}>
                  <input
                    type="checkbox"
                    checked={forceConfirmDivisaMismatch}
                    onChange={(event) => setForceConfirmDivisaMismatch(event.target.checked)}
                  />
                  Confirmo que quiero importar este archivo en {currentLote.divisa_esperada ?? '?'} aunque la cuenta sea {currentLote.divisa_cuenta}.
                </label>
              </div>
            )}

            {confirmResult && (
              <div className="import-result-box">
                <p>Procesadas: {confirmResult.filas_procesadas}</p>
                <p>Importadas: {confirmResult.filas_importadas}</p>
                <p>Duplicadas: {confirmResult.filas_duplicadas}</p>
                <p>Con error: {confirmResult.filas_con_error}</p>
                {confirmResult.advertencias.length > 0 && (
                  <p>{confirmResult.advertencias.join(' ')}</p>
                )}
              </div>
            )}

            {importAlreadyConfirmed && (
              <p className="import-muted">
                Esta importación ya fue confirmada. Inicia una nueva para evitar duplicados.
              </p>
            )}

            <div className="import-actions">
              {!importAlreadyConfirmed && (
                <>
                  <button type="button" className="button-secondary" onClick={() => setStep(1)}>Atras</button>
                  <button
                    type="button"
                    className="button-primary"
                    onClick={() => void confirmImport()}
                    disabled={submitting || selectedValidRowsCount === 0}
                  >
                    {submitting ? 'Importando...' : 'Confirmar importación'}
                  </button>
                </>
              )}
              <button type="button" className="button-secondary" onClick={startNextImport}>Nueva importación</button>
            </div>
          </>
        )}

        {step === 2 && !validacion && (
          <>
            <p className="import-muted">Primero valida los datos pegados.</p>
            <div className="import-actions">
              <button type="button" onClick={() => setStep(1)}>Volver</button>
            </div>
          </>
        )}
      </div>
      )}
      <ConfirmDialog {...confirmDialogProps} />
    </section>
  );
}
