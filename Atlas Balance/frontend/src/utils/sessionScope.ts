export type StorageLike = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;

export const USER_SCOPED_STORAGE_KEYS = {
  selectedPaisId: 'atlas_balance_selected_pais_id',
  alertasBannerDismissed: 'alertas_banner_dismissed_session',
} as const;

let sessionGeneration = 0;

export function getBrowserStorage(name: 'localStorage' | 'sessionStorage'): StorageLike | null {
  if (typeof window === 'undefined') {
    return null;
  }

  try {
    return window[name];
  } catch {
    return null;
  }
}

export function clearUserScopedStorage(
  local: StorageLike | null = getBrowserStorage('localStorage'),
  session: StorageLike | null = getBrowserStorage('sessionStorage')
): void {
  local?.removeItem(USER_SCOPED_STORAGE_KEYS.selectedPaisId);
  session?.removeItem(USER_SCOPED_STORAGE_KEYS.alertasBannerDismissed);
}

export function advanceSessionGeneration(): number {
  sessionGeneration += 1;
  return sessionGeneration;
}

export function getSessionGeneration(): number {
  return sessionGeneration;
}

export function isSessionGenerationCurrent(generation: number): boolean {
  return generation === sessionGeneration;
}

export function isSessionResponseCurrent(
  generation: number,
  expectedUserId: string | null,
  currentUserId: string | null,
): boolean {
  return isSessionGenerationCurrent(generation) && expectedUserId === currentUserId;
}

export function shouldResetUserScopedState(previousUserId: string | null, nextUserId: string): boolean {
  return previousUserId !== null && previousUserId !== nextUserId;
}
