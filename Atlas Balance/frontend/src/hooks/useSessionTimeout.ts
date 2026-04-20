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

  const [inactiveSeconds, setInactiveSeconds] = useState(0);
  const [isToastVisible, setIsToastVisible] = useState(false);
  const [isWarningVisible, setIsWarningVisible] = useState(false);

  const lastActivityRef = useRef<number>(Date.now());
  const debounceTimerRef = useRef<NodeJS.Timeout | null>(null);
  const toastShownRef = useRef(false);
  const warningShownRef = useRef(false);

  const timeoutSeconds = TIMEOUT_MINUTES * 60;
  const warningSeconds = WARNING_MINUTES * 60;
  const toastWarningSeconds = TOAST_WARNING_MINUTES * 60;
  const remainingSeconds = Math.max(0, timeoutSeconds - inactiveSeconds);

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
    setInactiveSeconds(0);
    setIsToastVisible(false);
    setIsWarningVisible(false);
    toastShownRef.current = false;
    warningShownRef.current = false;
  }, []);

  // Debounced activity handler
  const handleActivity = useCallback(() => {
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
    }

    debounceTimerRef.current = setTimeout(() => {
      resetTimeout();
    }, ACTIVITY_DEBOUNCE_MS);
  }, [resetTimeout]);

  // Timer interval to track inactive seconds
  useEffect(() => {
    const interval = setInterval(() => {
      const now = Date.now();
      const elapsed = Math.floor((now - lastActivityRef.current) / 1000);
      setInactiveSeconds(elapsed);

      // Auto logout after timeout
      if (elapsed >= timeoutSeconds) {
        performLogout();
      }
    }, 1000);

    return () => clearInterval(interval);
  }, [timeoutSeconds, performLogout]);

  // Show toast warning at 18 minutes
  useEffect(() => {
    if (inactiveSeconds >= toastWarningSeconds && !toastShownRef.current) {
      toastShownRef.current = true;
      setIsToastVisible(true);
    } else if (inactiveSeconds < toastWarningSeconds && toastShownRef.current) {
      toastShownRef.current = false;
      setIsToastVisible(false);
    }
  }, [inactiveSeconds, toastWarningSeconds]);

  // Show warning modal at 19 minutes
  useEffect(() => {
    if (inactiveSeconds >= timeoutSeconds - warningSeconds && !warningShownRef.current) {
      warningShownRef.current = true;
      setIsWarningVisible(true);
    } else if (inactiveSeconds < timeoutSeconds - warningSeconds && warningShownRef.current) {
      warningShownRef.current = false;
      setIsWarningVisible(false);
    }
  }, [inactiveSeconds, timeoutSeconds, warningSeconds]);

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
