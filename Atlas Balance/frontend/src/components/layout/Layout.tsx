import { useEffect, useRef } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { SessionTimeoutWarning } from '@/components/auth/SessionTimeoutWarning';
import { ToastViewport } from '@/components/common/ToastViewport';
import { AlertBanner } from '@/components/layout/AlertBanner';
import { BottomNav } from '@/components/layout/BottomNav';
import { Sidebar } from '@/components/layout/Sidebar';
import { TopBar } from '@/components/layout/TopBar';
import { useSessionTimeout } from '@/hooks/useSessionTimeout';
import { useAlertasStore } from '@/stores/alertasStore';
import { useAuthStore } from '@/stores/authStore';
import { useIaAvailabilityStore } from '@/stores/iaAvailabilityStore';
import { usePaisScopeStore } from '@/stores/paisScopeStore';
import { useUiStore } from '@/stores/uiStore';

export function Layout() {
  const location = useLocation();
  const isEmbedded = new URLSearchParams(location.search).get('embedded') === '1';
  const sidebarCollapsed = useUiStore((state) => state.sidebarCollapsed);
  const blockingOverlayCount = useUiStore((state) => state.blockingOverlayCount);
  const setSidebarCollapsed = useUiStore((state) => state.setSidebarCollapsed);
  const addToast = useUiStore((state) => state.addToast);
  const usuarioId = useAuthStore((state) => state.usuario?.id ?? null);
  const loadIaAvailability = useIaAvailabilityStore((state) => state.load);
  const clearIaAvailability = useIaAvailabilityStore((state) => state.clear);
  const selectedPaisId = usePaisScopeStore((state) => state.selectedPaisId);
  const loadPaises = usePaisScopeStore((state) => state.loadPaises);
  const loadAlertasActivas = useAlertasStore((state) => state.loadAlertasActivas);

  const { isToastVisible, isWarningVisible, remainingSeconds, resetTimeout, performLogout } =
    useSessionTimeout();
  const toastShownRef = useRef(false);
  const hasBlockingOverlay = blockingOverlayCount > 0;

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
      clearIaAvailability();
      return undefined;
    }

    void loadIaAvailability(true);
    const timer = window.setInterval(() => void loadIaAvailability(true), 60000);
    return () => window.clearInterval(timer);
  }, [clearIaAvailability, loadIaAvailability, usuarioId]);

  useEffect(() => {
    if (!usuarioId) {
      return;
    }

    void loadPaises();
  }, [loadPaises, usuarioId]);

  useEffect(() => {
    if (!usuarioId) {
      return;
    }

    void loadAlertasActivas(selectedPaisId || undefined);
  }, [loadAlertasActivas, selectedPaisId, usuarioId]);

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
