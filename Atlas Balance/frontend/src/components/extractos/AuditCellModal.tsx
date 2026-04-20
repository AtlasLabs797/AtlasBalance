import { SignedAmount } from '@/components/common/SignedAmount';
import type { AuditCellEntry } from '@/types';

interface AuditCellModalProps {
  open: boolean;
  column: string | null;
  data: AuditCellEntry[];
  loading: boolean;
  onClose: () => void;
}

export default function AuditCellModal({ open, column, data, loading, onClose }: AuditCellModalProps) {
  if (!open) {
    return null;
  }

  const normalizedColumn = column?.trim().toLowerCase();
  const isAmountColumn = normalizedColumn === 'monto' || normalizedColumn === 'saldo';

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="audit-modal" onClick={(e) => e.stopPropagation()}>
        <header className="audit-modal-header">
          <h3>Auditoria de celda{column ? `: ${column}` : ''}</h3>
          <button type="button" onClick={onClose}>Cerrar</button>
        </header>
        {loading ? (
          <p>Cargando...</p>
        ) : data.length === 0 ? (
          <p>Sin cambios registrados.</p>
        ) : (
          <table>
            <thead>
              <tr>
                <th>Fecha</th>
                <th>Celda</th>
                <th>Antes</th>
                <th>Despues</th>
              </tr>
            </thead>
            <tbody>
              {data.map((item) => (
                <tr key={item.id}>
                  <td>{new Date(item.timestamp).toLocaleString()}</td>
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
