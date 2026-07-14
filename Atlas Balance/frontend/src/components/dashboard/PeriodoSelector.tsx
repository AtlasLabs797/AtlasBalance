import type { KeyboardEvent } from 'react';
import type { PeriodoDashboard } from '@/types';

const PERIODOS_DASHBOARD: PeriodoDashboard[] = ['1m', '3m', '6m', '9m', '12m', '18m', '24m'];

interface PeriodoSelectorProps {
  value: PeriodoDashboard;
  onChange: (next: PeriodoDashboard) => void;
  label?: string;
  className?: string;
}

export function PeriodoSelector({
  value,
  onChange,
  label = 'Periodo',
  className = 'dashboard-periodo dashboard-select-control',
}: PeriodoSelectorProps) {
  const moveSelection = (event: KeyboardEvent<HTMLButtonElement>, index: number, offset: number) => {
    event.preventDefault();
    const nextIndex = (index + offset + PERIODOS_DASHBOARD.length) % PERIODOS_DASHBOARD.length;
    const nextValue = PERIODOS_DASHBOARD[nextIndex];
    onChange(nextValue);

    window.requestAnimationFrame(() => {
      event.currentTarget.parentElement
        ?.querySelectorAll<HTMLButtonElement>('[role="radio"]')
        .item(nextIndex)
        ?.focus();
    });
  };

  return (
    <div className={`${className} dashboard-periodo-tabs`} role="radiogroup" aria-label={label}>
      <span className="dashboard-periodo-label">{label}</span>
      <div className="ab-tabs">
        {PERIODOS_DASHBOARD.map((periodo, index) => (
          <button
            key={periodo}
            type="button"
            className="ab-tab"
            role="radio"
            aria-checked={periodo === value}
            tabIndex={periodo === value ? 0 : -1}
            onClick={() => onChange(periodo)}
            onKeyDown={(event) => {
              if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
                moveSelection(event, index, 1);
              } else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
                moveSelection(event, index, -1);
              } else if (event.key === 'Home') {
                moveSelection(event, index, -index);
              } else if (event.key === 'End') {
                moveSelection(event, index, PERIODOS_DASHBOARD.length - index - 1);
              }
            }}
          >
            {periodo}
          </button>
        ))}
      </div>
    </div>
  );
}
