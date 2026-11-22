import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';

// A-8: ConfiguracionPage perdia lo escrito sin guardar porque las acciones
// menores llamaban a load(), que pone la pagina en skeleton y recarga todo el
// estado. Este test comprueba sobre el fuente (no hay DOM en este repo) que
// cada accion menor recarga SOLO su seccion y que load() sigue siendo la
// carga inicial.

const source = readFileSync(new URL('../../src/pages/ConfiguracionPage.tsx', import.meta.url), 'utf8');
const sourceFile = ts.createSourceFile('ConfiguracionPage.tsx', source, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TSX);

// Recargadores por seccion extraidos de load() y el endpoint que le toca.
const SECCIONES = [
  { recargador: 'loadTiposCambio', endpoint: '/tipos-cambio' },
  { recargador: 'loadDivisas', endpoint: '/divisas' },
  { recargador: 'loadTokens', endpoint: '/integraciones/tokens' },
  { recargador: 'loadConfiguracion', endpoint: '/configuracion' },
  { recargador: 'loadCatalogos', endpoint: '/usuarios/catalogos-permisos' },
] as const;

// Acciones menores -> seccion a la que pertenece lo que modifican.
const ACCIONES = [
  { accion: 'saveManualRate', recargador: 'loadTiposCambio' },
  { accion: 'syncRates', recargador: 'loadTiposCambio' },
  { accion: 'saveDivisa', recargador: 'loadDivisas' },
  { accion: 'createDivisa', recargador: 'loadDivisas' },
  // La divisa base solo se expone en GET /divisas (es_base): GET /configuracion
  // no la devuelve y recargarla descartaria lo escrito sin guardar.
  { accion: 'guardarDivisaPorDefecto', recargador: 'loadDivisas' },
  { accion: 'revokeToken', recargador: 'loadTokens' },
  { accion: 'rotateToken', recargador: 'loadTokens' },
  { accion: 'deleteToken', recargador: 'loadTokens' },
] as const;

function componentStatements(): ts.NodeArray<ts.Statement> {
  const fn = sourceFile.statements.find(
    (stmt): stmt is ts.FunctionDeclaration => ts.isFunctionDeclaration(stmt) && stmt.name?.text === 'ConfiguracionPage'
  );
  assert.ok(fn?.body, 'No se encuentra el componente ConfiguracionPage.');
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

// Nombres de las funciones invocadas dentro del nodo (await load() -> 'load').
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

// Metodos de api invocados (get/post/put/delete) dentro del nodo.
function apiMethods(node: ts.Node): string[] {
  const methods: string[] = [];
  const visit = (child: ts.Node) => {
    if (
      ts.isCallExpression(child) &&
      ts.isPropertyAccessExpression(child.expression) &&
      ts.isIdentifier(child.expression.expression) &&
      child.expression.expression.text === 'api'
    ) {
      methods.push(child.expression.name.text);
    }
    ts.forEachChild(child, visit);
  };
  visit(node);
  return methods;
}

// Endpoints literales de api.get dentro del nodo.
function apiGetEndpoints(node: ts.Node): string[] {
  const endpoints: string[] = [];
  const visit = (child: ts.Node) => {
    if (
      ts.isCallExpression(child) &&
      ts.isPropertyAccessExpression(child.expression) &&
      ts.isIdentifier(child.expression.expression) &&
      child.expression.expression.text === 'api' &&
      child.expression.name.text === 'get'
    ) {
      const first = child.arguments[0];
      endpoints.push(first && ts.isStringLiteral(first) ? first.text : '<no-literal>');
    }
    ts.forEachChild(child, visit);
  };
  visit(node);
  return endpoints;
}

// Callback onCreated del modal de creacion de tokens.
function createTokenOnCreated(): ts.ArrowFunction | undefined {
  let found: ts.ArrowFunction | undefined;
  const visit = (node: ts.Node) => {
    if (found) {
      return;
    }
    if (ts.isJsxSelfClosingElement(node) && ts.isIdentifier(node.tagName) && node.tagName.text === 'CreateTokenModal') {
      for (const attr of node.attributes.properties) {
        if (
          ts.isJsxAttribute(attr) &&
          ts.isIdentifier(attr.name) &&
          attr.name.text === 'onCreated' &&
          attr.initializer &&
          ts.isJsxExpression(attr.initializer) &&
          attr.initializer.expression &&
          ts.isArrowFunction(attr.initializer.expression)
        ) {
          found = attr.initializer.expression;
          return;
        }
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return found;
}

// Efectos useEffect declarados dentro del componente.
function useEffectCallbacks(): Array<{ callback: ts.ArrowFunction; deps: ts.ArrayLiteralExpression | undefined }> {
  const effects: Array<{ callback: ts.ArrowFunction; deps: ts.ArrayLiteralExpression | undefined }> = [];
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
    effects.push({
      callback: first,
      deps: second && ts.isArrayLiteralExpression(second) ? second : undefined,
    });
  }
  return effects;
}

test('load() sigue existiendo y es la unica que activa el skeleton de pagina', () => {
  const load = findArrowFunction('load');
  assert.ok(load, 'load() debe seguir existiendo para la carga inicial.');
  const llamadas = calledIdentifiers(load);
  assert.ok(llamadas.includes('setLoading'), 'load() debe seguir activando el estado de carga de pagina.');

  for (const seccion of SECCIONES) {
    const recargador = findArrowFunction(seccion.recargador);
    assert.ok(recargador, `Falta el recargador de seccion ${seccion.recargador}.`);
    assert.ok(
      !calledIdentifiers(recargador).includes('setLoading'),
      `${seccion.recargador} no debe activar el loading global (skeleton de pagina completa).`
    );
  }
});

test('la carga inicial sigue usando load() con dependencias vacias', () => {
  const inicial = useEffectCallbacks().find(
    (efecto) => efecto.deps !== undefined && efecto.deps.elements.length === 0
  );
  assert.ok(inicial, 'Debe existir un useEffect de carga inicial con dependencias vacias.');
  assert.ok(
    calledIdentifiers(inicial.callback).includes('load'),
    'El useEffect de carga inicial debe llamar a load().'
  );
});

test('load() delega en los cinco recargadores de seccion', () => {
  const load = findArrowFunction('load');
  assert.ok(load, 'load() debe existir.');
  const llamadas = calledIdentifiers(load);
  for (const seccion of SECCIONES) {
    assert.ok(
      llamadas.includes(seccion.recargador),
      `load() debe delegar en ${seccion.recargador}.`
    );
  }
});

test('cada recargador de seccion pide solo su propio endpoint', () => {
  for (const seccion of SECCIONES) {
    const recargador = findArrowFunction(seccion.recargador);
    assert.ok(recargador, `Falta el recargador de seccion ${seccion.recargador}.`);
    assert.deepEqual(
      apiGetEndpoints(recargador),
      [seccion.endpoint],
      `${seccion.recargador} debe pedir solo ${seccion.endpoint}.`
    );
    assert.deepEqual(
      apiMethods(recargador),
      ['get'],
      `${seccion.recargador} solo puede leer; no debe escribir ni tocar otras secciones.`
    );
  }
});

test('las acciones menores recargan solo su seccion y no llaman a load()', () => {
  for (const { accion, recargador } of ACCIONES) {
    const fn = findArrowFunction(accion);
    assert.ok(fn, `Falta la accion ${accion}.`);
    const llamadas = calledIdentifiers(fn);
    assert.ok(
      !llamadas.includes('load'),
      `${accion} no debe llamar a load(): recargaria toda la pagina y perderia lo sin guardar.`
    );
    assert.ok(
      llamadas.includes(recargador),
      `${accion} debe llamar a ${recargador}.`
    );
    assert.ok(
      !llamadas.includes('setLoading'),
      `${accion} no debe activar el skeleton de pagina completa.`
    );
    const otros = SECCIONES.filter((seccion) => seccion.recargador !== recargador).map((seccion) => seccion.recargador);
    for (const otro of otros) {
      assert.ok(
        !llamadas.includes(otro),
        `${accion} no debe recargar ${otro}: solo su seccion.`
      );
    }
  }
});

test('crear un token recarga solo la seccion de tokens', () => {
  const onCreated = createTokenOnCreated();
  assert.ok(onCreated, 'No se encuentra el callback onCreated de CreateTokenModal.');
  const llamadas = calledIdentifiers(onCreated);
  assert.ok(!llamadas.includes('load'), 'onCreated no debe llamar a load().');
  assert.ok(llamadas.includes('loadTokens'), 'onCreated debe llamar a loadTokens().');
  assert.ok(!llamadas.includes('setLoading'), 'onCreated no debe activar el skeleton de pagina completa.');
  for (const otro of SECCIONES.filter((seccion) => seccion.recargador !== 'loadTokens')) {
    assert.ok(!llamadas.includes(otro.recargador), `onCreated no debe recargar ${otro.recargador}.`);
  }
});

test('el catalogo de permisos solo se carga en la carga inicial', () => {
  for (const { accion } of ACCIONES) {
    const fn = findArrowFunction(accion);
    assert.ok(fn, `Falta la accion ${accion}.`);
    assert.ok(
      !calledIdentifiers(fn).includes('loadCatalogos'),
      `${accion} no debe recargar el catalogo de permisos.`
    );
  }
});
