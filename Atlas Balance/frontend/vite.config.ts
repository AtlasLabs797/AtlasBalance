import { createLogger, defineConfig, type Logger, type LogErrorOptions, type LogOptions } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';

const baseLogger = createLogger();

const sensitiveLogPatterns: Array<[RegExp, string]> = [
  [/Cookie:\s*[^\r\n]*/gi, 'Cookie: [REDACTED]'],
  [/Set-Cookie:\s*[^\r\n]*/gi, 'Set-Cookie: [REDACTED]'],
  [/Authorization:\s*Bearer\s+[^\s\r\n]+/gi, 'Authorization: Bearer [REDACTED]'],
  [/X-CSRF-Token:\s*[^\r\n]*/gi, 'X-CSRF-Token: [REDACTED]'],
  [/(access_token|refresh_token|mfa_trusted|csrf_token)=([^;\s\r\n]+)/gi, '$1=[REDACTED]'],
  [/\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b/g, '[JWT_REDACTED]'],
  [/\b(sk-[A-Za-z0-9_-]{16,}|sk_live_[A-Za-z0-9_-]{16,}|github_pat_[A-Za-z0-9_]{20,})\b/g, '[SECRET_REDACTED]'],
];

const redactLogMessage = (message: string) =>
  sensitiveLogPatterns.reduce(
    (current, [pattern, replacement]) => current.replace(pattern, replacement),
    message
  );

const redactingLogger: Logger = {
  ...baseLogger,
  info(message: string, options?: LogOptions) {
    baseLogger.info(redactLogMessage(message), options);
  },
  warn(message: string, options?: LogOptions) {
    baseLogger.warn(redactLogMessage(message), options);
  },
  warnOnce(message: string, options?: LogOptions) {
    baseLogger.warnOnce(redactLogMessage(message), options);
  },
  error(message: string, options?: LogErrorOptions) {
    baseLogger.error(redactLogMessage(message), options);
  },
};

export default defineConfig({
  customLogger: redactingLogger,
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: false,
    reportCompressedSize: false,
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (id.includes('node_modules/recharts')) return 'charts';
          if (id.includes('node_modules/zustand')) return 'state';
          if (id.includes('node_modules/lucide-react')) return 'icons';
          if (id.includes('node_modules/react-hook-form')) return 'forms';
          if (id.includes('node_modules/axios')) return 'http';
          if (
            id.includes('node_modules/react/') ||
            id.includes('node_modules/react-dom/') ||
            id.includes('node_modules/react-router-dom/')
          ) return 'vendor';
        },
      },
    },
  },
});
