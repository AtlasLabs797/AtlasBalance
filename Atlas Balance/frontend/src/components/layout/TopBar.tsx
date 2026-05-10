import { useEffect, useMemo, useState } from 'react';
import { Bot } from 'lucide-react';
import { useLocation, useNavigate } from 'react-router-dom';
import { IconMenu, IconMoon, IconSalir, IconSun } from '@/components/Icons';
import { AiChatPanel } from '@/components/ia/AiChatPanel';
import { navigationItems } from '@/utils/navigation';
import api from '@/services/api';
import { useAlertasStore } from '@/stores/alertasStore';
import { useAuthStore } from '@/stores/authStore';
import { usePermisosStore } from '@/stores/permisosStore';
import { useUiStore } from '@/stores/uiStore';
import type { IaConfig } from '@/types';

export function TopBar() {
  const navigate = useNavigate();
  const location = useLocation();
  const theme = useUiStore((state) => state.theme);
  const sidebarCollapsed = useUiStore((state) => state.sidebarCollapsed);
  const toggleTheme = useUiStore((state) => state.toggleTheme);
  const toggleSidebar = useUiStore((state) => state.toggleSidebar);
  const usuario = useAuthStore((state) => state.usuario);
  const logout = useAuthStore((state) => state.logout);
  const clearPermisos = usePermisosStore((state) => state.clear);
  const clearAlertas = useAlertasStore((state) => state.clear);
  const [chatOpen, setChatOpen] = useState(false);
  const [aiAvailable, setAiAvailable] = useState(false);

  const pageContext = useMemo(() => {
    const exact = navigationItems.find((item) => item.to === location.pathname);
    if (exact) {
      return { title: exact.label, breadcrumb: 'Atlas Balance' };
    }

    const section = navigationItems
      .filter((item) => location.pathname.startsWith(`${item.to}/`))
      .sort((a, b) => b.to.length - a.to.length)[0];

    if (section) {
      return { title: section.label, breadcrumb: 'Detalle' };
    }

    return { title: 'Atlas Balance', breadcrumb: 'Operacion local' };
  }, [location.pathname]);

  const handleLogout = async () => {
    try {
      await api.post('/auth/logout');
    } catch {
      // no-op
    } finally {
      logout();
      clearPermisos();
      clearAlertas();
      navigate('/login', { replace: true });
    }
  };

  useEffect(() => {
    let cancelled = false;

    const loadIaStatus = async () => {
      if (!usuario) {
        setAiAvailable(false);
        setChatOpen(false);
        return;
      }

      try {
        const { data } = await api.get<IaConfig>('/ia/config');
        if (cancelled) return;
        const available = Boolean(data.habilitada && data.usuario_puede_usar);
        setAiAvailable(available);
        if (!available) {
          setChatOpen(false);
        }
      } catch {
        if (!cancelled) {
          setAiAvailable(false);
          setChatOpen(false);
        }
      }
    };

    void loadIaStatus();
    const timer = window.setInterval(() => void loadIaStatus(), 60000);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [location.pathname, usuario]);

  return (
    <header className="app-topbar">
      <div className="app-topbar-title">
        <button
          type="button"
          className={`sidebar-toggle${sidebarCollapsed ? ' sidebar-toggle--collapsed' : ''}`}
          onClick={toggleSidebar}
          aria-expanded={!sidebarCollapsed}
          aria-label={sidebarCollapsed ? 'Expandir navegacion lateral' : 'Contraer navegacion lateral'}
          title={sidebarCollapsed ? 'Expandir navegacion lateral' : 'Contraer navegacion lateral'}
        >
          <IconMenu />
        </button>
        <div className="app-topbar-heading">
          <span className="app-topbar-page">{pageContext.title}</span>
          <span className="app-topbar-breadcrumb">{pageContext.breadcrumb}</span>
        </div>
      </div>
      <div className="app-topbar-actions">
        <span className="app-topbar-user">{usuario?.nombre_completo ?? 'Sin sesion'}</span>
        <button
          type="button"
          className="theme-toggle"
          onClick={toggleTheme}
          aria-pressed={theme === 'dark'}
          aria-label={`Cambiar a modo ${theme === 'light' ? 'oscuro' : 'claro'}`}
          title={`Cambiar a modo ${theme === 'light' ? 'oscuro' : 'claro'}`}
        >
          {theme === 'light' ? <IconMoon /> : <IconSun />}
        </button>
        <button type="button" className="logout-button" onClick={handleLogout} aria-label="Cerrar sesion">
          <IconSalir />
        </button>
      </div>
      {aiAvailable ? (
        <div className="ai-floating-widget">
          <button
            type="button"
            className={`ai-floating-button${chatOpen ? ' ai-floating-button--active' : ''}`}
            onClick={() => setChatOpen((current) => !current)}
            aria-pressed={chatOpen}
            aria-label={chatOpen ? 'Cerrar chat IA' : 'Abrir chat IA'}
            title={chatOpen ? 'Cerrar chat IA' : 'Abrir chat IA'}
          >
            <Bot size={20} aria-hidden="true" />
          </button>
          {chatOpen ? (
            <div className="ai-floating-chat" role="dialog" aria-modal="false" aria-label="Chat flotante IA">
              <AiChatPanel compact onClose={() => setChatOpen(false)} />
            </div>
          ) : null}
        </div>
      ) : null}
    </header>
  );
}
