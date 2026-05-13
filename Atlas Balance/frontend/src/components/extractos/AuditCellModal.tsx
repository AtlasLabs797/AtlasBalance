import { useEffect, useRef } from 'react';
import { CloseIconButton } from '@/components/common/CloseIconButton';
import { SignedAmount } from '@/components/common/SignedAmount';
import type { AuditCellEntry } from '@/types';
import { formatDateTime } from '@/utils/formatters';

interface AuditCellModalProps {
  open: boolean;
  column: string | null;
  data: AuditCellEntry[];
  loading: boolean;
  error?: string | null;
  onClose: () => void;
}

export default function AuditCellModal({ open, column, data, loading, error, onClose }: AuditCellModalProps) {
  const dialogRef = useRef<HTMLDivElement | null>(null);
  const triggerRef = useRef<Element | null>(null);

  useEffect(() => {
    if (!open) {
      return undefined;
    }

    triggerRef.current = document.activeElement;
    window.setTimeout(() => dialogRef.current?.focus(), 0);

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
        return;
      }

      if (event.key !== 'Tab') {
        return;
      }

      const focusable = Array.from(
        dialogRef.current?.querySelectorAll<HTMLElement>(
          'button:not(:disabled), [href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])'
        ) ?? []
      );

      if (focusable.length === 0) {
        event.preventDefault();
        dialogRef.current?.focus();
        return;
      }

      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => {
      window.removeEventListener('keydown', handleKeyDown);
      if (triggerRef.current instanceof HTMLElement) {
        triggerRef.current.focus();
      }
    };
  }, [onClose, open]);

  if (!open) {
    return null;
  }

  const normalizedColumn = column?.trim().toLowerCase();
  const isAmountColumn = normalizedColumn === 'monto' || normalizedColumn === 'saldo';

  return (
    <div className="modal-backdrop" role="presentation" onClick={onClose}>
      <div
        ref={dialogRef}
        className="audit-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="audit-cell-modal-title"
        tabIndex={-1}
        onClick={(e) => e.stopPropagation()}
      >
        <header className="audit-modal-header">
          <h3 id="audit-cell-modal-title">Auditoría de celda{column ? `: ${column}` : ''}</h3>
          <CloseIconButton onClick={onClose} ariaLabel="Cerrar auditoría de celda" />
        </header>
        {loading ? (
          <p>Cargando historial de cambios de la celda...</p>
        ) : error ? (
          <p className="auth-error" role="alert">{error}</p>
        ) : data.length === 0 ? (
          <p>Sin cambios registrados.</p>
        ) : (
          <table>
            <thead>
              <tr>
                <th>Fecha</th>
                <th>Celda</th>
                <th>Antes</th>
                <th>Después</th>
              </tr>
            </thead>
            <tbody>
              {data.map((item) => (
                <tr key={item.id}>
                  <td>{formatDateTime(item.timestamp)}</td>
                  <td>{item.celda_referencia ?? '-'}</td>
                  <td>
                    {isAmountColumn && item.valor_anterior !== null ? (
                      <SignedAmount value={item.valor_anterior}>{item.valor_anterior}</SignedAmount>
                    ) : (
                      item.valor_anterior ?? '-'
                    )}
                  </td>
                  <td>
                    {isAmountColumn && item.valor_nuevo !== null ? (
                      <SignedAmount value={item.valor_nuevo}>{item.valor_nuevo}</SignedAmount>
                    ) : (
                      item.valor_nuevo ?? '-'
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
