import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';

// V-03.02 (F-4): los sondeos de ConfiguracionPage (estado de la actualizacion)
// y BackupsPage (estado de una operacion de backup y vinculacion con Google
// Drive) consultan cada 2,5 s con la API caida a proposito. El interceptor
// pintaba un toast por cada error, y los bucles seguian consultando tras
// desmontar la pagina.
//
// No hay DOM en este repo: el test comprueba sobre el fuente.

const PAGINAS = [
  {
    nombre: 'ConfiguracionPage',
    ruta: '../../src/pages/ConfiguracionPage.tsx',
    sondeos: ['/sistema/estado'],
  },
  {
    nombre: 'BackupsPage',
    ruta: '../../src/pages/BackupsPage.tsx',
    sondeos: ['/backups/operations/', '/backups/google-drive/link/'],
  },
] as const;

function cargar(ruta: string): ts.SourceFile {
  const source = readFileSync(new URL(ruta, import.meta.url), 'utf8');
  return ts.createSourceFile('pagina.tsx', source, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TSX);
}

// Texto de la url de una llamada (literal o plantilla).
function urlText(node: ts.Expression | undefined): string {
  if (!node) {
    return '';
  }
  if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) {
    return node.text;
  }
  if (ts.isTemplateExpression(node)) {
    return node.head.text + node.templateSpans.map((span) => span.literal.text).join('');
  }
  return '';
}

// Todas las llamadas api.get(...) del fichero.
function llamadasApiGet(sourceFile: ts.SourceFile): ts.CallExpression[] {
  const llamadas: ts.CallExpression[] = [];
  const visit = (node: ts.Node) => {
    if (
      ts.isCallExpression(node) &&
      ts.isPropertyAccessExpression(node.expression) &&
      ts.isIdentifier(node.expression.expression) &&
      node.expression.expression.text === 'api' &&
      node.expression.name.text === 'get'
    ) {
      llamadas.push(node);
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return llamadas;
}

// La peticion lleva { skipErrorToast: true } en su configuracion.
function llevaSkipErrorToast(llamada: ts.CallExpression): boolean {
  const configuracion = llamada.arguments[1];
  if (!configuracion || !ts.isObjectLiteralExpression(configuracion)) {
    return false;
  }
  return configuracion.properties.some(
    (propiedad) =>
      ts.isPropertyAssignment(propiedad) &&
      ts.isIdentifier(propiedad.name) &&
      propiedad.name.text === 'skipErrorToast' &&
      propiedad.initializer.kind === ts.SyntaxKind.TrueKeyword
  );
}

// useEffect con dependencias vacias (limpieza al desmontar).
function efectosConDependenciasVacias(sourceFile: ts.SourceFile): ts.ArrowFunction[] {
  const efectos: ts.ArrowFunction[] = [];
  const visit = (node: ts.Node) => {
    if (
      ts.isCallExpression(node) &&
      ts.isIdentifier(node.expression) &&
      node.expression.text === 'useEffect' &&
      node.arguments[0] &&
      ts.isArrowFunction(node.arguments[0])
    ) {
      const dependencias = node.arguments[1];
      if (dependencias && ts.isArrayLiteralExpression(dependencias) && dependencias.elements.length === 0) {
        efectos.push(node.arguments[0]);
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return efectos;
}

// Llamadas X.stop() dentro de un nodo.
function invocaStop(node: ts.Node): boolean {
  let encontrado = false;
  const visit = (child: ts.Node) => {
    if (
      ts.isCallExpression(child) &&
      ts.isPropertyAccessExpression(child.expression) &&
      child.expression.name.text === 'stop'
    ) {
      encontrado = true;
    }
    ts.forEachChild(child, visit);
  };
  visit(node);
  return encontrado;
}

// Llamadas X.wait(<ms>) dentro de un nodo.
function esperasDeSondeo(node: ts.Node): number[] {
  const esperas: number[] = [];
  const visit = (child: ts.Node) => {
    if (
      ts.isCallExpression(child) &&
      ts.isPropertyAccessExpression(child.expression) &&
      child.expression.name.text === 'wait' &&
      child.arguments[0] &&
      ts.isNumericLiteral(child.arguments[0])
    ) {
      esperas.push(Number(child.arguments[0].text));
    }
    ts.forEachChild(child, visit);
  };
  visit(node);
  return esperas;
}

test('los sondeos de las dos paginas piden la peticion sin toast global', () => {
  for (const pagina of PAGINAS) {
    const sourceFile = cargar(pagina.ruta);
    const llamadas = llamadasApiGet(sourceFile);

    for (const sondeo of pagina.sondeos) {
      const llamada = llamadas.find((c) => urlText(c.arguments[0]).includes(sondeo));
      assert.ok(llamada, `${pagina.nombre} debe consultar ${sondeo}.`);
      assert.ok(
        llevaSkipErrorToast(llamada),
        `${pagina.nombre}: el sondeo de ${sondeo} debe pedir skipErrorToast para no pintar un toast cada 2,5 s.`
      );
    }
  }
});

test('las dos paginas detienen el sondeo al desmontar', () => {
  for (const pagina of PAGINAS) {
    const sourceFile = cargar(pagina.ruta);
    const limpiezas = efectosConDependenciasVacias(sourceFile).filter(invocaStop);
    assert.ok(
      limpiezas.length > 0,
      `${pagina.nombre} debe detener el sondeo al desmontar (useEffect de limpieza que llama a stop()).`
    );
  }
});

test('los bucles de sondeo esperan con el ciclo de vida y no con un setTimeout suelto', () => {
  for (const pagina of PAGINAS) {
    const sourceFile = cargar(pagina.ruta);
    const esperas = esperasDeSondeo(sourceFile);
    assert.ok(
      esperas.includes(2500),
      `${pagina.nombre} debe esperar 2500 ms entre iteraciones usando el ciclo de vida del sondeo.`
    );
  }
});
