import { AppSelect } from '@/components/common/AppSelect';
import { usePaisScopeStore } from '@/stores/paisScopeStore';

interface PaisScopeSelectProps {
  compact?: boolean;
}

export function PaisScopeSelect({ compact = false }: PaisScopeSelectProps) {
  const selectedPaisId = usePaisScopeStore((state) => state.selectedPaisId);
  const paises = usePaisScopeStore((state) => state.paises);
  const loading = usePaisScopeStore((state) => state.loading);
  const setSelectedPaisId = usePaisScopeStore((state) => state.setSelectedPaisId);
  const options = [
    { value: '', label: compact ? 'Gen' : 'General' },
    ...paises.map((pais) => ({
      value: pais.id,
      label: compact
        ? (pais.codigo_iso2 ?? pais.nombre.slice(0, 3)).toUpperCase()
        : pais.codigo_iso2
          ? `${pais.nombre} (${pais.codigo_iso2})`
          : pais.nombre,
    })),
  ];

  return (
    <div className={`pais-scope${compact ? ' pais-scope--compact' : ''}`}>
      {!compact ? <span className="pais-scope-label">Organizacion</span> : null}
      <AppSelect
        ariaLabel="Scope global por pais"
        value={selectedPaisId}
        options={options}
        onChange={setSelectedPaisId}
        disabled={loading}
      />
    </div>
  );
}
