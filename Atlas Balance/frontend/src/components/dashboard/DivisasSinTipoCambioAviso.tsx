import { buildAvisoDivisasSinTipoCambio } from '@/utils/dashboardDivisasSinTipoCambio';

interface DivisasSinTipoCambioAvisoProps {
  divisas: string[] | null | undefined;
}

// Lote 5 (B-1): aviso de que el total convertido no incluye las divisas sin
// tipo de cambio. Sin esto, el total se lee mas bajo del que es.
export function DivisasSinTipoCambioAviso({ divisas }: DivisasSinTipoCambioAvisoProps) {
  const aviso = buildAvisoDivisasSinTipoCambio(divisas);

  if (!aviso) {
    return null;
  }

  return (
    <div className="dashboard-aviso" role="status">
      <strong>{aviso.lead}</strong> {aviso.detalle}
    </div>
  );
}
