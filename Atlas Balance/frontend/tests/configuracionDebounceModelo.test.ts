import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';

// V-03.02 (R-6): el campo "Modelo" de la seccion IA de Configuracion
// pedia /ia/modelos en cada pulsacion de tecla. El proyecto ya tiene el hook
// src/hooks/useDebouncedValue.ts: se reutiliza, no se crea otro debounce.
//
// No hay DOM en este repo: el test comprueba sobre el fuente.

const source = readFileSync(new URL('../../src/pages/ConfiguracionPage.tsx', import.meta.url), 'utf8');
const sourceFile = ts.createSourceFile('ConfiguracionPage.tsx', source, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TSX);

const VALOR_DEBOUNCED = 'debouncedModelSearch';

function urlText(node: ts.Expression | undefined): string {
  if (!node) {
    return '';
  }
  if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) {
    return node.text;
  }
  return '';
}

// La llamada api.get('/ia/modelos').
function llamadaModelos(): ts.CallExpression | undefined {
  const visit = (node: ts.Node): ts.CallExpression | undefined => {
    if (
      ts.isCallExpression(node) &&
      ts.isPropertyAccessExpression(node.expression) &&
      ts.isIdentifier(node.expression.expression) &&
      node.expression.expression.text === 'api' &&
      node.expression.name.text === 'get' &&
      urlText(node.arguments[0]) === '/ia/modelos'
    ) {
      return node;
    }
    let encontrado: ts.CallExpression | undefined;
    ts.forEachChild(node, (child) => {
      if (!encontrado) {
        encontrado = visit(child);
      }
    });
    return encontrado;
  };
  return visit(sourceFile);
}

// Propiedad 'search' dentro de un nodo.
function propiedadSearch(node: ts.Node): ts.PropertyAssignment | undefined {
  let encontrada: ts.PropertyAssignment | undefined;
  const visit = (child: ts.Node) => {
    if (
      ts.isPropertyAssignment(child) &&
      ts.isIdentifier(child.name) &&
      child.name.text === 'search'
    ) {
      encontrada = child;
    }
    ts.forEachChild(child, visit);
  };
  visit(node);
  return encontrada;
}

// Identificadores usados dentro de un nodo.
function identificadores(node: ts.Node): Set<string> {
  const nombres = new Set<string>();
  const visit = (child: ts.Node) => {
    if (ts.isIdentifier(child)) {
      nombres.add(child.text);
    }
    ts.forEachChild(child, visit);
  };
  visit(node);
  return nombres;
}

// Llamadas al hook de debounce.
function llamadasDebounce(): ts.CallExpression[] {
  const llamadas: ts.CallExpression[] = [];
  const visit = (node: ts.Node) => {
    if (
      ts.isCallExpression(node) &&
      ts.isIdentifier(node.expression) &&
      node.expression.text === 'useDebouncedValue'
    ) {
      llamadas.push(node);
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return llamadas;
}

// Importa el hook desde la ruta real del proyecto.
function importaHook(): boolean {
  for (const stmt of sourceFile.statements) {
    if (!ts.isImportDeclaration(stmt) || !ts.isStringLiteral(stmt.moduleSpecifier)) {
      continue;
    }
    if (stmt.moduleSpecifier.text !== '@/hooks/useDebouncedValue') {
      continue;
    }
    const bindings = stmt.importClause?.namedBindings;
    if (bindings && ts.isNamedImports(bindings)) {
      if (bindings.elements.some((el) => el.name.text === 'useDebouncedValue')) {
        return true;
      }
    }
  }
  return false;
}

test('ConfiguracionPage reutiliza el hook useDebouncedValue existente', () => {
  assert.ok(
    importaHook(),
    "ConfiguracionPage debe importar useDebouncedValue desde '@/hooks/useDebouncedValue'."
  );
});

// Una cadena de accesos a propiedad que acaba en `config.<...>.model`.
function esAccesoAModelo(node: ts.Expression): boolean {
  if (!ts.isPropertyAccessExpression(node) || node.name.text !== 'model') {
    return false;
  }

  let actual: ts.Expression = node.expression;
  while (ts.isPropertyAccessExpression(actual)) {
    actual = actual.expression;
  }

  return ts.isIdentifier(actual) && actual.text === 'config';
}

test('el valor de busqueda del campo Modelo pasa por useDebouncedValue', () => {
  const llamadas = llamadasDebounce();
  assert.ok(llamadas.length > 0, 'ConfiguracionPage debe llamar a useDebouncedValue.');

  const sobreModelo = llamadas.filter((llamada) => {
    const primero = llamada.arguments[0];
    return !!primero && esAccesoAModelo(primero);
  });
  assert.ok(
    sobreModelo.length > 0,
    'El valor de busqueda del campo Modelo debe pasar por useDebouncedValue.'
  );
});

test('la consulta de modelos usa el valor debounced y no el texto escrito tecla a tecla', () => {
  const llamada = llamadaModelos();
  assert.ok(llamada, 'ConfiguracionPage debe pedir /ia/modelos.');

  const search = propiedadSearch(llamada);
  assert.ok(search, 'La consulta de modelos debe enviar un parametro search.');

  const usados = identificadores(search.initializer);
  assert.ok(
    usados.has(VALOR_DEBOUNCED),
    'La consulta de modelos debe enviar el valor debounced del campo Modelo.'
  );
  assert.ok(
    !usados.has('model'),
    'La consulta de modelos no debe enviar el texto escrito sin debouncear.'
  );
});

test('el efecto que recarga los modelos depende del valor debounced', () => {
  const dependencias: ts.ArrayLiteralExpression[] = [];
  const visit = (node: ts.Node) => {
    if (
      ts.isCallExpression(node) &&
      ts.isIdentifier(node.expression) &&
      node.expression.text === 'useEffect' &&
      node.arguments[1] &&
      ts.isArrayLiteralExpression(node.arguments[1])
    ) {
      dependencias.push(node.arguments[1]);
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);

  const conDebounced = dependencias.filter((deps) =>
    deps.elements.some((elemento) => ts.isIdentifier(elemento) && elemento.text === VALOR_DEBOUNCED)
  );
  assert.ok(
    conDebounced.length > 0,
    'El efecto que recarga los modelos debe depender del valor debounced.'
  );

  const conModelo = dependencias.filter((deps) =>
    deps.elements.some((elemento) => ts.isExpression(elemento) && esAccesoAModelo(elemento))
  );
  assert.equal(
    conModelo.length,
    0,
    'El efecto que recarga los modelos no debe depender del texto sin debouncear.'
  );
});
