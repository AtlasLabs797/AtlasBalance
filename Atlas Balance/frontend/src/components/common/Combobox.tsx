import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';

export interface ComboboxOption {
  value: string;
  label?: string;
  disabled?: boolean;
}

interface ComboboxProps {
  value: string;
  options: ComboboxOption[];
  onChange: (next: string) => void;
  placeholder?: string;
  ariaLabel?: string;
  className?: string;
  disabled?: boolean;
  emptyHint?: string;
}

const normalizeForFilter = (raw: string) => raw.trim().toLowerCase();

export function Combobox({
  value,
  options,
  onChange,
  placeholder,
  ariaLabel,
  className,
  disabled = false,
  emptyHint,
}: ComboboxProps) {
  const inputId = useId();
  const listId = useId();
  const rootRef = useRef<HTMLDivElement | null>(null);
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState<number>(-1);

  const normalizedValue = normalizeForFilter(value);

  const filteredOptions = useMemo(() => {
    return options
      .filter((option) => !option.disabled)
      .filter((option) => {
        if (!normalizedValue) return true;
        return normalizeForFilter(option.label ?? option.value).includes(normalizedValue);
      })
      .slice(0, 12);
  }, [options, normalizedValue]);

  const exactMatch = filteredOptions.some(
    (option) => normalizeForFilter(option.value) === normalizedValue,
  );

  const showCreateHint = !!normalizedValue && !exactMatch;

  useEffect(() => {
    if (activeIndex >= filteredOptions.length) {
      setActiveIndex(filteredOptions.length - 1);
    }
  }, [activeIndex, filteredOptions.length]);

  const closeAndReset = useCallback(() => {
    setOpen(false);
    setActiveIndex(-1);
  }, []);

  useEffect(() => {
    const handler = (event: MouseEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) {
        closeAndReset();
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [closeAndReset]);

  const selectOption = (next: string) => {
    onChange(next);
    closeAndReset();
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setOpen(true);
      setActiveIndex((current) => {
        const next = current + 1;
        return next >= filteredOptions.length ? 0 : next;
      });
      return;
    }
    if (event.key === 'ArrowUp') {
      event.preventDefault();
      setOpen(true);
      setActiveIndex((current) => {
        const next = current - 1;
        return next < 0 ? filteredOptions.length - 1 : next;
      });
      return;
    }
    if (event.key === 'Enter') {
      if (open && activeIndex >= 0 && activeIndex < filteredOptions.length) {
        event.preventDefault();
        selectOption(filteredOptions[activeIndex].value);
        return;
      }
      if (open) {
        event.preventDefault();
        closeAndReset();
        return;
      }
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      closeAndReset();
      return;
    }
    if (event.key === 'Tab') {
      closeAndReset();
    }
  };

  const showList = open && (filteredOptions.length > 0 || showCreateHint);
  const activeDescendant =
    open && activeIndex >= 0 && activeIndex < filteredOptions.length
      ? `${listId}-option-${activeIndex}`
      : undefined;

  return (
    <div
      ref={rootRef}
      className={['app-combobox', className].filter(Boolean).join(' ')}
      data-state={open ? 'open' : 'closed'}
    >
      <input
        id={inputId}
        type="text"
        role="combobox"
        className="app-combobox-input"
        value={value}
        disabled={disabled}
        placeholder={placeholder}
        aria-label={ariaLabel}
        aria-autocomplete="list"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listId}
        aria-activedescendant={activeDescendant}
        autoComplete="off"
        spellCheck={false}
        onChange={(event) => {
          onChange(event.target.value);
          setOpen(true);
          setActiveIndex(-1);
        }}
        onFocus={() => {
          if (filteredOptions.length > 0 || showCreateHint) {
            setOpen(true);
          }
        }}
        onKeyDown={handleKeyDown}
      />
      {showList ? (
        <ul id={listId} role="listbox" className="app-combobox-list">
          {filteredOptions.map((option, index) => {
            const isActive = index === activeIndex;
            return (
              <li
                key={`${option.value}-${index}`}
                id={`${listId}-option-${index}`}
                role="option"
                aria-selected={isActive}
                className={['app-combobox-option', isActive ? 'app-combobox-option--active' : '']
                  .filter(Boolean)
                  .join(' ')}
                onMouseDown={(event) => {
                  event.preventDefault();
                  selectOption(option.value);
                }}
                onMouseEnter={() => setActiveIndex(index)}
              >
                {option.label ?? option.value}
              </li>
            );
          })}
          {showCreateHint ? (
            <li className="app-combobox-hint" role="presentation" aria-disabled="true">
              {emptyHint ?? `Crear "${value}"`}
            </li>
          ) : null}
        </ul>
      ) : null}
    </div>
  );
}
