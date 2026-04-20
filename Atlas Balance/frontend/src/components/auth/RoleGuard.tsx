import type { ReactNode } from 'react';
import { Navigate } from 'react-router-dom';
import { useAuthStore } from '@/stores/authStore';

interface RoleGuardProps {
  roles: Array<'ADMIN' | 'GERENTE' | 'EMPLEADO_ULTRA' | 'EMPLEADO_PLUS' | 'EMPLEADO'>;
  children: ReactNode;
}

export function RoleGuard({ roles, children }: RoleGuardProps) {
  const usuario = useAuthStore((state) => state.usuario);

  if (!usuario || !roles.includes(usuario.rol)) {
    return <Navigate to="/dashboard" replace />;
  }

  return <>{children}</>;
}
