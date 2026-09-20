import type { KeyboardEvent } from 'react';

interface DivisaSelectorProps {
  value: string;
  options: string[];
  onChange: (next: string) => void;
  label?: string;
}

export function DivisaSelector({ value, options, onChange, label = 'Divisa principal' }: DivisaSelectorProps) {
  const moveSelection = (event: KeyboardEvent<HTMLButtonElement>, index: number, offset: number) => {
    event.preventDefault();
    const nextIndex = (index + offset + options.length) % options.length;
    onChange(options[nextIndex]);

    window.requestAnimationFrame(() => {
      event.currentTarget.parentElement
        ?.querySelectorAll<HTMLButtonElement>('[role="radio"]')
        .item(nextIndex)
        ?.focus();
    });
  };

  return (
    <div className="dashboard-divisa-tabs dashboard-select-control" role="radiogroup" aria-label={label}>
      <span className="dashboard-periodo-label">{label}</span>
      <div className="ab-tabs">
        {options.map((divisa, index) => (
          <button
            key={divisa}
            type="button"
            className="ab-tab"
            role="radio"
            aria-checked={divisa === value}
            tabIndex={divisa === value ? 0 : -1}
            onClick={() => onChange(divisa)}
            onKeyDown={(event) => {
              if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
                moveSelection(event, index, 1);
              } else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
                moveSelection(event, index, -1);
              } else if (event.key === 'Home') {
                moveSelection(event, index, -index);
              } else if (event.key === 'End') {
                moveSelection(event, index, options.length - index - 1);
              }
            }}
          >
            {divisa}
          </button>
        ))}
      </div>
    </div>
  );
}
