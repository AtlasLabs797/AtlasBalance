import { useEffect, useRef } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { SessionTimeoutWarning } from '@/components/auth/SessionTimeoutWarning';
import { ToastViewport } from '@/components/common/ToastViewport';
import { AlertBanner } from '@/components/layout/AlertBanner';
import { BottomNav } from '@/components/layout/BottomNav';
import { Sidebar } from '@/components/layout/Sidebar';
import { TopBar } from '@/components/layout/TopBar';
import { useSessionTimeout } from '@/hooks/useSessionTimeout';
import { useAuthStore } from '@/stores/authStore';
import { useIaAvailabilityStore } from '@/stores/iaAvailabilityStore';
import { useUiStore } from '@/stores/uiStore';

export function Layout() {
  const location = useLocation();
  const isEmbedded = new URLSearchParams(location.search).get('embedded') === '1';
  const sidebarCollapsed = useUiStore((state) => state.sidebarCollapsed);
  const setSidebarCollapsed = useUiStore((state) => state.setSidebarCollapsed);
  const addToast = useUiStore((state) => state.addToast);
  const usuarioId = useAuthStore((state) => state.usuario?.id ?? null);
  const loadIaAvailability = useIaAvailabilityStore((state) => state.load);
  const clearIaAvailability = useIaAvailabilityStore((state) => state.clear);

  const { isToastVisible, isWarningVisible, remainingSeconds, resetTimeout, performLogout } =
    useSessionTimeout();
  const toastShownRef = useRef(false);

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
      setSidebarCollapsed(window.matchMedia('(min-width: 768px) and (max-width: 1023.98px)').matches);
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
    <div className={`app-shell ${sidebarCollapsed ? 'app-shell--collapsed' : ''}`}>
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
