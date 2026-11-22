import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

// D-3: un unico componente de estado de carga (PageSkeleton). Este test
// comprueba sobre el fuente (en este repo los tests unitarios no tienen DOM)
// que:
// - los ficheros del grupo ya no pintan "<p ...>Cargando...";
// - PageSkeleton expone role="status" y la variante de tabla;
// - que quedan sitios "<p ...>Cargando" en el resto de src, y los lista, para
//   que el grupo pendiente los cierre.

const SRC = fileURLToPath(new URL('../../src/', import.meta.url));

// Ficheros migrados a PageSkeleton en el lote D-3.
const GRUPO_D3 = [
  'pages/ConciliacionPage.tsx',
  'pages/AuditoriaPage.tsx',
  'pages/AlertasPage.tsx',
  'pages/FormatosImportacionPage.tsx',
  'pages/ExportacionesPage.tsx',
  'pages/PapeleraPage.tsx',
  'pages/ImportacionPage.tsx',
  'components/auditoria/IntegrationAuditTable.tsx',
  'components/extractos/DesgloseModal.tsx',
  'components/extractos/AuditCellModal.tsx',
];

// Parrafo (o cualquier elemento de bloque) cuyo contenido empieza por
// "Cargando": el lookahead impide que la coincidencia cruce un </p>.
const RE_CARGANDO_EN_P = /<p\b[^>]*>(?:(?!<\/p>)[\s\S])*?Cargando/g;

function walk(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = join(dir, entry.name);
    return entry.isDirectory() ? walk(full) : [full];
  });
}

function relativeToSrc(file: string): string {
  return relative(SRC, file).split(sep).join('/');
}

function lineOf(source: string, index: number): number {
  return source.slice(0, index).split('\n').length;
}

// Todos los ficheros de codigo de src (tsx y ts).
const SRC_FILES = walk(SRC)
  .filter((file) => file.endsWith('.tsx') || file.endsWith('.ts'))
  .sort();

// Todos los sitios "<p ...>Cargando" que quedan en src, con fichero:linea.
const SITIOS_PENDIENTES = SRC_FILES.flatMap((file) => {
  const source = readFileSync(file, 'utf8');
  const relativePath = relativeToSrc(file);
  const hits: { fichero: string; linea: number }[] = [];
  for (const match of source.matchAll(RE_CARGANDO_EN_P)) {
    hits.push({ fichero: relativePath, linea: lineOf(source, match.index ?? 0) });
  }
  return hits;
});

test('D-3: los ficheros del grupo ya no pintan "<p>Cargando..."', () => {
  const pendientes = SITIOS_PENDIENTES.filter((sitio) => GRUPO_D3.includes(sitio.fichero));
  assert.deepEqual(
    pendientes,
    [],
    `Sitios de carga sin migrar en el grupo D-3:\n${pendientes.map((s) => `${s.fichero}:${s.linea}`).join('\n')}`,
  );
});

test('D-3: cada fichero del grupo que carga datos usa PageSkeleton', () => {
  for (const relativePath of GRUPO_D3) {
    const source = readFileSync(join(SRC, relativePath), 'utf8');
    assert.match(source, /PageSkeleton/, `${relativePath} no usa PageSkeleton para el estado de carga.`);
  }
});

test('D-3: PageSkeleton expone role="status" y la variante de tabla', () => {
  const source = readFileSync(join(SRC, 'components/common/PageSkeleton.tsx'), 'utf8');
  assert.match(source, /role="status"/);
  assert.match(source, /aria-live="polite"/);
  assert.match(source, /variant/);
  assert.match(source, /'table'/);
  assert.match(source, /skeleton-table/);
});

test('D-3: informe de sitios "<p>Cargando" que quedan en src', () => {
  const informe = SITIOS_PENDIENTES.map((sitio) => `${sitio.fichero}:${sitio.linea}`).join('\n');
  console.log(`Sitios "<p>Cargando" restantes en src (${SITIOS_PENDIENTES.length}):\n${informe || 'ninguno'}`);
  assert.ok(Array.isArray(SITIOS_PENDIENTES));
});
