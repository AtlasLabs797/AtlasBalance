import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';

// R-4: CuentaDetailPage invalidaba las mismas consultas que tenia activas.
// La invalidation se lanzaba desde un efecto que se reejecutaba en cada
// cambio de pagina o de periodo, de modo que se volvia a pedir la consulta
// que la propia pagina ya habia pedido (peticion duplicada). Se comprueba
// sobre el fuente (no hay DOM en este repo) que ningun efecto invalida las
// consultas activas y que la invalidacion explicita sigue viva.

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

function findArrowFunction(name: string): ts.ArrowFunction | ts.FunctionExpression | undefined {
  for (const stmt of componentStatements()) {
    if (!ts.isVariableStatement(stmt)) {
      continue;
    }
    for (const decl of stmt.declarationList.declarations) {
      if (!ts.isIdentifier(decl.name) || decl.name.text !== name || !decl.initializer) {
        continue;
      }
      if (ts.isArrowFunction(decl.initializer) || ts.isFunctionExpression(decl.initializer)) {
        return decl.initializer;
      }
      // useCallback(async () => {...}, [deps])
      if (
        ts.isCallExpression(decl.initializer) &&
        ts.isIdentifier(decl.initializer.expression) &&
        decl.initializer.expression.text === 'useCallback'
      ) {
        const first = decl.initializer.arguments[0];
        if (first && (ts.isArrowFunction(first) || ts.isFunctionExpression(first))) {
          return first;
        }
      }
    }
  }
  return undefined;
}

// Efectos useEffect declarados dentro del componente.
function useEffectCallbacks(): Array<{ callback: ts.ArrowFunction; deps: ts.ArrayLiteralExpression | undefined }> {
  const efectos: Array<{ callback: ts.ArrowFunction; deps: ts.ArrayLiteralExpression | undefined }> = [];
  for (const stmt of componentStatements()) {
    if (!ts.isExpressionStatement(stmt) || !ts.isCallExpression(stmt.expression)) {
      continue;
    }
    const call = stmt.expression;
    if (!ts.isIdentifier(call.expression) || call.expression.text !== 'useEffect') {
      continue;
    }
    const first = call.arguments[0];
    if (!first || !ts.isArrowFunction(first)) {
      continue;
    }
    const second = call.arguments[1];
    efectos.push({
      callback: first,
      deps: second && ts.isArrayLiteralExpression(second) ? second : undefined,
    });
  }
  return efectos;
}

// Llamadas hechas en el cuerpo del efecto, sin entrar en funciones anidadas
// (un manejador de evento no se dispara solo: solo el cuerpo del efecto se
// ejecuta al cambiar de pagina o de periodo).
function llamadasDirectas(node: ts.Node): string[] {
  const llamadas: string[] = [];
  const visit = (child: ts.Node) => {
    if (ts.isCallExpression(child) && ts.isIdentifier(child.expression)) {
      llamadas.push(child.expression.text);
    }
    if (ts.isArrowFunction(child) || ts.isFunctionExpression(child)) {
      return;
    }
    ts.forEachChild(child, visit);
  };
  // Se empiezan a visitar los hijos del nodo: el propio nodo puede ser la
  // funcion del efecto y no debe cortarse la recursion.
  ts.forEachChild(node, visit);
  return llamadas;
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

// Metodos invocados dentro del nodo (queryClient.invalidateQueries ->
// 'invalidateQueries').
function calledMembers(node: ts.Node): string[] {
  const llamadas: string[] = [];
  const visit = (child: ts.Node) => {
    if (
      ts.isCallExpression(child) &&
      ts.isPropertyAccessExpression(child.expression) &&
      ts.isIdentifier(child.expression.name)
    ) {
      llamadas.push(child.expression.name.text);
    }
    ts.forEachChild(child, visit);
  };
  visit(node);
  return llamadas;
}

test('ningun efecto de la pagina invalida las consultas que la pagina tiene activas', () => {
  const efectos = useEffectCallbacks();
  assert.ok(efectos.length > 0, 'No se encuentra ningun useEffect en CuentaDetailPage.');

  for (const efecto of efectos) {
    const directas = llamadasDirectas(efecto.callback);
    assert.ok(
      !directas.includes('loadCuentaData'),
      'Un efecto no puede llamar a loadCuentaData: al cambiar de pagina o de periodo la consulta se vuelve a pedir sola.'
    );
    assert.ok(
      !directas.includes('invalidateQueries'),
      'Un efecto no puede invalidar las consultas activas de la pagina.'
    );
  }
});

test('la recarga explicita sigue invalidando el resumen y la lista de la cuenta', () => {
  const fn = findArrowFunction('loadCuentaData');
  assert.ok(fn, 'No se encuentra loadCuentaData.');
  const texto = fn.getText(sourceFile);

  assert.ok(texto.includes('queryKeys.extractos.cuentaResumen'), 'loadCuentaData debe invalidar el resumen de la cuenta.');
  assert.ok(texto.includes('queryKeys.extractos.list'), 'loadCuentaData debe invalidar la lista de movimientos.');
  assert.ok(calledMembers(fn).includes('invalidateQueries'), 'loadCuentaData debe seguir invalidando consultas.');
});

test('las mutaciones siguen recargando la vista despues de cambiar datos', () => {
  const guardarCelda = findArrowFunction('saveCell');
  assert.ok(guardarCelda, 'No se encuentra saveCell.');
  const llamadasGuardar = calledIdentifiers(guardarCelda);
  assert.ok(llamadasGuardar.includes('loadCuentaData'), 'saveCell debe recargar la vista tras guardar.');
  assert.ok(llamadasGuardar.includes('invalidate'), 'saveCell debe invalidar las familias afectadas.');

  const insertar = findArrowFunction('insertRow');
  assert.ok(insertar, 'No se encuentra insertRow.');
  assert.ok(calledIdentifiers(insertar).includes('invalidate'), 'insertRow debe invalidar las familias afectadas.');
});
