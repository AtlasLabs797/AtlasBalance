import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';

// F-7: el borrado y el marcado en lote deben ser UNA peticion atomica al
// backend (POST /extractos/lote/eliminar y /extractos/lote/flag), no una
// peticion por fila. Se comprueba sobre el fuente de la pagina (no hay DOM en
// este repo).

const pagePath = new URL('../../src/pages/CuentaDetailPage.tsx', import.meta.url);
const source = readFileSync(pagePath, 'utf8');
const sourceFile = ts.createSourceFile('CuentaDetailPage.tsx', source, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TSX);

function findArrowFunction(name: string): ts.ArrowFunction | undefined {
  const fn = sourceFile.statements.find(
    (stmt): stmt is ts.FunctionDeclaration => ts.isFunctionDeclaration(stmt) && stmt.name?.text === 'CuentaDetailPage'
  );
  assert.ok(fn?.body, 'No se encuentra el componente CuentaDetailPage.');
  for (const stmt of fn.body.statements) {
    if (!ts.isVariableStatement(stmt)) {
      continue;
    }
    for (const decl of stmt.declarationList.declarations) {
      if (ts.isIdentifier(decl.name) && decl.name.text === name && decl.initializer && ts.isArrowFunction(decl.initializer)) {
        return decl.initializer;
      }
    }
  }
  return undefined;
}

// Llamadas del tipo api.<metodo>(<primer argumento>) dentro del nodo.
function apiCalls(node: ts.Node): Array<{ method: string; firstArg: string }> {
  const calls: Array<{ method: string; firstArg: string }> = [];
  const visit = (child: ts.Node) => {
    if (
      ts.isCallExpression(child) &&
      ts.isPropertyAccessExpression(child.expression) &&
      ts.isIdentifier(child.expression.expression) &&
      child.expression.expression.text === 'api'
    ) {
      calls.push({ method: child.expression.name.text, firstArg: child.arguments[0]?.getText(sourceFile) ?? '' });
    }
    ts.forEachChild(child, visit);
  };
  visit(node);
  return calls;
}

function hayAwaitEnBucleOMap(node: ts.Node): boolean {
  let encontrado = false;
  const visit = (child: ts.Node) => {
    if (encontrado) {
      return;
    }
    const esBucle =
      ts.isForOfStatement(child) || ts.isForStatement(child) || ts.isForInStatement(child) ||
      ts.isWhileStatement(child) || ts.isDoStatement(child);
    const esMapDeFilas =
      ts.isCallExpression(child) && ts.isPropertyAccessExpression(child.expression) && child.expression.name.text === 'map' &&
      apiCalls(child).length > 0;
    if (esBucle || esMapDeFilas) {
      if (apiCalls(child).length > 0) {
        encontrado = true;
        return;
      }
    }
    ts.forEachChild(child, visit);
  };
  visit(node);
  return encontrado;
}

test('el borrado en lote hace una sola peticion a /extractos/lote/eliminar', () => {
  const fn = findArrowFunction('confirmDeleteSelectedRows');
  assert.ok(fn, 'No se encuentra la accion de borrado en lote.');

  const calls = apiCalls(fn);
  assert.equal(calls.length, 1, 'El borrado en lote debe hacer exactamente una peticion.');
  assert.equal(calls[0].method, 'post');
  assert.match(calls[0].firstArg, /extractos\/lote\/eliminar/);
  assert.ok(!hayAwaitEnBucleOMap(fn), 'El borrado en lote no puede hacer una peticion por fila.');
});

test('el marcado en lote hace una sola peticion a /extractos/lote/flag', () => {
  const fn = findArrowFunction('flagSelectedRows');
  assert.ok(fn, 'No se encuentra la accion de marcado en lote.');

  const calls = apiCalls(fn);
  assert.equal(calls.length, 1, 'El marcado en lote debe hacer exactamente una peticion.');
  assert.equal(calls[0].method, 'post');
  assert.match(calls[0].firstArg, /extractos\/lote\/flag/);
  assert.ok(!hayAwaitEnBucleOMap(fn), 'El marcado en lote no puede hacer una peticion por fila.');
});

test('ya no queda el helper de concurrencia por fila en la pagina', () => {
  assert.ok(!source.includes('ejecutarEnLote'), 'CuentaDetailPage no debe usar ejecutarEnLote.');
  assert.ok(!source.includes('utils/concurrencia'), 'CuentaDetailPage no debe importar utils/concurrencia.');
});
