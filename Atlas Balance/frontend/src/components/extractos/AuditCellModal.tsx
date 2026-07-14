import { CloseIconButton } from '@/components/common/CloseIconButton';
import { SignedAmount } from '@/components/common/SignedAmount';
import { useDialogFocus } from '@/hooks/useDialogFocus';
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
  const dialogRef = useDialogFocus<HTMLDivElement>(open, {
    onEscape: onClose,
  });

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
