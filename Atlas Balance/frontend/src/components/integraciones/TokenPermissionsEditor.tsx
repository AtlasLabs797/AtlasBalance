interface CatalogoPermisos {
  titulares: Array<{ id: string; nombre: string }>;
  cuentas: Array<{ id: string; nombre: string; titular_id: string }>;
}

import { AppSelect } from '@/components/common/AppSelect';

export interface TokenPermisoDraft {
  titular_id: string | null;
  cuenta_id: string | null;
  acceso_tipo: string;
}

interface TokenPermissionsEditorProps {
  permisos: TokenPermisoDraft[];
  onChange: (permisos: TokenPermisoDraft[]) => void;
  catalogos: CatalogoPermisos;
}

export function TokenPermissionsEditor({ permisos, onChange, catalogos }: TokenPermissionsEditorProps) {
  const addPermiso = () => onChange([...permisos, { titular_id: null, cuenta_id: null, acceso_tipo: 'lectura' }]);
  const removePermiso = (index: number) => onChange(permisos.filter((_, i) => i !== index));

  return (
    <div className="config-token-perm-list">
      {permisos.length === 0 ? (
        <p className="import-muted">Añade al menos un alcance. Sin alcance, el token no podrá ver ni escribir datos.</p>
      ) : null}
      {permisos.map((permiso, index) => (
        <div className="config-token-perm-row" key={`permiso-${index}`}>
          <AppSelect
            label="Titular"
            value={permiso.titular_id ?? ''}
            options={[
              { value: '', label: 'Todos' },
              ...catalogos.titulares.map((titular) => ({ value: titular.id, label: titular.nombre })),
            ]}
            onChange={(nextValue) => {
              const next = permisos.map((p, i) =>
                i === index
                  ? { ...p, titular_id: nextValue || null, cuenta_id: null }
                  : p);
              onChange(next);
            }}
          />
          <AppSelect
            label="Cuenta"
            value={permiso.cuenta_id ?? ''}
            options={[
              { value: '', label: 'Todas' },
              ...catalogos.cuentas
                .filter((cuenta) => !permiso.titular_id || cuenta.titular_id === permiso.titular_id)
                .map((cuenta) => ({ value: cuenta.id, label: cuenta.nombre })),
            ]}
            onChange={(nextValue) => {
              const next = permisos.map((p, i) =>
                i === index
                  ? { ...p, cuenta_id: nextValue || null }
                  : p);
              onChange(next);
            }}
          />
          <AppSelect
            label="Acceso"
            value={permiso.acceso_tipo}
            options={[
              { value: 'lectura', label: 'Lectura' },
              { value: 'escritura', label: 'Escritura' },
            ]}
            onChange={(nextValue) => {
              const next = permisos.map((p, i) =>
                i === index ? { ...p, acceso_tipo: nextValue } : p);
              onChange(next);
            }}
          />
          <button type="button" onClick={() => removePermiso(index)}>
            Quitar
          </button>
        </div>
      ))}
      <div className="import-actions">
        <button type="button" onClick={addPermiso}>Añadir permiso</button>
      </div>
    </div>
  );
}
