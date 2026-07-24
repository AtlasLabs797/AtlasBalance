import { createLogger, defineConfig, type Logger, type LogErrorOptions, type LogOptions, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';
import { fileURLToPath } from 'url';
import { readFileSync } from 'fs';

// V-02-03: leemos la version del package.json para inyectarla como
// VITE_APP_VERSION (y la fuente sigue siendo appVersion). Asi sidebar /
// topbar ya no divergen del backend (VERSION, Directory.Build.props).
const packageJson = JSON.parse(
  readFileSync(path.resolve(__dirname, 'package.json'), 'utf-8'),
) as { version?: string; appVersion?: string };
const injectAppVersion = packageJson.appVersion ?? packageJson.version ?? 'desarrollo';

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

const editorPositionPattern = /:(\d+)(:(\d+))?$/;

const stripEditorPosition = (file: string) => file.trim().replace(editorPositionPattern, '');

const decodedPathCandidates = (file: string) => {
  const trimmed = stripEditorPosition(file);

  try {
    return [trimmed, stripEditorPosition(decodeURIComponent(trimmed))];
  } catch {
    return [trimmed];
  }
};

const hasUncPrefix = (file: string) =>
  decodedPathCandidates(file).some((candidate) => candidate.replace(/\//g, '\\').startsWith('\\\\'));

const resolveEditorPath = (file: string, root: string) => {
  const candidate = stripEditorPosition(file);

  if (hasUncPrefix(candidate)) {
    return { allowed: false, reason: 'UNC paths are not allowed' };
  }

  let localPath = candidate;
  if (/^file:/i.test(candidate)) {
    let fileUrl: URL;
    try {
      fileUrl = new URL(candidate);
    } catch {
      return { allowed: false, reason: 'invalid file URL' };
    }

    if (fileUrl.host && fileUrl.host.toLowerCase() !== 'localhost') {
      return { allowed: false, reason: 'remote file URLs are not allowed' };
    }

    localPath = fileURLToPath(fileUrl);
  }

  const resolvedRoot = path.resolve(root);
  const resolvedPath = path.resolve(resolvedRoot, localPath);
  const relativePath = path.relative(resolvedRoot, resolvedPath);
  const isInsideRoot = relativePath === '' || (!relativePath.startsWith('..') && !path.isAbsolute(relativePath));

  if (!isInsideRoot) {
    return { allowed: false, reason: 'path is outside the frontend root' };
  }

  return { allowed: true, path: resolvedPath };
};

const openInEditorGuard = (root: string): Plugin => ({
  name: 'atlas-open-in-editor-guard',
  apply: 'serve',
  configureServer(server) {
    server.middlewares.use('/__open-in-editor', (req, res, next) => {
      let file: string | null;

      try {
        const requestUrl = req.url?.startsWith('http') ? req.url : `http://localhost${req.url ?? ''}`;
        file = new URL(requestUrl).searchParams.get('file');
      } catch {
        res.statusCode = 400;
        res.end('Invalid open-in-editor URL.');
        return;
      }

      if (!file) {
        next();
        return;
      }

      const validation = resolveEditorPath(file, root);
      if (!validation.allowed) {
        server.config.logger.warn(`[security] Blocked unsafe open-in-editor request: ${validation.reason}.`);
        res.statusCode = 400;
        res.end('Unsafe open-in-editor path rejected.');
        return;
      }

      next();
    });
  },
});

export default defineConfig({
  customLogger: redactingLogger,
  plugins: [openInEditorGuard(__dirname), react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
    allowedHosts: ['localhost', '127.0.0.1'],
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
  define: {
    'import.meta.env.VITE_APP_VERSION': JSON.stringify(injectAppVersion),
  },
  build: {
    outDir: process.env.VITE_BUILD_OUT_DIR ?? 'dist',
    emptyOutDir: process.env.VITE_BUILD_OUT_DIR ? true : false,
    sourcemap: 'hidden',
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
            id.includes('node_modules/react-router-dom/') ||
            id.includes('node_modules/react-router/')
          ) return 'vendor';
        },
      },
    },
  },
});
