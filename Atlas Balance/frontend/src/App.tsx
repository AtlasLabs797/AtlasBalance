import { lazy, Suspense, useEffect } from 'react';
import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { ProtectedRoute } from '@/components/auth/ProtectedRoute';
import { RoleGuard } from '@/components/auth/RoleGuard';
import AppErrorBoundary from '@/components/common/AppErrorBoundary';
import { PageSkeleton } from '@/components/common/PageSkeleton';
import { Layout } from '@/components/layout/Layout';
import LoginPage from '@/pages/LoginPage';
import api from '@/services/api';
import { useAlertasStore } from '@/stores/alertasStore';
import { useAuthStore } from '@/stores/authStore';
import { usePermisosStore } from '@/stores/permisosStore';

const AlertasPage            = lazy(() => import('@/pages/AlertasPage'));
const AuditoriaPage          = lazy(() => import('@/pages/AuditoriaPage'));
const BackupsPage            = lazy(() => import('@/pages/BackupsPage'));
const ChangePasswordPage     = lazy(() => import('@/pages/ChangePasswordPage'));
const ConfiguracionPage      = lazy(() => import('@/pages/ConfiguracionPage'));
const CuentaDetailPage       = lazy(() => import('@/pages/CuentaDetailPage'));
const CuentasPage            = lazy(() => import('@/pages/CuentasPage'));
const DashboardPage          = lazy(() => import('@/pages/DashboardPage'));
const DashboardTitularPage   = lazy(() => import('@/pages/DashboardTitularPage'));
const ExportacionesPage      = lazy(() => import('@/pages/ExportacionesPage'));
const ExtractosPage          = lazy(() => import('@/pages/ExtractosPage'));
const FormatosImportacionPage = lazy(() => import('@/pages/FormatosImportacionPage'));
const ImportacionPage        = lazy(() => import('@/pages/ImportacionPage'));
const IaPage                 = lazy(() => import('@/pages/IaPage'));
const NotFoundPage           = lazy(() => import('@/pages/NotFoundPage'));
const PapeleraPage           = lazy(() => import('@/pages/PapeleraPage'));
const RevisionPage           = lazy(() => import('@/pages/RevisionPage'));
const TitularDetailPage      = lazy(() => import('@/pages/TitularDetailPage'));
const TitularesPage          = lazy(() => import('@/pages/TitularesPage'));
const UsuariosPage           = lazy(() => import('@/pages/UsuariosPage'));

function DashboardRoute({ children }: { children: JSX.Element }) {
  const usuario = useAuthStore((state) => state.usuario);
  const canViewDashboard = usePermisosStore((state) => state.canViewDashboard);
  const allowed = usuario?.rol === 'ADMIN' || (usuario?.rol === 'GERENTE' && canViewDashboard());

  return allowed ? children : <Navigate to="/extractos" replace />;
}

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

  const section = (element: JSX.Element) => (
    <AppErrorBoundary resetKey={location.key}>
      <Suspense fallback={<PageSkeleton />}>
        {element}
      </Suspense>
    </AppErrorBoundary>
  );

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
        <Route path="/dashboard" element={section(<DashboardRoute><DashboardPage /></DashboardRoute>)} />
        <Route path="/dashboard/titular/:id" element={section(<DashboardTitularPage />)} />
        <Route path="/dashboard/cuenta/:id" element={section(<CuentaDetailPage />)} />
        <Route path="/titulares" element={section(<TitularesPage />)} />
        <Route path="/titulares/:id" element={section(<TitularDetailPage />)} />
        <Route path="/cuentas" element={section(<CuentasPage />)} />
        <Route path="/cuentas/:id" element={section(<CuentaDetailPage />)} />
        <Route path="/extractos" element={section(<ExtractosPage />)} />
        <Route path="/importacion" element={section(<ImportacionPage />)} />
        <Route path="/revision" element={section(<RevisionPage />)} />
        <Route path="/ia" element={section(<IaPage />)} />
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
