const PROPS = {
  width: 20,
  height: 20,
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 1.75,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
  'aria-hidden': true,
};

export function IconSalir() {
  return (
    <svg {...PROPS}>
      <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
      <polyline points="16,17 21,12 16,7" />
      <line x1="21" y1="12" x2="9" y2="12" />
    </svg>
  );
}

export function IconSun() {
  return (
    <svg {...PROPS}>
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41" />
    </svg>
  );
}

export function IconMoon() {
  return (
    <svg {...PROPS}>
      <path d="M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z" />
    </svg>
  );
}

export function IconMenu() {
  return (
    <svg {...PROPS}>
      <line x1="4" y1="6" x2="20" y2="6" />
      <line x1="4" y1="12" x2="20" y2="12" />
      <line x1="4" y1="18" x2="20" y2="18" />
    </svg>
  );
}

// Marca del asistente (AiFace, DESIGN.md §5.5). Sustituye al icono generico
// "bot" en las superficies del canal de IA que este equipo tiene en ambito.
export type AiFaceState = 'idle' | 'listening' | 'thinking';

export function IconAiFace({ state = 'idle', size = 34 }: { state?: AiFaceState; size?: number }) {
  return (
    <span
      className={`atl-face atl-face--${state}`}
      style={{ width: size, height: size }}
      aria-hidden="true"
    >
      <span className="atl-face__shape" />
      <span className="atl-face__eye atl-face__eye--l" />
      <span className="atl-face__eye atl-face__eye--r" />
    </span>
  );
}

// Severidad de alerta de saldo (DESIGN.md §5.4: NotificationList).
export function IconAlertaDanger() {
  return (
    <svg {...PROPS}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7.5v6" />
      <circle cx="12" cy="16.5" r="1" fill="currentColor" stroke="none" />
    </svg>
  );
}

export function IconAlertaWarning() {
  return (
    <svg {...PROPS}>
      <path d="M12 3.5 21.5 20h-19Z" />
      <path d="M12 9.5v4.5" />
      <circle cx="12" cy="17" r="1" fill="currentColor" stroke="none" />
    </svg>
  );
}
