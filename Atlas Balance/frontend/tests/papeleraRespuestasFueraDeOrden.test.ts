import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';
import { crearSecuenciaPeticion } from '../src/utils/peticionSecuencial.js';

// F-2 (segunda parte): la papelera tambien debe descartar las respuestas que
// llegan fuera de orden. Si el usuario cambia de pestaña o de pagina mientras
// una peticion esta en vuelo, la respuesta vieja no debe pisar la nueva ni
// apagar el estado de carga de la peticion vigente.
//
// La regla vive en `utils/peticionSecuencial.ts` (testeable sin DOM) y la
// pagina la usa antes de escribir en el estado.

test('una peticion deja de ser vigente en cuanto se abre otra', () => {
  const secuencia = crearSecuenciaPeticion();

  const primera = secuencia.siguiente();
  assert.equal(secuencia.esActual(primera), true, 'La primera peticion es la vigente mientras no haya otra.');

  const segunda = secuencia.siguiente();
  assert.equal(secuencia.esActual(segunda), true, 'La segunda peticion es la vigente.');
  assert.equal(secuencia.esActual(primera), false, 'La respuesta de la primera peticion llega fuera de orden y se descarta.');
});

test('tres peticiones encadenadas dejan vigente solo la ultima', () => {
  const secuencia = crearSecuenciaPeticion();

  const a = secuencia.siguiente();
  const b = secuencia.siguiente();
  const c = secuencia.siguiente();

  assert.equal(secuencia.esActual(a), false);
  assert.equal(secuencia.esActual(b), false);
  assert.equal(secuencia.esActual(c), true);
});

test('la secuencia es independiente por instancia', () => {
  const una = crearSecuenciaPeticion();
  const otra = crearSecuenciaPeticion();

  const peticion = una.siguiente();
  otra.siguiente();

  assert.equal(una.esActual(peticion), true, 'Una peticion de otra instancia no invalida esta.');
});

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

function visit(node: ts.Node, fn: (node: ts.Node) => void): void {
  fn(node);
  ts.forEachChild(node, (child) => visit(child, fn));
}

// Guarda que protege una escritura: puede ser
//  (a) la rama THEN de un if (la escritura esta dentro del if), o
//  (b) una salida temprana previa en el mismo bloque:
//        if (!vigente) { return; }
//        escribir(...)   <-- protegido por la salida temprana
function earlyReturnGuard(node: ts.Node): string | undefined {
  let actual: ts.Node | undefined = node.parent;
  let bloque: ts.Block | ts.SourceFile | undefined;
  let sentencia: ts.Statement | undefined;

  while (actual) {
    if (ts.isBlock(actual) || ts.isSourceFile(actual)) {
      bloque = actual;
      break;
    }
    if (!sentencia && ts.isStatement(actual)) {
      sentencia = actual;
    }
    actual = actual.parent;
  }

  if (!bloque || !sentencia) {
    return undefined;
  }

  const statements = bloque.statements;
  const indice = statements.findIndex(
    (s) => s === sentencia || (s.getStart(sourceFile) <= sentencia!.getStart(sourceFile) && s.getEnd() >= sentencia!.getEnd())
  );

  for (let i = 0; i < indice; i += 1) {
    const previo = statements[i];
    if (!ts.isIfStatement(previo)) {
      continue;
    }
    const condicion = previo.expression.getText(sourceFile);
    if (!condicion.includes('esActual')) {
      continue;
    }
    const rama = previo.thenStatement;
    if (ts.isReturnStatement(rama) || (ts.isBlock(rama) && rama.statements.some((s) => ts.isReturnStatement(s)))) {
      return condicion;
    }
  }

  return undefined;
}

// Texto de la condicion de la guarda que protege al nodo.
function guardText(node: ts.Node): string | undefined {
  // (a) la escritura esta dentro de la rama THEN de un if.
  let actual: ts.Node | undefined = node.parent;
  while (actual) {
    if (ts.isIfStatement(actual)) {
      const ramaThen = actual.thenStatement;
      const dentroDeThen =
        node.getStart(sourceFile) >= ramaThen.getStart(sourceFile) && node.getEnd() <= ramaThen.getEnd();
      if (dentroDeThen) {
        return actual.expression.getText(sourceFile);
      }
      return `ELSE(${actual.expression.getText(sourceFile)})`;
    }
    actual = actual.parent;
  }

  // (b) hay una salida temprana previa que protege la escritura.
  return earlyReturnGuard(node);
}

// Llamadas a un identificador concreto dentro de load().
function callsInLoad(name: string): ts.CallExpression[] {
  const load = findArrowFunction('load');
  assert.ok(load, 'load() debe seguir existiendo.');
  const calls: ts.CallExpression[] = [];
  visit(load, (node) => {
    if (
      ts.isCallExpression(node) &&
      ts.isIdentifier(node.expression) &&
      node.expression.text === name
    ) {
      calls.push(node);
    }
  });
  return calls;
}

test('cada carga abre una peticion con numero de secuencia', () => {
  const load = findArrowFunction('load');
  assert.ok(load, 'load() debe seguir existiendo.');
  assert.ok(
    source.includes('crearSecuenciaPeticion'),
    'La pagina debe usar la secuencia de peticion compartida.'
  );
  assert.ok(
    /secuenciaRef\.current\.siguiente\(\)/.test(source),
    'Cada carga debe abrir una secuencia nueva.'
  );
  assert.ok(
    load.body.getText(sourceFile).includes('siguiente()'),
    'La secuencia debe abrirse dentro de load().'
  );
});

test('las filas y el total solo se escriben si la respuesta sigue siendo la vigente', () => {
  for (const nombre of ['setRows', 'setTotal', 'setTotalPages']) {
    const llamadas = callsInLoad(nombre);
    assert.ok(llamadas.length > 0, `load() debe escribir ${nombre}.`);

    for (const llamada of llamadas) {
      const guard = guardText(llamada);
      assert.ok(guard, `${nombre} debe escribirse dentro de una comprobacion de vigencia.`);
      assert.ok(
        guard.includes('esActual'),
        `${nombre} solo puede escribirse si la respuesta es la vigente: la guarda es "${guard}".`
      );
      assert.ok(!guard.startsWith('ELSE('), `${nombre} no puede escribirse en la rama ELSE de la guarda: "${guard}".`);
    }
  }
});

test('un error de una peticion vieja no se pinta y no apaga la carga de la vigente', () => {
  // El error que se pinta al usuario es el del fallo de la peticion, no el
  // setError(null) con el que load() limpia el aviso al empezar.
  const errorMostrado = callsInLoad('setError').find((c) => c.arguments[0]?.getText(sourceFile) !== 'null');
  assert.ok(errorMostrado, 'load() debe pintar el error de la peticion.');

  const guard = guardText(errorMostrado);
  assert.ok(guard, 'El error debe escribirse dentro de una comprobacion de vigencia.');
  assert.ok(guard.includes('esActual'), 'Un error de una peticion descartada no se muestra.');
  assert.ok(!guard.startsWith('ELSE('), 'El error no se escribe en la rama ELSE de la guarda.');

  const guardsLoading = callsInLoad('setLoading');
  const fin = guardsLoading.find((c) => c.arguments[0]?.getText(sourceFile) === 'false');
  assert.ok(fin, 'load() debe apagar el estado de carga al terminar.');
  const guardLoading = guardText(fin);
  assert.ok(guardLoading, 'El estado de carga se apaga dentro de una comprobacion.');
  assert.ok(
    guardLoading.includes('esActual'),
    'Una peticion vieja no puede apagar el estado de carga de la vigente.'
  );
  assert.ok(!guardLoading.startsWith('ELSE('), 'El estado de carga no se apaga en la rama ELSE de la guarda.');
});
