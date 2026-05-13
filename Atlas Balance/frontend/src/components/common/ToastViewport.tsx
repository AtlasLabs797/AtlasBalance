import type { FocusEvent } from 'react';
import { useCallback, useEffect, useRef } from 'react';
import { CloseIconButton } from '@/components/common/CloseIconButton';
import { useUiStore } from '@/stores/uiStore';

const TOAST_TIMEOUT_MS = 4200;

interface ToastItemProps {
  id: string;
  type: 'success' | 'error' | 'warning' | 'info';
  message: string;
  onDismiss: (id: string) => void;
}

function ToastItem({ id, type, message, onDismiss }: ToastItemProps) {
  const timeoutRef = useRef<number | null>(null);
  const startedAtRef = useRef<number | null>(null);
  const remainingRef = useRef(TOAST_TIMEOUT_MS);
  const pausedByInteractionRef = useRef(false);

  const clearTimer = useCallback(() => {
    if (timeoutRef.current === null) {
      return;
    }

    window.clearTimeout(timeoutRef.current);
    timeoutRef.current = null;
  }, []);

  const pauseTimer = useCallback(() => {
    if (timeoutRef.current === null || startedAtRef.current === null) {
      return;
    }

    remainingRef.current = Math.max(0, remainingRef.current - (Date.now() - startedAtRef.current));
    startedAtRef.current = null;
    clearTimer();
  }, [clearTimer]);

  const startTimer = useCallback(() => {
    clearTimer();

    if (document.hidden || pausedByInteractionRef.current) {
      return;
    }

    startedAtRef.current = Date.now();
    timeoutRef.current = window.setTimeout(() => {
      onDismiss(id);
    }, remainingRef.current);
  }, [clearTimer, id, onDismiss]);

  const handlePause = () => {
    pausedByInteractionRef.current = true;
    pauseTimer();
  };

  const handleResume = () => {
    pausedByInteractionRef.current = false;
    startTimer();
  };

  const handleBlur = (event: FocusEvent<HTMLDivElement>) => {
    const nextTarget = event.relatedTarget;
    if (nextTarget instanceof Node && event.currentTarget.contains(nextTarget)) {
      return;
    }

    handleResume();
  };

  useEffect(() => {
    startTimer();

    const handleVisibilityChange = () => {
      if (document.hidden) {
        pauseTimer();
      } else {
        startTimer();
      }
    };

    document.addEventListener('visibilitychange', handleVisibilityChange);
    return () => {
      document.removeEventListener('visibilitychange', handleVisibilityChange);
      clearTimer();
    };
  }, [clearTimer, pauseTimer, startTimer]);

  return (
    <div
      className={`toast-item toast-item--${type}`}
      role={type === 'error' ? 'alert' : 'status'}
      onPointerEnter={handlePause}
      onPointerLeave={handleResume}
      onFocus={handlePause}
      onBlur={handleBlur}
    >
      <span>{message}</span>
      <CloseIconButton onClick={() => onDismiss(id)} ariaLabel="Cerrar notificación" />
    </div>
  );
}

export function ToastViewport() {
  const toasts = useUiStore((state) => state.toasts);
  const removeToast = useUiStore((state) => state.removeToast);

  if (toasts.length === 0) {
    return null;
  }

  return (
    <div className="toast-viewport" aria-live="polite" aria-atomic="false">
      {toasts.map((toast) => (
        <ToastItem
          key={toast.id}
          id={toast.id}
          type={toast.type}
          message={toast.message}
          onDismiss={removeToast}
        />
      ))}
    </div>
  );
}
