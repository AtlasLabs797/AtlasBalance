import { Navigate, useLocation } from 'react-router-dom';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import { useAuthStore } from '@/stores/authStore';

interface ProtectedRouteProps {
  children: JSX.Element;
  allowPrimerLogin?: boolean;
}

export function ProtectedRoute({ children, allowPrimerLogin = false }: ProtectedRouteProps) {
  const location = useLocation();
  const { isAuthenticated, isLoading, usuario } = useAuthStore();

  if (isLoading) {
    return <PageSkeleton rows={2} />;
  }

  if (!isAuthenticated || !usuario) {
    return <Navigate to="/login" replace state={{ from: location }} />;
  }

  if (usuario.primer_login && !allowPrimerLogin) {
    return <Navigate to="/cambiar-password" replace />;
  }

  if (!usuario.primer_login && allowPrimerLogin) {
    return <Navigate to="/dashboard" replace />;
  }

  return children;
}
