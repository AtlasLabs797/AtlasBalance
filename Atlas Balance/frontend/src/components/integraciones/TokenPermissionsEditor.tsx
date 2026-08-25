import { AppSelect } from '@/components/common/AppSelect';

interface CatalogoPermisos {
  paises: Array<{ id: string; nombre: string }>;
  titulares: Array<{ id: string; nombre: string }>;
  cuentas: Array<{ id: string; nombre: string; titular_id: string; pais_id: string | null }>;
}

export interface TokenPermisoDraft {
  pais_id: string | null;
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
  const addPermiso = () =>
    onChange([...permisos, { pais_id: null, titular_id: null, cuenta_id: null, acceso_tipo: 'lectura' }]);
  const removePermiso = (index: number) => onChange(permisos.filter((_, i) => i !== index));

  return (
    <div className="config-token-perm-list">
      {permisos.length === 0 ? (
        <p className="import-muted">Anade al menos un alcance. Sin alcance, el token no podra ver ni escribir datos.</p>
      ) : null}
      {permisos.map((permiso, index) => {
        const titularesFiltrados = permiso.pais_id
          ? catalogos.titulares.filter((titular) =>
              catalogos.cuentas.some((cuenta) => cuenta.pais_id === permiso.pais_id && cuenta.titular_id === titular.id)
            )
          : catalogos.titulares;
        const cuentasFiltradas = catalogos.cuentas
          .filter((cuenta) => !permiso.pais_id || cuenta.pais_id === permiso.pais_id)
          .filter((cuenta) => !permiso.titular_id || cuenta.titular_id === permiso.titular_id);

        return (
          <div className="config-token-perm-row" key={`permiso-${index}`}>
            <AppSelect
              label="Pais"
              value={permiso.pais_id ?? ''}
              options={[
                { value: '', label: 'Todos' },
                ...catalogos.paises.map((pais) => ({ value: pais.id, label: pais.nombre })),
              ]}
              onChange={(nextValue) => {
                const next = permisos.map((p, i) =>
                  i === index ? { ...p, pais_id: nextValue || null, titular_id: null, cuenta_id: null } : p);
                onChange(next);
              }}
            />
            <AppSelect
              label="Titular"
              value={permiso.titular_id ?? ''}
              options={[
                { value: '', label: 'Todos' },
                ...titularesFiltrados.map((titular) => ({ value: titular.id, label: titular.nombre })),
              ]}
              onChange={(nextValue) => {
                const next = permisos.map((p, i) =>
                  i === index ? { ...p, titular_id: nextValue || null, cuenta_id: null } : p);
                onChange(next);
              }}
            />
            <AppSelect
              label="Cuenta"
              value={permiso.cuenta_id ?? ''}
              options={[
                { value: '', label: 'Todas' },
                ...cuentasFiltradas.map((cuenta) => ({ value: cuenta.id, label: cuenta.nombre })),
              ]}
              onChange={(nextValue) => {
                const next = permisos.map((p, i) =>
                  i === index ? { ...p, cuenta_id: nextValue || null } : p);
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
            <button type="button" onClick={() => removePermiso(index)} aria-label={`Quitar permiso ${index + 1}`}>
              Quitar
            </button>
          </div>
        );
      })}
      <div className="import-actions">
        <button type="button" onClick={addPermiso}>Añadir permiso</button>
      </div>
    </div>
  );
}
