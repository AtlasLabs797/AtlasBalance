import { AppSelect } from '@/components/common/AppSelect';

interface PageSizeSelectProps {
  value: number;
  options: number[];
  onChange: (next: number) => void;
  ariaLabel?: string;
  className?: string;
}

export function PageSizeSelect({
  value,
  options,
  onChange,
  ariaLabel = 'Filas por pagina',
  className = 'pagination-select',
}: PageSizeSelectProps) {
  return (
    <AppSelect
      className={className}
      ariaLabel={ariaLabel}
      value={String(value)}
      options={options.map((option) => ({ value: String(option), label: String(option) }))}
      onChange={(next) => onChange(Number(next))}
    />
  );
}
