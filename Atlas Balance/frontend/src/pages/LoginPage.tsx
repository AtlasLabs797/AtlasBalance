import { useEffect, useState } from 'react';
import { Eye, EyeOff } from 'lucide-react';
import { useForm } from 'react-hook-form';
import { useLocation, useNavigate, useSearchParams } from 'react-router';
import api from '@/services/api';
import { useAlertasStore } from '@/stores/alertasStore';
import { useAuthStore } from '@/stores/authStore';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
import { usePermisosStore } from '@/stores/permisosStore';
import { useUiStore } from '@/stores/uiStore';
import type { LoginResponse } from '@/types';
import { IconMoon, IconSun } from '@/components/Icons';
import { extractErrorMessage } from '@/utils/errorMessage';
import { sanitizeInternalPath } from '@/utils/safeRoute';

interface LoginForm {
  email: string;
  password: string;
  mfaCode: string;
  rememberDevice: boolean;
}

interface MfaChallenge {
  id: string;
  setupRequired: boolean;
  secret: string | null;
  otpAuthUri: string | null;
  rememberDeviceAllowed: boolean;
  rememberDeviceDays: number;
}

export default function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();
  // F-NEW-1 (V-02-03): ProtectedRoute redirige con state.from cuando el usuario
  // intenta entrar a una ruta protegida sin sesion. Asi, tras hacer login
  // lo mandamos de vuelta a donde queria ir, no siempre a /dashboard.
  // V-02.07 (#17): sanitizeInternalPath cierra el open redirect del CVE
  // GHSA-wrjc-x8rr-h8h6 aunque react-router-dom volviese a fallar.
  const stateFrom = (location.state as { from?: { pathname?: string; search?: string } } | null)?.from;
  const fromReturnTo = stateFrom?.pathname ? `${stateFrom.pathname ?? ''}${stateFrom.search ?? ''}` : null;
  const candidateReturnTo = sanitizeInternalPath(searchParams.get('returnTo'), '') || sanitizeInternalPath(fromReturnTo, '');
  const returnTo = candidateReturnTo || '/dashboard';
  const setUsuario = useAuthStore((state) => state.setUsuario);
  const setPermisos = usePermisosStore((state) => state.setPermisos);
  const loadAlertasActivas = useAlertasStore((state) => state.loadAlertasActivas);
  const selectedPaisId = usePaisScopeStore((state) => state.selectedPaisId);
  const theme = useUiStore((state) => state.theme);
  const toggleTheme = useUiStore((state) => state.toggleTheme);
  // V-02-05 (LOW-FE-5): opt-in para recordar email. Antes se hacia
  // automaticamente; ahora el usuario debe marcar un checkbox. Por defecto OFF.
  const [rememberEmail, setRememberEmail] = useState<boolean>(() => {
    if (typeof window === 'undefined') return false;
    return window.localStorage.getItem('atlas_remember_email_opt_in') === '1';
  });
  const { register, handleSubmit, formState: { errors, isSubmitting }, setFocus, setValue } = useForm<LoginForm>({
    defaultValues: {
      email: rememberEmail
        ? (typeof window !== 'undefined' ? window.localStorage.getItem('atlas_last_email') ?? '' : '')
        : '',
    },
  });
  const [error, setError] = useState<string | null>(null);
  const [postUpdateMessage, setPostUpdateMessage] = useState<string | null>(null);
  const [showPassword, setShowPassword] = useState(false);
  const [mfaChallenge, setMfaChallenge] = useState<MfaChallenge | null>(null);
  const [mfaQrCode, setMfaQrCode] = useState<string | null>(null);

  useEffect(() => {
    const message = sessionStorage.getItem('atlas_balance_update_message');
    if (!message) {
      return;
    }

    setPostUpdateMessage(message);
    sessionStorage.removeItem('atlas_balance_update_message');
  }, []);

  useEffect(() => {
    if (mfaChallenge) {
      setFocus('mfaCode');
    }
  }, [mfaChallenge, setFocus]);

  useEffect(() => {
    let cancelled = false;

    const otpAuthUri = mfaChallenge?.otpAuthUri;
    if (!mfaChallenge?.setupRequired || !otpAuthUri) {
      setMfaQrCode(null);
      return;
    }

    const renderQrCode = async () => {
      const QRCode = await import('qrcode');
      const dataUrl: string = await QRCode.toDataURL(otpAuthUri, {
        errorCorrectionLevel: 'M',
        margin: 2,
        width: 208,
      });

      if (!cancelled) {
        setMfaQrCode(dataUrl);
      }
    };

    void renderQrCode().catch(() => {
      if (!cancelled) {
        setMfaQrCode(null);
      }
    });

    return () => {
      cancelled = true;
    };
  }, [mfaChallenge]);

  const completeLogin = async (data: LoginResponse) => {
    if (!data.usuario) {
      setError('No pudimos confirmar el inicio de sesión. Vuelve a intentarlo; si se repite, avisa al administrador.');
      return;
    }

    setUsuario(data.usuario, data.csrf_token);
    setPermisos(data.permisos ?? []);

    if (!data.usuario.primer_login) {
      await loadAlertasActivas(selectedPaisId || undefined);
    }

    if (data.usuario.primer_login) {
      navigate('/cambiar-password', { replace: true });
      return;
    }

    navigate(returnTo ?? '/dashboard', { replace: true });
  };

  const onSubmit = handleSubmit(async (values) => {
    setError(null);
    // V-02-05 (LOW-FE-5): solo recordar email si el usuario marco la opcion.
    if (typeof window !== 'undefined' && values.email && rememberEmail) {
      try {
        window.localStorage.setItem('atlas_last_email', values.email);
        window.localStorage.setItem('atlas_remember_email_opt_in', '1');
      } catch {
        // Si el storage esta bloqueado seguimos sin guardar.
      }
    }
    try {
      if (mfaChallenge) {
        const { data } = await api.post<LoginResponse>('/auth/mfa/verify', {
          challenge_id: mfaChallenge.id,
          code: values.mfaCode,
          remember_device: mfaChallenge.rememberDeviceAllowed && values.rememberDevice === true,
        });
        await completeLogin(data);
        return;
      }

      const { data } = await api.post<LoginResponse>('/auth/login', {
        email: values.email,
        password: values.password,
      });
      if (data.mfa_required && data.mfa_challenge_id) {
        setMfaChallenge({
          id: data.mfa_challenge_id,
          setupRequired: !!data.mfa_setup_required,
          secret: data.mfa_secret ?? null,
          otpAuthUri: data.mfa_otp_auth_uri ?? null,
          rememberDeviceAllowed: !!data.mfa_remember_device_allowed,
          rememberDeviceDays: data.mfa_remember_device_days ?? 62,
        });
        setValue('rememberDevice', false);
        return;
      }

      await completeLogin(data);
    } catch (err) {
      setError(
        extractErrorMessage(
          err,
          'Revisa el email y la contraseña. Si has fallado varias veces, espera 30 minutos.'
        )
      );
    }
  });

  return (
    <section className="auth-page">
      <button
        type="button"
        className="auth-theme-toggle"
        onClick={toggleTheme}
        aria-pressed={theme === 'dark'}
        aria-label={`Cambiar a modo ${theme === 'light' ? 'oscuro' : 'claro'}`}
        title={`Cambiar a modo ${theme === 'light' ? 'oscuro' : 'claro'}`}
      >
        {theme === 'light' ? <IconMoon /> : <IconSun />}
      </button>

      <aside className="auth-brand-panel" aria-label="Atlas Balance">
        <div className="auth-brand-lockup">
          <span className="auth-logo-image" aria-hidden="true" />
          <div className="auth-branding">
            <h1>Atlas Balance</h1>
          </div>
        </div>

        <div className="auth-brand-copy">
          <strong>Tesorería local, control real.</strong>
          <p>Saldos, extractos y previsiones de todos tus bancos, titulares y divisas — centralizados en tu propia red, sin salir de ella.</p>
          <div className="auth-brand-tags" aria-label="Capacidades principales">
            <span>Multi-banco</span>
            <span>Multi-divisa</span>
            <span>Red local</span>
          </div>
        </div>

        <div className="auth-brand-footer">
          <span>by</span>
          <img
            src="/logos/Atlas Labs.png"
            alt="Atlas Labs"
            className="auth-footer-logo"
          />
          <strong>Atlas Labs</strong>
        </div>
      </aside>

      <main className="auth-main-panel">
      <form className="auth-card" onSubmit={onSubmit}>
        <h2 className="auth-card-title">{mfaChallenge ? 'Verificar acceso' : 'Iniciar sesión'}</h2>
        <p className="auth-card-description">
          {mfaChallenge ? 'Introduce el código temporal de tu app de autenticación.' : 'Acceso privado para operar saldos, extractos y alertas.'}
        </p>

        {!mfaChallenge && (
          <>
            <div className="auth-form-group">
              <label htmlFor="email" className="auth-label">Email</label>
              <input
                id="email"
                type="email"
                autoComplete="username"
                className="auth-input"
                placeholder="tu@email.com"
                aria-invalid={errors.email ? true : undefined}
                aria-describedby={errors.email ? 'email-error' : undefined}
                {...register('email', { required: 'Email obligatorio' })}
              />
              {errors.email && <p id="email-error" className="auth-error" role="alert">{errors.email.message}</p>}
            </div>

            <div className="auth-form-group">
              <label htmlFor="password" className="auth-label">Contraseña</label>
              <div className="auth-password-row">
                <input
                  id="password"
                  type={showPassword ? 'text' : 'password'}
                  autoComplete="current-password"
                  className="auth-input"
                  placeholder="Contraseña"
                  aria-invalid={errors.password ? true : undefined}
                  aria-describedby={errors.password ? 'password-error' : undefined}
                  {...register('password', { required: 'Contraseña obligatoria' })}
                />
                <button
                  type="button"
                  className="auth-password-toggle"
                  onClick={() => setShowPassword((current) => !current)}
                  aria-pressed={showPassword}
                  aria-label={showPassword ? 'Ocultar contraseña' : 'Mostrar contraseña'}
                  title={showPassword ? 'Ocultar contraseña' : 'Mostrar contraseña'}
                >
                  {showPassword ? <EyeOff aria-hidden="true" /> : <Eye aria-hidden="true" />}
                </button>
              </div>
              {errors.password && <p id="password-error" className="auth-error" role="alert">{errors.password.message}</p>}
            </div>

            <div className="auth-form-group auth-remember-email">
              <label className="auth-checkbox-label">
                <input
                  type="checkbox"
                  checked={rememberEmail}
                  onChange={(event) => {
                    const next = event.target.checked;
                    setRememberEmail(next);
                    if (!next && typeof window !== 'undefined') {
                      window.localStorage.removeItem('atlas_last_email');
                      window.localStorage.setItem('atlas_remember_email_opt_in', '0');
                    }
                  }}
                />
                <span>Recordar mi email en este navegador</span>
              </label>
            </div>
          </>
        )}

        {mfaChallenge && (
          <>
            {mfaChallenge.setupRequired && mfaChallenge.secret && (
              <div className="auth-mfa-setup">
                <span className="auth-label">Escanea este QR con Google Authenticator</span>
                {mfaQrCode && (
                  <img
                    src={mfaQrCode}
                    alt="QR para configurar Google Authenticator"
                    className="auth-mfa-qr"
                  />
                )}
                <code className="auth-secret">{mfaChallenge.secret}</code>
                <p className="auth-card-description">Si el QR falla, introduce la clave manualmente y confirma el primer código.</p>
              </div>
            )}

            <div className="auth-form-group">
              <label htmlFor="mfaCode" className="auth-label">Código de verificación</label>
              <input
                id="mfaCode"
                type="text"
                inputMode="numeric"
                autoComplete="one-time-code"
                className="auth-input"
                placeholder="000000"
                aria-invalid={errors.mfaCode ? true : undefined}
                aria-describedby={errors.mfaCode ? 'mfa-code-error' : undefined}
                {...register('mfaCode', { required: 'Introduce el código de verificación.' })}
              />
              {errors.mfaCode && <p id="mfa-code-error" className="auth-error" role="alert">{errors.mfaCode.message}</p>}
            </div>

            {mfaChallenge.rememberDeviceAllowed && (
              <label className="auth-checkbox-row">
                <input
                  type="checkbox"
                  className="auth-checkbox"
                  {...register('rememberDevice')}
                />
                <span>Recordar este dispositivo durante {mfaChallenge.rememberDeviceDays} días</span>
              </label>
            )}
          </>
        )}

        {postUpdateMessage && <p className="auth-success">{postUpdateMessage}</p>}
        {error && <p className="auth-error" role="alert">{error}</p>}

        <button
          type="submit"
          disabled={isSubmitting}
          className="auth-button"
        >
          {isSubmitting ? 'Validando...' : (mfaChallenge ? 'Verificar acceso' : 'Entrar')}
        </button>
      </form>
      </main>
    </section>
  );
}
