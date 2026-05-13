import { useEffect, useState, useCallback, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import api from '@/services/api';
import { useAlertasStore } from '@/stores/alertasStore';
import { useAuthStore } from '@/stores/authStore';
import { usePermisosStore } from '@/stores/permisosStore';

const TIMEOUT_MINUTES = 20;
const WARNING_MINUTES = 1;
const TOAST_WARNING_MINUTES = 2;
const ACTIVITY_DEBOUNCE_MS = 2000;

export interface SessionTimeoutState {
  isToastVisible: boolean;
  isWarningVisible: boolean;
  remainingSeconds: number;
  resetTimeout: () => void;
  performLogout: () => void;
}

export const useSessionTimeout = (): SessionTimeoutState => {
  const navigate = useNavigate();
  const { logout } = useAuthStore();
  const clearPermisos = usePermisosStore((state) => state.clear);
  const clearAlertas = useAlertasStore((state) => state.clear);

  const [remainingSeconds, setRemainingSeconds] = useState(TIMEOUT_MINUTES * 60);
  const [isToastVisible, setIsToastVisible] = useState(false);
  const [isWarningVisible, setIsWarningVisible] = useState(false);

  const lastActivityRef = useRef<number>(Date.now());
  const inactiveSecondsRef = useRef(0);
  const debounceTimerRef = useRef<NodeJS.Timeout | null>(null);
  const toastShownRef = useRef(false);
  const warningShownRef = useRef(false);
  const logoutStartedRef = useRef(false);

  const timeoutSeconds = TIMEOUT_MINUTES * 60;
  const warningSeconds = WARNING_MINUTES * 60;
  const toastWarningSeconds = TOAST_WARNING_MINUTES * 60;

  const performLogout = useCallback(async () => {
    try {
      await api.post('/auth/logout');
    } catch {
      // La API puede estar reiniciando o la cookie ya puede haber expirado.
    } finally {
      logout();
      clearPermisos();
      clearAlertas();
      navigate('/login', { replace: true });
    }
  }, [clearAlertas, clearPermisos, logout, navigate]);

  const resetTimeout = useCallback(() => {
    lastActivityRef.current = Date.now();
    inactiveSecondsRef.current = 0;
    logoutStartedRef.current = false;
    setRemainingSeconds(timeoutSeconds);
    setIsToastVisible(false);
    setIsWarningVisible(false);
    toastShownRef.current = false;
    warningShownRef.current = false;
  }, [timeoutSeconds]);

  // Debounced activity handler
  const handleActivity = useCallback(() => {
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
    }

    debounceTimerRef.current = setTimeout(() => {
      resetTimeout();
    }, ACTIVITY_DEBOUNCE_MS);
  }, [resetTimeout]);

  // Keep the shell quiet: only update React state when warning surfaces change.
  useEffect(() => {
    const interval = setInterval(() => {
      const now = Date.now();
      const elapsed = Math.floor((now - lastActivityRef.current) / 1000);
      inactiveSecondsRef.current = elapsed;

      if (elapsed >= toastWarningSeconds && !toastShownRef.current) {
        toastShownRef.current = true;
        setIsToastVisible(true);
      } else if (elapsed < toastWarningSeconds && toastShownRef.current) {
        toastShownRef.current = false;
        setIsToastVisible(false);
      }

      const shouldShowModal = elapsed >= timeoutSeconds - warningSeconds;
      if (shouldShowModal) {
        const nextRemainingSeconds = Math.max(0, timeoutSeconds - elapsed);
        if (!warningShownRef.current) {
          warningShownRef.current = true;
          setIsWarningVisible(true);
        }
        setRemainingSeconds(nextRemainingSeconds);
      } else if (warningShownRef.current) {
        warningShownRef.current = false;
        setIsWarningVisible(false);
        setRemainingSeconds(timeoutSeconds);
      }

      if (elapsed >= timeoutSeconds && !logoutStartedRef.current) {
        logoutStartedRef.current = true;
        void performLogout();
      }
    }, 1000);

    return () => clearInterval(interval);
  }, [performLogout, timeoutSeconds, toastWarningSeconds, warningSeconds]);

  // Attach activity listeners
  useEffect(() => {
    const events = ['mousedown', 'keydown', 'scroll', 'pointerdown'];

    events.forEach(event => {
      window.addEventListener(event, handleActivity, { passive: true });
    });

    return () => {
      events.forEach(event => {
        window.removeEventListener(event, handleActivity);
      });
      if (debounceTimerRef.current) {
        clearTimeout(debounceTimerRef.current);
      }
    };
  }, [handleActivity]);

  return {
    isToastVisible,
    isWarningVisible,
    remainingSeconds,
    resetTimeout,
    performLogout,
  };
};
