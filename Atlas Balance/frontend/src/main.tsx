import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClientProvider } from '@tanstack/react-query';
import App from './App';
import { queryClient } from '@/services/queryClient';
import '@/styles/variables.css';
import '@/styles/global.css';
import '@/styles/layout.css';
import '@/styles/auth.css';

const rawTheme = localStorage.getItem('theme');
const savedTheme = rawTheme === 'dark' || rawTheme === 'light' ? rawTheme : 'light';
document.documentElement.setAttribute('data-theme', savedTheme);

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </QueryClientProvider>
  </React.StrictMode>
);
