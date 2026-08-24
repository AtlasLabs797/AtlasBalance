import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AxiosError } from 'axios';
import { AppSelect } from '@/components/common/AppSelect';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { DatePickerField } from '@/components/common/DatePickerField';
import { EmptyState } from '@/components/common/EmptyState';
import { SignedAmount } from '@/components/common/SignedAmount';
import { useConfirmDialog } from '@/hooks/useConfirmDialog';
import { useInvalidateAfterMutation } from '@/hooks/queries/useInvalidateAfterMutation';
import api from '@/services/api';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatCurrency, parseEuropeanNumber } from '@/utils/formatters';
import type { Conciliacion, ImportContextoResponse, ImportCuentaContexto, MovimientoEsperado } from '@/types';

function today(): string {
  const now = new Date();
  const offsetMs = now.getTimezoneOffset() * 60_000;
  return new Date(now.getTime() - offsetMs).toISOString().slice(0, 10);
}

export default function ConciliacionPage() {
  const selectedPaisId = usePaisScopeStore((state) => state.selectedPaisId);
  const invalidate = useInvalidateAfterMutation();
  const [cuentas, setCuentas] = useState<ImportCuentaContexto[]>([]);
  const [cuentaId, setCuentaId] = useState('');
  const [movimientos, setMovimientos] = useState<MovimientoEsperado[]>([]);
  const [conciliaciones, setConciliaciones] = useState<Conciliacion[]>([]);
  const [fecha, setFecha] = useState(today);
  const [monto, setMonto] = useState('');
  const [referencia, setReferencia] = useState('');
  const [concepto, setConcepto] = useState('');
  const [ventanaDias, setVentanaDias] = useState(3);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const { confirm, dialogProps: confirmDialogProps } = useConfirmDialog();
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const selectedCuenta = useMemo(
    () => cuentas.find((cuenta) => cuenta.id === cuentaId) ?? null,
    [cuentaId, cuentas],
  );
  const montoNumber = useMemo(() => parseEuropeanNumber(monto), [monto]);

  // V-02.08: guard anti-carrera + gestion de error propia. Antes loadData
  // propagaba la excepcion y los callers `void loadData(...)` producian un
  // unhandled rejection sin feedback, dejando en pantalla los datos de la
  // cuenta ANTERIOR bajo la cuenta recien seleccionada.
  const loadDataRequestIdRef = useRef(0);

  const loadData = useCallback(async (targetCuentaId?: string) => {
    const requestId = ++loadDataRequestIdRef.current;
    setError(null);
    const params = targetCuentaId ? { cuentaId: targetCuentaId } : undefined;
    try {
      const [movimientosResponse, conciliacionesResponse] = await Promise.all([
        api.get<MovimientoEsperado[]>('/conciliacion/movimientos-esperados', { params }),
        api.get<Conciliacion[]>('/conciliacion', { params }),
      ]);
      if (requestId !== loadDataRequestIdRef.current) return;
      setMovimientos(movimientosResponse.data);
      setConciliaciones(conciliacionesResponse.data);
    } catch (err: unknown) {
      if (requestId !== loadDataRequestIdRef.current) return;
      setError(extractErrorMessage(err, 'No se pudo actualizar la conciliacion.'));
      // Sin esto, si falla la carga de la cuenta nueva, movimientos y
      // conciliaciones de la cuenta anterior quedan visibles con botones de
      // accion usables bajo el cuentaId ya actualizado.
      setMovimientos([]);
      setConciliaciones([]);
      throw err;
    }
  }, []);

  useEffect(() => {
    let mounted = true;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const { data } = await api.get<ImportContextoResponse>('/importacion/contexto', {
          params: { paisId: selectedPaisId || undefined },
        });
        if (!mounted) return;
        const nextCuentas = data.cuentas ?? [];
        setCuentas(nextCuentas);
        const initialCuenta = nextCuentas[0]?.id ?? '';
        setCuentaId(initialCuenta);
        await loadData(initialCuenta);
      } catch (err: unknown) {
        if (mounted) setError(extractErrorMessage(err, 'No se pudo cargar conciliacion.'));
      } finally {
        if (mounted) setLoading(false);
      }
    };

    void load();
    return () => {
      mounted = false;
    };
  }, [loadData, selectedPaisId]);

  const createMovimiento = async () => {
    if (!selectedCuenta || !fecha || montoNumber === null || montoNumber === 0) {
      setError('Selecciona cuenta, fecha e importe distinto de cero.');
      return;
    }

    setSubmitting(true);
    setError(null);
    setSuccess(null);
    try {
      await api.post('/conciliacion/movimientos-esperados', {
        cuenta_id: selectedCuenta.id,
        fecha_esperada: fecha,
        monto: montoNumber,
        divisa: selectedCuenta.divisa,
        referencia,
        concepto,
        origen: 'manual',
      });
      setMonto('');
      setReferencia('');
      setConcepto('');
      setSuccess('Movimiento esperado creado.');
      await loadData(selectedCuenta.id);
      await invalidate('conciliacion');
    } catch (err: unknown) {
      setError(extractErrorMessage(err, 'No se pudo crear el movimiento esperado.'));
    } finally {
      setSubmitting(false);
    }
  };

  const sugerir = async () => {
    setSubmitting(true);
    setError(null);
    setSuccess(null);
    try {
      const { data } = await api.post('/conciliacion/sugerir', {
        cuenta_id: cuentaId || null,
        ventana_dias: ventanaDias,
      });
      setSuccess(`Sugerencias creadas: ${data.sugerencias_creadas ?? 0}.`);
      await loadData(cuentaId);
      await invalidate('conciliacion');
    } catch (err: unknown) {
      setError(extractErrorMessage(err, 'No se pudieron generar sugerencias.'));
    } finally {
      setSubmitting(false);
    }
  };

  const cambiarEstado = async (id: string, action: 'confirmar' | 'excepcion' | 'resolver') => {
    if (action === 'confirmar') {
      const confirmed = await confirm({
        title: 'Conciliar movimiento',
        message: 'Vas a conciliar este movimiento con su extracto. Es una acción de cierre contable. ¿Continuar?',
        confirmLabel: 'Conciliar',
      });
      if (!confirmed) {
        return;
      }
    }

    setSubmitting(true);
    setError(null);
    setSuccess(null);
    try {
      await api.post(`/conciliacion/${id}/${action}`, {});
      setSuccess(action === 'confirmar' ? 'Match conciliado.' : action === 'resolver' ? 'Excepcion resuelta.' : 'Marcado como excepcion.');
      await loadData(cuentaId);
      await invalidate('conciliacion');
    } catch (err: unknown) {
      setError(extractErrorMessage(err, 'No se pudo actualizar la conciliacion.'));
      // V-02.08: tras un 409 otro usuario gano el match; resincroniza para no
      // seguir mostrando la fila como pendiente (patron de ExtractosPage).
      if (err instanceof AxiosError && err.response?.status === 409) {
        await loadData(cuentaId).catch(() => undefined);
      }
    } finally {
      setSubmitting(false);
    }
  };

  if (loading) {
    return <section className="conciliacion-page"><p>Cargando conciliacion...</p></section>;
  }

  if (error && cuentas.length === 0) {
    return (
      <section className="conciliacion-page">
        <EmptyState variant="error" title="No se pudo cargar conciliacion." subtitle={error} />
      </section>
    );
  }

  return (
    <section className="conciliacion-page">
      <header className="conciliacion-header">
        <div>
          <h1>Conciliacion</h1>
          <p>Libro esperado interno contra extractos reales, con score y cierre auditado.</p>
        </div>
        <div className="conciliacion-actions">
          <AppSelect
            ariaLabel="Cuenta"
            value={cuentaId}
            options={cuentas.map((cuenta) => ({
              value: cuenta.id,
              label: `${cuenta.titular_nombre} / ${cuenta.nombre} (${cuenta.divisa})`,
            }))}
            onChange={(next) => {
              setCuentaId(next);
              // Vacia las filas de la cuenta anterior al iniciar la carga de la
              // nueva: si la peticion falla, el catch de loadData ya limpia,
              // pero mientras esta en vuelo no deben verse filas de otra cuenta.
              setMovimientos([]);
              setConciliaciones([]);
              void loadData(next).catch(() => undefined);
            }}
          />
          <button type="button" className="button-secondary" disabled={submitting} onClick={() => void loadData(cuentaId).catch(() => undefined)}>
            Actualizar
          </button>
        </div>
      </header>

      {error && <p className="auth-error" role="alert">{error}</p>}
      {success && <p className="import-success" role="status">{success}</p>}

      <section className="conciliacion-panel">
        <h2>Movimiento esperado</h2>
        <div className="conciliacion-form">
          <div className="date-field">
            <span>Fecha</span>
            <DatePickerField ariaLabel="Fecha esperada" value={fecha} onChange={setFecha} />
          </div>
          <label>
            Importe
            <input inputMode="decimal" value={monto} onChange={(event) => setMonto(event.target.value)} placeholder="0,00" />
          </label>
          <label>
            Referencia
            <input value={referencia} onChange={(event) => setReferencia(event.target.value)} />
          </label>
          <label>
            Concepto
            <input value={concepto} onChange={(event) => setConcepto(event.target.value)} />
          </label>
          <button type="button" className="button-primary" disabled={submitting || !selectedCuenta} onClick={() => void createMovimiento()}>
            Crear esperado
          </button>
        </div>
      </section>

      <section className="conciliacion-panel">
        <div className="conciliacion-section-header">
          <div>
            <h2>Sugerencias</h2>
            <p>{movimientos.length} movimientos esperados · {conciliaciones.length} conciliaciones.</p>
          </div>
          <div className="conciliacion-actions">
            <label className="conciliacion-days">
              Dias
              <input
                type="number"
                min={0}
                max={10}
                value={ventanaDias}
                onChange={(event) => setVentanaDias(Number(event.target.value))}
              />
            </label>
            <button type="button" className="button-primary" disabled={submitting} onClick={() => void sugerir()}>
              Generar sugerencias
            </button>
          </div>
        </div>

        <div className="import-validation-table-wrap">
          <table className="import-validation-table">
            <thead>
              <tr>
                <th>Estado</th>
                <th>Score</th>
                <th>Esperado</th>
                <th>Extracto</th>
                <th>Dias</th>
                <th>Acciones</th>
              </tr>
            </thead>
            <tbody>
              {conciliaciones.map((item) => (
                <tr key={item.id}>
                  <td>{item.estado}</td>
                  <td>{item.score}</td>
                  <td>
                    {item.movimiento_esperado ? (
                      <>
                        <strong>{item.movimiento_esperado.fecha_esperada}</strong><br />
                        <SignedAmount value={item.movimiento_esperado.monto}>
                          {formatCurrency(item.movimiento_esperado.monto, item.movimiento_esperado.divisa)}
                        </SignedAmount><br />
                        <span>{item.movimiento_esperado.concepto ?? item.movimiento_esperado.referencia ?? ''}</span>
                      </>
                    ) : null}
                  </td>
                  <td>
                    {item.extracto ? (
                      <>
                        <strong>{item.extracto.fecha}</strong><br />
                        <SignedAmount value={item.extracto.monto}>
                          {formatCurrency(item.extracto.monto, selectedCuenta?.divisa ?? item.movimiento_esperado?.divisa ?? 'EUR')}
                        </SignedAmount><br />
                        <span>{item.extracto.concepto ?? ''}</span>
                      </>
                    ) : 'Sin match'}
                  </td>
                  <td>{item.diferencia_dias}</td>
                  <td>
                    <div className="conciliacion-row-actions">
                      {item.estado !== 'conciliada' && (
                        <button type="button" onClick={() => void cambiarEstado(item.id, 'confirmar')} disabled={submitting || !item.extracto_id}>
                          Conciliar
                        </button>
                      )}
                      <button type="button" onClick={() => void cambiarEstado(item.id, 'excepcion')} disabled={submitting}>
                        Excepcion
                      </button>
                      <button type="button" onClick={() => void cambiarEstado(item.id, 'resolver')} disabled={submitting}>
                        Resolver
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
              {conciliaciones.length === 0 && (
                <tr>
                  <td colSpan={6}>No hay sugerencias todavia.</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
      <ConfirmDialog {...confirmDialogProps} />
    </section>
  );
}
