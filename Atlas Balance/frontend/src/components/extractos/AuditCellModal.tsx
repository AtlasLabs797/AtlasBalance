import Modal from '@/components/common/Modal';
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
        <p>Cargando historial de cambios de la celda...</p>
      ) : error ? (
        <p className="auth-error" role="alert">{error}</p>
      ) : data.length === 0 ? (
        <p>Sin cambios registrados.</p>
      ) : (
        <table>
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
    </Modal>
  );
}
