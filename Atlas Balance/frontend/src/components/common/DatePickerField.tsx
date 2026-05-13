import { type KeyboardEvent as ReactKeyboardEvent, useCallback, useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { CalendarDays, ChevronLeft, ChevronRight } from 'lucide-react';

interface DatePickerFieldProps {
  value: string;
  onChange: (value: string) => void;
  ariaLabel: string;
  label?: string;
  disabled?: boolean;
  placeholder?: string;
  allowClear?: boolean;
}

const WEEKDAYS = ['L', 'M', 'X', 'J', 'V', 'S', 'D'];
const MONTH_FORMATTER = new Intl.DateTimeFormat('es-ES', { month: 'long', year: 'numeric' });
const DISPLAY_FORMATTER = new Intl.DateTimeFormat('es-ES', { day: '2-digit', month: '2-digit', year: 'numeric' });
const FULL_DATE_FORMATTER = new Intl.DateTimeFormat('es-ES', { dateStyle: 'full' });

function parseIsoDate(value: string): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return null;

  const [, year, month, day] = match;
  const date = new Date(Number(year), Number(month) - 1, Number(day));
  return Number.isNaN(date.getTime()) ? null : date;
}

function toIsoDate(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function startOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

function addMonths(date: Date, amount: number): Date {
  return new Date(date.getFullYear(), date.getMonth() + amount, 1);
}

function sameDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

function buildMonthDays(viewMonth: Date): Array<Date | null> {
  const firstDay = startOfMonth(viewMonth);
  const leadingBlanks = (firstDay.getDay() + 6) % 7;
  const daysInMonth = new Date(viewMonth.getFullYear(), viewMonth.getMonth() + 1, 0).getDate();
  const days: Array<Date | null> = Array.from({ length: leadingBlanks }, () => null);

  for (let day = 1; day <= daysInMonth; day += 1) {
    days.push(new Date(viewMonth.getFullYear(), viewMonth.getMonth(), day));
  }

  while (days.length % 7 !== 0) {
    days.push(null);
  }

  return days;
}

export function DatePickerField({
  value,
  onChange,
  ariaLabel,
  label,
  disabled = false,
  placeholder = 'Selecciona fecha',
  allowClear = true,
}: DatePickerFieldProps) {
  const dialogId = useId();
  const labelId = useId();
  const valueId = useId();
  const selectedDate = useMemo(() => parseIsoDate(value), [value]);
  const [isOpen, setIsOpen] = useState(false);
  const [viewMonth, setViewMonth] = useState(() => startOfMonth(selectedDate ?? new Date()));
  const [placement, setPlacement] = useState<'bottom' | 'top'>('bottom');
  const [horizontalPlacement, setHorizontalPlacement] = useState<'left' | 'right'>('left');
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const popoverRef = useRef<HTMLDivElement>(null);
  const today = useMemo(() => new Date(), []);
  const days = useMemo(() => buildMonthDays(viewMonth), [viewMonth]);
  const displayValue = selectedDate ? DISPLAY_FORMATTER.format(selectedDate) : placeholder;

  const closePicker = useCallback((restoreFocus = false) => {
    setIsOpen(false);

    if (restoreFocus) {
      window.setTimeout(() => triggerRef.current?.focus(), 0);
    }
  }, []);

  const focusDate = (date: Date) => {
    const iso = toIsoDate(date);
    window.setTimeout(() => {
      rootRef.current?.querySelector<HTMLButtonElement>(`[data-date="${iso}"]`)?.focus();
    }, 0);
  };

  const handleDayKeyDown = (event: ReactKeyboardEvent<HTMLButtonElement>, date: Date) => {
    const offsets: Record<string, number> = {
      ArrowLeft: -1,
      ArrowRight: 1,
      ArrowUp: -7,
      ArrowDown: 7,
    };

    if (event.key in offsets) {
      event.preventDefault();
      const nextDate = new Date(date.getFullYear(), date.getMonth(), date.getDate() + offsets[event.key]);
      if (nextDate.getMonth() !== viewMonth.getMonth() || nextDate.getFullYear() !== viewMonth.getFullYear()) {
        setViewMonth(startOfMonth(nextDate));
      }
      focusDate(nextDate);
      return;
    }

    if (event.key === 'Home' || event.key === 'End') {
      event.preventDefault();
      const weekStartIndex = days.findIndex((item) => item && sameDay(item, date));
      const rowStart = Math.floor(weekStartIndex / 7) * 7;
      const row = days.slice(rowStart, rowStart + 7).filter((item): item is Date => item !== null);
      const target = event.key === 'Home' ? row[0] : row[row.length - 1];
      if (target) focusDate(target);
    }
  };

  useEffect(() => {
    if (isOpen) {
      setViewMonth(startOfMonth(selectedDate ?? new Date()));
    }
  }, [isOpen, selectedDate]);

  useLayoutEffect(() => {
    if (!isOpen || !rootRef.current || !popoverRef.current) return;

    const triggerRect = rootRef.current.getBoundingClientRect();
    const popoverHeight = popoverRef.current.offsetHeight;
    const popoverWidth = popoverRef.current.offsetWidth;
    const spaceBelow = window.innerHeight - triggerRect.bottom;
    const spaceAbove = triggerRect.top;
    const shouldOpenUp = spaceBelow < popoverHeight + 16 && spaceAbove > spaceBelow;
    const wouldOverflowRight = triggerRect.left + popoverWidth > window.innerWidth - 16;
    const hasRoomOnLeft = triggerRect.right - popoverWidth >= 16;

    setPlacement(shouldOpenUp ? 'top' : 'bottom');
    setHorizontalPlacement(wouldOverflowRight && hasRoomOnLeft ? 'right' : 'left');
  }, [isOpen, viewMonth]);

  useEffect(() => {
    if (!isOpen) return;

    const handlePointerDown = (event: PointerEvent) => {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) {
        closePicker(false);
      }
    };

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        closePicker(true);
      }
    };

    document.addEventListener('pointerdown', handlePointerDown);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('pointerdown', handlePointerDown);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [closePicker, isOpen]);

  return (
    <div className={label ? 'date-picker-field date-field' : 'date-picker-field'} ref={rootRef}>
      {label ? <span id={labelId}>{label}</span> : null}
      <button
        ref={triggerRef}
        type="button"
        className={`date-picker-trigger ${selectedDate ? '' : 'date-picker-trigger--empty'}`.trim()}
        aria-label={label ? undefined : ariaLabel}
        aria-labelledby={label ? `${labelId} ${valueId}` : undefined}
        aria-expanded={isOpen}
        aria-haspopup="dialog"
        aria-controls={isOpen ? dialogId : undefined}
        disabled={disabled}
        onClick={() => setIsOpen((current) => !current)}
      >
        <span id={valueId}>{displayValue}</span>
        <CalendarDays aria-hidden="true" size={20} strokeWidth={1.9} />
      </button>

      {isOpen ? (
        <div
          ref={popoverRef}
          id={dialogId}
          className={`date-picker-popover date-picker-popover--${placement} date-picker-popover--align-${horizontalPlacement}`}
          role="dialog"
          aria-label={ariaLabel}
        >
          <div className="date-picker-header">
            <button type="button" aria-label="Mes anterior" onClick={() => setViewMonth((current) => addMonths(current, -1))}>
              <ChevronLeft aria-hidden="true" size={18} strokeWidth={2} />
            </button>
            <strong>{MONTH_FORMATTER.format(viewMonth)}</strong>
            <button type="button" aria-label="Mes siguiente" onClick={() => setViewMonth((current) => addMonths(current, 1))}>
              <ChevronRight aria-hidden="true" size={18} strokeWidth={2} />
            </button>
          </div>

          <div className="date-picker-weekdays" aria-hidden="true">
            {WEEKDAYS.map((weekday) => (
              <span key={weekday}>{weekday}</span>
            ))}
          </div>

          <div className="date-picker-grid" role="grid" aria-label={MONTH_FORMATTER.format(viewMonth)}>
            {days.map((date, index) =>
              date ? (
                <button
                  key={toIsoDate(date)}
                  type="button"
                  data-date={toIsoDate(date)}
                  role="gridcell"
                  className={[
                    sameDay(date, today) ? 'date-picker-day--today' : '',
                    selectedDate && sameDay(date, selectedDate) ? 'date-picker-day--selected' : '',
                  ]
                    .filter(Boolean)
                    .join(' ')}
                  aria-label={FULL_DATE_FORMATTER.format(date)}
                  aria-pressed={selectedDate ? sameDay(date, selectedDate) : false}
                  onKeyDown={(event) => handleDayKeyDown(event, date)}
                  onClick={() => {
                    onChange(toIsoDate(date));
                    closePicker(true);
                  }}
                >
                  {date.getDate()}
                </button>
              ) : (
                <span key={`blank-${index}`} aria-hidden="true" />
              )
            )}
          </div>

          <div className="date-picker-footer">
            <button
              type="button"
              onClick={() => {
                onChange(toIsoDate(new Date()));
                closePicker(true);
              }}
            >
              Hoy
            </button>
            {allowClear ? (
              <button
                type="button"
                onClick={() => {
                  onChange('');
                  closePicker(true);
                }}
              >
                Limpiar
              </button>
            ) : null}
          </div>
        </div>
      ) : null}
    </div>
  );
}
