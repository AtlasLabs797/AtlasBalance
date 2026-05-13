import { useEffect, useMemo, useRef, useState } from 'react';
import type { CSSProperties } from 'react';
import { useVirtualizer } from '@tanstack/react-virtual';
import { useId } from 'react';
import { AppSelect } from '@/components/common/AppSelect';
import { EmptyState } from '@/components/common/EmptyState';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import EditableCell from '@/components/extractos/EditableCell';
import type { Extracto } from '@/types';
import { formatCurrency, formatDate, getAmountTone } from '@/utils/formatters';

interface ExtractoTableProps {
  rows: Extracto[];
  loading: boolean;
  sortBy: string;
  sortDir: 'asc' | 'desc';
  visibleColumns: string[] | null;
  onSort: (field: string) => void;
  onToggleColumn: (column: string) => void;
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

export default function ExtractoTable({
  rows,
  loading,
  sortBy,
  sortDir,
  visibleColumns,
  onSort,
  onToggleColumn,
  onSaveCell,
  onToggleCheck,
  onToggleFlag,
  onOpenAudit,
  canEditCell
}: ExtractoTableProps) {
  const [filters, setFilters] = useState<Record<string, string>>({});
  const [flagNotes, setFlagNotes] = useState<Record<string, string>>({});
  const [showColumns, setShowColumns] = useState(false);
  const [showFilters, setShowFilters] = useState(false);
  const [density, setDensity] = useState<'comfortable' | 'compact'>('comfortable');
  const [selectedCell, setSelectedCell] = useState(DEFAULT_SELECTED_CELL);
  const parentRef = useRef<HTMLDivElement | null>(null);
  const filtersId = useId();
  const columnsId = useId();

  const extraColumns = useMemo(() => {
    const set = new Set<string>();
    rows.forEach((row) => Object.keys(row.columnas_extra ?? {}).forEach((key) => set.add(key)));
    return [...set].sort((a, b) => a.localeCompare(b));
  }, [rows]);

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
        const term = (filters[column] ?? '').trim().toLowerCase();
        if (!term) return true;
        const value = getCellValue(row, column);
        return value.toLowerCase().includes(term);
      });
    });
  }, [rows, filters, activeColumns]);

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

  const gridTemplateColumns = activeColumns.length > 0 ? activeColumns.map(getColumnTrack).join(' ') : '1fr';
  const sheetWidth = activeColumns.reduce((total, column) => total + getColumnWidth(column), 0);
  const sheetRootStyle = {
    '--extracto-sheet-width': `${sheetWidth}px`,
  } as CSSProperties;
  const sheetGridStyle = {
    gridTemplateColumns
  } as CSSProperties;

  return (
    <section
      className={`extracto-table-section extracto-table-section--${density}`}
      aria-label="Extractos en formato hoja de calculo"
    >
      <div className="extracto-table-toolbar">
        <div>
          <strong>{filteredRows.length.toLocaleString('es-ES')} filas</strong>
          <span>{activeColumns.length} columnas visibles</span>
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
          {allColumns.map((column) => {
            const checked = visibleColumns ? visibleColumns.includes(column) || (visibleColumns.length === 0 && column === 'fila_numero') : true;
            const isLastVisibleColumn = checked && activeColumns.length <= 1 && activeColumns.includes(column);

            return (
              <label key={column} title={isLastVisibleColumn ? 'Debe quedar al menos una columna visible.' : undefined}>
                <input
                  type="checkbox"
                  checked={checked}
                  disabled={isLastVisibleColumn}
                  onChange={() => onToggleColumn(column)}
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
        aria-rowcount={filteredRows.length + 1}
        aria-colcount={activeColumns.length}
      >
        <div id={filtersId} className="extracto-table-head" style={sheetGridStyle} role="row">
          {activeColumns.map((column) => (
            <div
              key={column}
              className={`cell head ${getColumnClassName(column)}`}
              role="columnheader"
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
                  placeholder="filtrar"
                  value={filters[column] ?? ''}
                  onChange={(e) => setFilters((prev) => ({ ...prev, [column]: e.target.value }))}
                />
              ) : null}
            </div>
          ))}
        </div>

        <div className="extracto-table-body">
          {loading ? (
            <div className="extracto-empty">
              <PageSkeleton rows={5} variant="table" />
            </div>
          ) : filteredRows.length === 0 ? (
            <div className="extracto-empty">
              <EmptyState
                title="No hay movimientos con estos filtros"
                subtitle="Ajusta los filtros o importa movimientos para llenar esta vista."
              />
            </div>
          ) : (
            <div
              className="extracto-table-spacer"
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
                    {activeColumns.map((column) => (
                      <div
                        key={`${row.id}-${column}`}
                        className={`cell ${getColumnClassName(column)}`}
                        role="gridcell"
                        tabIndex={ACTION_COLUMNS.has(column) ? -1 : 0}
                        onClick={() => {
                          if (!ACTION_COLUMNS.has(column)) {
                            setSelectedCell({
                              ref: getSheetCellReference(row.fila_numero, activeColumns.indexOf(column)),
                              label: getColumnLabel(column),
                              value: getDisplayCellValue(row, column),
                            });
                          }
                        }}
                        onFocus={() => {
                          if (!ACTION_COLUMNS.has(column)) {
                            setSelectedCell({
                              ref: getSheetCellReference(row.fila_numero, activeColumns.indexOf(column)),
                              label: getColumnLabel(column),
                              value: getDisplayCellValue(row, column),
                            });
                          }
                        }}
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
                          onToggleFlag
                        })}
                        {!ACTION_COLUMNS.has(column) ? (
                          <button
                            type="button"
                            className="cell-audit-button"
                            onClick={() => onOpenAudit(row, column)}
                            aria-label={`Ver auditoría de ${column} en fila ${row.fila_numero}`}
                          >
                            Historial
                          </button>
                        ) : null}
                      </div>
                    ))}
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

function renderCell({
  row,
  column,
  canEdit,
  amountClassName,
  note,
  onNoteChange,
  onSaveCell,
  onToggleCheck,
  onToggleFlag
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
}) {
  if (column === 'fila_numero') return <span>{row.fila_numero}</span>;
  if (column === 'checked') {
    return (
      <input
        type="checkbox"
        checked={row.checked}
        disabled={!canEdit}
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
          aria-label={`Marcar fila ${row.fila_numero} con alerta`}
          onChange={(e) => void onToggleFlag(row, e.target.checked, note)}
        />
        <span className="flag-label">{row.flagged ? 'Marcada' : 'Sin marca'}</span>
        <input
          value={note}
          placeholder="Nota de alerta"
          disabled={!canEdit}
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
  if (column === 'flagged') return 210;
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
