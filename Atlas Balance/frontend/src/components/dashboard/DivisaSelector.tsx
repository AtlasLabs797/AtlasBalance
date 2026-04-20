import { AppSelect } from '@/components/common/AppSelect';

interface DivisaSelectorProps {
  value: string;
  options: string[];
  onChange: (next: string) => void;
  label?: string;
}

export function DivisaSelector({ value, options, onChange, label = 'Divisa principal' }: DivisaSelectorProps) {
  return (
    <AppSelect
      className="dashboard-divisa-selector dashboard-select-control"
      label={label}
      value={value}
      options={options.map((divisa) => ({ value: divisa, label: divisa }))}
      onChange={onChange}
    />
  );
}
