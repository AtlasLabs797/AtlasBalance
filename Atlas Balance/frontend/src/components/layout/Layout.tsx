import { useEffect, useRef } from 'react';
import { Outlet, useLocation } from 'react-router';
import { SessionTimeoutWarning } from '@/components/auth/SessionTimeoutWarning';
import { ToastViewport } from '@/components/common/ToastViewport';
import { AlertBanner } from '@/components/layout/AlertBanner';
import { BottomNav } from '@/components/layout/BottomNav';
import { Sidebar } from '@/components/layout/Sidebar';
import { TopBar } from '@/components/layout/TopBar';
import { useAlertasActivasQuery } from '@/hooks/queries/useAlertasActivasQuery';
import { useIaConfigQuery } from '@/hooks/queries/useIaConfigQuery';
import { usePaisesQuery } from '@/hooks/queries/usePaisesQuery';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useSessionTimeout } from '@/hooks/useSessionTimeout';
import { useAuthStore } from '@/stores/authStore';
import { useUiStore } from '@/stores/uiStore';

// Titulos por ruta de App.tsx. Los segmentos ":param" casan con cualquier valor.
const TITULOS_POR_RUTA: ReadonlyArray<readonly [string, string]> = [
  ['/dashboard', 'Dashboard'],
  ['/dashboard/titular/:id', 'Dashboard titular'],
  ['/dashboard/cuenta/:id', 'Dashboard cuenta'],
  ['/titulares', 'Titulares'],
  ['/titulares/:id', 'Detalle de titular'],
  ['/cuentas', 'Cuentas'],
  ['/cuentas/:id', 'Detalle de cuenta'],
  ['/extractos', 'Extractos'],
  ['/importacion', 'Importación'],
  ['/conciliacion', 'Conciliación'],
  ['/revision', 'Revisión'],
  ['/ia', 'IA'],
  ['/formatos-importacion', 'Formatos de importación'],
  ['/alertas', 'Alertas'],
  ['/exportaciones', 'Exportaciones'],
  ['/usuarios', 'Usuarios'],
  ['/auditoria', 'Auditoría'],
  ['/configuracion', 'Configuración'],
  ['/configuracion/paises', 'Configuración de países'],
  ['/backups', 'Backups'],
  ['/papelera', 'Papelera'],
];

// Compara segmento a segmento; un segmento ":param" casa con cualquier valor.
function coincideRuta(patron: string, ruta: string): boolean {
  const segmentosPatron = patron.split('/').filter(Boolean);
  const segmentosRuta = ruta.split('/').filter(Boolean);
  if (segmentosPatron.length !== segmentosRuta.length) {
    return false;
  }
  return segmentosPatron.every(
    (segmento, indice) => segmento.startsWith(':') || segmento === segmentosRuta[indice]
  );
}

function obtenerTituloRuta(pathname: string): string {
  const coincidencia = TITULOS_POR_RUTA.find(([patron]) => coincideRuta(patron, pathname));
  return coincidencia ? coincidencia[1] : 'Atlas Balance';
}

export function Layout() {
  const location = useLocation();
  useDocumentTitle(obtenerTituloRuta(location.pathname));
  const isEmbedded = new URLSearchParams(location.search).get('embedded') === '1';
  const sidebarCollapsed = useUiStore((state) => state.sidebarCollapsed);
  const blockingOverlayCount = useUiStore((state) => state.blockingOverlayCount);
  const setSidebarCollapsed = useUiStore((state) => state.setSidebarCollapsed);
  const addToast = useUiStore((state) => state.addToast);
  const usuarioId = useAuthStore((state) => state.usuario?.id ?? null);

  const { isToastVisible, isWarningVisible, remainingSeconds, resetTimeout, performLogout } =
    useSessionTimeout();
  const toastShownRef = useRef(false);
  const hasBlockingOverlay = blockingOverlayCount > 0;

  // Hidratar caches de shell con TanStack Query (alertas, IA, paises).
  useAlertasActivasQuery();
  useIaConfigQuery();
  usePaisesQuery();

  useEffect(() => {
    if (!hasBlockingOverlay) {
      return undefined;
    }

    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    document.body.dataset.overlayOpen = 'true';

    return () => {
      document.body.style.overflow = previousOverflow;
      delete document.body.dataset.overlayOpen;
    };
  }, [hasBlockingOverlay]);

  // Show toast warning when inactivity reaches 18 minutes.
  useEffect(() => {
    if (isToastVisible && !toastShownRef.current) {
      toastShownRef.current = true;
      addToast({
        type: 'warning',
        message: 'Tu sesión expirará en 2 minutos si no hay actividad',
      });
    } else if (!isToastVisible && toastShownRef.current) {
      toastShownRef.current = false;
    }
  }, [isToastVisible, addToast]);

  useEffect(() => {
    if (isEmbedded) {
      return;
    }

    const onResize = () => {
      setSidebarCollapsed(window.matchMedia('(min-width: 768px) and (max-width: 1199.98px)').matches);
    };

    onResize();
    window.addEventListener('resize', onResize);
    return () => window.removeEventListener('resize', onResize);
  }, [isEmbedded, setSidebarCollapsed]);

  useEffect(() => {
    if (!usuarioId) {
      return undefined;
    }
    return undefined;
  }, [usuarioId]);

  if (isEmbedded) {
    return (
      <div className="app-shell-embedded">
        <main className="app-content app-content--embedded">
          <Outlet />
        </main>
        <ToastViewport />
        <SessionTimeoutWarning
          open={isWarningVisible}
          remainingSeconds={remainingSeconds}
          onContinue={resetTimeout}
          onLogout={performLogout}
        />
      </div>
    );
  }

  return (
    <div className={`app-shell ${sidebarCollapsed ? 'app-shell--collapsed' : ''}${hasBlockingOverlay ? ' app-shell--overlay-open' : ''}`}>
      <a className="skip-link" href="#main-content">Saltar al contenido</a>
      <Sidebar />
      <div className="app-main">
        <TopBar />
        <AlertBanner />
        <main id="main-content" className="app-content">
          <Outlet />
        </main>
      </div>
      <BottomNav />
      <ToastViewport />
      <SessionTimeoutWarning
        open={isWarningVisible}
        remainingSeconds={remainingSeconds}
        onContinue={resetTimeout}
        onLogout={performLogout}
      />
    </div>
  );
}
