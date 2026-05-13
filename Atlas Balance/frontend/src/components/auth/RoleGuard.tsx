import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { EmptyState } from '@/components/common/EmptyState';
import { useAuthStore } from '@/stores/authStore';

interface RoleGuardProps {
  roles: Array<'ADMIN' | 'GERENTE' | 'EMPLEADO_ULTRA' | 'EMPLEADO_PLUS' | 'EMPLEADO'>;
  children: ReactNode;
}

export function RoleGuard({ roles, children }: RoleGuardProps) {
  const usuario = useAuthStore((state) => state.usuario);

  if (!usuario || !roles.includes(usuario.rol)) {
    return (
      <section className="page-placeholder">
        <EmptyState
          variant="permission"
          title="No tienes permiso para abrir esta vista."
          subtitle="Si necesitas acceso, pide a un administrador que revise tu rol o permisos."
          primaryAction={<Link to="/extractos">Ir a Extractos</Link>}
        />
      </section>
    );
  }

  return <>{children}</>;
}
