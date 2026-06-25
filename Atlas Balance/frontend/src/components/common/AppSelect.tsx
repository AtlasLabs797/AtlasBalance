import { useEffect, useId, useRef, useState } from 'react';

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
  const listboxId = useId();
  const rootRef = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const selectedOption = options.find((option) => option.value === value);

  useEffect(() => {
    if (!open) return;

    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    };

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false);
      }
    };

    document.addEventListener('pointerdown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);

    return () => {
      document.removeEventListener('pointerdown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open]);

  const selectOption = (next: string) => {
    onChange(next);
    setOpen(false);
  };

  return (
    <div ref={rootRef} className={['app-select-field', className].filter(Boolean).join(' ')}>
      {label ? <span id={labelId} className="app-select-label">{label}</span> : null}
      <button
        id={selectId}
        type="button"
        className="app-select-trigger"
        disabled={disabled}
        aria-label={label ? undefined : ariaLabel}
        aria-labelledby={label ? labelId : undefined}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={open ? listboxId : undefined}
        onClick={() => setOpen((current) => !current)}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown' || event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            setOpen(true);
          }
        }}
      >
        <span>{selectedOption?.label ?? value}</span>
        <span className="app-select-chevron" aria-hidden="true" />
      </button>

      {open ? (
        <div id={listboxId} className="app-select-popover" role="listbox" aria-labelledby={label ? labelId : undefined}>
          {options.map((option) => (
            <button
              key={option.value}
              type="button"
              className={`app-select-option${option.value === value ? ' app-select-option--selected' : ''}`}
              role="option"
              aria-selected={option.value === value}
              disabled={option.disabled}
              onClick={() => selectOption(option.value)}
            >
              <span className="app-select-option-label">{option.label}</span>
            </button>
          ))}
        </div>
      ) : null}

      <noscript>
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
      </noscript>
    </div>
  );
}
