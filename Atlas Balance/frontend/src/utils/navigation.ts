import { createElement } from 'react';
import type { ReactNode } from 'react';
import {
  BellRing,
  Bot,
  Building2,
  ClipboardList,
  DatabaseBackup,
  DownloadCloud,
  FileCog,
  LayoutDashboard,
  SearchCheck,
  Settings,
  TableProperties,
  Trash2,
  Upload,
  UsersRound,
  WalletCards,
} from 'lucide-react';

export type NavigationGroup = 'operacion' | 'control' | 'sistema';

export const navigationGroups: Record<NavigationGroup, { label: string }> = {
  operacion: { label: 'Operación' },
  control: { label: 'Control' },
  sistema: { label: 'Sistema' },
};

const iconProps = {
  size: 20,
  strokeWidth: 1.9,
  'aria-hidden': true,
} as const;

export interface NavigationItem {
  to: string;
  label: string;
  /** Etiqueta corta para el menú inferior móvil */
  short: string;
  icon: ReactNode;
  group: NavigationGroup;
  adminOnly?: boolean;
  aiOnly?: boolean;
}

export const navigationItems: NavigationItem[] = [
  { to: '/dashboard',            label: 'Dashboard',     short: 'Inicio',    icon: createElement(LayoutDashboard, iconProps), group: 'operacion' },
  { to: '/titulares',            label: 'Titulares',     short: 'Titulares', icon: createElement(Building2, iconProps), group: 'operacion' },
  { to: '/cuentas',              label: 'Cuentas',       short: 'Cuentas',   icon: createElement(WalletCards, iconProps), group: 'operacion' },
  { to: '/extractos',            label: 'Extractos',     short: 'Extractos', icon: createElement(TableProperties, iconProps), group: 'operacion' },
  { to: '/importacion',          label: 'Importación',   short: 'Importar',  icon: createElement(Upload, iconProps), group: 'operacion' },
  { to: '/revision',             label: 'Revisión',      short: 'Revisión',  icon: createElement(SearchCheck, iconProps), group: 'control' },
  { to: '/ia',                   label: 'IA',            short: 'IA',        icon: createElement(Bot, iconProps), group: 'control', aiOnly: true },
  { to: '/alertas',              label: 'Alertas',       short: 'Alertas',   icon: createElement(BellRing, iconProps), group: 'control' },
  { to: '/exportaciones',        label: 'Exportaciones', short: 'Exportar',  icon: createElement(DownloadCloud, iconProps), group: 'control' },
  { to: '/usuarios',             label: 'Usuarios',      short: 'Usuarios',  icon: createElement(UsersRound, iconProps), group: 'sistema', adminOnly: true },
  { to: '/auditoria',            label: 'Auditoría',     short: 'Auditoría', icon: createElement(ClipboardList, iconProps), group: 'sistema', adminOnly: true },
  { to: '/formatos-importacion', label: 'Formatos',      short: 'Formatos',  icon: createElement(FileCog, iconProps), group: 'sistema', adminOnly: true },
  { to: '/backups',              label: 'Copias',        short: 'Copias',    icon: createElement(DatabaseBackup, iconProps), group: 'sistema', adminOnly: true },
  { to: '/configuracion',        label: 'Configuración', short: 'Ajustes',   icon: createElement(Settings, iconProps), group: 'sistema', adminOnly: true },
  { to: '/papelera',             label: 'Papelera',      short: 'Papelera',  icon: createElement(Trash2, iconProps), group: 'sistema', adminOnly: true },
];

export function getVisibleNavigationItems(role?: string | null, options?: { aiAvailable?: boolean }) {
  return navigationItems.filter((item) => {
    if (item.adminOnly && role !== 'ADMIN') {
      return false;
    }

    if (item.aiOnly && !options?.aiAvailable) {
      return false;
    }

    return true;
  });
}
