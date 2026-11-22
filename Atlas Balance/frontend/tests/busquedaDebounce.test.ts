import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';

// R-6: las busquedas de Formatos, Paises, Cuentas y Usuarios disparaban una
// peticion por pulsacion de tecla. Este test comprueba sobre el fuente (no hay
// DOM en este repo) que la peticion usa el valor debounced del hook existente
// (src/hooks/useDebouncedValue.ts) y que el input sigue mostrando al instante
// lo que el usuario escribe.

const PAGINAS = [
  { nombre: 'FormatosImportacionPage', ruta: '../../src/pages/FormatosImportacionPage.tsx' },
  { nombre: 'PaisesPage', ruta: '../../src/pages/configuracion/PaisesPage.tsx' },
  { nombre: 'CuentasPage', ruta: '../../src/pages/CuentasPage.tsx' },
  { nombre: 'UsuariosPage', ruta: '../../src/pages/UsuariosPage.tsx' },
] as const;

const ESTADO_BUSQUEDA = 'search';
const VALOR_DEBOUNCED = 'debouncedSearch';

function cargar(ruta: string): ts.SourceFile {
  const source = readFileSync(new URL(ruta, import.meta.url), 'utf8');
  return ts.createSourceFile('pagina.tsx', source, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TSX);
}

// Identificadores usados dentro de un nodo (no cuenta los nombres de propiedad).
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

// Propiedades de objeto que se llaman "search" (la que viaja a la peticion).
function propiedadesSearch(sourceFile: ts.SourceFile): ts.PropertyAssignment[] {
  const encontradas: ts.PropertyAssignment[] = [];
  const visit = (node: ts.Node) => {
    if (
      ts.isPropertyAssignment(node) &&
      ((ts.isIdentifier(node.name) && node.name.text === ESTADO_BUSQUEDA) ||
        (ts.isStringLiteral(node.name) && node.name.text === ESTADO_BUSQUEDA))
    ) {
      encontradas.push(node);
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return encontradas;
}

// Llamadas al hook de debounce.
function llamadasDebounce(sourceFile: ts.SourceFile): ts.CallExpression[] {
  const encontradas: ts.CallExpression[] = [];
  const visit = (node: ts.Node) => {
    if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === 'useDebouncedValue') {
      encontradas.push(node);
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return encontradas;
}

// Dependencias declaradas en los useEffect de la pagina.
function dependenciasEfectos(sourceFile: ts.SourceFile): ts.ArrayLiteralExpression[] {
  const dependencias: ts.ArrayLiteralExpression[] = [];
  const visit = (node: ts.Node) => {
    if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === 'useEffect') {
      const segundo = node.arguments[1];
      if (segundo && ts.isArrayLiteralExpression(segundo)) {
        dependencias.push(segundo);
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return dependencias;
}

// El input de busqueda debe seguir atado al estado bruto (filtro visible inmediato).
function inputVinculadoAlEstadoBruto(sourceFile: ts.SourceFile): boolean {
  let encontrado = false;
  const visit = (node: ts.Node) => {
    if (
      ts.isJsxAttribute(node) &&
      ts.isIdentifier(node.name) &&
      node.name.text === 'value' &&
      node.initializer &&
      ts.isJsxExpression(node.initializer) &&
      node.initializer.expression &&
      ts.isIdentifier(node.initializer.expression) &&
      node.initializer.expression.text === ESTADO_BUSQUEDA
    ) {
      encontrado = true;
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return encontrado;
}

// Importa el hook desde la ruta real del proyecto.
function importaHook(sourceFile: ts.SourceFile): boolean {
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

// Llamadas del tipo queryKeys.<seccion>.list(...) (React Query).
function llamadasQueryKeyList(sourceFile: ts.SourceFile): ts.CallExpression[] {
  const encontradas: ts.CallExpression[] = [];
  const visit = (node: ts.Node) => {
    if (
      ts.isCallExpression(node) &&
      ts.isPropertyAccessExpression(node.expression) &&
      node.expression.name.text === 'list' &&
      ts.isPropertyAccessExpression(node.expression.expression) &&
      node.expression.expression.name.text === 'usuarios'
    ) {
      encontradas.push(node);
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return encontradas;
}

test('las cuatro paginas importan el hook useDebouncedValue existente', () => {
  for (const pagina of PAGINAS) {
    const sourceFile = cargar(pagina.ruta);
    assert.ok(
      importaHook(sourceFile),
      `${pagina.nombre} debe importar useDebouncedValue desde '@/hooks/useDebouncedValue'.`
    );
  }
});

test('la busqueda se debouncea con el hook existente y su retardo por defecto', () => {
  for (const pagina of PAGINAS) {
    const sourceFile = cargar(pagina.ruta);
    const llamadas = llamadasDebounce(sourceFile);
    assert.ok(llamadas.length > 0, `${pagina.nombre} debe llamar a useDebouncedValue.`);

    const sobreEstadoBruto = llamadas.filter((llamada) => {
      const primero = llamada.arguments[0];
      return !!primero && ts.isIdentifier(primero) && primero.text === ESTADO_BUSQUEDA;
    });
    assert.ok(
      sobreEstadoBruto.length > 0,
      `${pagina.nombre} debe debouncear el estado de busqueda '${ESTADO_BUSQUEDA}'.`
    );

    for (const llamada of sobreEstadoBruto) {
      const segundo = llamada.arguments[1];
      if (segundo) {
        assert.ok(
          ts.isNumericLiteral(segundo) && segundo.text === '300',
          `${pagina.nombre} debe usar el retardo por defecto del hook (300 ms).`
        );
      }
    }
  }
});

test('la peticion usa el valor debounced y no el texto escrito tecla a tecla', () => {
  for (const pagina of PAGINAS) {
    const sourceFile = cargar(pagina.ruta);
    const propiedades = propiedadesSearch(sourceFile);
    assert.ok(propiedades.length > 0, `${pagina.nombre} debe enviar un parametro 'search' a la peticion.`);

    for (const propiedad of propiedades) {
      const usados = identificadores(propiedad.initializer);
      assert.ok(
        usados.has(VALOR_DEBOUNCED),
        `${pagina.nombre} debe enviar el valor debounced en 'search', no el estado bruto.`
      );
      assert.ok(
        !usados.has(ESTADO_BUSQUEDA),
        `${pagina.nombre} no debe enviar el estado de busqueda sin debouncear.`
      );
    }
  }
});

test('el input de busqueda sigue mostrando al instante lo que se escribe', () => {
  for (const pagina of PAGINAS) {
    const sourceFile = cargar(pagina.ruta);
    assert.ok(
      inputVinculadoAlEstadoBruto(sourceFile),
      `${pagina.nombre} debe mantener el input de busqueda atado al estado bruto (filtro visible inmediato).`
    );
  }
});

test('los efectos que recargan la lista dependen del valor debounced', () => {
  for (const pagina of PAGINAS) {
    const sourceFile = cargar(pagina.ruta);
    const dependencias = dependenciasEfectos(sourceFile);
    const conBruto = dependencias.filter((deps) =>
      deps.elements.some((elemento) => ts.isIdentifier(elemento) && elemento.text === ESTADO_BUSQUEDA)
    );
    assert.equal(
      conBruto.length,
      0,
      `${pagina.nombre} no debe recargar la lista cuando cambia el estado de busqueda sin debouncear.`
    );
  }
});

test('la clave de cache de la lista de usuarios usa el valor debounced', () => {
  const sourceFile = cargar('../../src/pages/UsuariosPage.tsx');
  const llamadas = llamadasQueryKeyList(sourceFile);
  assert.ok(llamadas.length > 0, 'UsuariosPage debe construir la queryKey de la lista de usuarios.');

  for (const llamada of llamadas) {
    const argumento = llamada.arguments[0];
    assert.ok(argumento && ts.isObjectLiteralExpression(argumento), 'La queryKey debe construirse con un objeto.');
    const propiedad = argumento.properties.find(
      (p): p is ts.PropertyAssignment => ts.isPropertyAssignment(p) && ts.isIdentifier(p.name) && p.name.text === 'search'
    );
    assert.ok(propiedad, 'La queryKey de la lista de usuarios debe incluir el termino de busqueda.');
    const usados = identificadores(propiedad.initializer);
    assert.ok(
      usados.has(VALOR_DEBOUNCED),
      'La queryKey de la lista de usuarios debe usar el valor debounced, no el texto escrito.'
    );
    assert.ok(
      !usados.has(ESTADO_BUSQUEDA),
      'La queryKey de la lista de usuarios no debe usar el estado de busqueda sin debouncear.'
    );
  }
});
