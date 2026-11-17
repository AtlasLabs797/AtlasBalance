import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';
import { LIMITE_CONCURRENCIA_LOTE, ejecutarEnLote } from '../src/utils/concurrencia.js';

// F-7: el borrado y el marcado en loteaban las peticiones de una en una (una
// peticion por fila y en serie). Se comprueba el helper (que ejecuta el lote
// con concurrencia limitada y no deja ninguna operacion colgada) y, sobre el
// fuente de la pagina (no hay DOM en este repo), que las dos acciones en lote
// usan ese helper en vez de un bucle con un await por fila.

const pagePath = new URL('../../src/pages/CuentaDetailPage.tsx', import.meta.url);
const source = readFileSync(pagePath, 'utf8');
const sourceFile = ts.createSourceFile('CuentaDetailPage.tsx', source, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TSX);

function componentStatements(): ts.NodeArray<ts.Statement> {
  const fn = sourceFile.statements.find(
    (stmt): stmt is ts.FunctionDeclaration => ts.isFunctionDeclaration(stmt) && stmt.name?.text === 'CuentaDetailPage'
  );
  assert.ok(fn?.body, 'No se encuentra el componente CuentaDetailPage.');
  return fn.body.statements;
}

function findArrowFunction(name: string): ts.ArrowFunction | undefined {
  for (const stmt of componentStatements()) {
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

// Nombres de las funciones invocadas dentro del nodo (await api.delete -> 'api').
function calledIdentifiers(node: ts.Node): string[] {
  const calls: string[] = [];
  const visit = (child: ts.Node) => {
    if (ts.isCallExpression(child) && ts.isIdentifier(child.expression)) {
      calls.push(child.expression.text);
    }
    ts.forEachChild(child, visit);
  };
  visit(node);
  return calls;
}

// True si hay un await dentro de un bucle: eso es una peticion por fila en serie.
function hayAwaitEnBucle(node: ts.Node): boolean {
  const contieneAwait = (sub: ts.Node): boolean => {
    let encontrado = false;
    const visit = (child: ts.Node) => {
      if (encontrado) {
        return;
      }
      if (ts.isAwaitExpression(child)) {
        encontrado = true;
        return;
      }
      ts.forEachChild(child, visit);
    };
    visit(sub);
    return encontrado;
  };

  let hayBucle = false;
  const visit = (child: ts.Node) => {
    if (hayBucle) {
      return;
    }
    if (
      ts.isForOfStatement(child) ||
      ts.isForStatement(child) ||
      ts.isForInStatement(child) ||
      ts.isWhileStatement(child) ||
      ts.isDoStatement(child)
    ) {
      if (contieneAwait(child)) {
        hayBucle = true;
        return;
      }
    }
    ts.forEachChild(child, visit);
  };
  visit(node);
  return hayBucle;
}

function importedNames(moduleSuffix: string): string[] {
  const nombres: string[] = [];
  for (const stmt of sourceFile.statements) {
    if (!ts.isImportDeclaration(stmt) || !ts.isStringLiteral(stmt.moduleSpecifier)) {
      continue;
    }
    if (!stmt.moduleSpecifier.text.endsWith(moduleSuffix)) {
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

test('ejecutarEnLote ejecuta todas las operaciones y devuelve un resultado por operacion', async () => {
  const resultados = await ejecutarEnLote([
    async () => 'a',
    async () => 'b',
    async () => 'c',
  ]);

  assert.equal(resultados.length, 3);
  assert.deepEqual(resultados.map((resultado) => resultado.exito), [true, true, true]);
  assert.deepEqual(resultados.map((resultado) => resultado.valor), ['a', 'b', 'c']);
  assert.deepEqual(resultados.map((resultado) => resultado.indice), [0, 1, 2]);
});

test('ejecutarEnLote nunca supera el limite de concurrencia', async () => {
  let activas = 0;
  let maximo = 0;

  const operaciones = Array.from({ length: 12 }, (_valor, indice) => async () => {
    activas += 1;
    maximo = Math.max(maximo, activas);
    await new Promise((resolve) => setTimeout(resolve, 2));
    activas -= 1;
    return indice;
  });

  const resultados = await ejecutarEnLote(operaciones, 3);

  assert.equal(maximo, 3, `El lote llego a ${maximo} peticiones simultaneas con un limite de 3.`);
  assert.equal(resultados.length, 12);
  assert.ok(resultados.every((resultado) => resultado.exito));
});

test('ejecutarEnLote usa el limite por defecto de cuatro peticiones', async () => {
  let activas = 0;
  let maximo = 0;

  const operaciones = Array.from({ length: 10 }, (_valor, indice) => async () => {
    activas += 1;
    maximo = Math.max(maximo, activas);
    await new Promise((resolve) => setTimeout(resolve, 2));
    activas -= 1;
    return indice;
  });

  await ejecutarEnLote(operaciones);

  assert.equal(LIMITE_CONCURRENCIA_LOTE, 4);
  assert.ok(maximo <= LIMITE_CONCURRENCIA_LOTE, `El lote llego a ${maximo} peticiones simultaneas.`);
  assert.ok(maximo > 1, 'El lote deberia работа en paralelo, no en serie.');
});

test('ejecutarEnLote no corta el lote cuando una operacion falla', async () => {
  const ejecutadas: number[] = [];
  const error = new Error('sin permiso');

  const resultados = await ejecutarEnLote([
    async () => {
      ejecutadas.push(0);
      return 'a';
    },
    async () => {
      ejecutadas.push(1);
      throw error;
    },
    async () => {
      ejecutadas.push(2);
      return 'c';
    },
  ]);

  assert.deepEqual(ejecutadas, [0, 1, 2]);
  assert.deepEqual(resultados.map((resultado) => resultado.exito), [true, false, true]);
  assert.equal(resultados[1].error, error);
  assert.equal(resultados[1].indice, 1);
});

test('ejecutarEnLote no deja ninguna operacion colgada cuando fallan varias', async () => {
  const resultados = await Promise.race([
    ejecutarEnLote([
      async () => {
        throw new Error('primera');
      },
      async () => {
        await new Promise((resolve) => setTimeout(resolve, 5));
        throw new Error('segunda');
      },
      async () => 'tercera',
    ]),
    new Promise((resolve) => setTimeout(() => resolve('colgado'), 2000)),
  ]);

  assert.notEqual(resultados, 'colgado', 'El lote dejo alguna operacion colgada.');
  assert.equal((resultados as Array<{ exito: boolean }>).length, 3);
  assert.deepEqual((resultados as Array<{ exito: boolean }>).map((resultado) => resultado.exito), [false, false, true]);
});

test('ejecutarEnLote con lista vacia no hace nada', async () => {
  const resultados = await ejecutarEnLote([]);
  assert.deepEqual(resultados, []);
});

test('el borrado en lote usa el helper y no hace una peticion por fila en serie', () => {
  const fn = findArrowFunction('confirmDeleteSelectedRows');
  assert.ok(fn, 'No se encuentra la accion de borrado en lote.');

  assert.ok(
    !hayAwaitEnBucle(fn),
    'El borrado en lote no puede esperar fila a fila: debe usar el helper de concurrencia.'
  );
  assert.ok(
    calledIdentifiers(fn).includes('ejecutarEnLote'),
    'El borrado en lote debe usar el helper ejecutarEnLote.'
  );
  assert.ok(importedNames('utils/concurrencia').includes('ejecutarEnLote'), 'Falta importar el helper de concurrencia.');
});

test('el marcado en lote usa el helper y no hace una peticion por fila en serie', () => {
  const fn = findArrowFunction('flagSelectedRows');
  assert.ok(fn, 'No se encuentra la accion de marcado en lote.');

  assert.ok(
    !hayAwaitEnBucle(fn),
    'El marcado en lote no puede esperar fila a fila: debe usar el helper de concurrencia.'
  );
  assert.ok(
    calledIdentifiers(fn).includes('ejecutarEnLote'),
    'El marcado en lote debe usar el helper ejecutarEnLote.'
  );
});
