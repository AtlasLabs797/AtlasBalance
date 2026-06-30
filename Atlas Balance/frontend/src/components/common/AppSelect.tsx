import { useId } from 'react';

export interface AppSelectOption {
  value: string;
  label: string;
  disabled?: boolean;
}

interface AppSelectProps {
  value: string;
  options: AppSelectOption[];
  onChange: (next: string) => void;
  label?: string;
  ariaLabel?: string;
  className?: string;
  disabled?: boolean;
}

export function AppSelect({ value, options, onChange, label, ariaLabel, className, disabled = false }: AppSelectProps) {
  const selectId = useId();

  return (
    <label className={['app-select-field', className].filter(Boolean).join(' ')} htmlFor={selectId}>
      {label ? <span className="app-select-label">{label}</span> : null}
      <span className="app-select-native-wrap">
        <select
          id={selectId}
          className="app-select-trigger app-select-native"
          disabled={disabled}
          value={value}
          aria-label={label ? undefined : ariaLabel}
          onChange={(event) => onChange(event.target.value)}
        >
          {options.map((option) => (
            <option key={option.value} value={option.value} disabled={option.disabled}>
              {option.label}
            </option>
          ))}
        </select>
        <span className="app-select-chevron" aria-hidden="true" />
      </span>
    </label>
  );
}
