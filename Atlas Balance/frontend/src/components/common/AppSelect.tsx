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
  const [open, setOpen] = useState(false);
  const [openedByKeyboard, setOpenedByKeyboard] = useState(false);
  const rootRef = useRef<HTMLDivElement | null>(null);
  const labelId = useId();
  const listboxId = useId();
  const selectedTextId = useId();
  const selected = options.find((option) => option.value === value);
  const selectedIndex = Math.max(0, options.findIndex((option) => option.value === value));
  const activeOptionId = `${listboxId}-option-${selectedIndex}`;

  useEffect(() => {
    if (!open) {
      return undefined;
    }

    const handlePointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) {
        setOpenedByKeyboard(false);
        setOpen(false);
      }
    };

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpenedByKeyboard(false);
        setOpen(false);
      }
    };

    document.addEventListener('pointerdown', handlePointerDown);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('pointerdown', handlePointerDown);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [open]);

  const chooseByOffset = (offset: number) => {
    const enabled = options.filter((option) => !option.disabled);
    const enabledIndex = enabled.findIndex((option) => option.value === value);
    const next = enabled[Math.max(0, Math.min(enabled.length - 1, enabledIndex + offset))];
    if (next) {
      onChange(next.value);
    }
  };

  return (
    <div ref={rootRef} className={['app-select-field', className].filter(Boolean).join(' ')}>
      {label ? <span id={labelId} className="app-select-label">{label}</span> : null}
      <button
        type="button"
        role="combobox"
        className="app-select-trigger"
        disabled={disabled}
        aria-label={label ? undefined : ariaLabel}
        aria-labelledby={label ? `${labelId} ${selectedTextId}` : undefined}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listboxId}
        aria-activedescendant={open ? activeOptionId : undefined}
        title={selected?.label ?? value}
        onClick={() => {
          setOpenedByKeyboard(false);
          setOpen((current) => !current);
        }}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            if (!open) {
              setOpenedByKeyboard(true);
              setOpen(true);
            } else {
              chooseByOffset(event.key === 'ArrowDown' ? 1 : -1);
            }
          }

          if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            setOpen((current) => {
              const next = !current;
              setOpenedByKeyboard(next);
              return next;
            });
          }

          if (event.key === 'Home' || event.key === 'End') {
            event.preventDefault();
            const enabled = options.filter((option) => !option.disabled);
            const next = event.key === 'Home' ? enabled[0] : enabled[enabled.length - 1];
            if (next) {
              onChange(next.value);
            }
          }
        }}
      >
        <span id={selectedTextId}>{selected?.label ?? value}</span>
        <span className="app-select-chevron" aria-hidden="true" />
      </button>

      {open ? (
        <div
          id={listboxId}
          className="app-select-popover"
          role="listbox"
          aria-label={ariaLabel ?? label}
          data-open-source={openedByKeyboard ? 'keyboard' : 'pointer'}
        >
          {options.map((option, index) => (
            <button
              key={option.value}
              id={`${listboxId}-option-${index}`}
              type="button"
              className={option.value === value ? 'app-select-option app-select-option--selected' : 'app-select-option'}
              role="option"
              aria-selected={option.value === value}
              aria-posinset={index + 1}
              aria-setsize={options.length}
              disabled={option.disabled}
              title={option.label}
              onClick={() => {
                onChange(option.value);
                setOpenedByKeyboard(false);
                setOpen(false);
              }}
            >
              <span className="app-select-option-label">{option.label}</span>
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}
