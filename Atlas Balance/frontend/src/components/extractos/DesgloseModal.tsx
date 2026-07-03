import { useEffect, useMemo, useState } from 'react';
import { Plus, Trash2 } from 'lucide-react';
import { CloseIconButton } from '@/components/common/CloseIconButton';
import { useDialogFocus } from '@/hooks/useDialogFocus';
import type { Extracto, ExtractoDesgloseResumen } from '@/types';
import { formatCurrency, parseEuropeanNumber } from '@/utils/formatters';

interface DesgloseModalProps {
  open: boolean;
  row: Extracto | null;
  data: ExtractoDesgloseResumen | null;
  loading: boolean;
  saving: boolean;
  error: string | null;
  canEdit: boolean;
  onClose: () => void;
  onSave: (lineas: DesgloseDraftPayload[]) => Promise<void>;
}

export interface DesgloseDraftPayload {
  id?: string;
  tercero_nombre: string;
  importe: number;
  notas?: string | null;
}

interface DesgloseDraftLine {
  id?: string;
  tercero_nombre: string;
  importe: string;
  notas: string;
}

export default function DesgloseModal({
  open,
  row,
  data,
  loading,
  saving,
  error,
  canEdit,
  onClose,
  onSave
}: DesgloseModalProps) {
  const dialogRef = useDialogFocus<HTMLDivElement>(open, {
    onEscape: onClose,
  });
  const [draft, setDraft] = useState<DesgloseDraftLine[]>([]);
  const [localError, setLocalError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) {
      setDraft([]);
      setLocalError(null);
      return;
    }

    setDraft(
      data?.lineas.map((line) => ({
        id: line.id,
        tercero_nombre: line.tercero_nombre,
        importe: String(line.importe),
        notas: line.notas ?? '',
      })) ?? [],
    );
    setLocalError(null);
  }, [data, open]);

  const draftTotal = useMemo(
    () => draft.reduce((sum, line) => sum + (parseEuropeanNumber(line.importe) ?? 0), 0),
    [draft],
  );
  const extractoMonto = row?.monto ?? data?.extracto_monto ?? 0;
  const diferencia = extractoMonto - draftTotal;
  const estado = draft.length === 0 ? 'sin_desglose' : Math.round(draftTotal * 10000) === Math.round(extractoMonto * 10000) ? 'cuadrado' : 'descuadrado';

  if (!open || !row) {
    return null;
  }

  const addLine = () => {
    setDraft((current) => [...current, { tercero_nombre: '', importe: '', notas: '' }]);
  };

  const updateLine = (index: number, patch: Partial<DesgloseDraftLine>) => {
    setDraft((current) => current.map((line, i) => (i === index ? { ...line, ...patch } : line)));
  };

  const removeLine = (index: number) => {
    setDraft((current) => current.filter((_, i) => i !== index));
  };

  const save = async () => {
    const payload: DesgloseDraftPayload[] = [];
    for (let index = 0; index < draft.length; index += 1) {
      const line = draft[index];
      const terceroNombre = line.tercero_nombre.trim();
      if (!terceroNombre) {
        setLocalError(`La linea ${index + 1} necesita nombre.`);
        return;
      }

      const importe = parseEuropeanNumber(line.importe);
      if (importe === null || importe === 0) {
        setLocalError(`La linea ${index + 1} necesita un importe distinto de cero.`);
        return;
      }

      payload.push({
        id: line.id,
        tercero_nombre: terceroNombre,
        importe,
        notas: line.notas.trim() || null,
      });
    }

    setLocalError(null);
    await onSave(payload);
  };

  return (
    <div className="modal-backdrop" role="presentation" onClick={saving ? undefined : onClose}>
      <div
        ref={dialogRef}
        className="desglose-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="desglose-modal-title"
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
      >
        <header className="desglose-modal-header">
          <div>
            <h3 id="desglose-modal-title">Desglose del extracto</h3>
            <p>{row.fecha} - {row.concepto || 'Sin concepto'}</p>
          </div>
          <CloseIconButton onClick={onClose} ariaLabel="Cerrar desglose" />
        </header>

        <section className={`desglose-summary desglose-summary--${estado}`}>
          <span>Extracto: <strong>{formatCurrency(extractoMonto, row.divisa ?? 'EUR')}</strong></span>
          <span>Desglose: <strong>{formatCurrency(draftTotal, row.divisa ?? 'EUR')}</strong></span>
          <span>Diferencia: <strong>{formatCurrency(diferencia, row.divisa ?? 'EUR')}</strong></span>
        </section>

        {loading ? (
          <p>Cargando desglose...</p>
        ) : (
          <>
            {(error || localError) ? <p className="auth-error" role="alert">{localError ?? error}</p> : null}
            <div className="desglose-lines" role="table" aria-label="Lineas del desglose">
              <div className="desglose-line desglose-line--head" role="row">
                <span role="columnheader">Persona</span>
                <span role="columnheader">Importe</span>
                <span role="columnheader">Notas</span>
                <span role="columnheader">Accion</span>
              </div>
              {draft.length === 0 ? (
                <p className="desglose-empty">Sin lineas de desglose.</p>
              ) : (
                draft.map((line, index) => (
                  <div className="desglose-line" role="row" key={line.id ?? `new-${index}`}>
                    <input
                      value={line.tercero_nombre}
                      disabled={!canEdit || saving}
                      aria-label={`Persona de la linea ${index + 1}`}
                      onChange={(event) => updateLine(index, { tercero_nombre: event.target.value })}
                    />
                    <input
                      value={line.importe}
                      disabled={!canEdit || saving}
                      aria-label={`Importe de la linea ${index + 1}`}
                      inputMode="decimal"
                      onChange={(event) => updateLine(index, { importe: event.target.value })}
                    />
                    <input
                      value={line.notas}
                      disabled={!canEdit || saving}
                      aria-label={`Notas de la linea ${index + 1}`}
                      onChange={(event) => updateLine(index, { notas: event.target.value })}
                    />
                    <button
                      type="button"
                      className="desglose-icon-button"
                      disabled={!canEdit || saving}
                      title="Eliminar linea"
                      aria-label={`Eliminar linea ${index + 1}`}
                      onClick={() => removeLine(index)}
                    >
                      <Trash2 size={16} aria-hidden="true" />
                    </button>
                  </div>
                ))
              )}
            </div>
          </>
        )}

        <footer className="desglose-modal-actions">
          <button type="button" onClick={addLine} disabled={!canEdit || saving || loading}>
            <Plus size={16} aria-hidden="true" />
            Anadir linea
          </button>
          <button type="button" onClick={onClose} disabled={saving}>Cancelar</button>
          <button type="button" className="primary" onClick={() => void save()} disabled={!canEdit || saving || loading}>
            {saving ? 'Guardando...' : 'Guardar'}
          </button>
        </footer>
      </div>
    </div>
  );
}
