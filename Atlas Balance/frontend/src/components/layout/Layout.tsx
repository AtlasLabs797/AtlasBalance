import { useEffect, useRef } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { SessionTimeoutWarning } from '@/components/auth/SessionTimeoutWarning';
import { ToastViewport } from '@/components/common/ToastViewport';
import { AlertBanner } from '@/components/layout/AlertBanner';
import { BottomNav } from '@/components/layout/BottomNav';
import { Sidebar } from '@/components/layout/Sidebar';
import { TopBar } from '@/components/layout/TopBar';
import { useSessionTimeout } from '@/hooks/useSessionTimeout';
import { useUiStore } from '@/stores/uiStore';

export function Layout() {
  const location = useLocation();
  const isEmbedded = new URLSearchParams(location.search).get('embedded') === '1';
  const sidebarCollapsed = useUiStore((state) => state.sidebarCollapsed);
  const setSidebarCollapsed = useUiStore((state) => state.setSidebarCollapsed);
  const addToast = useUiStore((state) => state.addToast);

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
      setSidebarCollapsed(window.innerWidth <= 1024 && window.innerWidth > 768);
    };

    onResize();
    window.addEventListener('resize', onResize);
    return () => window.removeEventListener('resize', onResize);
  }, [isEmbedded, setSidebarCollapsed]);

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
