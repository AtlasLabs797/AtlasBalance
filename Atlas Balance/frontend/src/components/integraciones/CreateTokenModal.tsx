import { FormEvent, useId, useState } from 'react';
import api from '@/services/api';
import type { CreateIntegrationTokenResponse } from '@/types';
import { TokenPermissionsEditor, type TokenPermisoDraft } from '@/components/integraciones/TokenPermissionsEditor';
import { useDialogFocus } from '@/hooks/useDialogFocus';
import { extractErrorMessage } from '@/utils/errorMessage';

// Scopes marcados por defecto al abrir el modal.
const OPENCLAW_SCOPES_POR_DEFECTO = ['titulares', 'saldos', 'extractos', 'evolucion', 'alertas', 'auditoria'] as const;

// V-02.07: `resolver-nombres` se ofrece pero NO viene marcado. Deshace la
// pseudonimizacion de nombres (re-identificacion), asi que se concede solo si el
// admin lo marca a proposito. Mismo criterio que `KnownOpenClawScopes` vs
// `DefaultOpenClawScopes` en IntegracionesController.
const OPENCLAW_SCOPES = [...OPENCLAW_SCOPES_POR_DEFECTO, 'resolver-nombres'] as const;

interface CatalogoPermisos {
  paises: Array<{ id: string; nombre: string }>;
  titulares: Array<{ id: string; nombre: string }>;
  cuentas: Array<{ id: string; nombre: string; titular_id: string; pais_id: string | null }>;
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
  const [tokenExpiracion, setTokenExpiracion] = useState('');
  const [sinExpiracion, setSinExpiracion] = useState(false);
  const [tokenScopes, setTokenScopes] = useState<string[]>([...OPENCLAW_SCOPES_POR_DEFECTO]);
  const [tokenPermisos, setTokenPermisos] = useState<TokenPermisoDraft[]>([]);
  const [formError, setFormError] = useState<string | null>(null);
  const formErrorId = useId();

  const closeModal = () => {
    setFormError(null);
    onClose();
  };

  const dialogRef = useDialogFocus<HTMLDivElement>(open, {
    onEscape: busy || submitting ? undefined : closeModal,
  });

  if (!open) {
    return null;
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setFormError(null);
    onError(null);

    if (!tokenNombre.trim()) {
      setFormError('Escribe un nombre para el token.');
      return;
    }

    if (!tokenLectura && !tokenEscritura) {
      setFormError('Activa al menos lectura o escritura para el token.');
      return;
    }

    if (tokenPermisos.length === 0) {
      setFormError('Añade al menos un permiso de alcance para el token.');
      return;
    }

    if (!tokenLectura && tokenPermisos.some((permiso) => permiso.acceso_tipo === 'lectura')) {
      setFormError('El token no permite lectura, pero has definido alcances de lectura.');
      return;
    }

    if (!tokenEscritura && tokenPermisos.some((permiso) => permiso.acceso_tipo === 'escritura')) {
      setFormError('El token no permite escritura, pero has definido alcances de escritura.');
      return;
    }

    // La fecha elegida en el date picker es local (YYYY-MM-DD). La convertimos al
    // fin de ese dia en hora LOCAL y luego a UTC, para que la expiracion caiga en
    // el dia correcto del usuario (antes se forzaba 23:59:59Z, desplazando el
    // vencimiento a traves de la frontera de dia segun la zona horaria).
    const buildExpiracionIso = (): string | null => {
      if (sinExpiracion || !tokenExpiracion) {
        return null;
      }
      const [year, month, day] = tokenExpiracion.split('-').map(Number);
      if (!year || !month || !day) {
        return null;
      }
      return new Date(year, month - 1, day, 23, 59, 59, 999).toISOString();
    };

    try {
      setSubmitting(true);
      const { data } = await api.post<CreateIntegrationTokenResponse>('/integraciones/tokens', {
        nombre: tokenNombre.trim(),
        descripcion: tokenDescripcion.trim() || null,
        permiso_lectura: tokenLectura,
        permiso_escritura: tokenEscritura,
        fecha_expiracion: buildExpiracionIso(),
        sin_expiracion_confirmada: sinExpiracion,
        scopes: tokenScopes,
        permisos: tokenPermisos,
      });
      setTokenNombre('');
      setTokenDescripcion('');
      setTokenLectura(true);
      setTokenEscritura(false);
      setTokenExpiracion('');
      setSinExpiracion(false);
      setTokenScopes([...OPENCLAW_SCOPES_POR_DEFECTO]);
      setTokenPermisos([]);
      closeModal();
      onCreated(data.token_plano);
    } catch (err) {
      setFormError(extractErrorMessage(err, 'No se pudo crear token.'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="config-modal-backdrop" role="presentation" onClick={busy || submitting ? undefined : closeModal}>
      <div
        ref={dialogRef}
        className="config-modal-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="create-token-modal-title"
        aria-describedby={formError ? formErrorId : undefined}
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
      >
        <header className="config-modal-header">
          <h3 id="create-token-modal-title">Crear token OpenClaw</h3>
        </header>
        <form onSubmit={submit}>
          {formError ? <p id={formErrorId} className="auth-error" role="alert">{formError}</p> : null}
          <div className="config-grid-3">
            <label>
              Nombre
              <input value={tokenNombre} onChange={(event) => setTokenNombre(event.target.value)} />
            </label>
            <label>
              Descripción
              <input value={tokenDescripcion} onChange={(event) => setTokenDescripcion(event.target.value)} />
            </label>
            <span aria-hidden="true" />
          </div>
          <div className="users-check-row">
            <label><input type="checkbox" checked={tokenLectura} onChange={(event) => setTokenLectura(event.target.checked)} /> Lectura</label>
            <label><input type="checkbox" checked={tokenEscritura} onChange={(event) => setTokenEscritura(event.target.checked)} /> Escritura</label>
          </div>
          <p className="import-muted">
            La integración OpenClaw es de solo lectura por ahora: los endpoints disponibles
            solo consultan datos. El permiso de escritura no habilita ninguna operación todavía.
          </p>
          <div className="config-grid-3">
            <label>
              Expira el
              <input
                type="date"
                value={tokenExpiracion}
                disabled={sinExpiracion}
                onChange={(event) => setTokenExpiracion(event.target.value)}
              />
            </label>
            <label className="users-check-row">
              <input
                type="checkbox"
                checked={sinExpiracion}
                onChange={(event) => setSinExpiracion(event.target.checked)}
              />
              Sin expiracion
            </label>
            <p className="import-muted">Si no eliges fecha, la API usa 90 dias.</p>
          </div>
          <fieldset className="config-token-scopes">
            <legend>Scopes OpenClaw</legend>
            {OPENCLAW_SCOPES.map((scope) => (
              <label key={scope}>
                <input
                  type="checkbox"
                  checked={tokenScopes.includes(scope)}
                  onChange={(event) => {
                    setTokenScopes((current) =>
                      event.target.checked
                        ? [...new Set([...current, scope])]
                        : current.filter((item) => item !== scope));
                  }}
                />
                {scope}
              </label>
            ))}
          </fieldset>
          <TokenPermissionsEditor permisos={tokenPermisos} onChange={setTokenPermisos} catalogos={catalogos} />
          <div className="import-actions">
            <button type="button" className="button-secondary" onClick={closeModal} disabled={busy || submitting}>Cancelar</button>
            <button type="submit" className="button-primary" disabled={busy || submitting}>{submitting ? 'Creando...' : 'Crear token'}</button>
          </div>
        </form>
      </div>
    </div>
  );
}
