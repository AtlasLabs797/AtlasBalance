import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';
import { hoy } from '../src/utils/hoy.js';

// B-16: "hoy" en UTC. Entre las 00:00 y las 02:00 en Espana la fecha en UTC
// es la del dia anterior. El helper comun `hoy` devuelve la fecha local y la
// pagina de cuenta debe usarlo en vez de calcular el dia por su cuenta.

const pagePath = new URL('../../src/pages/CuentaDetailPage.tsx', import.meta.url);
const source = readFileSync(pagePath, 'utf8');
const sourceFile = ts.createSourceFile('CuentaDetailPage.tsx', source, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TSX);

function importedNames(modulePath: string): string[] {
  const nombres: string[] = [];
  for (const stmt of sourceFile.statements) {
    if (!ts.isImportDeclaration(stmt) || !ts.isStringLiteral(stmt.moduleSpecifier)) {
      continue;
    }
    if (stmt.moduleSpecifier.text !== modulePath) {
      continue;
    }
    const bindings = stmt.importClause?.namedBindings;
    if (bindings && ts.isNamedImports(bindings)) {
      for (const element of bindings.elements) {
        nombres.push(element.name.text);
      }
    }
  }
  return nombres;
}

function calledIdentifiers(node: ts.Node): string[] {
  const llamadas: string[] = [];
  const visit = (child: ts.Node) => {
    if (ts.isCallExpression(child) && ts.isIdentifier(child.expression)) {
      llamadas.push(child.expression.text);
    }
    ts.forEachChild(child, visit);
  };
  visit(node);
  return llamadas;
}

test('hoy devuelve el dia local, no el dia en UTC', () => {
  // 00:30 del 17 de noviembre en la zona local: el helper debe devolver ese
  // dia, no el anterior (que es lo que daria la fecha en UTC).
  const instante = new Date(2026, 10, 17, 0, 30);

  assert.equal(hoy(instante), '2026-11-17');
});

test('la pagina de cuenta usa el helper de hoy y no calcula el dia por su cuenta', () => {
  assert.ok(
    importedNames('@/utils/hoy').includes('hoy'),
    'CuentaDetailPage debe usar el helper hoy en vez de calcular "hoy" por su cuenta.'
  );
  assert.ok(calledIdentifiers(sourceFile).includes('hoy'), 'CuentaDetailPage debe llamar a hoy.');

  const patronUtc = /toISOString\(\)\.slice\(0,\s*10\)/;
  assert.equal(
    patronUtc.test(source),
    false,
    'CuentaDetailPage no debe construir una fecha de dia con toISOString(): eso devuelve el dia en UTC.'
  );
});
