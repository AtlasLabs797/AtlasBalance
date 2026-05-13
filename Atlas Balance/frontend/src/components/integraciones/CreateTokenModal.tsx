import { FormEvent, useState } from 'react';
import api from '@/services/api';
import type { CreateIntegrationTokenResponse } from '@/types';
import { TokenPermissionsEditor, type TokenPermisoDraft } from '@/components/integraciones/TokenPermissionsEditor';
import { useDialogFocus } from '@/hooks/useDialogFocus';
import { extractErrorMessage } from '@/utils/errorMessage';

interface CatalogoPermisos {
  titulares: Array<{ id: string; nombre: string }>;
  cuentas: Array<{ id: string; nombre: string; titular_id: string }>;
}

interface CreateTokenModalProps {
  open: boolean;
  busy: boolean;
  catalogos: CatalogoPermisos;
  onClose: () => void;
  onCreated: (tokenPlano: string) => void;
  onError: (message: string | null) => void;
}

export function CreateTokenModal({ open, busy, catalogos, onClose, onCreated, onError }: CreateTokenModalProps) {
  const [submitting, setSubmitting] = useState(false);
  const [tokenNombre, setTokenNombre] = useState('');
  const [tokenDescripcion, setTokenDescripcion] = useState('');
  const [tokenLectura, setTokenLectura] = useState(true);
  const [tokenEscritura, setTokenEscritura] = useState(false);
  const [tokenPermisos, setTokenPermisos] = useState<TokenPermisoDraft[]>([]);
  const dialogRef = useDialogFocus<HTMLDivElement>(open, {
    onEscape: busy || submitting ? undefined : onClose,
  });

  if (!open) {
    return null;
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!tokenNombre.trim()) {
      onError('Escribe un nombre para el token.');
      return;
    }

    if (!tokenLectura && !tokenEscritura) {
      onError('Activa al menos lectura o escritura para el token.');
      return;
    }

    if (tokenPermisos.length === 0) {
      onError('Añade al menos un permiso de alcance para el token.');
      return;
    }

    if (!tokenLectura && tokenPermisos.some((permiso) => permiso.acceso_tipo === 'lectura')) {
      onError('El token no permite lectura, pero has definido alcances de lectura.');
      return;
    }

    if (!tokenEscritura && tokenPermisos.some((permiso) => permiso.acceso_tipo === 'escritura')) {
      onError('El token no permite escritura, pero has definido alcances de escritura.');
      return;
    }

    try {
      setSubmitting(true);
      onError(null);
      const { data } = await api.post<CreateIntegrationTokenResponse>('/integraciones/tokens', {
        nombre: tokenNombre.trim(),
        descripcion: tokenDescripcion.trim() || null,
        permiso_lectura: tokenLectura,
        permiso_escritura: tokenEscritura,
        permisos: tokenPermisos,
      });
      setTokenNombre('');
      setTokenDescripcion('');
      setTokenLectura(true);
      setTokenEscritura(false);
      setTokenPermisos([]);
      onClose();
      onCreated(data.token_plano);
    } catch (err) {
      onError(extractErrorMessage(err, 'No se pudo crear token.'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="config-modal-backdrop" role="presentation" onClick={busy || submitting ? undefined : onClose}>
      <div
        ref={dialogRef}
        className="config-modal-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="create-token-modal-title"
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
      >
        <header className="config-modal-header">
          <h3 id="create-token-modal-title">Crear token OpenClaw</h3>
        </header>
        <form onSubmit={submit}>
          <div className="config-grid-3">
            <label>
              Nombre
              <input value={tokenNombre} onChange={(event) => setTokenNombre(event.target.value)} />
            </label>
            <label>
              Descripción
              <input value={tokenDescripcion} onChange={(event) => setTokenDescripcion(event.target.value)} />
            </label>
            <label />
          </div>
          <div className="users-check-row">
            <label><input type="checkbox" checked={tokenLectura} onChange={(event) => setTokenLectura(event.target.checked)} /> Lectura</label>
            <label><input type="checkbox" checked={tokenEscritura} onChange={(event) => setTokenEscritura(event.target.checked)} /> Escritura</label>
          </div>
          <TokenPermissionsEditor permisos={tokenPermisos} onChange={setTokenPermisos} catalogos={catalogos} />
          <div className="import-actions">
            <button type="button" onClick={onClose} disabled={busy || submitting}>Cancelar</button>
            <button type="submit" disabled={busy || submitting}>{submitting ? 'Creando...' : 'Crear token'}</button>
          </div>
        </form>
      </div>
    </div>
  );
}
