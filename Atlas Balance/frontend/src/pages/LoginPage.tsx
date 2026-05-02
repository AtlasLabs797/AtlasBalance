import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate } from 'react-router-dom';
import QRCode from 'qrcode';
import api from '@/services/api';
import { useAlertasStore } from '@/stores/alertasStore';
import { useAuthStore } from '@/stores/authStore';
import { usePermisosStore } from '@/stores/permisosStore';
import type { LoginResponse } from '@/types';
import { extractErrorMessage } from '@/utils/errorMessage';

interface LoginForm {
  email: string;
  password: string;
  mfaCode: string;
}

interface MfaChallenge {
  id: string;
  setupRequired: boolean;
  secret: string | null;
  otpAuthUri: string | null;
}

export default function LoginPage() {
  const navigate = useNavigate();
  const setUsuario = useAuthStore((state) => state.setUsuario);
  const setPermisos = usePermisosStore((state) => state.setPermisos);
  const loadAlertasActivas = useAlertasStore((state) => state.loadAlertasActivas);
  const { register, handleSubmit, formState: { errors, isSubmitting }, setFocus } = useForm<LoginForm>();
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

    if (!mfaChallenge?.setupRequired || !mfaChallenge.otpAuthUri) {
      setMfaQrCode(null);
      return;
    }

    QRCode.toDataURL(mfaChallenge.otpAuthUri, {
      errorCorrectionLevel: 'M',
      margin: 2,
      width: 208,
    })
      .then((dataUrl) => {
        if (!cancelled) {
          setMfaQrCode(dataUrl);
        }
      })
      .catch(() => {
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
      setError('Respuesta de login invalida');
      return;
    }

    setUsuario(data.usuario, data.csrf_token);
    setPermisos(data.permisos ?? []);

    if (!data.usuario.primer_login) {
      await loadAlertasActivas();
    }

    navigate(data.usuario.primer_login ? '/cambiar-password' : '/dashboard', { replace: true });
  };

  const onSubmit = handleSubmit(async (values) => {
    setError(null);
    try {
      if (mfaChallenge) {
        const { data } = await api.post<LoginResponse>('/auth/mfa/verify', {
          challenge_id: mfaChallenge.id,
          code: values.mfaCode,
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
        });
        return;
      }

      await completeLogin(data);
    } catch (err) {
      setError(
        extractErrorMessage(
          err,
          'Revisa email y contrasena. Si has fallado varias veces, espera 30 minutos.'
        )
      );
    }
  });

  return (
    <section className="auth-page">
      <div className="auth-header">
        <div className="auth-logo-container">
          <img
            src="/logos/Atlas Balance.png"
            alt="Atlas Balance"
            className="auth-logo-image"
          />
          <div className="auth-branding">
            <h1>Atlas Balance</h1>
          </div>
        </div>
      </div>

      <form className="auth-card" onSubmit={onSubmit}>
        <h2 className="auth-card-title">{mfaChallenge ? 'Verificar acceso' : 'Iniciar sesion'}</h2>
        <p className="auth-card-description">
          {mfaChallenge ? 'Introduce el codigo temporal de tu app de autenticacion.' : 'Acceso privado para operar saldos, extractos y alertas.'}
        </p>

        {!mfaChallenge && (
          <>
            <div className="auth-form-group">
              <label htmlFor="email" className="auth-label">Email</label>
              <input
                id="email"
                type="email"
                autoFocus
                autoComplete="username"
                className="auth-input"
                placeholder="tu@email.com"
                {...register('email', { required: 'Email obligatorio' })}
              />
              {errors.email && <p className="auth-error">{errors.email.message}</p>}
            </div>

            <div className="auth-form-group">
              <label htmlFor="password" className="auth-label">Contrasena</label>
              <div className="auth-password-row">
                <input
                  id="password"
                  type={showPassword ? 'text' : 'password'}
                  autoComplete="current-password"
                  className="auth-input"
                  placeholder="Contrasena"
                  {...register('password', { required: 'Contrasena obligatoria' })}
                />
                <button
                  type="button"
                  className="auth-password-toggle"
                  onClick={() => setShowPassword((current) => !current)}
                  aria-pressed={showPassword}
                >
                  {showPassword ? 'Ocultar' : 'Mostrar'}
                </button>
              </div>
              {errors.password && <p className="auth-error">{errors.password.message}</p>}
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
                <p className="auth-card-description">Si el QR falla, introduce la clave manualmente y confirma el primer codigo.</p>
              </div>
            )}

            <div className="auth-form-group">
              <label htmlFor="mfaCode" className="auth-label">Codigo MFA</label>
              <input
                id="mfaCode"
                type="text"
                inputMode="numeric"
                autoComplete="one-time-code"
                className="auth-input"
                placeholder="000000"
                {...register('mfaCode', { required: 'Codigo obligatorio' })}
              />
              {errors.mfaCode && <p className="auth-error">{errors.mfaCode.message}</p>}
            </div>
          </>
        )}

        {postUpdateMessage && <p className="auth-success">{postUpdateMessage}</p>}
        {error && <p className="auth-error">{error}</p>}

        <button
          type="submit"
          disabled={isSubmitting}
          className="auth-button"
        >
          {isSubmitting ? 'Validando...' : (mfaChallenge ? 'Verificar' : 'Entrar')}
        </button>
      </form>

      <div className="auth-footer">
        <div className="auth-footer-content">
          <p className="auth-footer-text">by</p>
          <img
            src="/logos/Atlas Labs.png"
            alt="Atlas Labs"
            className="auth-footer-logo"
          />
          <span className="auth-footer-name">Atlas Labs</span>
        </div>
      </div>
    </section>
  );
}
