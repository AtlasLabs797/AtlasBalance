import { useMemo, useState } from 'react';

import { AppSelect } from '@/components/common/AppSelect';
import { DatePickerField } from '@/components/common/DatePickerField';

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

  const canSubmit = useMemo(() => !!cuentaId && !!fecha && monto !== '' && saldo !== '', [cuentaId, fecha, monto, saldo]);

  return (
    <form
      className="add-row-form"
      onSubmit={(e) => {
        e.preventDefault();
        if (!canSubmit) {
          return;
        }
        setSaving(true);
        void onCreate({
          cuenta_id: cuentaId,
          fecha,
          concepto,
          comentarios,
          monto: Number(monto),
          saldo: Number(saldo),
          columnas_extra: extras
        }).finally(() => setSaving(false));
      }}
    >
      <h3>Agregar fila manual</h3>
      <div className="add-row-grid">
        <AppSelect
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
        <DatePickerField ariaLabel="Fecha" value={fecha} onChange={setFecha} />
        <input placeholder="Concepto" value={concepto} onChange={(e) => setConcepto(e.target.value)} />
        <input placeholder="Comentarios" value={comentarios} onChange={(e) => setComentarios(e.target.value)} />
        <input type="number" step="0.0001" placeholder="Monto" value={monto} onChange={(e) => setMonto(e.target.value)} />
        <input type="number" step="0.0001" placeholder="Saldo" value={saldo} onChange={(e) => setSaldo(e.target.value)} />
      </div>
      {extraColumns.length > 0 && (
        <div className="add-row-extra">
          {extraColumns.map((name) => (
            <input
              key={name}
              placeholder={name}
              value={extras[name] ?? ''}
              onChange={(e) => setExtras((prev) => ({ ...prev, [name]: e.target.value }))}
            />
          ))}
        </div>
      )}
      <button type="submit" disabled={!canSubmit || saving}>
        {saving ? 'Guardando...' : 'Agregar fila'}
      </button>
    </form>
  );
}
