import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';

// F-2: la papelera pide al backend el filtro de solo eliminados, pagina de
// verdad y usa la divisa real. Este test comprueba sobre el fuente (no hay DOM
// en este repo) que el filtro lo hace el servidor y que la pagina pinta lo
// que el servidor devuelve.

const source = readFileSync(new URL('../../src/pages/PapeleraPage.tsx', import.meta.url), 'utf8');
const sourceFile = ts.createSourceFile('PapeleraPage.tsx', source, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TSX);

function componentStatements(): ts.NodeArray<ts.Statement> {
  const fn = sourceFile.statements.find(
    (stmt): stmt is ts.FunctionDeclaration => ts.isFunctionDeclaration(stmt) && stmt.name?.text === 'PapeleraPage'
  );
  assert.ok(fn?.body, 'No se encuentra el componente PapeleraPage.');
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

// La funcion que lanza las peticiones: puede ser la propia load() o una
// funcion de modulo aparte (la peticion se normaliza fuera del componente).
function requestFunction(): ts.Node {
  const fetchFn = sourceFile.statements.find(
    (stmt): stmt is ts.FunctionDeclaration => ts.isFunctionDeclaration(stmt) && stmt.name?.text === 'fetchPapelera'
  );
  if (fetchFn) {
    return fetchFn;
  }
  const load = findArrowFunction('load');
  assert.ok(load, 'load() debe seguir existiendo.');
  return load;
}

function visit(node: ts.Node, fn: (node: ts.Node) => void): void {
  fn(node);
  ts.forEachChild(node, (child) => visit(child, fn));
}

// Llamadas api.get dentro de un nodo: devuelve el objeto "params" de cada una.
function apiGetParams(node: ts.Node): ts.ObjectLiteralExpression[] {
  const params: ts.ObjectLiteralExpression[] = [];
  visit(node, (child) => {
    if (
      ts.isCallExpression(child) &&
      ts.isPropertyAccessExpression(child.expression) &&
      ts.isIdentifier(child.expression.expression) &&
      child.expression.expression.text === 'api' &&
      child.expression.name.text === 'get'
    ) {
      const config = child.arguments[1];
      if (!config || !ts.isObjectLiteralExpression(config)) {
        return;
      }
      for (const prop of config.properties) {
        if (ts.isPropertyAssignment(prop) && prop.name.getText(sourceFile) === 'params' && ts.isObjectLiteralExpression(prop.initializer)) {
          params.push(prop.initializer);
        }
      }
    }
  });
  return params;
}

function propertyValue(obj: ts.ObjectLiteralExpression, name: string): ts.Expression | undefined {
  for (const prop of obj.properties) {
    if (ts.isPropertyAssignment(prop) && prop.name.getText(sourceFile) === name) {
      return prop.initializer;
    }
    // { page } (shorthand) cuenta como pedir la pagina actual.
    if (ts.isShorthandPropertyAssignment(prop) && prop.name.text === name) {
      return prop.name;
    }
  }
  return undefined;
}

test('la papelera pide al backend el filtro de solo eliminados en los cuatro listados', () => {
  const params = apiGetParams(requestFunction());
  assert.equal(params.length, 4, 'La papelera consulta titulares, cuentas, extractos y usuarios.');

  for (const p of params) {
    const soloEliminados = propertyValue(p, 'soloEliminados');
    assert.ok(soloEliminados, 'Cada listado debe pedir el filtro de solo eliminados.');
    assert.equal(soloEliminados.getText(sourceFile), 'true', 'El filtro de solo eliminados debe ir activo.');
  }
});

test('la papelera pide la pagina actual y no 500 filas de golpe', () => {
  for (const p of apiGetParams(requestFunction())) {
    const pageSize = propertyValue(p, 'pageSize');
    assert.ok(pageSize, 'Cada listado debe pedir un tamaño de pagina.');
    assert.notEqual(pageSize.getText(sourceFile), '500', 'Pedir 500 filas es lo que dejaba fuera los eliminados antiguos.');
    assert.ok(propertyValue(p, 'page'), 'Cada listado debe pedir la pagina actual.');
  }
});

test('la papelera no filtra por deleted_at en cliente', () => {
  const filtros: string[] = [];
  visit(sourceFile, (node) => {
    if (ts.isCallExpression(node) && ts.isPropertyAccessExpression(node.expression) && node.expression.name.text === 'filter') {
      const texto = node.arguments.map((a) => a.getText(sourceFile)).join(' ');
      if (texto.includes('deleted_at')) {
        filtros.push(texto);
      }
    }
  });

  assert.deepEqual(filtros, [], 'El filtro de eliminados lo hace el backend, no el cliente.');
});

test('los importes de los extractos se pintan en la divisa real de la cuenta', () => {
  const llamadas: ts.CallExpression[] = [];
  visit(sourceFile, (node) => {
    if (
      ts.isCallExpression(node) &&
      ts.isIdentifier(node.expression) &&
      node.expression.text === 'formatCurrency'
    ) {
      llamadas.push(node);
    }
  });

  assert.ok(llamadas.length > 0, 'La papelera debe formatear importes.');
  for (const llamada of llamadas) {
    const divisa = llamada.arguments[1];
    assert.ok(divisa, 'formatCurrency debe recibir la divisa.');
    assert.notEqual(divisa.getText(sourceFile), "'EUR'", 'La divisa no puede ser EUR fijo: se ignora la divisa de la cuenta.');
    assert.ok(divisa.getText(sourceFile).includes('divisa'), 'La divisa debe venir de la fila.');
  }
});

test('la papelera pinta la paginacion real con el total y la pagina', () => {
  assert.ok(source.includes('total_pages'), 'La pagina debe leer el total de paginas de la respuesta.');
  assert.ok(source.includes('total'), 'La pagina debe leer el total de registros.');
  assert.ok(/P[aá]gina\s*\{/.test(source), 'La pagina debe mostrar el numero de pagina.');
  assert.ok(source.includes('Anterior'), 'La papelera debe permitir ir a la pagina anterior.');
  assert.ok(source.includes('Siguiente'), 'La papelera debe permitir ir a la pagina siguiente.');
});
