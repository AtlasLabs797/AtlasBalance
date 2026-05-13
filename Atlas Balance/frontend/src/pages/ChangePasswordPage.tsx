import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate } from 'react-router-dom';
import api from '@/services/api';
import { useAuthStore } from '@/stores/authStore';
import { usePermisosStore } from '@/stores/permisosStore';
import { extractErrorMessage } from '@/utils/errorMessage';

interface ChangePasswordForm {
  passwordActual: string;
  passwordNueva: string;
  confirmacion: string;
}

export default function ChangePasswordPage() {
  const navigate = useNavigate();
  const usuario = useAuthStore((state) => state.usuario);
  const setUsuario = useAuthStore((state) => state.setUsuario);
  const setPermisos = usePermisosStore((state) => state.setPermisos);
  const [error, setError] = useState<string | null>(null);
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<ChangePasswordForm>();

  const onSubmit = handleSubmit(async ({ passwordActual, passwordNueva, confirmacion }) => {
    setError(null);

    if (passwordNueva !== confirmacion) {
      setError('La confirmación no coincide.');
      return;
    }

    try {
      const { data } = await api.put('/auth/cambiar-password', { password_actual: passwordActual, password_nueva: passwordNueva });
      if (data.usuario) {
        setUsuario(data.usuario, data.csrf_token);
        setPermisos(data.permisos ?? []);
      } else if (usuario) {
        setUsuario({ ...usuario, primer_login: false });
      }
      navigate('/dashboard', { replace: true });
    } catch (err) {
      setError(extractErrorMessage(err, 'No se pudo cambiar la contraseña.'));
    }
  });

  return (
    <section className="auth-page">
      <form className="auth-card" onSubmit={onSubmit}>
        <h1 className="auth-card-title">Cambio obligatorio de contraseña</h1>
        <p className="auth-card-description">Es tu primer inicio de sesión. Cambia la contraseña para continuar.</p>

        <div className="auth-form-group">
          <label htmlFor="passwordActual" className="auth-label">Contraseña actual</label>
          <input
            id="passwordActual"
            type="password"
            autoComplete="current-password"
            className="auth-input"
            aria-invalid={errors.passwordActual ? true : undefined}
            aria-describedby={errors.passwordActual ? 'password-actual-error' : undefined}
            {...register('passwordActual', { required: 'Introduce tu contraseña actual.' })}
          />
          {errors.passwordActual ? (
            <p id="password-actual-error" className="auth-error" role="alert">{errors.passwordActual.message}</p>
          ) : null}
        </div>

        <div className="auth-form-group">
          <label htmlFor="passwordNueva" className="auth-label">Nueva contraseña</label>
          <input
            id="passwordNueva"
            type="password"
            autoComplete="new-password"
            className="auth-input"
            aria-invalid={errors.passwordNueva ? true : undefined}
            aria-describedby={errors.passwordNueva ? 'password-nueva-error' : undefined}
            {...register('passwordNueva', {
              required: 'Introduce una contraseña nueva.',
              minLength: { value: 12, message: 'Mínimo 12 caracteres' },
            })}
          />
          {errors.passwordNueva ? (
            <p id="password-nueva-error" className="auth-error" role="alert">{errors.passwordNueva.message}</p>
          ) : null}
        </div>

        <div className="auth-form-group">
          <label htmlFor="confirmacion" className="auth-label">Confirmar nueva contraseña</label>
          <input
            id="confirmacion"
            type="password"
            autoComplete="new-password"
            className="auth-input"
            aria-invalid={errors.confirmacion ? true : undefined}
            aria-describedby={errors.confirmacion ? 'confirmacion-error' : undefined}
            {...register('confirmacion', { required: 'Repite la contraseña nueva.' })}
          />
          {errors.confirmacion ? (
            <p id="confirmacion-error" className="auth-error" role="alert">{errors.confirmacion.message}</p>
          ) : null}
        </div>

        {error ? <p className="auth-error" role="alert">{error}</p> : null}

        <button type="submit" disabled={isSubmitting} className="auth-button">
          {isSubmitting ? 'Guardando...' : 'Actualizar contraseña'}
        </button>
      </form>
    </section>
  );
}
