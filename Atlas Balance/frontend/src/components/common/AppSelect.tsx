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
  const labelId = useId();
  const selectId = useId();

  return (
    <div className={['app-select-field', className].filter(Boolean).join(' ')}>
      {label ? <label id={labelId} className="app-select-label" htmlFor={selectId}>{label}</label> : null}
      <div className="app-select-native-wrap">
        <select
          id={selectId}
          className="app-select-trigger app-select-native"
          disabled={disabled}
          value={value}
          aria-label={label ? undefined : ariaLabel}
          aria-labelledby={label ? labelId : undefined}
          onChange={(event) => onChange(event.target.value)}
        >
          {options.map((option) => (
            <option key={option.value} value={option.value} disabled={option.disabled}>
              {option.label}
            </option>
          ))}
        </select>
        <span className="app-select-chevron app-select-chevron--native" aria-hidden="true" />
      </div>
    </div>
  );
}
