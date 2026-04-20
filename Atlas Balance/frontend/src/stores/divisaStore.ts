import { create } from 'zustand';
import type { DivisaActiva, TipoCambio } from '@/types';

interface DivisaState {
  divisas: DivisaActiva[];
  tiposCambio: TipoCambio[];
  divisaPrincipal: string; // código (EUR by default)

  // Actions
  setDivisas: (divisas: DivisaActiva[]) => void;
  setTiposCambio: (tipos: TipoCambio[]) => void;
  setDivisaPrincipal: (codigo: string) => void;

  // Computed
  convertir: (monto: number, origen: string, destino?: string) => number;
}

export const useDivisaStore = create<DivisaState>((set, get) => ({
  divisas: [],
  tiposCambio: [],
  divisaPrincipal: 'EUR',

  setDivisas: (divisas) => set({ divisas }),
  setTiposCambio: (tiposCambio) => set({ tiposCambio }),
  setDivisaPrincipal: (divisaPrincipal) => set({ divisaPrincipal }),

  convertir: (monto, origen, destino) => {
    const target = destino ?? get().divisaPrincipal;
    if (origen === target) return monto;

    const { tiposCambio } = get();
    const tipo = tiposCambio.find(
      (t) => t.divisa_origen === origen && t.divisa_destino === target
    );

    if (tipo) return monto * tipo.tasa;

    // Try inverse
    const inverso = tiposCambio.find(
      (t) => t.divisa_origen === target && t.divisa_destino === origen
    );

    if (inverso) return monto / inverso.tasa;

    console.warn(`No exchange rate found: ${origen} -> ${target}`);
    return monto;
  },
}));
