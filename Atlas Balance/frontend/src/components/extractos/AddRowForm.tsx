import { useMemo, useState } from 'react';

import { AppSelect } from '@/components/common/AppSelect';
import { DatePickerField } from '@/components/common/DatePickerField';
import { extractErrorMessage } from '@/utils/errorMessage';
import { parseEuropeanNumber } from '@/utils/formatters';

interface AddRowFormProps {
  cuentas: Array<{ id: string; nombre: string; titular_nombre: string; divisa: string }>;
  extraColumns: string[];
  onCreate: (payload: {
    cuenta_id: string;
    fecha: string;
    concepto: string;
    comentarios: string;
    monto: number;
    saldo: number;
    columnas_extra: Record<string, string>;
  }) => Promise<void>;
}

export default function AddRowForm({ cuentas, extraColumns, onCreate }: AddRowFormProps) {
  const [cuentaId, setCuentaId] = useState('');
  const [fecha, setFecha] = useState(new Date().toISOString().slice(0, 10));
  const [concepto, setConcepto] = useState('');
  const [comentarios, setComentarios] = useState('');
  const [monto, setMonto] = useState('');
  const [saldo, setSaldo] = useState('');
  const [extras, setExtras] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const montoNumber = useMemo(() => parseEuropeanNumber(monto), [monto]);
  const saldoNumber = useMemo(() => parseEuropeanNumber(saldo), [saldo]);
  const canSubmit = useMemo(
    () => !!cuentaId && !!fecha && montoNumber !== null && saldoNumber !== null,
    [cuentaId, fecha, montoNumber, saldoNumber]
  );

  return (
    <form
      className="add-row-form"
      onSubmit={(e) => {
        e.preventDefault();
        if (!canSubmit) {
          return;
        }
        setSaving(true);
        setSubmitError(null);
        void onCreate({
          cuenta_id: cuentaId,
          fecha,
          concepto,
          comentarios,
          monto: montoNumber ?? 0,
          saldo: saldoNumber ?? 0,
          columnas_extra: extras
        })
          .catch((error) => {
            setSubmitError(extractErrorMessage(error, 'No se pudo agregar la fila manual.'));
          })
          .finally(() => setSaving(false));
      }}
    >
      <h3>Agregar fila manual</h3>
      <div className="add-row-grid">
        <AppSelect
          label="Cuenta"
          ariaLabel="Cuenta"
          value={cuentaId}
          options={[
            { value: '', label: 'Cuenta' },
            ...cuentas.map((c) => ({
              value: c.id,
              label: `${c.titular_nombre} - ${c.nombre} (${c.divisa})`,
            })),
          ]}
          onChange={setCuentaId}
        />
        <DatePickerField label="Fecha" ariaLabel="Fecha" value={fecha} onChange={setFecha} />
        <label className="add-row-field">
          <span>Concepto</span>
          <input value={concepto} onChange={(e) => setConcepto(e.target.value)} />
        </label>
        <label className="add-row-field">
          <span>Comentarios</span>
          <input value={comentarios} onChange={(e) => setComentarios(e.target.value)} />
        </label>
        <label className="add-row-field">
          <span>Monto</span>
          <input inputMode="decimal" value={monto} onChange={(e) => setMonto(e.target.value)} />
        </label>
        <label className="add-row-field">
          <span>Saldo</span>
          <input inputMode="decimal" value={saldo} onChange={(e) => setSaldo(e.target.value)} />
        </label>
      </div>
      {extraColumns.length > 0 && (
        <div className="add-row-extra">
          {extraColumns.map((name) => (
            <label key={name} className="add-row-field">
              <span>{name}</span>
              <input
                value={extras[name] ?? ''}
                onChange={(e) => setExtras((prev) => ({ ...prev, [name]: e.target.value }))}
              />
            </label>
          ))}
        </div>
      )}
      {submitError ? <p className="auth-error" role="alert">{submitError}</p> : null}
      <button type="submit" disabled={!canSubmit || saving}>
        {saving ? 'Guardando...' : 'Agregar fila'}
      </button>
    </form>
  );
}
