import { createElement } from 'react';
import type { ReactNode } from 'react';
import {
  IconDashboard,
  IconTitulares,
  IconCuentas,
  IconExtractos,
  IconImportacion,
  IconFormatos,
  IconAlertas,
  IconExportaciones,
  IconUsuarios,
  IconAuditoria,
  IconConfiguracion,
  IconBackups,
  IconPapelera,
} from '@/components/Icons';

export interface NavigationItem {
  to: string;
  label: string;
  /** Etiqueta corta para el menu inferior movil */
  short: string;
  icon: ReactNode;
  adminOnly?: boolean;
}

export const navigationItems: NavigationItem[] = [
  { to: '/dashboard',            label: 'Dashboard',     short: 'Inicio',    icon: createElement(IconDashboard) },
  { to: '/titulares',            label: 'Titulares',     short: 'Titulares', icon: createElement(IconTitulares) },
  { to: '/cuentas',              label: 'Cuentas',       short: 'Cuentas',   icon: createElement(IconCuentas) },
  { to: '/extractos',            label: 'Extractos',     short: 'Extractos', icon: createElement(IconExtractos) },
  { to: '/importacion',          label: 'Importacion',   short: 'Importar',  icon: createElement(IconImportacion) },
  { to: '/formatos-importacion', label: 'Formatos',      short: 'Formatos',  icon: createElement(IconFormatos),   adminOnly: true },
  { to: '/alertas',              label: 'Alertas',       short: 'Alertas',   icon: createElement(IconAlertas) },
  { to: '/exportaciones',        label: 'Exportaciones', short: 'Exportar',  icon: createElement(IconExportaciones) },
  { to: '/usuarios',             label: 'Usuarios',      short: 'Usuarios',  icon: createElement(IconUsuarios),    adminOnly: true },
  { to: '/auditoria',            label: 'Auditoria',     short: 'Auditoria', icon: createElement(IconAuditoria),   adminOnly: true },
  { to: '/configuracion',        label: 'Configuracion', short: 'Ajustes',   icon: createElement(IconConfiguracion), adminOnly: true },
  { to: '/backups',              label: 'Backups',       short: 'Backups',   icon: createElement(IconBackups),     adminOnly: true },
  { to: '/papelera',             label: 'Papelera',      short: 'Papelera',  icon: createElement(IconPapelera),    adminOnly: true },
];

export function getVisibleNavigationItems(role?: string | null) {
  return navigationItems.filter((item) => !item.adminOnly || role === 'ADMIN');
}
