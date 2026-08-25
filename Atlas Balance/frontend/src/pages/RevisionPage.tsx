import { useEffect, useMemo, useRef, useState } from 'react';
import { AppSelect } from '@/components/common/AppSelect';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import { SignedAmount } from '@/components/common/SignedAmount';
import { useInvalidateAfterMutation } from '@/hooks/queries/useInvalidateAfterMutation';
import api from '@/services/api';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
import { usePermisosStore } from '@/stores/permisosStore';
import type { PaginatedResponse, RevisionComisionItem, RevisionEstadoComision, RevisionEstadoSeguro, RevisionSeguroItem, VerificarDevolucionResponse } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';
import { formatCurrency, formatDate } from '@/utils/formatters';
import { Check, RotateCcw, X } from 'lucide-react';

type RevisionTab = 'comisiones' | 'seguros';
type EstadoFiltro = 'TODAS' | 'PENDIENTE' | 'DEVUELTA' | 'CORRECTO' | 'DESCARTADA';

export default function RevisionPage() {
  const selectedPaisId = usePaisScopeStore((state) => state.selectedPaisId);
  const [tab, setTab] = useState<RevisionTab>('comisiones');
  const [estado, setEstado] = useState<EstadoFiltro>('TODAS');
  const [comisiones, setComisiones] = useState<RevisionComisionItem[]>([]);
  const [seguros, setSeguros] = useState<RevisionSeguroItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [total, setTotal] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const canEditCuenta = usePermisosStore((state) => state.canEditCuenta);
  usePermisosStore((state) => state.permisos);
  const invalidate = useInvalidateAfterMutation();

  // V-02.08: guard anti-carrera. Sin el, cambiar rapido de pagina/filtro podia
  // dejar pintada la respuesta de una peticion antigua que llegaba ultima.
  const loadRequestIdRef = useRef(0);

  const load = async () => {
    const requestId = ++loadRequestIdRef.current;
    setLoading(true);
    setError(null);
    try {
      const params = estado === 'TODAS'
        ? { page, pageSize, paisId: selectedPaisId || undefined }
        : { estado, page, pageSize, paisId: selectedPaisId || undefined };
      if (tab === 'comisiones') {
        const { data } = await api.get<PaginatedResponse<RevisionComisionItem>>('/revision/comisiones', { params });
        if (requestId !== loadRequestIdRef.current) return;
        setComisiones(data?.data ?? []);
        setTotal(data?.total ?? 0);
        setTotalPages(data?.total_pages ?? 0);
      } else {
        const { data } = await api.get<PaginatedResponse<RevisionSeguroItem>>('/revision/seguros', { params });
        if (requestId !== loadRequestIdRef.current) return;
        setSeguros(data?.data ?? []);
        setTotal(data?.total ?? 0);
        setTotalPages(data?.total_pages ?? 0);
      }
    } catch (err) {
      if (requestId !== loadRequestIdRef.current) return;
      setError(extractErrorMessage(err, 'No se pudo cargar la revisión.'));
    } finally {
      if (requestId === loadRequestIdRef.current) {
        setLoading(false);
      }
    }
  };

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- tab/filtro controlan la consulta
  }, [tab, estado, page, pageSize, selectedPaisId]);

  useEffect(() => {
    setPage(1);
  }, [selectedPaisId]);

  const filtroOptions = useMemo(() => {
    if (tab === 'comisiones') {
      return [
        { value: 'TODAS', label: 'Todas' },
        { value: 'PENDIENTE', label: 'Pendientes' },
        { value: 'DEVUELTA', label: 'Devueltas' },
        { value: 'DESCARTADA', label: 'Descartadas' },
      ];
    }

    return [
      { value: 'TODAS', label: 'Todos' },
      { value: 'PENDIENTE', label: 'Pendientes' },
      { value: 'CORRECTO', label: 'Correctos' },
      { value: 'DESCARTADA', label: 'Descartados' },
    ];
  }, [tab]);

  const setComisionEstado = async (item: RevisionComisionItem, next: RevisionEstadoComision) => {
    setBusyId(item.extracto_id);
    try {
      await api.patch(`/revision/COMISION/${item.extracto_id}`, { estado: next });
      setComisiones((current) =>
        current.map((row) => (row.extracto_id === item.extracto_id ? { ...row, estado_devolucion: next } : row))
      );
      if (estado !== 'TODAS' && estado !== next) {
        setComisiones((current) => current.filter((row) => row.extracto_id !== item.extracto_id));
      }
      await invalidate('revision');
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo guardar el estado de devolucion.'));
    } finally {
      setBusyId(null);
    }
  };

  // V-02.08: empareja la comision con su bonificacion (misma cuenta e importe
  // exacto) y la marca como devuelta en un solo clic. Si el abono ya se asigno
  // a otra comision (409) o no hay candidata, se resincroniza la lista.
  const verificarDevolucion = async (item: RevisionComisionItem) => {
    setBusyId(item.extracto_id);
    setError(null);
    try {
      const { data } = await api.post<VerificarDevolucionResponse>(`/revision/comision/${item.extracto_id}/verificar-devolucion`);
      if (data?.encontrada && data.devolucion_extracto_id) {
        setComisiones((current) =>
          current.map((row) => (
            row.extracto_id === item.extracto_id
              ? {
                  ...row,
                  estado_devolucion: 'DEVUELTA' as RevisionEstadoComision,
                  devolucion_extracto_id: data.devolucion_extracto_id,
                  devolucion_fecha: data.devolucion_fecha ?? row.devolucion_fecha,
                }
              : row
          ))
        );
        if (estado !== 'TODAS' && estado !== 'DEVUELTA') {
          setComisiones((current) => current.filter((row) => row.extracto_id !== item.extracto_id));
        }
      } else {
        setError(data?.message || 'No hay ninguna bonificación candidata para esta comisión.');
        await load();
      }
      await invalidate('revision');
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo verificar la devolución.'));
      await load();
    } finally {
      setBusyId(null);
    }
  };

  const setSeguroEstado = async (item: RevisionSeguroItem, next: RevisionEstadoSeguro) => {
    setBusyId(item.extracto_id);
    try {
      await api.patch(`/revision/SEGURO/${item.extracto_id}`, { estado: next });
      setSeguros((current) =>
        current.map((row) => (row.extracto_id === item.extracto_id ? { ...row, estado: next } : row))
      );
      if (estado !== 'TODAS' && estado !== next) {
        setSeguros((current) => current.filter((row) => row.extracto_id !== item.extracto_id));
      }
      await invalidate('revision');
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo guardar el estado del seguro.'));
    } finally {
      setBusyId(null);
    }
  };

  return (
    <section className="revision-page">
      <header className="dashboard-toolbar">
        <div>
          <h1>Revisión</h1>
          <p className="dashboard-subtitle">Detección automática de comisiones y seguros en todos los extractos.</p>
        </div>
        <AppSelect
          className="revision-filter"
          label="Estado"
          value={estado}
          options={filtroOptions}
          onChange={(value) => {
            setEstado(value as EstadoFiltro);
            setPage(1);
          }}
        />
      </header>

      <div className="config-tabs">
        <button
          type="button"
          className={tab === 'comisiones' ? 'config-tab config-tab--active' : 'config-tab'}
          onClick={() => {
            setTab('comisiones');
            setEstado('TODAS');
            setPage(1);
          }}
        >
          Comisiones
        </button>
        <button
          type="button"
          className={tab === 'seguros' ? 'config-tab config-tab--active' : 'config-tab'}
          onClick={() => {
            setTab('seguros');
            setEstado('TODAS');
            setPage(1);
          }}
        >
          Seguros
        </button>
      </div>

      {error ? <p className="auth-error" role="alert">{error}</p> : null}

      {loading ? <PageSkeleton rows={5} variant="table" /> : tab === 'comisiones' ? (
        <ComisionesTable
          rows={comisiones}
          busyId={busyId}
          canEditItem={(item) => canEditCuenta(item.cuenta_id, item.titular_id, item.pais_id)}
          onSetEstado={setComisionEstado}
          onVerificar={verificarDevolucion}
        />
      ) : (
        <SegurosTable
          rows={seguros}
          busyId={busyId}
          canEditItem={(item) => canEditCuenta(item.cuenta_id, item.titular_id, item.pais_id)}
          onSetEstado={setSeguroEstado}
        />
      )}

      {!loading && total > 0 ? (
        <div className="users-pagination revision-pagination" aria-label="Paginación de revisión">
          <button type="button" disabled={page <= 1} onClick={() => setPage((current) => Math.max(1, current - 1))}>
            Anterior
          </button>
          <span>
            Página {page} de {totalPages || 1} - {total} movimientos
          </span>
          <button
            type="button"
            disabled={totalPages === 0 || page >= totalPages}
            onClick={() => setPage((current) => current + 1)}
          >
            Siguiente
          </button>
          <PageSizeSelect
            value={pageSize}
            options={[25, 50, 100, 200]}
            ariaLabel="Filas por página de revisión"
            onChange={(next) => {
              setPageSize(next);
              setPage(1);
            }}
          />
        </div>
      ) : null}
    </section>
  );
}

function ComisionesTable({
  rows,
  busyId,
  canEditItem,
  onSetEstado,
  onVerificar,
}: {
  rows: RevisionComisionItem[];
  busyId: string | null;
  canEditItem: (item: RevisionComisionItem) => boolean;
  onSetEstado: (item: RevisionComisionItem, next: RevisionEstadoComision) => void;
  onVerificar: (item: RevisionComisionItem) => void;
}) {
  if (rows.length === 0) {
    return <EmptyState title="Sin comisiones detectadas." subtitle="Ajusta el umbral en Configuración si necesitas afinar la revisión." />;
  }

  return (
    <div className="revision-table-wrap">
      <table className="revision-table">
        <thead>
          <tr>
            <th>Titular</th>
            <th>Cuenta</th>
            <th>Fecha</th>
            <th>Monto</th>
            <th>Concepto</th>
            <th>Devolución</th>
            <th>Revisión</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((item) => (
            <tr key={item.extracto_id}>
              <td data-label="Titular">{item.titular}</td>
              <td data-label="Cuenta">{item.cuenta}</td>
              <td data-label="Fecha" className="revision-cell-fixed">{formatDate(item.fecha)}</td>
              <td data-label="Monto" className="revision-cell-money">
                <SignedAmount value={item.monto}>{formatCurrency(item.monto, item.divisa)}</SignedAmount>
              </td>
              <td data-label="Concepto" title={item.concepto}>{item.concepto}</td>
              <td data-label="Devolución" className="revision-cell-fixed" title={item.devolucion_fecha ? `Bonificación del ${formatDate(item.devolucion_fecha)}` : undefined}>
                {item.devolucion_fecha ? formatDate(item.devolucion_fecha) : '—'}
              </td>
              <td data-label="Revisión">
                <RevisionComisionActions
                  item={item}
                  busy={busyId === item.extracto_id}
                  canEdit={canEditItem(item)}
                  onSetEstado={onSetEstado}
                  onVerificar={onVerificar}
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function SegurosTable({
  rows,
  busyId,
  canEditItem,
  onSetEstado,
}: {
  rows: RevisionSeguroItem[];
  busyId: string | null;
  canEditItem: (item: RevisionSeguroItem) => boolean;
  onSetEstado: (item: RevisionSeguroItem, next: RevisionEstadoSeguro) => void;
}) {
  if (rows.length === 0) {
    return <EmptyState title="Sin seguros detectados." subtitle="No hay movimientos con conceptos de seguros en las cuentas visibles." />;
  }

  return (
    <div className="revision-table-wrap">
      <table className="revision-table">
        <thead>
          <tr>
            <th>Titular</th>
            <th>Cuenta</th>
            <th>Fecha</th>
            <th>Importe</th>
            <th>Concepto</th>
            <th>Estado</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((item) => (
            <tr key={item.extracto_id}>
              <td data-label="Titular">{item.titular}</td>
              <td data-label="Cuenta">{item.cuenta}</td>
              <td data-label="Fecha" className="revision-cell-fixed">{formatDate(item.fecha)}</td>
              <td data-label="Importe" className="revision-cell-money">
                <SignedAmount value={item.importe}>{formatCurrency(item.importe, item.divisa)}</SignedAmount>
              </td>
              <td data-label="Concepto" title={item.concepto}>{item.concepto}</td>
              <td data-label="Estado">
                <RevisionSeguroActions
                  item={item}
                  busy={busyId === item.extracto_id}
                  canEdit={canEditItem(item)}
                  onSetEstado={onSetEstado}
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function RevisionComisionActions({
  item,
  busy,
  canEdit,
  onSetEstado,
  onVerificar,
}: {
  item: RevisionComisionItem;
  busy: boolean;
  canEdit: boolean;
  onSetEstado: (item: RevisionComisionItem, next: RevisionEstadoComision) => void;
  onVerificar: (item: RevisionComisionItem) => void;
}) {
  if (!canEdit) {
    return <span className="revision-status revision-status--readonly">Solo lectura</span>;
  }

  return (
    <div className="revision-actions">
      {item.estado_devolucion === 'DESCARTADA' ? (
        <span className="revision-status revision-status--discarded">Descartada</span>
      ) : (
        <button
          type="button"
          className={item.estado_devolucion === 'DEVUELTA' ? 'revision-status revision-status--ok' : 'revision-status'}
          disabled={busy}
          onClick={() =>
            onSetEstado(
              item,
              item.estado_devolucion === 'DEVUELTA' ? 'PENDIENTE' : 'DEVUELTA'
            )
          }
        >
          {item.estado_devolucion === 'DEVUELTA' ? 'Devuelta' : 'Pendiente'}
        </button>
      )}
      {item.estado_devolucion === 'PENDIENTE' && item.devolucion_extracto_id && item.devolucion_fecha ? (
        <button
          type="button"
          className="revision-verify-button"
          aria-label={`Verificar devolución del ${formatDate(item.devolucion_fecha)}`}
          title={`Verificar devolución del ${formatDate(item.devolucion_fecha)}`}
          disabled={busy}
          onClick={() => onVerificar(item)}
        >
          <Check aria-hidden="true" />
        </button>
      ) : null}
      <button
        type="button"
        className="revision-discard-button"
        aria-label={item.estado_devolucion === 'DESCARTADA' ? 'Restaurar comision' : 'No es comision'}
        title={item.estado_devolucion === 'DESCARTADA' ? 'Restaurar comision' : 'No es comision'}
        disabled={busy}
        onClick={() => onSetEstado(item, item.estado_devolucion === 'DESCARTADA' ? 'PENDIENTE' : 'DESCARTADA')}
      >
        {item.estado_devolucion === 'DESCARTADA' ? <RotateCcw aria-hidden="true" /> : <X aria-hidden="true" />}
      </button>
    </div>
  );
}

function RevisionSeguroActions({
  item,
  busy,
  canEdit,
  onSetEstado,
}: {
  item: RevisionSeguroItem;
  busy: boolean;
  canEdit: boolean;
  onSetEstado: (item: RevisionSeguroItem, next: RevisionEstadoSeguro) => void;
}) {
  if (!canEdit) {
    return <span className="revision-status revision-status--readonly">Solo lectura</span>;
  }

  return (
    <div className="revision-actions">
      {item.estado === 'DESCARTADA' ? (
        <span className="revision-status revision-status--discarded">Descartado</span>
      ) : (
        <button
          type="button"
          className={item.estado === 'CORRECTO' ? 'revision-status revision-status--ok' : 'revision-status'}
          disabled={busy}
          onClick={() => onSetEstado(item, item.estado === 'CORRECTO' ? 'PENDIENTE' : 'CORRECTO')}
        >
          {item.estado === 'CORRECTO' ? 'Correcto' : 'Pendiente'}
        </button>
      )}
      <button
        type="button"
        className="revision-discard-button"
        aria-label={item.estado === 'DESCARTADA' ? 'Restaurar seguro' : 'No es seguro'}
        title={item.estado === 'DESCARTADA' ? 'Restaurar seguro' : 'No es seguro'}
        disabled={busy}
        onClick={() => onSetEstado(item, item.estado === 'DESCARTADA' ? 'PENDIENTE' : 'DESCARTADA')}
      >
        {item.estado === 'DESCARTADA' ? <RotateCcw aria-hidden="true" /> : <X aria-hidden="true" />}
      </button>
    </div>
  );
}
