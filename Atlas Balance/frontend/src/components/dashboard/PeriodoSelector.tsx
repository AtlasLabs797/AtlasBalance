import { AppSelect } from '@/components/common/AppSelect';
import type { PeriodoDashboard } from '@/types';

const PERIODOS_DASHBOARD: PeriodoDashboard[] = ['1m', '3m', '6m', '9m', '12m', '18m', '24m'];

interface PeriodoSelectorProps {
  value: PeriodoDashboard;
  onChange: (next: PeriodoDashboard) => void;
  label?: string;
  className?: string;
}

export function PeriodoSelector({
  value,
  onChange,
  label = 'Periodo',
  className = 'dashboard-periodo dashboard-select-control',
}: PeriodoSelectorProps) {
  return (
    <AppSelect
      className={className}
      label={label}
      value={value}
      options={PERIODOS_DASHBOARD.map((periodo) => ({ value: periodo, label: periodo }))}
      onChange={(next) => onChange(next as PeriodoDashboard)}
    />
  );
}
