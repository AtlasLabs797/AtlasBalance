import { useEffect } from 'react';
import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { ProtectedRoute } from '@/components/auth/ProtectedRoute';
import { RoleGuard } from '@/components/auth/RoleGuard';
import AppErrorBoundary from '@/components/common/AppErrorBoundary';
import { Layout } from '@/components/layout/Layout';
import AlertasPage from '@/pages/AlertasPage';
import AuditoriaPage from '@/pages/AuditoriaPage';
import BackupsPage from '@/pages/BackupsPage';
import ChangePasswordPage from '@/pages/ChangePasswordPage';
import ConfiguracionPage from '@/pages/ConfiguracionPage';
import CuentaDetailPage from '@/pages/CuentaDetailPage';
import CuentasPage from '@/pages/CuentasPage';
import DashboardPage from '@/pages/DashboardPage';
import DashboardTitularPage from '@/pages/DashboardTitularPage';
import ExportacionesPage from '@/pages/ExportacionesPage';
import ExtractosPage from '@/pages/ExtractosPage';
import FormatosImportacionPage from '@/pages/FormatosImportacionPage';
import ImportacionPage from '@/pages/ImportacionPage';
import LoginPage from '@/pages/LoginPage';
import NotFoundPage from '@/pages/NotFoundPage';
import PapeleraPage from '@/pages/PapeleraPage';
import TitularDetailPage from '@/pages/TitularDetailPage';
import TitularesPage from '@/pages/TitularesPage';
import UsuariosPage from '@/pages/UsuariosPage';
import api from '@/services/api';
import { useAlertasStore } from '@/stores/alertasStore';
import { useAuthStore } from '@/stores/authStore';
import { usePermisosStore } from '@/stores/permisosStore';

function getCsrfTokenFromCookie(): string | null {
  const cookies = document.cookie.split(';').map((item) => item.trim());
  const tokenPair = cookies.find((item) => item.startsWith('csrf_token='));
  if (!tokenPair) {
    return null;
  }

  const rawValue = tokenPair.substring('csrf_token='.length);
  if (!rawValue) {
    return null;
  }

  const decoded = decodeURIComponent(rawValue);
  return decoded ? decoded : null;
}

export default function App() {
  const location = useLocation();
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const setUsuario = useAuthStore((state) => state.setUsuario);
  const logout = useAuthStore((state) => state.logout);
  const setLoading = useAuthStore((state) => state.setLoading);
  const setPermisos = usePermisosStore((state) => state.setPermisos);
  const clearAlertas = useAlertasStore((state) => state.clear);
  const loadAlertasActivas = useAlertasStore((state) => state.loadAlertasActivas);

  useEffect(() => {
    if (location.pathname === '/login' || isAuthenticated) {
      setLoading(false);
      return;
    }

    let mounted = true;

    const bootstrapSession = async () => {
      setLoading(true);
      try {
        const { data } = await api.get('/auth/me');
        if (!mounted) return;

        setUsuario(data.usuario, getCsrfTokenFromCookie());
        setPermisos(data.permisos ?? []);
        await loadAlertasActivas();
      } catch {
        if (!mounted) return;

        logout();
        setPermisos([]);
        clearAlertas();
      } finally {
        if (mounted) {
          setLoading(false);
        }
      }
    };

    void bootstrapSession();

    return () => {
      mounted = false;
    };
  }, [isAuthenticated, location.pathname, clearAlertas, loadAlertasActivas, logout, setLoading, setPermisos, setUsuario]);

  const section = (element: JSX.Element) => <AppErrorBoundary resetKey={location.key}>{element}</AppErrorBoundary>;

  return (
    <Routes>
      <Route path="/login" element={section(<LoginPage />)} />
      <Route
        path="/cambiar-password"
        element={section(
          <ProtectedRoute allowPrimerLogin>
            <ChangePasswordPage />
          </ProtectedRoute>
        )}
      />

      <Route
        element={
          <ProtectedRoute>
            <Layout />
          </ProtectedRoute>
        }
      >
        <Route path="/" element={<Navigate to="/dashboard" replace />} />
        <Route path="/dashboard" element={section(<DashboardPage />)} />
        <Route path="/dashboard/titular/:id" element={section(<DashboardTitularPage />)} />
        <Route path="/dashboard/cuenta/:id" element={section(<CuentaDetailPage />)} />
        <Route path="/titulares" element={section(<TitularesPage />)} />
        <Route path="/titulares/:id" element={section(<TitularDetailPage />)} />
        <Route path="/cuentas" element={section(<CuentasPage />)} />
        <Route path="/cuentas/:id" element={section(<CuentaDetailPage />)} />
        <Route path="/extractos" element={section(<ExtractosPage />)} />
        <Route path="/importacion" element={section(<ImportacionPage />)} />
        <Route
          path="/formatos-importacion"
          element={section(
            <RoleGuard roles={['ADMIN']}>
              <FormatosImportacionPage />
            </RoleGuard>
          )}
        />
        <Route path="/alertas" element={section(<AlertasPage />)} />
        <Route path="/exportaciones" element={section(<ExportacionesPage />)} />
        <Route
          path="/usuarios"
          element={section(
            <RoleGuard roles={['ADMIN']}>
              <UsuariosPage />
            </RoleGuard>
          )}
        />
        <Route
          path="/auditoria"
          element={section(
            <RoleGuard roles={['ADMIN']}>
              <AuditoriaPage />
            </RoleGuard>
          )}
        />
        <Route
          path="/configuracion"
          element={section(
            <RoleGuard roles={['ADMIN']}>
              <ConfiguracionPage />
            </RoleGuard>
          )}
        />
        <Route
          path="/backups"
          element={section(
            <RoleGuard roles={['ADMIN']}>
              <BackupsPage />
            </RoleGuard>
          )}
        />
        <Route
          path="/papelera"
          element={section(
            <RoleGuard roles={['ADMIN']}>
              <PapeleraPage />
            </RoleGuard>
          )}
        />
      </Route>

      <Route path="*" element={section(<NotFoundPage />)} />
    </Routes>
  );
}
