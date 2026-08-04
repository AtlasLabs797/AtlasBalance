/// <reference types="vite/client" />

interface ImportMetaEnv {
  // Inyectada por `define` en vite.config.ts desde appVersion de package.json.
  readonly VITE_APP_VERSION: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
