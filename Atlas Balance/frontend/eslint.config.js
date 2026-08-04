// ESLint 10 usa flat config. Sustituye a .eslintrc.cjs, que ya no se lee.
//
// El conjunto de reglas exigido es el MISMO que antes de la subida. Esto es
// una migracion de tooling, no un endurecimiento del linter: si algo falla
// aqui, es codigo que ya fallaba antes.
//
// PENDIENTE, decision aparte: eslint-plugin-react-hooks 7 trae el paquete de
// reglas de React Compiler, que no existian en la version 4. Activarlas
// (cambiando el bloque de `rules` de abajo por
// `...reactHooks.configs['recommended-latest'].rules`) saca 105 errores hoy:
//
//     62  react-hooks/set-state-in-effect
//     34  react-hooks/refs
//      4  react-hooks/purity
//      2  react-hooks/immutability
//      2  react-hooks/preserve-manual-memoization
//      1  react-hooks/incompatible-library
//
// No son regresiones ni bugs detectados: son patrones que esas reglas nuevas
// desaconsejan. Arreglarlos es un trabajo de refactor de la app con riesgo
// real, no un ajuste de configuracion, asi que se deja fuera a proposito.
import js from '@eslint/js';
import globals from 'globals';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  {
    ignores: ['dist', '.test-dist', '.test-dist-build-*', 'node_modules.blocked-*'],
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ['**/*.{ts,tsx}'],
    languageOptions: {
      ecmaVersion: 2020,
      globals: globals.browser,
    },
    linterOptions: {
      reportUnusedDisableDirectives: 'error',
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      // Equivalente a lo que traia react-hooks 4 en `recommended`.
      'react-hooks/rules-of-hooks': 'error',
      'react-hooks/exhaustive-deps': 'warn',
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
      '@typescript-eslint/no-unused-vars': ['warn', { argsIgnorePattern: '^_' }],
      '@typescript-eslint/no-explicit-any': 'warn',
    },
  }
);
