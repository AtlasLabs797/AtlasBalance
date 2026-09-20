import { createPortal } from 'react-dom';
import { useCallback, useEffect, useId, useRef, useState } from 'react';

export interface PaisScopeOption {
  value: string;
  label: string;
}

interface PaisScopeDropdownProps {
  value: string;
  options: PaisScopeOption[];
  onChange: (next: string) => void;
  ariaLabel: string;
  compact?: boolean;
  disabled?: boolean;
}

export function PaisScopeDropdown({
  value,
  options,
  onChange,
  ariaLabel,
  compact = false,
  disabled = false,
}: PaisScopeDropdownProps) {
  const rootRef = useRef<HTMLDivElement | null>(null);
  const triggerRef = useRef<HTMLButtonElement | null>(null);
  const menuRef = useRef<HTMLDivElement | null>(null);
  const listId = useId();
  const selectedIndex = Math.max(0, options.findIndex((option) => option.value === value));
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(selectedIndex);
  const [menuPosition, setMenuPosition] = useState({ top: 0, left: 0, width: 0 });
  const selectedOption = options[selectedIndex] ?? options[0];

  const updateMenuPosition = useCallback(() => {
    const trigger = triggerRef.current;
    if (!trigger) return;

    const rect = trigger.getBoundingClientRect();
    const width = compact ? Math.max(220, rect.width) : rect.width;
    const left = Math.max(8, Math.min(compact ? rect.right + 8 : rect.left, window.innerWidth - width - 8));
    const top = Math.min(rect.bottom + 6, window.innerHeight - 8);
    setMenuPosition({ top, left, width });
  }, [compact]);

  const closeMenu = useCallback(() => {
    setOpen(false);
    setActiveIndex(selectedIndex);
  }, [selectedIndex]);

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

  const selectOption = (next: string) => {
    onChange(next);
    closeMenu();
    triggerRef.current?.focus();
  };

  const moveActive = (direction: 1 | -1) => {
    setActiveIndex((current) => {
      const next = current + direction;
      if (next < 0) return options.length - 1;
      if (next >= options.length) return 0;
      return next;
    });
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLButtonElement>) => {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      if (!open) {
        setActiveIndex(selectedIndex);
        setOpen(true);
      } else {
        moveActive(event.key === 'ArrowDown' ? 1 : -1);
      }
      return;
    }

    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      if (!open) {
        setActiveIndex(selectedIndex);
        setOpen(true);
      } else if (options[activeIndex]) {
        selectOption(options[activeIndex].value);
      }
      return;
    }

    if (event.key === 'Escape' || event.key === 'Tab') {
      closeMenu();
    }
  };

  return (
    <div ref={rootRef} className="app-select-field pais-scope-dropdown" data-state={open ? 'open' : 'closed'}>
      <button
        ref={triggerRef}
        type="button"
        className="app-select-trigger"
        disabled={disabled}
        aria-label={ariaLabel}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listId}
        aria-activedescendant={open ? `${listId}-option-${activeIndex}` : undefined}
        onClick={() => {
          setActiveIndex(selectedIndex);
          setOpen((current) => !current);
        }}
        onKeyDown={handleKeyDown}
      >
        <span>{selectedOption?.label ?? ''}</span>
        <span className="app-select-chevron" aria-hidden="true" />
      </button>

      {open
        ? createPortal(
            <div
              ref={menuRef}
              id={listId}
              role="listbox"
              aria-label={ariaLabel}
              className="app-select-popover pais-scope-popover"
              style={{ top: menuPosition.top, left: menuPosition.left, width: menuPosition.width }}
            >
              {options.map((option, index) => (
                <div
                  key={option.value}
                  id={`${listId}-option-${index}`}
                  role="option"
                  aria-selected={option.value === value}
                  className="pais-scope-option"
                  data-active={index === activeIndex ? 'true' : undefined}
                  onMouseDown={(event) => {
                    event.preventDefault();
                    selectOption(option.value);
                  }}
                  onMouseEnter={() => setActiveIndex(index)}
                >
                  <span>{option.label}</span>
                </div>
              ))}
            </div>,
            document.body,
          )
        : null}
    </div>
  );
}
