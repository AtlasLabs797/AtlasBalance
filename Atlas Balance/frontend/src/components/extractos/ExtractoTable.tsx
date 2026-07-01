import { useEffect, useMemo, useRef, useState } from 'react';
import type { CSSProperties, KeyboardEvent as ReactKeyboardEvent } from 'react';
import { useVirtualizer } from '@tanstack/react-virtual';
import { useId } from 'react';
import { AppSelect } from '@/components/common/AppSelect';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import EditableCell from '@/components/extractos/EditableCell';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import type { Extracto } from '@/types';
import { formatCurrency, formatDate, getAmountTone } from '@/utils/formatters';

interface ExtractoTableProps {
  rows: Extracto[];
  totalRows: number;
  loading: boolean;
  sortBy: string;
  sortDir: 'asc' | 'desc';
  visibleColumns: string[] | null;
  availableExtraColumns: string[];
  onSort: (field: string) => void;
  onToggleColumn: (column: string, availableColumns: string[]) => void;
  onShowAllColumns: (availableColumns: string[]) => void;
  onSaveCell: (row: Extracto, column: string, value: string) => Promise<void>;
  onToggleCheck: (row: Extracto, checked: boolean) => Promise<void>;
  onToggleFlag: (row: Extracto, flagged: boolean, nota?: string) => Promise<void>;
  onOpenAudit: (row: Extracto, column: string) => void;
  canEditCell: (row: Extracto, column: string) => boolean;
}

const BASE_COLUMNS = ['fila_numero', 'checked', 'flagged', 'fecha', 'concepto', 'comentarios', 'monto', 'saldo'] as const;
const AMOUNT_COLUMNS = new Set(['monto', 'saldo']);
const ACTION_COLUMNS = new Set(['checked', 'flagged']);
const DEFAULT_SELECTED_CELL = { ref: 'A1', label: 'Celda', value: 'Selecciona una celda' };
const DEFAULT_FOCUSED_CELL = { rowIndex: 0, colIndex: 0 };

export default function ExtractoTable({
  rows,
  totalRows,
  loading,
  sortBy,
  sortDir,
  visibleColumns,
  availableExtraColumns,
  onSort,
  onToggleColumn,
  onShowAllColumns,
  onSaveCell,
  onToggleCheck,
  onToggleFlag,
  onOpenAudit,
  canEditCell
}: ExtractoTableProps) {
  const [filters, setFilters] = useState<Record<string, string>>({});
  // F-NEW-11 (V-02-03): debounce del input para no re-virtualizar
  // 200 filas en cada pulsacion. 250 ms es suficiente para escritura
  // natural y mantiene la sensacion de respuesta inmediata.
  const debouncedFilters = useDebouncedValue(filters, 250);
  const [flagNotes, setFlagNotes] = useState<Record<string, string>>({});
  const [showColumns, setShowColumns] = useState(false);
  const [showFilters, setShowFilters] = useState(false);
  const [density, setDensity] = useState<'comfortable' | 'compact'>('comfortable');
  const [selectedCell, setSelectedCell] = useState(DEFAULT_SELECTED_CELL);
  const [focusedCell, setFocusedCell] = useState(DEFAULT_FOCUSED_CELL);
  const parentRef = useRef<HTMLDivElement | null>(null);
  const cellRefs = useRef<Map<string, HTMLDivElement>>(new Map());
  const filtersId = useId();
  const columnsId = useId();

  const extraColumns = useMemo(() => {
    const set = new Set<string>();
    availableExtraColumns.forEach((column) => {
      const trimmed = column.trim();
      if (trimmed) {
        set.add(trimmed);
      }
    });
    rows.forEach((row) => Object.keys(row.columnas_extra ?? {}).forEach((key) => set.add(key)));
    return [...set].sort((a, b) => a.localeCompare(b));
  }, [availableExtraColumns, rows]);

  const allColumns = useMemo(() => [...BASE_COLUMNS, ...extraColumns], [extraColumns]);
  const activeColumns = useMemo(() => {
    if (!visibleColumns) {
      return allColumns;
    }
    const selected = new Set(visibleColumns);
    const next = allColumns.filter((col) => selected.has(col));
    return next.length > 0 ? next : ['fila_numero'];
  }, [allColumns, visibleColumns]);

  const filteredRows = useMemo(() => {
    return rows.filter((row) => {
      return activeColumns.every((column) => {
        const term = (debouncedFilters[column] ?? '').trim().toLowerCase();
        if (!term) return true;
        const value = getCellValue(row, column);
        return value.toLowerCase().includes(term);
      });
    });
  }, [rows, debouncedFilters, activeColumns]);

  const headerOffset = density === 'compact' ? 40 : 46;
  const rowVirtualizer = useVirtualizer({
    count: filteredRows.length,
    getScrollElement: () => parentRef.current,
    estimateSize: () => (density === 'compact' ? 34 : 42),
    overscan: 15,
    scrollMargin: 0,
    scrollPaddingStart: headerOffset,
    getItemKey: (index) => filteredRows[index]?.id ?? index
  });

  useEffect(() => {
    rowVirtualizer.measure();
  }, [density, rowVirtualizer]);

  useEffect(() => {
    setFocusedCell((current) => ({
      rowIndex: clampNumber(current.rowIndex, 0, Math.max(filteredRows.length - 1, 0)),
      colIndex: clampNumber(current.colIndex, 0, Math.max(activeColumns.length - 1, 0)),
    }));
  }, [activeColumns.length, filteredRows.length]);

  const gridTemplateColumns = activeColumns.length > 0 ? activeColumns.map(getColumnTrack).join(' ') : '1fr';
  const sheetWidth = activeColumns.reduce((total, column) => total + getColumnWidth(column), 0);
  const sheetRootStyle = {
    '--extracto-sheet-width': `${sheetWidth}px`,
  } as CSSProperties;
  const sheetGridStyle = {
    gridTemplateColumns
  } as CSSProperties;

  const selectCell = (row: Extracto, column: string, colIndex: number) => {
    if (ACTION_COLUMNS.has(column)) {
      return;
    }

    setSelectedCell({
      ref: getSheetCellReference(row.fila_numero, colIndex),
      label: getColumnLabel(column),
      value: getDisplayCellValue(row, column),
    });
  };

  const focusGridCell = (rowIndex: number, colIndex: number) => {
    if (filteredRows.length === 0 || activeColumns.length === 0) {
      return;
    }

    const nextCell = {
      rowIndex: clampNumber(rowIndex, 0, filteredRows.length - 1),
      colIndex: clampNumber(colIndex, 0, activeColumns.length - 1),
    };

    setFocusedCell(nextCell);
    rowVirtualizer.scrollToIndex(nextCell.rowIndex, { align: 'auto' });

    window.requestAnimationFrame(() => {
      cellRefs.current.get(getCellKey(nextCell.rowIndex, nextCell.colIndex))?.focus({ preventScroll: true });
    });
  };

  const handleGridCellKeyDown = (
    event: ReactKeyboardEvent<HTMLDivElement>,
    rowIndex: number,
    colIndex: number,
  ) => {
    if (isInteractiveTarget(event.target)) {
      return;
    }

    const pageSize = Math.max(
      1,
      Math.floor((parentRef.current?.clientHeight ?? 420) / (density === 'compact' ? 34 : 42)),
    );

    switch (event.key) {
      case 'ArrowLeft':
        event.preventDefault();
        focusGridCell(rowIndex, colIndex - 1);
        break;
      case 'ArrowRight':
        event.preventDefault();
        focusGridCell(rowIndex, colIndex + 1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        focusGridCell(rowIndex - 1, colIndex);
        break;
      case 'ArrowDown':
        event.preventDefault();
        focusGridCell(rowIndex + 1, colIndex);
        break;
      case 'Home':
        event.preventDefault();
        focusGridCell(event.ctrlKey ? 0 : rowIndex, 0);
        break;
      case 'End':
        event.preventDefault();
        focusGridCell(event.ctrlKey ? filteredRows.length - 1 : rowIndex, activeColumns.length - 1);
        break;
      case 'PageUp':
        event.preventDefault();
        focusGridCell(rowIndex - pageSize, colIndex);
        break;
      case 'PageDown':
        event.preventDefault();
        focusGridCell(rowIndex + pageSize, colIndex);
        break;
      case 'Enter':
      case 'F2': {
        event.preventDefault();
        const cell = cellRefs.current.get(getCellKey(rowIndex, colIndex));
        const editButton = cell?.querySelector<HTMLButtonElement>('.cell-edit-button:not(:disabled)');
        if (editButton) {
          editButton.click();
          return;
        }

        cell
          ?.querySelector<HTMLElement>('input:not(:disabled), select:not(:disabled), textarea:not(:disabled), button:not(.cell-audit-button):not(:disabled)')
          ?.focus();
        break;
      }
      default:
        break;
    }
  };

  return (
    <section
      className={`extracto-table-section extracto-table-section--${density}`}
      aria-label="Extractos de la página actual en formato tabla editable"
    >
      <div className="extracto-table-toolbar">
        <div>
          <strong>{filteredRows.length.toLocaleString('es-ES')} de {rows.length.toLocaleString('es-ES')} filas en esta página</strong>
          <span>{totalRows.toLocaleString('es-ES')} movimientos totales · {activeColumns.length} columnas visibles</span>
        </div>
        <div className="extracto-table-actions">
          <button
            type="button"
            onClick={() => setShowFilters((current) => !current)}
            aria-expanded={showFilters}
            aria-controls={filtersId}
          >
            Filtros
          </button>
          <button
            type="button"
            onClick={() => setShowColumns((current) => !current)}
            aria-expanded={showColumns}
            aria-controls={columnsId}
          >
            Columnas
          </button>
          <AppSelect
            className="extracto-density-control"
            label="Densidad"
            value={density}
            options={[
              { value: 'comfortable', label: 'Comoda' },
              { value: 'compact', label: 'Compacta' },
            ]}
            onChange={(next) => setDensity(next as 'comfortable' | 'compact')}
          />
        </div>
      </div>

      {showColumns ? (
        <div id={columnsId} className="column-visibility-panel" role="group" aria-label="Columnas visibles">
          <div className="column-visibility-panel-actions" aria-label="Acciones de columnas">
            <button
              type="button"
              onClick={() => onShowAllColumns(allColumns)}
              disabled={activeColumns.length === allColumns.length}
            >
              Mostrar todas
            </button>
          </div>
          {allColumns.map((column) => {
            const checked = visibleColumns ? visibleColumns.includes(column) || (visibleColumns.length === 0 && column === 'fila_numero') : true;
            const isLastVisibleColumn = checked && activeColumns.length <= 1 && activeColumns.includes(column);

            return (
              <label key={column} title={isLastVisibleColumn ? 'Debe quedar al menos una columna visible.' : undefined}>
                <input
                  type="checkbox"
                  checked={checked}
                  disabled={isLastVisibleColumn}
                  onChange={() => onToggleColumn(column, allColumns)}
                />
                {getColumnLabel(column)}
              </label>
            );
          })}
        </div>
      ) : null}

      <div className="extracto-formula-bar" aria-live="polite">
        <span className="extracto-formula-ref">{selectedCell.ref}</span>
        <span className="extracto-formula-label">{selectedCell.label}</span>
        <output>{selectedCell.value || '-'}</output>
      </div>

      <div
        ref={parentRef}
        className="extracto-table-viewport"
        style={sheetRootStyle}
        role="grid"
        aria-label="Extractos de la pagina actual en formato hoja editable"
        aria-rowcount={filteredRows.length + 1}
        aria-colcount={activeColumns.length}
      >
        <div id={filtersId} className="extracto-table-head" style={sheetGridStyle} role="row" aria-rowindex={1}>
          {activeColumns.map((column, columnIndex) => (
            <div
              key={column}
              className={`cell head ${getColumnClassName(column)}`}
              role="columnheader"
              aria-colindex={columnIndex + 1}
              aria-sort={sortBy === column ? (sortDir === 'asc' ? 'ascending' : 'descending') : 'none'}
            >
              <button
                type="button"
                onClick={() => onSort(column)}
              >
                <span>{getColumnLabel(column)}</span>
                {sortBy === column ? <small>{sortDir === 'asc' ? 'asc' : 'desc'}</small> : null}
              </button>
              {showFilters ? (
                <input
                  aria-label={`Filtrar por ${getColumnLabel(column)}`}
                  placeholder="filtrar página actual"
                  value={filters[column] ?? ''}
                  onChange={(e) => setFilters((prev) => ({ ...prev, [column]: e.target.value }))}
                />
              ) : null}
            </div>
          ))}
        </div>

        <div className="extracto-table-body" role="rowgroup">
          {loading ? (
            <div className="extracto-empty">
              <PageSkeleton rows={5} variant="table" />
            </div>
          ) : filteredRows.length === 0 ? (
            <div className="extracto-empty">
              <EmptyState
                title="No hay movimientos en esta página con estos filtros"
                subtitle="Ajusta los filtros de columna o cambia de página para revisar más movimientos."
              />
            </div>
          ) : (
            <div
              className="extracto-table-spacer"
              role="presentation"
              style={{
                height: `${rowVirtualizer.getTotalSize()}px`
              } as CSSProperties}
            >
              {rowVirtualizer.getVirtualItems().map((virtualRow) => {
                const row = filteredRows[virtualRow.index];
                return (
                  <div
                    key={row.id}
                    className={`extracto-row ${row.flagged ? 'flagged' : ''}`}
                    style={{
                      transform: `translateY(${virtualRow.start}px)`,
                      gridTemplateColumns
                    }}
                    role="row"
                    aria-rowindex={virtualRow.index + 2}
                  >
                    {activeColumns.map((column, columnIndex) => {
                      const isFocusedCell =
                        focusedCell.rowIndex === virtualRow.index && focusedCell.colIndex === columnIndex;
                      const cellKey = getCellKey(virtualRow.index, columnIndex);

                      return (
                      <div
                        key={`${row.id}-${column}`}
                        ref={(node) => {
                          if (node) {
                            cellRefs.current.set(cellKey, node);
                          } else {
                            cellRefs.current.delete(cellKey);
                          }
                        }}
                        className={`cell ${getColumnClassName(column)}`}
                        role="gridcell"
                        aria-colindex={columnIndex + 1}
                        aria-selected={isFocusedCell}
                        tabIndex={isFocusedCell ? 0 : -1}
                        onClick={(event) => {
                          setFocusedCell({ rowIndex: virtualRow.index, colIndex: columnIndex });
                          selectCell(row, column, columnIndex);
                          if (event.target === event.currentTarget) {
                            event.currentTarget.focus();
                          }
                        }}
                        onFocus={() => {
                          setFocusedCell({ rowIndex: virtualRow.index, colIndex: columnIndex });
                          selectCell(row, column, columnIndex);
                        }}
                        onKeyDown={(event) => handleGridCellKeyDown(event, virtualRow.index, columnIndex)}
                        onContextMenu={(e) => {
                          e.preventDefault();
                          onOpenAudit(row, column);
                        }}
                      >
                        {renderCell({
                          row,
                          column,
                          canEdit: canEditCell(row, column),
                          amountClassName: getAmountClassName(row, column),
                          note: flagNotes[row.id] ?? row.flagged_nota ?? '',
                          onNoteChange: (next) => setFlagNotes((prev) => ({ ...prev, [row.id]: next })),
                          onSaveCell,
                          onToggleCheck,
                          onToggleFlag,
                          isActive: isFocusedCell
                        })}
                        {column === 'fila_numero' ? (
                          <button
                            type="button"
                            className="cell-audit-button"
                            tabIndex={isFocusedCell ? 0 : -1}
                            onClick={() => onOpenAudit(row, column)}
                            aria-label={`Ver auditoría de ${column} en fila ${row.fila_numero}`}
                          >
                            Historial
                          </button>
                        ) : null}
                      </div>
                      );
                    })}
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>
    </section>
  );
}

function getCellKey(rowIndex: number, colIndex: number): string {
  return `${rowIndex}:${colIndex}`;
}

function clampNumber(value: number, min: number, max: number): number {
  if (max < min) {
    return min;
  }

  return Math.min(Math.max(value, min), max);
}

function isInteractiveTarget(target: EventTarget | null): boolean {
  return target instanceof HTMLElement && Boolean(target.closest('button, input, select, textarea, a, [contenteditable="true"]'));
}

function renderCell({
  row,
  column,
  canEdit,
  amountClassName,
  note,
  onNoteChange,
  onSaveCell,
  onToggleCheck,
  onToggleFlag,
  isActive
}: {
  row: Extracto;
  column: string;
  canEdit: boolean;
  amountClassName: string;
  note: string;
  onNoteChange: (next: string) => void;
  onSaveCell: (row: Extracto, column: string, value: string) => Promise<void>;
  onToggleCheck: (row: Extracto, checked: boolean) => Promise<void>;
  onToggleFlag: (row: Extracto, flagged: boolean, nota?: string) => Promise<void>;
  isActive: boolean;
}) {
  if (column === 'fila_numero') return <span>{row.fila_numero}</span>;
  if (column === 'checked') {
    return (
      <input
        type="checkbox"
        checked={row.checked}
        disabled={!canEdit}
        tabIndex={isActive ? 0 : -1}
        aria-label={`Marcar fila ${row.fila_numero} como revisada`}
        onChange={(e) => void onToggleCheck(row, e.target.checked)}
      />
    );
  }
  if (column === 'flagged') {
    return (
      <div className="flag-cell">
        <input
          type="checkbox"
          checked={row.flagged}
          disabled={!canEdit}
          tabIndex={isActive ? 0 : -1}
          aria-label={`Marcar fila ${row.fila_numero} con alerta`}
          onChange={(e) => void onToggleFlag(row, e.target.checked, note)}
        />
        <input
          value={note}
          placeholder="Nota de alerta"
          disabled={!canEdit}
          tabIndex={isActive ? 0 : -1}
          aria-label={`Nota de alerta para fila ${row.fila_numero}`}
          onChange={(e) => onNoteChange(e.target.value)}
          onBlur={() => {
            if (canEdit && row.flagged) {
              void onToggleFlag(row, row.flagged, note);
            }
          }}
        />
      </div>
    );
  }

  return (
    <EditableCell
      value={getCellValue(row, column)}
      editable={canEdit}
      displayValue={getDisplayCellValue(row, column)}
      displayClassName={amountClassName}
      tabIndex={-1}
      onSave={(value) => onSaveCell(row, column, value)}
    />
  );
}

function getDisplayCellValue(row: Extracto, column: string): string {
  if (column === 'fecha' && row.fecha) {
    return formatDate(row.fecha);
  }

  if (column === 'monto') {
    return formatCurrency(row.monto, row.divisa ?? 'EUR');
  }

  if (column === 'saldo') {
    return formatCurrency(row.saldo, row.divisa ?? 'EUR');
  }

  return getCellValue(row, column);
}

function getCellValue(row: Extracto, column: string): string {
  switch (column) {
    case 'fecha':
      return row.fecha ?? '';
    case 'concepto':
      return row.concepto ?? '';
    case 'comentarios':
      return row.comentarios ?? '';
    case 'monto':
      return String(row.monto ?? '');
    case 'saldo':
      return String(row.saldo ?? '');
    default:
      return row.columnas_extra?.[column] ?? '';
  }
}

function getAmountClassName(row: Extracto, column: string): string {
  if (!AMOUNT_COLUMNS.has(column)) {
    return '';
  }

  const amount = column === 'monto' ? row.monto : row.saldo;
  return `signed-amount--${getAmountTone(amount)}`;
}

function getColumnTrack(column: string): string {
  return `${getColumnWidth(column)}px`;
}

function getColumnWidth(column: string): number {
  if (column === 'fila_numero') return 88;
  if (column === 'checked') return 112;
  if (column === 'flagged') return 176;
  if (column === 'fecha') return 124;
  if (column === 'concepto') return 420;
  if (column === 'comentarios') return 316;
  if (AMOUNT_COLUMNS.has(column)) return 164;
  return 176;
}

function getColumnClassName(column: string): string {
  const classes = [`cell--${column.replace(/[^a-z0-9_-]/gi, '-').toLowerCase()}`];
  if (AMOUNT_COLUMNS.has(column)) {
    classes.push('cell--amount');
  }

  return classes.join(' ');
}

function getColumnLabel(column: string): string {
  switch (column) {
    case 'fila_numero':
      return 'Fila';
    case 'checked':
      return 'Revisada';
    case 'flagged':
      return 'Alerta';
    case 'fecha':
      return 'Fecha';
    case 'concepto':
      return 'Concepto';
    case 'comentarios':
      return 'Comentarios';
    case 'monto':
      return 'Importe';
    case 'saldo':
      return 'Saldo';
    default:
      return column.replace(/_/g, ' ');
  }
}

function getSheetCellReference(filaNumero: number, columnIndex: number): string {
  const letters = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';
  const index = Math.max(0, columnIndex);
  const letter =
    index < letters.length
      ? letters[index]
      : `${letters[Math.floor(index / letters.length) - 1] ?? 'Z'}${letters[index % letters.length]}`;
  return `${letter}${filaNumero}`;
}
