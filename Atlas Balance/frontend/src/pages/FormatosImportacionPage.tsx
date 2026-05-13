import { useEffect, useState } from 'react';
import { AppSelect } from '@/components/common/AppSelect';
import ConfirmDialog from '@/components/common/ConfirmDialog';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSizeSelect } from '@/components/common/PageSizeSelect';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import type { PaginatedResponse } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';

interface ColumnaExtra {
  nombre: string;
  indice: number;
  etiqueta?: string;
}

interface MapeoColumnas {
  tipo_monto?: TipoMontoImportacion;
  fecha: number;
  concepto: number;
  monto?: number | null;
  ingreso?: number | null;
  egreso?: number | null;
  saldo: number;
  columnas_extra?: ColumnaExtra[];
}

type TipoMontoImportacion = 'una_columna' | 'dos_columnas' | 'tres_columnas';

interface FormatoRow {
  id: string;
  nombre: string;
  banco_nombre: string | null;
  divisa: string | null;
  mapeo_json: MapeoColumnas;
  activo: boolean;
  deleted_at: string | null;
}

interface ColumnaOrdenada {
  nombre: string;
  tipo: 'base' | 'extra';
  indice: number;
}

interface FormatoFormState {
  banco_nombre: string;
  divisa: string;
  activo: boolean;
  tipo_monto: TipoMontoImportacion;
  columnas: ColumnaOrdenada[];
  columnas_extra_nuevas: string[];
}

interface DeleteCandidate {
  id: string;
  nombre: string;
}

interface DivisaOption {
  codigo: string;
  nombre: string | null;
}

const emptyForm: FormatoFormState = {
  banco_nombre: '',
  divisa: 'EUR',
  activo: true,
  tipo_monto: 'una_columna',
  columnas: [
    { nombre: 'Fecha', tipo: 'base', indice: 0 },
    { nombre: 'Concepto', tipo: 'base', indice: 1 },
    { nombre: 'Importe', tipo: 'base', indice: 2 },
    { nombre: 'Saldo', tipo: 'base', indice: 3 },
  ],
  columnas_extra_nuevas: [],
};

const EMPTY_VALUE = 'Sin dato';

function getBaseColumns(tipoMonto: TipoMontoImportacion): ColumnaOrdenada[] {
  const labels = tipoMonto === 'tres_columnas'
    ? ['Fecha', 'Concepto', 'Ingreso', 'Egreso', 'Importe', 'Saldo']
    : tipoMonto === 'dos_columnas'
      ? ['Fecha', 'Concepto', 'Ingreso', 'Egreso', 'Saldo']
      : ['Fecha', 'Concepto', 'Importe', 'Saldo'];

  return labels.map((nombre, indice) => ({
    nombre,
    tipo: 'base',
    indice,
  }));
}

function normalizeTipoMonto(mapeo: Partial<MapeoColumnas>): TipoMontoImportacion {
  if (mapeo.tipo_monto === 'tres_columnas' || (mapeo.ingreso !== undefined && mapeo.egreso !== undefined && mapeo.monto !== undefined)) {
    return 'tres_columnas';
  }

  if (mapeo.tipo_monto === 'dos_columnas' || (mapeo.ingreso !== undefined && mapeo.egreso !== undefined)) {
    return 'dos_columnas';
  }

  return 'una_columna';
}

export default function FormatosImportacionPage() {
  const usuario = useAuthStore((state) => state.usuario);
  const isAdmin = usuario?.rol === 'ADMIN';

  const [items, setItems] = useState<FormatoRow[]>([]);
  const [divisas, setDivisas] = useState<DivisaOption[]>([]);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [totalPages, setTotalPages] = useState(1);
  const [search, setSearch] = useState('');
  const [incluirEliminados, setIncluirEliminados] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [editingId, setEditingId] = useState<string | null>(null);
  const [form, setForm] = useState<FormatoFormState>(emptyForm);
  const [saving, setSaving] = useState(false);
  const [deleteCandidate, setDeleteCandidate] = useState<DeleteCandidate | null>(null);

  const loadDivisas = async () => {
    try {
      const { data } = await api.get<DivisaOption[]>('/cuentas/divisas-activas');
      setDivisas(data ?? []);
      if ((data?.length ?? 0) > 0) {
        setForm((prev) => ({
          ...prev,
          divisa: prev.divisa || data[0].codigo,
        }));
      }
    } catch {
      setDivisas([]);
    }
  };

  const loadData = async () => {
    setLoading(true);
    setError(null);
    try {
      const { data } = await api.get<PaginatedResponse<FormatoRow>>('/formatos-importacion', {
        params: {
          page,
          pageSize,
          search: search || undefined,
          incluirEliminados: incluirEliminados && isAdmin,
          sortBy: 'fecha_creacion',
          sortDir: 'desc',
        },
      });
      setItems(data.data ?? []);
      setTotalPages(Math.max(data.total_pages ?? 1, 1));
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudieron cargar formatos'));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadDivisas();
  }, []);

  useEffect(() => {
    void loadData();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- recarga controlada por filtros y paginación
  }, [page, pageSize, search, incluirEliminados, isAdmin]);

  const resetForm = () => {
    setEditingId(null);
    setForm({
      ...emptyForm,
      divisa: divisas[0]?.codigo ?? emptyForm.divisa,
    });
  };

  const startEdit = async (id: string) => {
    setSaving(true);
    setError(null);
    try {
      const { data } = await api.get<FormatoRow>(`/formatos-importacion/${id}`, { params: { incluirEliminados: true } });
      setEditingId(id);

      const mapeo = data.mapeo_json ?? {};
      const tipoMonto = normalizeTipoMonto(mapeo);
      const columnasBase: ColumnaOrdenada[] = tipoMonto === 'tres_columnas'
        ? [
            { nombre: 'Fecha', tipo: 'base', indice: mapeo.fecha ?? 0 },
            { nombre: 'Concepto', tipo: 'base', indice: mapeo.concepto ?? 1 },
            { nombre: 'Ingreso', tipo: 'base', indice: mapeo.ingreso ?? 2 },
            { nombre: 'Egreso', tipo: 'base', indice: mapeo.egreso ?? 3 },
            { nombre: 'Importe', tipo: 'base', indice: mapeo.monto ?? 4 },
            { nombre: 'Saldo', tipo: 'base', indice: mapeo.saldo ?? 5 },
          ]
        : tipoMonto === 'dos_columnas'
          ? [
              { nombre: 'Fecha', tipo: 'base', indice: mapeo.fecha ?? 0 },
              { nombre: 'Concepto', tipo: 'base', indice: mapeo.concepto ?? 1 },
              { nombre: 'Ingreso', tipo: 'base', indice: mapeo.ingreso ?? 2 },
              { nombre: 'Egreso', tipo: 'base', indice: mapeo.egreso ?? 3 },
              { nombre: 'Saldo', tipo: 'base', indice: mapeo.saldo ?? 4 },
            ]
          : [
              { nombre: 'Fecha', tipo: 'base', indice: mapeo.fecha ?? 0 },
              { nombre: 'Concepto', tipo: 'base', indice: mapeo.concepto ?? 1 },
              { nombre: 'Importe', tipo: 'base', indice: mapeo.monto ?? 2 },
              { nombre: 'Saldo', tipo: 'base', indice: mapeo.saldo ?? 3 },
            ];

      const columnasExtra: ColumnaOrdenada[] = (mapeo.columnas_extra ?? []).map((col) => ({
        nombre: col.nombre,
        tipo: 'extra',
        indice: col.indice,
        etiqueta: col.etiqueta ?? '',
      }));

      const todasLasColumnas = [...columnasBase, ...columnasExtra].sort((a, b) => a.indice - b.indice);

      setForm({
        banco_nombre: data.banco_nombre ?? '',
        divisa: data.divisa ?? 'EUR',
        activo: data.activo,
        tipo_monto: tipoMonto,
        columnas: todasLasColumnas,
        columnas_extra_nuevas: [],
      });
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo cargar formato'));
    } finally {
      setSaving(false);
    }
  };

  const addExtraColumn = () => {
    const newName = `Columna ${form.columnas_extra_nuevas.length + 1}`;
    setForm((prev) => ({
      ...prev,
      columnas: [...prev.columnas, { nombre: newName, tipo: 'extra', indice: prev.columnas.length }],
      columnas_extra_nuevas: [...prev.columnas_extra_nuevas, newName],
    }));
  };

  const removeColumn = (index: number) => {
    setForm((prev) => ({
      ...prev,
      columnas: prev.columnas.filter((_, idx) => idx !== index),
    }));
  };

  const updateColumnName = (index: number, newName: string) => {
    setForm((prev) => ({
      ...prev,
      columnas: prev.columnas.map((col, idx) => (idx === index ? { ...col, nombre: newName } : col)),
    }));
  };

  const moveColumn = (index: number, direction: 'up' | 'down') => {
    setForm((prev) => {
      const newColumnas = [...prev.columnas];
      const newIndex = direction === 'up' ? index - 1 : index + 1;
      if (newIndex < 0 || newIndex >= newColumnas.length) return prev;
      [newColumnas[index], newColumnas[newIndex]] = [newColumnas[newIndex], newColumnas[index]];
      return { ...prev, columnas: newColumnas };
    });
  };

  const buildMapeo = (): MapeoColumnas => {
    const mapeo: MapeoColumnas = {
      tipo_monto: form.tipo_monto,
      fecha: 0,
      concepto: 0,
      saldo: 0,
    };

    const columnasExtra: ColumnaExtra[] = [];

    form.columnas.forEach((col, index) => {
      if (col.tipo === 'base') {
        if (col.nombre === 'Fecha') mapeo.fecha = index;
        else if (col.nombre === 'Concepto') mapeo.concepto = index;
        else if (col.nombre === 'Monto' || col.nombre === 'Importe') mapeo.monto = index;
        else if (col.nombre === 'Ingreso') mapeo.ingreso = index;
        else if (col.nombre === 'Egreso') mapeo.egreso = index;
        else if (col.nombre === 'Saldo') mapeo.saldo = index;
      } else {
        const nombre = col.nombre.trim();
        columnasExtra.push({
          nombre,
          indice: index,
          etiqueta: nombre.toLowerCase(),
        });
      }
    });

    if (columnasExtra.length > 0) {
      mapeo.columnas_extra = columnasExtra;
    }

    return mapeo;
  };

  const save = async () => {
    if (!isAdmin) return;
    const bancoNombre = form.banco_nombre.trim();
    if (!bancoNombre) {
      setError('Escribe el banco del formato.');
      return;
    }

    if (!form.divisa.trim()) {
      setError('Selecciona la divisa del formato.');
      return;
    }

    if (form.columnas.length === 0) {
      setError('Manten al menos las columnas base necesarias para importar.');
      return;
    }

    const baseColumnas = form.columnas.filter((col) => col.tipo === 'base');
    const expectedBaseCount = form.tipo_monto === 'tres_columnas' ? 6 : form.tipo_monto === 'dos_columnas' ? 5 : 4;
    const expectedBaseLabels = form.tipo_monto === 'tres_columnas'
      ? 'Fecha, Concepto, Ingreso, Egreso, Importe, Saldo'
      : form.tipo_monto === 'dos_columnas'
        ? 'Fecha, Concepto, Ingreso, Egreso, Saldo'
        : 'Fecha, Concepto, Importe, Saldo';
    if (baseColumnas.length !== expectedBaseCount) {
      setError(`Manten exactamente ${expectedBaseCount} columnas base: ${expectedBaseLabels}.`);
      return;
    }

    const extraClaves = new Set<string>();
    for (const col of form.columnas) {
      if (col.tipo === 'extra') {
        const trimmedName = col.nombre.trim();
        if (trimmedName.length === 0) {
          setError('Escribe un nombre para cada columna extra.');
          return;
        }
        const clave = trimmedName.toLowerCase();
        if (extraClaves.has(clave)) {
          setError(`No repitas nombres de columnas extra: "${trimmedName}"`);
          return;
        }
        extraClaves.add(clave);
      }
    }

    setSaving(true);
    setError(null);
    const payload = {
      nombre: bancoNombre,
      banco_nombre: bancoNombre,
      divisa: form.divisa.trim().toUpperCase(),
      activo: form.activo,
      mapeo_json: buildMapeo(),
    };

    try {
      if (editingId) {
        await api.put(`/formatos-importacion/${editingId}`, payload);
      } else {
        await api.post('/formatos-importacion', payload);
      }
      resetForm();
      await loadData();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo guardar formato'));
    } finally {
      setSaving(false);
    }
  };

  const remove = async () => {
    if (!isAdmin || !deleteCandidate) return;
    setSaving(true);
    setError(null);
    try {
      await api.delete(`/formatos-importacion/${deleteCandidate.id}`);
      setDeleteCandidate(null);
      await loadData();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo eliminar formato'));
    } finally {
      setSaving(false);
    }
  };

  const restore = async (id: string) => {
    if (!isAdmin) return;
    try {
      await api.post(`/formatos-importacion/${id}/restaurar`);
      await loadData();
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo restaurar formato'));
    }
  };

  const updateTipoMonto = (tipoMonto: TipoMontoImportacion) => {
    setForm((prev) => {
      const extras = prev.columnas.filter((col) => col.tipo === 'extra');
      return {
        ...prev,
        tipo_monto: tipoMonto,
        columnas: [...getBaseColumns(tipoMonto), ...extras],
      };
    });
  };

  return (
    <section className="phase2-page">
      <header className="phase2-header">
        <h1>Formatos de Importación</h1>
        {isAdmin && <button type="button" className="users-primary-button" onClick={resetForm}>Nuevo Formato</button>}
      </header>

      <div className="phase2-filters">
        <input
          type="search"
          aria-label="Buscar formatos por nombre, banco o divisa"
          placeholder="Buscar por nombre/banco/divisa"
          value={search}
          onChange={(e) => {
            setPage(1);
            setSearch(e.target.value);
          }}
        />
        <PageSizeSelect
          value={pageSize}
          options={[10, 20, 50]}
          onChange={(next) => {
            setPage(1);
            setPageSize(next);
          }}
        />
        {isAdmin && (
          <label>
            <input
              type="checkbox"
              checked={incluirEliminados}
              onChange={(e) => {
                setPage(1);
                setIncluirEliminados(e.target.checked);
              }}
            />
            Ver eliminados
          </label>
        )}
      </div>

      {error && <p className="auth-error">{error}</p>}

      <div className="phase2-grid">
        <div className="users-table-card">
          {loading ? <p className="import-muted">Cargando formatos...</p> : null}
          {!loading && items.length === 0 ? (
            <EmptyState
              title="No hay formatos con estos filtros."
              subtitle="Crea un formato para importar extractos bancarios sin mapear columnas a mano cada vez."
            />
          ) : null}
          {!loading && items.length > 0 && (
            <div className="users-table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Banco</th>
                  <th>Divisa</th>
                  <th>Columnas base</th>
                  <th>Extra</th>
                  <th>Estado</th>
                  {isAdmin && <th>Acciones</th>}
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={item.id}>
                    <td>{item.banco_nombre || EMPTY_VALUE}</td>
                    <td>{item.divisa || EMPTY_VALUE}</td>
                    <td>
                      {(item.mapeo_json?.tipo_monto ?? 'una_columna') === 'tres_columnas'
                        ? `F:${item.mapeo_json?.fecha ?? '-'} / C:${item.mapeo_json?.concepto ?? '-'} / I:${item.mapeo_json?.ingreso ?? '-'} / E:${item.mapeo_json?.egreso ?? '-'} / M:${item.mapeo_json?.monto ?? '-'} / S:${item.mapeo_json?.saldo ?? '-'}`
                        : (item.mapeo_json?.tipo_monto ?? 'una_columna') === 'dos_columnas'
                          ? `F:${item.mapeo_json?.fecha ?? '-'} / C:${item.mapeo_json?.concepto ?? '-'} / I:${item.mapeo_json?.ingreso ?? '-'} / E:${item.mapeo_json?.egreso ?? '-'} / S:${item.mapeo_json?.saldo ?? '-'}`
                          : `F:${item.mapeo_json?.fecha ?? '-'} / C:${item.mapeo_json?.concepto ?? '-'} / M:${item.mapeo_json?.monto ?? '-'} / S:${item.mapeo_json?.saldo ?? '-'}`}
                    </td>
                    <td>
                      {(item.mapeo_json?.columnas_extra?.length ?? 0) === 0
                        ? <span style={{ color: 'var(--color-text-secondary)' }}>—</span>
                        : (
                          <span style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-1)' }}>
                            {item.mapeo_json!.columnas_extra!.map((col, i) => (
                              <span
                                key={i}
                                style={{
                                  display: 'inline-block',
                                  padding: '0.1em 0.45em',
                                  borderRadius: '0.85em',
                                  fontSize: 'var(--font-size-xs)',
                                  background: 'var(--surface-bg-raised)',
                                  color: 'var(--color-text-secondary)',
                                  border: '1px solid var(--surface-border)',
                                }}
                              >
                                {col.nombre}
                              </span>
                            ))}
                          </span>
                        )
                      }
                    </td>
                    <td>
                      {item.deleted_at
                        ? <span className="users-badge users-badge--danger">Eliminado</span>
                        : item.activo
                          ? <span className="users-badge users-badge--ok">Activo</span>
                          : <span className="users-badge">Inactivo</span>}
                    </td>
                    {isAdmin && (
                      <td className="phase2-row-actions">
                        <button type="button" onClick={() => startEdit(item.id)} disabled={saving} aria-label={`Editar formato ${item.nombre}`}>Editar</button>
                        {!item.deleted_at ? (
                          <button
                            type="button"
                            onClick={() => setDeleteCandidate({ id: item.id, nombre: item.nombre })}
                            disabled={saving}
                            aria-label={`Eliminar formato ${item.nombre}`}
                          >
                            Eliminar
                          </button>
                        ) : (
                          <button type="button" onClick={() => restore(item.id)} disabled={saving} aria-label={`Restaurar formato ${item.nombre}`}>Restaurar</button>
                        )}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
            </div>
          )}
          <div className="users-pagination">
            <button type="button" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1}>Anterior</button>
            <span>Página {page} / {totalPages}</span>
            <button type="button" onClick={() => setPage((p) => Math.min(totalPages, p + 1))} disabled={page >= totalPages}>Siguiente</button>
          </div>
        </div>

        {isAdmin && (
          <form
            className="users-form-card"
            onSubmit={(e) => {
              e.preventDefault();
              void save();
            }}
          >
            <h2>{editingId ? 'Editar Formato' : 'Nuevo Formato'}</h2>

            <label>Banco</label>
            <input value={form.banco_nombre} onChange={(e) => setForm((prev) => ({ ...prev, banco_nombre: e.target.value }))} />

            <label>Divisa</label>
            <AppSelect
              ariaLabel="Divisa"
              value={form.divisa}
              options={divisas.map((divisa) => ({
                value: divisa.codigo,
                label: `${divisa.codigo} ${divisa.nombre ? `- ${divisa.nombre}` : ''}`,
              }))}
              onChange={(next) => setForm((prev) => ({ ...prev, divisa: next }))}
            />

            <label>Tipo de importe</label>
            <AppSelect
              ariaLabel="Tipo de importe"
              value={form.tipo_monto}
              options={[
                { value: 'una_columna', label: 'Una columna: Importe firmado' },
                { value: 'dos_columnas', label: 'Dos columnas: Ingreso y Egreso' },
                { value: 'tres_columnas', label: 'Tres columnas: Ingreso, Egreso e Importe' },
              ]}
              onChange={(next) => updateTipoMonto(next as TipoMontoImportacion)}
            />

            <fieldset>
              <legend>Orden de columnas de importación</legend>
              <p style={{ color: 'var(--color-text-secondary)', fontSize: 'var(--font-size-sm)', marginBottom: 'var(--space-2)' }}>
                Define el orden real del archivo bancario. En dos/tres columnas, Ingreso/Egreso calculan el importe firmado; Importe banco solo valida cuadre.
              </p>
              <div style={{ display: 'grid', gap: 'var(--space-2)' }}>
                {form.columnas.map((col, index) => (
                  <div key={`${col.tipo}-${index}`} className="extra-col-row">
                    <span style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
                      <strong style={{ minWidth: '2rem', textAlign: 'center', color: 'var(--color-text-secondary)', fontSize: 'var(--font-size-sm)' }}>
                        [{index}]
                      </strong>
                      {col.tipo === 'base' ? (
                        <span>{col.nombre}</span>
                      ) : (
                        <input
                          type="text"
                          placeholder="Nombre de columna"
                          value={col.nombre}
                          onChange={(e) => updateColumnName(index, e.target.value)}
                          style={{ flex: 1 }}
                        />
                      )}
                    </span>
                    <div style={{ display: 'flex', gap: 'var(--space-1)', alignItems: 'center' }}>
                      <button
                        type="button"
                        className="extra-col-btn-icon"
                        onClick={() => moveColumn(index, 'up')}
                        disabled={index === 0}
                        title="Mover arriba"
                        aria-label={`Mover columna ${col.nombre} arriba`}
                      >
                        ↑
                      </button>
                      <button
                        type="button"
                        className="extra-col-btn-icon"
                        onClick={() => moveColumn(index, 'down')}
                        disabled={index === form.columnas.length - 1}
                        title="Mover abajo"
                        aria-label={`Mover columna ${col.nombre} abajo`}
                      >
                        ↓
                      </button>
                      {col.tipo === 'extra' && (
                        <button type="button" onClick={() => removeColumn(index)} aria-label={`Quitar columna ${col.nombre}`}>Quitar</button>
                      )}
                    </div>
                  </div>
                ))}
              </div>
              <button type="button" onClick={addExtraColumn} style={{ marginTop: 'var(--space-2)' }}>Añadir columna extra</button>
            </fieldset>

            <label className="users-check-row-item">
              <input
                type="checkbox"
                className="users-check-input"
                checked={form.activo}
                onChange={(e) => setForm((prev) => ({ ...prev, activo: e.target.checked }))}
              />
              <span>Formato activo</span>
            </label>

            <div className="users-form-actions">
              <button type="button" onClick={resetForm} disabled={saving}>Limpiar</button>
              <button type="submit" disabled={saving}>{saving ? 'Guardando...' : 'Guardar'}</button>
            </div>
          </form>
        )}
      </div>

      <ConfirmDialog
        open={!!deleteCandidate}
        title="Eliminar formato"
        message={
          deleteCandidate
            ? `Vas a enviar a papelera el formato ${deleteCandidate.nombre}. Seguirás pudiendo restaurarlo y la acción quedará auditada.`
            : ''
        }
        confirmLabel="Confirmar eliminación"
        loadingLabel="Enviando..."
        loading={saving}
        onCancel={() => setDeleteCandidate(null)}
        onConfirm={remove}
      />
    </section>
  );
}
