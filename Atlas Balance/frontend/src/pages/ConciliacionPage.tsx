import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AxiosError } from 'axios';
import { AppSelect } from '@/components/common/AppSelect';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { DatePickerField } from '@/components/common/DatePickerField';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import { SignedAmount } from '@/components/common/SignedAmount';
import { useConfirmDialog } from '@/hooks/useConfirmDialog';
import { useInvalidateAfterMutation } from '@/hooks/queries/useInvalidateAfterMutation';
import api from '@/services/api';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
import { usePermisosStore } from '@/stores/permisosStore';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatCurrency, parseEuropeanNumber } from '@/utils/formatters';
// B-16: "hoy" en la zona horaria del usuario, no en UTC. El helper es el
// compartido: ExtractoTable, CuentaDetailPage e ImportacionPage usan el mismo.
import { hoy } from '@/utils/hoy';
// R-3 (seccion 9): helpers puros de paginacion (mismo patron que
// ExtractosPage y CuentaDetailPage, pero testeables sin React).
import { hayPaginaAnterior, hayPaginaSiguiente, textoMostrando, totalPaginas } from '@/utils/paginacion';
import type { Conciliacion, ImportContextoResponse, ImportCuentaContexto, MovimientoEsperado, PaginatedResponse } from '@/types';
import '@/styles/layout/routes/conciliacion.css';

// Consulta vigente del listado. El backend pagina y ordena el server-side,
// asi que la pantalla solo tiene que saber que pagina, tamano, orden y estado
// filtro estan vigentes.
interface ConsultaConciliacion {
  page: number;
  pageSize: number;
  sortBy: string;
  sortDir: 'asc' | 'desc';
  estado: string;
}

const CONSULTA_INICIAL: ConsultaConciliacion = {
  page: 1,
  pageSize: 50,
  sortBy: 'fecha_creacion',
  sortDir: 'desc',
  estado: '',
};

// Opciones del filtro de estado. Son las que el backend acepta para los dos
// listados (movimientos esperados y conciliaciones).
const ESTADOS_FILTRO = [
  { value: '', label: 'Todos los estados' },
  { value: 'pendiente', label: 'Pendiente' },
  { value: 'sugerida', label: 'Sugerida' },
  { value: 'conciliada', label: 'Conciliada' },
  { value: 'excepcion', label: 'Excepción' },
  { value: 'resuelta', label: 'Resuelta' },
];

// Campos por los que el backend sabe ordenar las conciliaciones.
const ORDENES = [
  { value: 'fecha_creacion', label: 'Fecha de creación' },
  { value: 'score', label: 'Score' },
  { value: 'diferencia_dias', label: 'Dias' },
  { value: 'estado', label: 'Estado' },
];

export default function ConciliacionPage() {
  const selectedPaisId = usePaisScopeStore((state) => state.selectedPaisId);
  const canConciliar = usePermisosStore((state) => state.canConciliarCuenta);
  const canCerrarConciliacion = usePermisosStore((state) => state.canCerrarConciliacionCuenta);
  // Los permisos llegan despues del login: hay que volver a pintar cuando cambian.
  usePermisosStore((state) => state.permisos);
  const invalidate = useInvalidateAfterMutation();
  const [cuentas, setCuentas] = useState<ImportCuentaContexto[]>([]);
  const [cuentaId, setCuentaId] = useState('');
  const [conciliaciones, setConciliaciones] = useState<Conciliacion[]>([]);
  // R-3 (seccion 9): los totales reales llegan en `total`, no en el numero de
  // filas de la pagina.
  const [totalMovimientos, setTotalMovimientos] = useState(0);
  const [totalConciliaciones, setTotalConciliaciones] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [rowsPage, setRowsPage] = useState(CONSULTA_INICIAL.page);
  const [rowsPageSize, setRowsPageSize] = useState(CONSULTA_INICIAL.pageSize);
  const [sortBy, setSortBy] = useState(CONSULTA_INICIAL.sortBy);
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>(CONSULTA_INICIAL.sortDir);
  const [estadoFiltro, setEstadoFiltro] = useState(CONSULTA_INICIAL.estado);
  const [fecha, setFecha] = useState(() => hoy());
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

  // LOTE 6 (D-13): el backend exige `puede_conciliar` (estricto) para
  // conciliar y `puede_cerrar_conciliacion` (estricto) para cerrar. El
  // frontend solo oculta lo que el backend ya deniega; la cuenta se evalua con
  // el pais que devuelve el contexto (el titular no viene ahi).
  const esConciliable = useCallback(
    (cuenta: ImportCuentaContexto | null | undefined) =>
      cuenta ? canConciliar(cuenta.id, undefined, cuenta.pais_id) : false,
    [canConciliar]
  );
  const puedeConciliarCuenta = esConciliable(selectedCuenta);
  // Las filas llegan filtradas por el backend segun el alcance real del
  // usuario (pais + titular + cuenta), asi que de cada fila solo hace falta
  // la dimension de cuenta: el backend ya deniega con 403 lo que no cuadra.
  const puedeConciliarFila = (item: Conciliacion) => canConciliar(item.cuenta_id, undefined, undefined);
  const puedeCerrarFila = (item: Conciliacion) => canCerrarConciliacion(item.cuenta_id, undefined, undefined);

  // V-02.08: guard anti-carrera + gestion de error propia. Antes loadData
  // propagaba la excepcion y los callers `void loadData(...)` producian un
  // unhandled rejection sin feedback, dejando en pantalla los datos de la
  // cuenta ANTERIOR bajo la cuenta recien seleccionada.
  const loadDataRequestIdRef = useRef(0);
  // Las cuentas se cargan despues del primer render: loadData necesita el
  // pais de la cuenta para decidir si puede pedir su detalle y no puede
  // depender de un estado que todavia no existe.
  const cuentasRef = useRef<ImportCuentaContexto[]>([]);
  // R-3 (seccion 9): la consulta vigente se guarda en un ref para que
  // loadData conserve su identidad. Si loadData dependiera del estado de
  // pagina, filtro u orden, el efecto de carga inicial se repetiria en cada
  // cambio y volveria a elegir la primera cuenta.
  const consultaRef = useRef<ConsultaConciliacion>({ ...CONSULTA_INICIAL });

  const loadData = useCallback(async (targetCuentaId?: string) => {
    const requestId = ++loadDataRequestIdRef.current;
    setError(null);
    // El backend exige `puede_conciliar` incluso para LEER las conciliaciones
    // de una cuenta. Sin ese permiso la peticion respondia 403 con toast, asi
    // que se piden las conciliaciones del alcance del usuario (sin cuenta).
    const target = cuentasRef.current.find((cuenta) => cuenta.id === targetCuentaId);
    const consulta = consultaRef.current;
    const params = target && esConciliable(target) ? { cuentaId: target.id } : undefined;
    const filtro = {
      ...params,
      estado: consulta.estado || undefined,
      page: consulta.page,
      pageSize: consulta.pageSize,
    };
    try {
      const [movimientosResponse, conciliacionesResponse] = await Promise.all([
        // De los movimientos esperados solo se muestra el contador, y su total
        // real llega en `total`.
        api.get<PaginatedResponse<MovimientoEsperado>>('/conciliacion/movimientos-esperados', { params: filtro }),
        api.get<PaginatedResponse<Conciliacion>>('/conciliacion', {
          params: { ...filtro, sortBy: consulta.sortBy, sortDir: consulta.sortDir },
        }),
      ]);
      if (requestId !== loadDataRequestIdRef.current) return;
      setTotalMovimientos(movimientosResponse.data.total ?? 0);
      setConciliaciones(conciliacionesResponse.data.data ?? []);
      setTotalConciliaciones(conciliacionesResponse.data.total ?? 0);
      setTotalPages(
        Math.max(
          1,
          conciliacionesResponse.data.total_pages
            ?? totalPaginas(conciliacionesResponse.data.total ?? 0, consulta.pageSize),
        )
      );
    } catch (err: unknown) {
      if (requestId !== loadDataRequestIdRef.current) return;
      setError(extractErrorMessage(err, 'No se pudo actualizar la conciliación.'));
      // Sin esto, si falla la carga de la cuenta nueva, movimientos y
      // conciliaciones de la cuenta anterior quedan visibles con botones de
      // accion usables bajo el cuentaId ya actualizado.
      setConciliaciones([]);
      setTotalMovimientos(0);
      setTotalConciliaciones(0);
      throw err;
    }
  }, [esConciliable]);

  // Aplica un cambio de pagina, tamano, orden o filtro y vuelve a pedir la
  // lista. El estado y la peticion se actualizan juntos: la peticion lleva ya
  // los valores nuevos.
  const aplicarConsulta = useCallback((cambio: Partial<ConsultaConciliacion>, cuenta?: string) => {
    const siguiente = { ...consultaRef.current, ...cambio };
    consultaRef.current = siguiente;
    setRowsPage(siguiente.page);
    setRowsPageSize(siguiente.pageSize);
    setSortBy(siguiente.sortBy);
    setSortDir(siguiente.sortDir);
    setEstadoFiltro(siguiente.estado);
    void loadData(cuenta ?? cuentaId).catch(() => undefined);
  }, [loadData, cuentaId]);

  const irAPagina = (pagina: number) => {
    aplicarConsulta({ page: Math.min(Math.max(1, pagina), totalPages) });
  };

  const cambiarTamanoPagina = (next: number) => {
    aplicarConsulta({ pageSize: next, page: 1 });
  };

  const cambiarEstadoFiltro = (next: string) => {
    aplicarConsulta({ estado: next, page: 1 });
  };

  const cambiarOrden = (campo: string) => {
    if (sortBy === campo) {
      aplicarConsulta({ sortDir: sortDir === 'asc' ? 'desc' : 'asc', page: 1 });
      return;
    }
    aplicarConsulta({ sortBy: campo, sortDir: 'asc', page: 1 });
  };

  const cambiarDireccionOrden = () => {
    aplicarConsulta({ sortDir: sortDir === 'asc' ? 'desc' : 'asc', page: 1 });
  };

  // Si la lista se acorta (conciliacion resuelta, filtro nuevo), la pagina
  // vigente puede quedar mas alla del final: se recorta a la ultima valida.
  useEffect(() => {
    if (rowsPage > totalPages) {
      aplicarConsulta({ page: totalPages });
    }
  }, [rowsPage, totalPages, aplicarConsulta]);

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
        cuentasRef.current = nextCuentas;
        const initialCuenta = nextCuentas[0]?.id ?? '';
        setCuentaId(initialCuenta);
        // La cuenta puede cambiar: la lista vuelve a la primera pagina.
        consultaRef.current = { ...consultaRef.current, page: 1 };
        setRowsPage(1);
        await loadData(initialCuenta);
      } catch (err: unknown) {
        if (mounted) setError(extractErrorMessage(err, 'No se pudo cargar conciliación.'));
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
        confirmLabel: 'Conciliar movimiento',
        tone: 'normal',
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
      setSuccess(action === 'confirmar' ? 'Match conciliado.' : action === 'resolver' ? 'Excepción resuelta.' : 'Marcado como excepcion.');
      await loadData(cuentaId);
      await invalidate('conciliacion');
    } catch (err: unknown) {
      setError(extractErrorMessage(err, 'No se pudo actualizar la conciliación.'));
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
    return (
      <section className="conciliacion-page">
        <PageSkeleton label="Cargando conciliación..." />
      </section>
    );
  }

  if (error && cuentas.length === 0) {
    return (
      <section className="conciliacion-page">
        <EmptyState variant="error" title="No se pudo cargar conciliación." subtitle={error} />
      </section>
    );
  }

  return (
    <section className="conciliacion-page">
      <header className="conciliacion-header page-header">
        <div className="page-header__lead">
          <h1 className="page-header__title">Conciliación</h1>
          <p className="page-header__subtitle">Libro esperado interno contra extractos reales, con score y cierre auditado.</p>
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
              setConciliaciones([]);
              // Cambiar de cuenta devuelve la lista a la primera pagina.
              aplicarConsulta({ page: 1 }, next);
            }}
          />
          <button type="button" className="button-secondary" disabled={submitting} onClick={() => void loadData(cuentaId).catch(() => undefined)}>
            Actualizar
          </button>
        </div>
      </header>

      {error && <p className="auth-error" role="alert">{error}</p>}
      {success && <p className="import-success" role="status">{success}</p>}

      {puedeConciliarCuenta && (
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
      )}

      <section className="conciliacion-panel">
        <div className="conciliacion-section-header">
          <div>
            <h2>Sugerencias</h2>
            <p>{totalMovimientos} movimientos esperados · {totalConciliaciones} conciliaciones.</p>
          </div>
          <div className="conciliacion-actions">
            <AppSelect
              ariaLabel="Estado"
              value={estadoFiltro}
              options={ESTADOS_FILTRO}
              onChange={cambiarEstadoFiltro}
            />
            <AppSelect
              ariaLabel="Ordenar por"
              value={sortBy}
              options={ORDENES}
              onChange={cambiarOrden}
            />
            <button type="button" className="button-secondary" onClick={cambiarDireccionOrden}>
              {sortDir === 'asc' ? 'Ascendente' : 'Descendente'}
            </button>
            {puedeConciliarCuenta && (
              <>
                <label className="conciliacion-days">
                  Días
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
              </>
            )}
          </div>
        </div>

        <div className="import-validation-table-wrap">
          <table className="import-validation-table">
            <caption className="sr-only">Sugerencias de conciliación</caption>
            <thead>
              <tr>
                <th scope="col">Estado</th>
                <th scope="col">Score</th>
                <th scope="col">Esperado</th>
                <th scope="col">Extracto</th>
                <th scope="col">Días</th>
                <th scope="col">Acciones</th>
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
                        <SignedAmount value={item.movimiento_esperado.monto} showSign>
                          {formatCurrency(item.movimiento_esperado.monto, item.movimiento_esperado.divisa)}
                        </SignedAmount><br />
                        <span className="conciliacion-concepto">{item.movimiento_esperado.concepto ?? item.movimiento_esperado.referencia ?? ''}</span>
                      </>
                    ) : null}
                  </td>
                  <td>
                    {item.extracto ? (
                      <>
                        <strong>{item.extracto.fecha}</strong><br />
                        <SignedAmount value={item.extracto.monto} showSign>
                          {formatCurrency(item.extracto.monto, selectedCuenta?.divisa ?? item.movimiento_esperado?.divisa ?? 'EUR')}
                        </SignedAmount><br />
                        <span>{item.extracto.concepto ?? ''}</span>
                      </>
                    ) : 'Sin match'}
                  </td>
                  <td>{item.diferencia_dias}</td>
                  <td>
                    <div className="conciliacion-row-actions">
                      {puedeConciliarFila(item) && item.estado !== 'conciliada' && item.estado !== 'resuelta' && (
                        <button type="button" onClick={() => void cambiarEstado(item.id, 'confirmar')} disabled={submitting || !item.extracto_id}>
                          Conciliar
                        </button>
                      )}
                      {puedeConciliarFila(item) && item.estado !== 'resuelta' && (
                        <button type="button" onClick={() => void cambiarEstado(item.id, 'excepcion')} disabled={submitting}>
                          Excepción
                        </button>
                      )}
                      {puedeCerrarFila(item) && (
                        <button type="button" onClick={() => void cambiarEstado(item.id, 'resolver')} disabled={submitting}>
                          Resolver
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
              {conciliaciones.length === 0 && (
                <tr>
                  <td colSpan={6}>No hay sugerencias todavía.</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>

        {/* R-3 (seccion 9): la lista se pagina en el servidor. Se reutiliza la
            clase de paginacion existente (la misma de ExtractosPage). */}
        <div className="users-pagination">
          <button type="button" onClick={() => irAPagina(rowsPage - 1)} disabled={!hayPaginaAnterior(rowsPage)}>
            Anterior
          </button>
          <span>
            {textoMostrando(rowsPage, rowsPageSize, totalConciliaciones, 'conciliaciones')} · Página {rowsPage} / {totalPages}
          </span>
          <button type="button" onClick={() => irAPagina(rowsPage + 1)} disabled={!hayPaginaSiguiente(rowsPage, totalPages)}>
            Siguiente
          </button>
          <PageSizeSelect
            value={rowsPageSize}
            options={[50, 100, 200]}
            ariaLabel="Conciliaciones por página"
            onChange={cambiarTamanoPagina}
          />
        </div>
      </section>
      <ConfirmDialog {...confirmDialogProps} />
    </section>
  );
}
