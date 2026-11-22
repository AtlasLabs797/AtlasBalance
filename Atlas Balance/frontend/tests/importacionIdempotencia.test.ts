import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';

// F-10: la clave de idempotencia de «Confirmar importación» debe identificar
// una operación, es decir, un lote concreto. El backend la valida contra el
// propio lote (columna ConfirmacionIdempotencyKey de IMPORTACION_LOTES), así que
// reutilizarla entre lotes no produce 409 ni corrupción, pero deja de ser una
// clave por operación. Este test comprueba sobre el fuente (no hay DOM en este
// repo) que la clave se regenera cada vez que cambia el lote al que se aplica
// y que la petición de confirmación envía la clave vigente.
//
// B-16 (añadido por el orquestador): la página usa el helper compartido `hoy`
// en vez de su copia local del cálculo del día.

const pagePath = new URL('../../src/pages/ImportacionPage.tsx', import.meta.url);
const source = readFileSync(pagePath, 'utf8');
const sourceFile = ts.createSourceFile('ImportacionPage.tsx', source, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TSX);

function componentStatements(): ts.NodeArray<ts.Statement> {
  const fn = sourceFile.statements.find(
    (stmt): stmt is ts.FunctionDeclaration => ts.isFunctionDeclaration(stmt) && stmt.name?.text === 'ImportacionPage'
  );
  assert.ok(fn?.body, 'No se encuentra el componente ImportacionPage.');
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

function findVariableDeclaration(name: string): ts.VariableDeclaration | undefined {
  for (const stmt of componentStatements()) {
    if (!ts.isVariableStatement(stmt)) {
      continue;
    }
    for (const decl of stmt.declarationList.declarations) {
      if (ts.isIdentifier(decl.name) && decl.name.text === name) {
        return decl;
      }
      // Estado desestructurado: const [valor, setValor] = useState(...)
      if (ts.isArrayBindingPattern(decl.name)) {
        for (const element of decl.name.elements) {
          if (!ts.isOmittedExpression(element) && ts.isIdentifier(element.name) && element.name.text === name) {
            return decl;
          }
        }
      }
    }
  }
  return undefined;
}

// Nombres de las funciones invocadas dentro del nodo (useState(() => hoy()) -> 'hoy').
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

// true si dentro del nodo se asigna una clave nueva a <ref>.current.
function asignaClave(ref: string) {
  return (node: ts.Node): boolean => {
    let encontrada = false;
    const visit = (child: ts.Node) => {
      if (encontrada) {
        return;
      }
      if (
        ts.isBinaryExpression(child) &&
        child.operatorToken.kind === ts.SyntaxKind.EqualsToken &&
        ts.isPropertyAccessExpression(child.left) &&
        ts.isIdentifier(child.left.expression) &&
        child.left.expression.text === ref &&
        child.left.name.text === 'current'
      ) {
        encontrada = true;
        return;
      }
      ts.forEachChild(child, visit);
    };
    visit(node);
    return encontrada;
  };
}

const asignaClaveConfirmacion = asignaClave('confirmIdempotencyKeyRef');
const asignaClaveCreate = asignaClave('createIdempotencyKeyRef');

// true si dentro del nodo se lee confirmIdempotencyKeyRef.current.
function leeClaveConfirmacion(node: ts.Node): boolean {
  let encontrada = false;
  const visit = (child: ts.Node) => {
    if (encontrada) {
      return;
    }
    if (
      ts.isPropertyAccessExpression(child) &&
      ts.isIdentifier(child.expression) &&
      child.expression.text === 'confirmIdempotencyKeyRef' &&
      child.name.text === 'current'
    ) {
      encontrada = true;
      return;
    }
    ts.forEachChild(child, visit);
  };
  visit(node);
  return encontrada;
}

// Todas las funciones flecha del fuente.
function arrowFunctions(): ts.ArrowFunction[] {
  const encontradas: ts.ArrowFunction[] = [];
  const visit = (node: ts.Node) => {
    if (ts.isArrowFunction(node)) {
      encontradas.push(node);
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return encontradas;
}

// Handler que abre un lote del historial: pide el detalle de un lote y fija el
// lote actual (la URL del detalle es la unica con /importacion/lotes/{id}).
function findAbrirLoteHandler(): ts.ArrowFunction | undefined {
  return arrowFunctions().find((fn) => {
    const texto = fn.getText(sourceFile);
    return texto.includes('/importacion/lotes/') && texto.includes('setCurrentLote');
  });
}

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

test('la clave de confirmacion se regenera al validar un lote nuevo', () => {
  const fn = findArrowFunction('validateImport');
  assert.ok(fn, 'No se encuentra validateImport.');

  assert.ok(
    asignaClaveConfirmacion(fn),
    'Al validar se cambia el lote al que se aplica la confirmacion: la clave de idempotencia debe regenerarse.'
  );
});

test('la clave de confirmacion se regenera al abrir un lote del historial', () => {
  const handler = findAbrirLoteHandler();
  assert.ok(handler, 'No se encuentra el handler que abre un lote del historial.');

  assert.ok(
    asignaClaveConfirmacion(handler),
    'Abrir un lote del historial cambia el lote al que se aplica la confirmacion: la clave de idempotencia debe regenerarse.'
  );
});

test('la peticion de confirmacion envia la clave vigente del ref', () => {
  const fn = findArrowFunction('confirmImport');
  assert.ok(fn, 'No se encuentra confirmImport.');

  assert.ok(
    leeClaveConfirmacion(fn),
    'La confirmacion debe enviar confirmIdempotencyKeyRef.current para usar la clave del lote abierto.'
  );
});

test('la clave de creacion se regenera al recargar el contexto de cuentas', () => {
  const load = arrowFunctions().find((fn) => fn.getText(sourceFile).includes('/importacion/contexto'));
  assert.ok(load, 'No se encuentra la carga del contexto de importacion.');

  assert.ok(
    asignaClaveCreate(load),
    'Al recargar el contexto cambia la cuenta objetivo del POST de creacion: la clave de idempotencia debe regenerarse.'
  );
});

test('la pagina usa el helper hoy en vez de su copia local', () => {
  assert.ok(
    importedNames('@/utils/hoy').includes('hoy'),
    'ImportacionPage debe usar el helper hoy en vez de calcular "hoy" por su cuenta.'
  );
  assert.ok(
    !/function getTodayInputValue/.test(source),
    'ImportacionPage no debe mantener una copia local del calculo de "hoy".'
  );
  assert.ok(
    !/toISOString\(\)\.slice\(0,\s*10\)/.test(source),
    'ImportacionPage no debe construir una fecha de dia con toISOString(): eso devuelve el dia en UTC.'
  );
});

test('la fecha del plazo fijo se inicializa con el inicializador perezoso de hoy', () => {
  const decl = findVariableDeclaration('plazoFecha');
  assert.ok(decl, 'No se encuentra el estado plazoFecha.');

  const init = decl.initializer;
  assert.ok(init && ts.isCallExpression(init), 'plazoFecha debe inicializarse con useState.');
  assert.equal(init.expression.getText(sourceFile), 'useState');

  const argumento = init.arguments[0];
  assert.ok(
    argumento && ts.isArrowFunction(argumento),
    'plazoFecha debe usar useState(() => hoy()): con useState(hoy) el helper recibiria el estado anterior como Date.'
  );
  assert.ok(calledIdentifiers(argumento).includes('hoy'), 'El inicializador debe llamar a hoy().');
});
