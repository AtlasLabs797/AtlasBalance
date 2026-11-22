import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

// La API serializa con DefaultIgnoreCondition = WhenWritingNull: un null no
// viaja al navegador, el campo llega undefined. Los tipos deben admitir la
// ausencia del campo, no solo el null (F-3).
//
// El chequeo de tipos se hace con la API del compilador en vez de con un
// `import type` en este fichero: `src/types/index.ts` reexporta tipos desde
// `@/stores/aiChatStore`, y bajo `moduleResolution: NodeNext` ese alias sin
// extension no resuelve, de modo que importar el fichero de tipos desde el
// programa de test rompe la compilacion entera de la suite.
const frontendRoot = path.resolve(fileURLToPath(new URL('../../', import.meta.url)));
const typesFile = path.join(frontendRoot, 'src', 'types', 'index.ts');

// TypeScript normaliza las rutas a barra final; para comparar hay que
// normalizar igual en el host y en el filtro de diagnosticos.
const normalizePath = (fileName: string) => fileName.replace(/\//g, '\\').toLowerCase();

const snippetFileName = path.join(frontendRoot, '__auditoriaValores_snippet.ts');
const snippet = `
import type { AuditCellEntry, Auditoria, AuditoriaListItem } from './src/types/index';

export const auditoria: Auditoria = {
  id: 'auditoria-1',
  usuario_id: null,
  tipo_accion: 'UPDATE',
  entidad_tipo: 'EXTRACTO',
  entidad_id: 'extrato-1',
  celda_referencia: 'Fila 3',
  columna_nombre: 'monto',
  timestamp: '2026-11-16T10:00:00Z',
  ip_address: null,
  detalles_json: null,
};

export const auditoriaLista: AuditoriaListItem = {
  id: 'auditoria-2',
  timestamp: '2026-11-16T10:00:00Z',
  usuario_id: null,
  usuario_nombre: null,
  tipo_accion: 'UPDATE',
  entidad_tipo: 'EXTRACTO',
  entidad_id: 'extrato-1',
  cuenta_id: 'cuenta-1',
  cuenta_nombre: 'Cuenta 1',
  titular_id: null,
  titular_nombre: null,
  celda_referencia: 'Fila 3',
  columna_nombre: 'monto',
  ip_address: null,
  detalles_json: null,
};

export const celdaAuditada: AuditCellEntry = {
  id: 'celda-1',
  tipo_accion: 'UPDATE',
  celda_referencia: 'Fila 3',
  columna_nombre: 'monto',
  timestamp: '2026-11-16T10:00:00Z',
  usuario_id: null,
};

// Control: si el import no resolviera, el tipo seria any y este error no
// apareceria. Su presencia demuestra que los tipos se estan comprobando.
export const controlDeResolucion: number = auditoria.id;
`;

function typeErrorsDelSnippet(): { code: number; message: string }[] {
  const options: ts.CompilerOptions = {
    target: ts.ScriptTarget.ES2020,
    module: ts.ModuleKind.ESNext,
    moduleResolution: ts.ModuleResolutionKind.Bundler,
    strict: true,
    noEmit: true,
    skipLibCheck: true,
    baseUrl: frontendRoot,
    paths: { '@/*': ['./src/*'] },
  };

  const host = ts.createCompilerHost(options);
  const getSourceFileOriginal = host.getSourceFile.bind(host);
  const esElSnippet = (fileName: string) => normalizePath(fileName) === normalizePath(snippetFileName);
  host.fileExists = (fileName) => esElSnippet(fileName) || ts.sys.fileExists(fileName);
  host.readFile = (fileName) => (esElSnippet(fileName) ? snippet : ts.sys.readFile(fileName));
  host.getSourceFile = (fileName, languageVersion, onError, shouldCreateNewSourceFile) => {
    if (esElSnippet(fileName)) {
      return ts.createSourceFile(fileName, snippet, languageVersion, true);
    }
    return getSourceFileOriginal(fileName, languageVersion, onError, shouldCreateNewSourceFile);
  };

  const program = ts.createProgram([snippetFileName], options, host);
  return ts
    .getPreEmitDiagnostics(program)
    .filter((diagnostic) =>
      (diagnostic.file?.fileName ?? '').replace(/\\/g, '/').endsWith('__auditoriaValores_snippet.ts')
    )
    .map((diagnostic) => ({
      code: diagnostic.code,
      message: ts.flattenDiagnosticMessageText(diagnostic.messageText, ' '),
    }));
}

test('los tipos de auditoria admiten la ausencia de valor_anterior/valor_nuevo', () => {
  assert.ok(ts.sys.fileExists(typesFile), `no se encuentra ${typesFile}`);
  const errores = typeErrorsDelSnippet();
  // 2322 = error del control de resolucion (obligatorio: si el import de los
  // tipos fallara, el tipo seria any y este error no saldria).
  assert.deepEqual(
    errores.map((error) => error.code),
    [2322],
    `esperado: solo el error de control; errores: ${errores.map((e) => `${e.code} ${e.message}`).join(' | ')}`
  );
});

test('un valor ausente se muestra como texto, no como importe', () => {
  const render = (valor: string | null | undefined) => (valor != null ? valor : 'Sin valor');
  assert.equal(render(undefined), 'Sin valor');
  assert.equal(render(null), 'Sin valor');
  assert.equal(render('12.50'), '12.50');
});

test('AuditoriaPage compara los valores de auditoria con != null', () => {
  const source = readFileSync(new URL('../../src/pages/AuditoriaPage.tsx', import.meta.url), 'utf8');
  assert.match(source, /valor_anterior\s*!=\s*null/);
  assert.doesNotMatch(source, /valor_anterior\s*!==\s*null/);
  assert.match(source, /valor_nuevo\s*!=\s*null/);
  assert.doesNotMatch(source, /valor_nuevo\s*!==\s*null/);
});

test('AuditCellModal compara los valores de auditoria con != null', () => {
  const source = readFileSync(new URL('../../src/components/extractos/AuditCellModal.tsx', import.meta.url), 'utf8');
  assert.match(source, /valor_anterior\s*!=\s*null/);
  assert.doesNotMatch(source, /valor_anterior\s*!==\s*null/);
  assert.match(source, /valor_nuevo\s*!=\s*null/);
  assert.doesNotMatch(source, /valor_nuevo\s*!==\s*null/);
});
