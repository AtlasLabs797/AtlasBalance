import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';
import { hoy } from '../src/utils/hoy.js';

// B-16 (parte frontend): la fecha por defecto de la fila nueva se calculaba en
// UTC, de modo que entre las 00:00 y las 02:00 de Espana el dia actual era el
// anterior. El helper `hoy` usa la zona local, el mismo criterio que
// ConciliacionPage e ImportacionPage.

process.env.TZ = 'Europe/Madrid';

const source = readFileSync(new URL('../../src/components/extractos/ExtractoTable.tsx', import.meta.url), 'utf8');
const sourceFile = ts.createSourceFile('ExtractoTable.tsx', source, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TSX);

test('hoy devuelve la fecha local, no la fecha UTC', () => {
  // 23:30 UTC del 16 de marzo en Madrid (UTC+1) ya es el 17 de marzo.
  const instante = new Date('2026-03-16T23:30:00Z');
  assert.equal(instante.toISOString().slice(0, 10), '2026-03-16');
  assert.equal(hoy(instante), '2026-03-17');
});

test('hoy devuelve la fecha local en horario de verano tambien', () => {
  // 23:30 UTC del 16 de junio en Madrid (UTC+2) ya es el 17 de junio.
  const instante = new Date('2026-06-16T23:30:00Z');
  assert.equal(instante.toISOString().slice(0, 10), '2026-06-16');
  assert.equal(hoy(instante), '2026-06-17');
});

test('hoy no cambia la fecha dentro del mismo dia local', () => {
  assert.equal(hoy(new Date('2026-01-15T12:00:00Z')), '2026-01-15');
});

test('hoy usa el formato AAAA-MM-DD', () => {
  assert.match(hoy(new Date('2026-01-05T12:00:00Z')), /^\d{4}-\d{2}-\d{2}$/);
});

test('la tabla no calcula la fecha por defecto en UTC', () => {
  const texto = sourceFile.getFullText();
  assert.ok(
    !texto.includes('toISOString().slice(0, 10)'),
    'ExtractoTable no debe calcular la fecha del dia con toISOString (UTC).'
  );

  let usaHoy = false;
  let importaHelper = false;
  const visit = (node: ts.Node) => {
    if (ts.isImportDeclaration(node) && ts.isStringLiteral(node.moduleSpecifier)) {
      if (node.moduleSpecifier.text === '@/utils/hoy') {
        importaHelper = true;
      }
    }
    if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === 'hoy') {
      usaHoy = true;
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);

  assert.ok(importaHelper, 'ExtractoTable debe importar el helper `hoy`.');
  assert.ok(usaHoy, 'ExtractoTable debe usar el helper `hoy` para la fecha por defecto.');
});
