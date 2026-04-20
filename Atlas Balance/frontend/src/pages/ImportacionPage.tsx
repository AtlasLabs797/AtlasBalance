import { AxiosError } from 'axios';
import { useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { AppSelect } from '@/components/common/AppSelect';
import { SignedAmount } from '@/components/common/SignedAmount';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import { IMPORTACION_COMPLETADA_EVENT } from '@/utils/appEvents';
import type {
  ImportConfirmResult,
  ImportContextoResponse,
  ImportCuentaContexto,
  ImportMapColumns,
  ImportValidationResult,
} from '@/types';

const EFFECTIVO_MARKER = '\u2022 Efectivo';
const EMPTY_MARKER = '\u2014';
const VALID_MARKER = '\u2713';
const INVALID_MARKER = '\u2717';

type ImportStep = 1 | 2;

function getApiErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof AxiosError) {
    return (error.response?.data as { error?: string } | undefined)?.error ?? fallback;
  }

  return fallback;
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
  const returnTo = searchParams.get('returnTo') || '/dashboard';
  const usuario = useAuthStore((state) => state.usuario);
  const [step, setStep] = useState<ImportStep>(1);
  const [contexto, setContexto] = useState<ImportCuentaContexto[]>([]);
  const [loadingContext, setLoadingContext] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const [cuentaId, setCuentaId] = useState('');
  const [rawData, setRawData] = useState('');
  const [separator, setSeparator] = useState<'tab' | 'comma' | 'semicolon'>('tab');
  const [validacion, setValidacion] = useState<ImportValidationResult | null>(null);
  const [selectedRows, setSelectedRows] = useState<number[]>([]);
  const [confirmResult, setConfirmResult] = useState<ImportConfirmResult | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [closeAttempted, setCloseAttempted] = useState(false);

  useEffect(() => {
    let mounted = true;
    const load = async () => {
      setLoadingContext(true);
      setError(null);
      try {
        const { data } = await api.get<ImportContextoResponse>('/importacion/contexto');
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
        }
      } catch (err: unknown) {
        if (!mounted) {
          return;
        }

        setError(getApiErrorMessage(err, 'No se pudo cargar el contexto de importacion'));
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
  }, [preselectedCuentaId]);

  const selectedCuenta = useMemo(
    () => contexto.find((cuenta) => cuenta.id === cuentaId) ?? null,
    [contexto, cuentaId]
  );

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
    const lines = rawData
      .replace(/\r\n/g, '\n')
      .split('\n')
      .map((line) => line.trim())
      .filter((line) => line.length > 0)
      .slice(0, 3);

    return lines.map((line) => splitLine(line, separator));
  }, [rawData, separator]);

  const canValidate = Boolean(cuentaId && rawData.trim().length > 0 && selectedMapeo && hasRequiredMapeo);
  const selectedValidRowsCount = selectedRows.length;
  const canManageFormatos = usuario?.rol === 'ADMIN';
  const importAlreadyConfirmed = confirmResult !== null;

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
    setValidacion(null);
    setSelectedRows([]);
    setConfirmResult(null);
    setSuccess(null);
  };

  const setCuenta = (nextId: string) => {
    setCuentaId(nextId);
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
      setError('La cuenta seleccionada no tiene un formato de importacion activo. Asignalo en Cuentas antes de importar.');
      return;
    }

    if (!hasRequiredMapeo) {
      setError('El formato de importacion no tiene las columnas de importe requeridas. Revisalo en Formatos.');
      return;
    }

    setSubmitting(true);
    setError(null);
    setSuccess(null);
    setConfirmResult(null);

    try {
      const { data } = await api.post<ImportValidationResult>('/importacion/validar', {
        cuenta_id: cuentaId,
        raw_data: rawData,
        separador: separator,
        mapeo: selectedMapeo,
      });

      setValidacion(data);
      setSelectedRows(data.filas.filter((row) => row.valida).map((row) => row.indice));
      setStep(2);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo validar la importacion'));
    } finally {
      setSubmitting(false);
    }
  };

  const confirmImport = async () => {
    if (!validacion || importAlreadyConfirmed || !selectedMapeo) {
      return;
    }

    setSubmitting(true);
    setError(null);
    setSuccess(null);

    try {
      const { data } = await api.post<ImportConfirmResult>('/importacion/confirmar', {
        cuenta_id: cuentaId,
        raw_data: rawData,
        separador: separator,
        mapeo: selectedMapeo,
        filas_a_importar: selectedRows,
      });

      setConfirmResult(data);
      setSuccess(`Importacion completada: ${data.filas_importadas} filas importadas.`);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo confirmar la importacion'));
    } finally {
      setSubmitting(false);
    }
  };

  const startNextImport = () => {
    setRawData('');
    setSeparator('tab');
    setValidacion(null);
    setSelectedRows([]);
    setConfirmResult(null);
    setSuccess(null);
    setError(null);
    setStep(1);
  };

  if (loadingContext) {
    return (
      <section className="import-page">
        <p>Cargando configuracion de importacion...</p>
      </section>
    );
  }

  if (contexto.length === 0) {
    return (
      <section className="import-page">
        <header className="import-header">
          <h1>Importacion de Extractos</h1>
          <p>No tienes cuentas habilitadas para importar.</p>
        </header>
        {canManageFormatos && (
          <div className="import-actions">
            <Link to="/formatos-importacion">Gestionar formatos de importacion</Link>
          </div>
        )}
      </section>
    );
  }

  return (
    <section className="import-page">
      <header className="import-header">
        <h1>Importacion de Extractos</h1>
        <p>Flujo de 2 pasos: pegar datos, validar y confirmar. El mapeo sale automaticamente del formato asignado a la cuenta.</p>
        {canManageFormatos && (
          <div className="import-actions">
            <Link to="/formatos-importacion">Gestionar formatos de importacion</Link>
          </div>
        )}
      </header>

      <ol className="import-steps">
        <li className={step === 1 ? 'active' : ''}>1. Pegar</li>
        <li className={step === 2 ? 'active' : ''}>2. Validar y confirmar</li>
      </ol>

      {error && <p className="auth-error">{error}</p>}
      {success && <p className="import-success">{success}</p>}
      {autoCloseOnSuccess && importAlreadyConfirmed && !isEmbedded && (
        <p className="import-muted">Importacion confirmada. Esta pestana se cerrara automaticamente.</p>
      )}
      {autoCloseOnSuccess && importAlreadyConfirmed && !isEmbedded && closeAttempted && (
        <p className="import-muted">
          Si no se cierra sola, vuelve a <Link to={returnTo}>la cuenta</Link> y cierra esta pestana manualmente.
        </p>
      )}

      <div className="import-card">
        {step === 1 && (
          <>
            <AppSelect
              label="Cuenta destino"
              value={cuentaId}
              options={contexto.map((cuenta) => ({
                value: cuenta.id,
                label: `${cuenta.titular_nombre} / ${cuenta.nombre} (${cuenta.divisa}) ${cuenta.es_efectivo ? EFFECTIVO_MARKER : ''}`,
              }))}
              onChange={setCuenta}
            />

            <p className={selectedMapeo ? 'import-muted' : 'auth-error'}>
              {selectedMapeo
                ? `Formato automatico aplicado: ${selectedMapeo.tipo_monto === 'tres_columnas' ? 'ingreso/egreso + monto de control' : selectedMapeo.tipo_monto === 'dos_columnas' ? 'ingreso/egreso separados' : 'monto firmado'} (${selectedMapeo.columnas_extra.length} columnas extra).`
                : 'Esta cuenta no tiene formato de importacion activo. Asignalo en la ficha de cuenta antes de importar.'}
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

            <label>Datos (pegar desde Excel/CSV)</label>
            <textarea
              rows={10}
              value={rawData}
              onChange={(e) => {
                const nextRaw = e.target.value;
                setRawData(nextRaw);
                const lines = nextRaw
                  .replace(/\r\n/g, '\n')
                  .split('\n')
                  .map((line) => line.trim())
                  .filter((line) => line.length > 0);
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
              <button type="button" disabled={!canValidate || submitting} onClick={() => void validateImport()}>
                {submitting ? 'Validando...' : 'Validar datos'}
              </button>
            </div>
          </>
        )}

        {step === 2 && validacion && (
          <>
            <h3>Validar y confirmar</h3>
            <p>
              {validacion.filas_ok} filas validas, {validacion.filas_error} con errores. Separador: {validacion.separador_detectado}.
            </p>
            <ul className="import-summary">
              <li>Cuenta: {selectedCuenta ? `${selectedCuenta.titular_nombre} / ${selectedCuenta.nombre}` : EMPTY_MARKER}</li>
              <li>Filas seleccionadas para importar: {selectedValidRowsCount}</li>
            </ul>

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
                    <th>Errores</th>
                  </tr>
                </thead>
                <tbody>
                  {validacion.filas.map((row) => (
                    <tr key={`valid-${row.indice}`} className={row.valida ? '' : 'invalid'}>
                      <td>
                        {row.valida ? (
                          <input
                            type="checkbox"
                            disabled={importAlreadyConfirmed}
                            checked={selectedRows.includes(row.indice)}
                            onChange={(e) => {
                              setSelectedRows((prev) => {
                                if (e.target.checked) {
                                  return [...new Set([...prev, row.indice])];
                                }

                                return prev.filter((value) => value !== row.indice);
                              });
                            }}
                          />
                        ) : EMPTY_MARKER}
                      </td>
                      <td>{row.indice}</td>
                      <td>{row.valida ? VALID_MARKER : INVALID_MARKER}</td>
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
                      <td>{row.errores.join(' | ')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {confirmResult && (
              <div className="import-result-box">
                <p>Procesadas: {confirmResult.filas_procesadas}</p>
                <p>Importadas: {confirmResult.filas_importadas}</p>
                <p>Con error: {confirmResult.filas_con_error}</p>
              </div>
            )}

            {importAlreadyConfirmed && (
              <p className="import-muted">
                Esta importacion ya fue confirmada. Inicia una nueva para evitar duplicados.
              </p>
            )}

            <div className="import-actions">
              {!importAlreadyConfirmed && (
                <>
                  <button type="button" onClick={() => setStep(1)}>Atras</button>
                  <button
                    type="button"
                    onClick={() => void confirmImport()}
                    disabled={submitting || selectedValidRowsCount === 0}
                  >
                    {submitting ? 'Importando...' : 'Confirmar importacion'}
                  </button>
                </>
              )}
              <button type="button" onClick={startNextImport}>Nueva importacion</button>
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
    </section>
  );
}
