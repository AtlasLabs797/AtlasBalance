import { createPortal } from 'react-dom';
import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent } from 'react';

export interface SearchableSelectOption {
  value: string;
  label: string;
  disabled?: boolean;
}

interface SearchableSelectProps {
  value: string;
  options: SearchableSelectOption[];
  onChange: (next: string) => void;
  label?: string;
  ariaLabel: string;
  placeholder?: string;
  className?: string;
  disabled?: boolean;
  noResultsLabel?: string;
}

const normalize = (value: string) => value.trim().toLocaleLowerCase('es');

export function SearchableSelect({
  value,
  options,
  onChange,
  label,
  ariaLabel,
  placeholder,
  className,
  disabled = false,
  noResultsLabel = 'No hay coincidencias',
}: SearchableSelectProps) {
  const rootRef = useRef<HTMLDivElement | null>(null);
  const menuRef = useRef<HTMLDivElement | null>(null);
  const controlRef = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const inputId = useId();
  const listId = useId();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [activeIndex, setActiveIndex] = useState(-1);
  const [menuPosition, setMenuPosition] = useState({ top: 0, left: 0, width: 0 });

  const selectedOption = options.find((option) => option.value === value) ?? options[0];
  const normalizedQuery = normalize(query);
  const filteredOptions = useMemo(
    () =>
      options.filter((option) => {
        if (option.disabled) return false;
        return !normalizedQuery || normalize(option.label).includes(normalizedQuery);
      }),
    [normalizedQuery, options],
  );

  const updateMenuPosition = useCallback(() => {
    const control = controlRef.current;
    if (!control) return;

    const rect = control.getBoundingClientRect();
    const width = rect.width;
    const left = Math.max(8, Math.min(rect.left, window.innerWidth - width - 8));
    const top = Math.min(rect.bottom + 6, window.innerHeight - 8);
    setMenuPosition({ top, left, width });
  }, []);

  const closeMenu = useCallback(() => {
    setOpen(false);
    setQuery('');
    setActiveIndex(-1);
  }, []);

  const openMenu = () => {
    if (disabled) return;

    const selectedIndex = options
      .filter((option) => !option.disabled)
      .findIndex((option) => option.value === value);
    setQuery('');
    setActiveIndex(selectedIndex >= 0 ? selectedIndex : 0);
    updateMenuPosition();
    setOpen(true);
  };

  useEffect(() => {
    if (!open) return undefined;

    updateMenuPosition();
    const handleViewportChange = () => updateMenuPosition();
    window.addEventListener('resize', handleViewportChange);
    window.addEventListener('scroll', handleViewportChange, true);
    return () => {
      window.removeEventListener('resize', handleViewportChange);
      window.removeEventListener('scroll', handleViewportChange, true);
    };
  }, [open, updateMenuPosition]);

  useEffect(() => {
    if (!open) return undefined;

    const handleOutsidePointer = (event: MouseEvent) => {
      const target = event.target as Node;
      if (rootRef.current?.contains(target) || menuRef.current?.contains(target)) return;
      closeMenu();
    };
    document.addEventListener('mousedown', handleOutsidePointer);
    return () => document.removeEventListener('mousedown', handleOutsidePointer);
  }, [closeMenu, open]);

  useEffect(() => {
    if (activeIndex < filteredOptions.length) return;
    setActiveIndex(filteredOptions.length > 0 ? filteredOptions.length - 1 : -1);
  }, [activeIndex, filteredOptions.length]);

  const selectOption = (next: string) => {
    onChange(next);
    closeMenu();
    inputRef.current?.focus();
  };

  const handleKeyDown = (event: ReactKeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      if (!open) openMenu();
      setActiveIndex((current) => {
        if (filteredOptions.length === 0) return -1;
        const next = current + (event.key === 'ArrowDown' ? 1 : -1);
        if (next < 0) return filteredOptions.length - 1;
        return next >= filteredOptions.length ? 0 : next;
      });
      return;
    }

    if (event.key === 'Enter') {
      const activeOption =
        activeIndex >= 0 && activeIndex < filteredOptions.length
          ? filteredOptions[activeIndex]
          : filteredOptions.length === 1
            ? filteredOptions[0]
            : undefined;
      if (open && activeOption) {
        event.preventDefault();
        selectOption(activeOption.value);
      } else if (open) {
        event.preventDefault();
        closeMenu();
      }
      return;
    }

    if (event.key === 'Escape') {
      event.preventDefault();
      closeMenu();
      return;
    }

    if (event.key === 'Tab') {
      closeMenu();
    }
  };

  const activeDescendant =
    open && activeIndex >= 0 && activeIndex < filteredOptions.length
      ? `${listId}-option-${activeIndex}`
      : undefined;

  return (
    <div
      ref={rootRef}
      className={['app-select-field', 'searchable-select', className].filter(Boolean).join(' ')}
      data-state={open ? 'open' : 'closed'}
    >
      {label ? (
        <label className="app-select-label" htmlFor={inputId}>
          {label}
        </label>
      ) : null}
      <div
        ref={controlRef}
        className="searchable-select-control"
        onMouseDown={(event) => {
          if (event.target !== inputRef.current) {
            event.preventDefault();
            inputRef.current?.focus();
            openMenu();
          }
        }}
      >
        <input
          ref={inputRef}
          id={inputId}
          type="text"
          role="combobox"
          className="searchable-select-input"
          value={open ? query : selectedOption?.label ?? ''}
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
            setQuery(event.target.value);
            setOpen(true);
            setActiveIndex(-1);
          }}
          onFocus={() => {
            if (!open) openMenu();
          }}
          onClick={() => {
            if (!open) openMenu();
          }}
          onKeyDown={handleKeyDown}
        />
        <span className="app-select-chevron" aria-hidden="true" />
      </div>

      {open
        ? createPortal(
            <div
              ref={menuRef}
              id={listId}
              role="listbox"
              aria-label={ariaLabel}
              className="extractos-searchable-select-popover"
              style={{ top: menuPosition.top, left: menuPosition.left, width: menuPosition.width }}
            >
              {filteredOptions.length > 0 ? (
                filteredOptions.map((option, index) => (
                  <div
                    key={option.value}
                    id={`${listId}-option-${index}`}
                    role="option"
                    aria-selected={option.value === value}
                    className="extractos-searchable-select-option"
                    data-active={index === activeIndex ? 'true' : undefined}
                    onMouseDown={(event) => {
                      event.preventDefault();
                      selectOption(option.value);
                    }}
                    onMouseEnter={() => setActiveIndex(index)}
                  >
                    <span>{option.label}</span>
                    {option.value === value ? <span aria-hidden="true">✓</span> : null}
                  </div>
                ))
              ) : (
                <div className="extractos-searchable-select-empty" role="status">
                  {noResultsLabel}
                </div>
              )}
            </div>,
            document.body,
          )
        : null}
    </div>
  );
}
