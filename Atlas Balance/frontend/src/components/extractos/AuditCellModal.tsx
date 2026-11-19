import Modal from '@/components/common/Modal';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import { SignedAmount } from '@/components/common/SignedAmount';
import type { AuditCellEntry } from '@/types';
import { formatDateTime, sinSignoPrevio } from '@/utils/formatters';

interface AuditCellModalProps {
  open: boolean;
  column: string | null;
  data: AuditCellEntry[];
  loading: boolean;
  error?: string | null;
  onClose: () => void;
}

// D-11: el "+" visible solo se anade cuando el dato crudo no lo trae ya, para
// no duplicar el signo (D-11: color nunca es el unico portador del signo).

export default function AuditCellModal({ open, column, data, loading, error, onClose }: AuditCellModalProps) {
  const normalizedColumn = column?.trim().toLowerCase();
  const isAmountColumn = normalizedColumn === 'monto' || normalizedColumn === 'saldo';

  return (
    <Modal
      open={open}
      onClose={onClose}
      titleId="audit-cell-modal-title"
      title={`Auditoría de celda${column ? `: ${column}` : ''}`}
      closeLabel="Cerrar auditoría de celda"
      size="lg"
    >
      {loading ? (
        <PageSkeleton variant="table" rows={3} columns={4} label="Cargando historial de cambios de la celda..." />
      ) : error ? (
        <p className="auth-error" role="alert">{error}</p>
      ) : data.length === 0 ? (
        <p>Sin cambios registrados.</p>
      ) : (
        <table>
          <caption className="sr-only">Historial de la celda</caption>
          <thead>
            <tr>
              <th scope="col">Fecha</th>
              <th scope="col">Celda</th>
              <th scope="col">Antes</th>
              <th scope="col">Después</th>
            </tr>
          </thead>
          <tbody>
            {data.map((item) => (
              <tr key={item.id}>
                <td>{formatDateTime(item.timestamp)}</td>
                <td>{item.celda_referencia ?? '-'}</td>
                <td>
                  {isAmountColumn && item.valor_anterior != null ? (
                    <SignedAmount value={item.valor_anterior} showSign={sinSignoPrevio(item.valor_anterior)}>{item.valor_anterior}</SignedAmount>
                  ) : (
                    item.valor_anterior ?? '-'
                  )}
                </td>
                <td>
                  {isAmountColumn && item.valor_nuevo != null ? (
                    <SignedAmount value={item.valor_nuevo} showSign={sinSignoPrevio(item.valor_nuevo)}>{item.valor_nuevo}</SignedAmount>
                  ) : (
                    item.valor_nuevo ?? '-'
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Modal>
  );
}
