import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router';
import { QueryClientProvider } from '@tanstack/react-query';
import App from './App';
import { queryClient } from '@/services/queryClient';
import AppErrorBoundary from '@/components/common/AppErrorBoundary';
import { reportClientError } from '@/utils/reportClientError';
import '@/styles/variables.css';
import '@/styles/global.css';
import '@/styles/layout.css';
import '@/styles/auth.css';

const rawTheme = localStorage.getItem('theme');
const savedTheme = rawTheme === 'dark' || rawTheme === 'light' ? rawTheme : 'light';
document.documentElement.setAttribute('data-theme', savedTheme);

// Listeners globales: capturan errores que no llegan a un error boundary de
// React (codigo fuera del arbol de componentes, promesas sin catch). Nunca
// escriben en la consola del navegador; solo reportan al servidor.
window.addEventListener('unhandledrejection', (event: PromiseRejectionEvent) => {
  const reason = event.reason;
  reportClientError({
    mensaje: reason instanceof Error ? reason.message : String(reason),
    stack: reason instanceof Error ? reason.stack : undefined
  });
});

window.addEventListener('error', (event: ErrorEvent) => {
  reportClientError({
    mensaje: event.message,
    stack: event.error instanceof Error ? event.error.stack : undefined
  });
});

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <AppErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          <App />
        </BrowserRouter>
      </QueryClientProvider>
    </AppErrorBoundary>
  </React.StrictMode>
);
