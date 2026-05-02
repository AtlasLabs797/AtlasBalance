import { createElement } from 'react';
import type { ReactNode } from 'react';
import {
  BellRing,
  Building2,
  ClipboardList,
  DatabaseBackup,
  DownloadCloud,
  FileCog,
  LayoutDashboard,
  Settings,
  TableProperties,
  Trash2,
  Upload,
  UsersRound,
  WalletCards,
} from 'lucide-react';

export type NavigationGroup = 'operacion' | 'control' | 'sistema';

export const navigationGroups: Record<NavigationGroup, { label: string }> = {
  operacion: { label: 'Operacion' },
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
  /** Etiqueta corta para el menu inferior movil */
  short: string;
  icon: ReactNode;
  group: NavigationGroup;
  adminOnly?: boolean;
}

export const navigationItems: NavigationItem[] = [
  { to: '/dashboard',            label: 'Dashboard',     short: 'Inicio',    icon: createElement(LayoutDashboard, iconProps), group: 'operacion' },
  { to: '/titulares',            label: 'Titulares',     short: 'Titulares', icon: createElement(Building2, iconProps), group: 'operacion' },
  { to: '/cuentas',              label: 'Cuentas',       short: 'Cuentas',   icon: createElement(WalletCards, iconProps), group: 'operacion' },
  { to: '/extractos',            label: 'Extractos',     short: 'Extractos', icon: createElement(TableProperties, iconProps), group: 'operacion' },
  { to: '/importacion',          label: 'Importacion',   short: 'Importar',  icon: createElement(Upload, iconProps), group: 'operacion' },
  { to: '/alertas',              label: 'Alertas',       short: 'Alertas',   icon: createElement(BellRing, iconProps), group: 'control' },
  { to: '/exportaciones',        label: 'Exportaciones', short: 'Exportar',  icon: createElement(DownloadCloud, iconProps), group: 'control' },
  { to: '/usuarios',             label: 'Usuarios',      short: 'Usuarios',  icon: createElement(UsersRound, iconProps), group: 'sistema', adminOnly: true },
  { to: '/auditoria',            label: 'Auditoria',     short: 'Auditoria', icon: createElement(ClipboardList, iconProps), group: 'sistema', adminOnly: true },
  { to: '/formatos-importacion', label: 'Formatos',      short: 'Formatos',  icon: createElement(FileCog, iconProps), group: 'sistema', adminOnly: true },
  { to: '/backups',              label: 'Backups',       short: 'Backups',   icon: createElement(DatabaseBackup, iconProps), group: 'sistema', adminOnly: true },
  { to: '/configuracion',        label: 'Configuracion', short: 'Ajustes',   icon: createElement(Settings, iconProps), group: 'sistema', adminOnly: true },
  { to: '/papelera',             label: 'Papelera',      short: 'Papelera',  icon: createElement(Trash2, iconProps), group: 'sistema', adminOnly: true },
];

export function getVisibleNavigationItems(role?: string | null) {
  return navigationItems.filter((item) => !item.adminOnly || role === 'ADMIN');
}
