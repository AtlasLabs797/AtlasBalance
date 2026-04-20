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
      setError('La confirmación no coincide');
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
      setError(extractErrorMessage(err, 'No se pudo cambiar la contraseña'));
    }
  });

  return (
    <section className="auth-page">
      <form className="auth-card" onSubmit={onSubmit}>
        <h1 className="auth-card-title">Cambio Obligatorio de Contraseña</h1>
        <p className="auth-card-description">Tu cuenta está en primer login. Cambia la contraseña para continuar.</p>

        <div className="auth-form-group">
          <label htmlFor="passwordActual" className="auth-label">Contraseña actual</label>
          <input id="passwordActual" type="password" autoComplete="current-password" className="auth-input" {...register('passwordActual', { required: 'Requerido' })} />
          {errors.passwordActual && <p className="auth-error">{errors.passwordActual.message}</p>}
        </div>

        <div className="auth-form-group">
          <label htmlFor="passwordNueva" className="auth-label">Nueva contraseña</label>
          <input id="passwordNueva" type="password" autoComplete="new-password" className="auth-input" {...register('passwordNueva', { required: 'Requerido', minLength: { value: 8, message: 'Mínimo 8 caracteres' } })} />
          {errors.passwordNueva && <p className="auth-error">{errors.passwordNueva.message}</p>}
        </div>

        <div className="auth-form-group">
          <label htmlFor="confirmacion" className="auth-label">Confirmar nueva contraseña</label>
          <input id="confirmacion" type="password" autoComplete="new-password" className="auth-input" {...register('confirmacion', { required: 'Requerido' })} />
          {errors.confirmacion && <p className="auth-error">{errors.confirmacion.message}</p>}
        </div>

        {error && <p className="auth-error">{error}</p>}

        <button type="submit" disabled={isSubmitting} className="auth-button">{isSubmitting ? 'Guardando...' : 'Actualizar contraseña'}</button>
      </form>
    </section>
  );
}
