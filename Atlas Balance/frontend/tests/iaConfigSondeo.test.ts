import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';
import {
  IA_CONFIG_STALE_MS,
  isIaConfigurationWrite,
  notifyIaConfigChanged,
  resolveIaAvailability,
  subscribeIaConfigChanged,
} from '../src/utils/iaConfigSource.js';

// R-8 (BAJA): GET /ia/config se consultaba cada 30 s en todas las pestañas
// (refetchInterval) y la configuracion de IA tenia tres fuentes de verdad:
// el hook (cache de TanStack Query), el store del chat (aiChatStore) y el
// store de disponibilidad (iaAvailabilityStore).
//
// El arreglo: sin sondeo periodico (staleTime en su lugar), una sola fuente
// para la disponibilidad (resolveIaAvailability) y refresco inmediato cuando
// la configuracion global se guarda.
//
// No hay DOM en este repo: la parte del hook se comprueba sobre el fuente con
// la API del compilador; la parte de logica pura se importa y se ejecuta.

const HOOK_RUTA = '../../src/hooks/queries/useIaConfigQuery.ts';

const source = readFileSync(new URL(HOOK_RUTA, import.meta.url), 'utf8');
const sourceFile = ts.createSourceFile('useIaConfigQuery.ts', source, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TS);

// Objeto de opciones que se pasa a useQuery(...).
function useQueryOptions(): ts.ObjectLiteralExpression {
  let opciones: ts.ObjectLiteralExpression | undefined;
  const visit = (node: ts.Node) => {
    if (
      !opciones &&
      ts.isCallExpression(node) &&
      ts.isIdentifier(node.expression) &&
      node.expression.text === 'useQuery' &&
      node.arguments[0] &&
      ts.isObjectLiteralExpression(node.arguments[0])
    ) {
      opciones = node.arguments[0];
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  assert.ok(opciones, 'El hook debe llamar a useQuery con un objeto de opciones.');
  return opciones;
}

// Nombre de una propiedad del objeto de opciones de useQuery.
function nombrePropiedad(propiedad: ts.ObjectLiteralElementLike): string {
  const nombre = propiedad.name;
  if (!nombre) {
    return '';
  }
  if (ts.isIdentifier(nombre) || ts.isStringLiteral(nombre)) {
    return nombre.text;
  }
  return '';
}

// Todos los identificadores que se invocan como funcion en el fichero.
function funcionesInvocadas(): string[] {
  const llamadas: string[] = [];
  const visit = (node: ts.Node) => {
    if (ts.isCallExpression(node) && ts.isIdentifier(node.expression)) {
      llamadas.push(node.expression.text);
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return llamadas;
}

// Modulo del que se importa cada simbolo.
function moduloImportado(simbolo: string): string | undefined {
  for (const statement of sourceFile.statements) {
    if (!ts.isImportDeclaration(statement) || !statement.importClause?.namedBindings) {
      continue;
    }
    if (!ts.isNamedImports(statement.importClause.namedBindings)) {
      continue;
    }
    for (const elemento of statement.importClause.namedBindings.elements) {
      if (elemento.name.text === simbolo) {
        return statement.moduleSpecifier.getText(sourceFile);
      }
    }
  }
  return undefined;
}

test('el hook no sondea /ia/config de forma periodica', () => {
  const opciones = useQueryOptions();
  const nombres = opciones.properties.map(nombrePropiedad);

  assert.ok(
    !nombres.includes('refetchInterval'),
    'El hook no debe volver a consultar /ia/config cada 30 s (refetchInterval).'
  );
  assert.ok(
    !nombres.includes('refetchIntervalInBackground'),
    'El hook no debe volver a consultar /ia/config cada 30 s (refetchIntervalInBackground).'
  );
});

test('el hook usa la ventana de frescura compartida en vez de un TTL propio', () => {
  const opciones = useQueryOptions();
  const staleTime = opciones.properties.find((p) => nombrePropiedad(p) === 'staleTime');

  assert.ok(staleTime, 'El hook debe definir staleTime para no repetir la peticion en cada navegacion.');
  assert.ok(ts.isPropertyAssignment(staleTime), 'staleTime debe ser una propiedad con valor.');
  assert.ok(
    ts.isIdentifier(staleTime.initializer) && staleTime.initializer.text === 'IA_CONFIG_STALE_MS',
    'staleTime debe usar IA_CONFIG_STALE_MS, la ventana de frescura compartida.'
  );
  assert.equal(
    moduloImportado('IA_CONFIG_STALE_MS'),
    "'@/utils/iaConfigSource'",
    'La ventana de frescura debe venir del modulo comun de la configuracion de IA.'
  );
});

test('el hook refresca al instante cuando la configuracion global se guarda', () => {
  const invocadas = funcionesInvocadas();

  assert.ok(
    invocadas.includes('subscribeIaConfigChanged'),
    'El hook debe suscribirse al aviso de cambio de la configuracion de IA.'
  );
  assert.ok(
    invocadas.includes('notifyIaConfigChanged'),
    'El hook debe avisar cuando se guarda la configuracion global para invalidar la cache.'
  );
});

test('la disponibilidad de la IA se decide en un solo sitio', () => {
  const invocadas = funcionesInvocadas();

  assert.ok(
    invocadas.includes('resolveIaAvailability'),
    'El hook debe usar resolveIaAvailability, la unica regla que decide si la IA esta disponible.'
  );
  assert.equal(
    moduloImportado('resolveIaAvailability'),
    "'@/utils/iaConfigSource'",
    'La regla de disponibilidad debe venir del modulo comun de la configuracion de IA.'
  );
});

test('resolveIaAvailability solo habilita la IA si el usuario puede usarla', () => {
  assert.equal(resolveIaAvailability(undefined), false);
  assert.equal(resolveIaAvailability(null), false);
  assert.equal(resolveIaAvailability({ habilitada: false, usuario_puede_usar: true }), false);
  assert.equal(resolveIaAvailability({ habilitada: true, usuario_puede_usar: false }), false);
  assert.equal(resolveIaAvailability({ habilitada: true, usuario_puede_usar: true }), true);
});

test('la ventana de frescura es de minutos, no de segundos', () => {
  assert.ok(
    IA_CONFIG_STALE_MS >= 60_000,
    `La ventana de frescura debe ser de al menos 1 min (ahora ${IA_CONFIG_STALE_MS} ms).`
  );
});

test('solo el guardado de la configuracion global se toma como cambio de la config de IA', () => {
  assert.equal(isIaConfigurationWrite('put', '/configuracion'), true);
  assert.equal(isIaConfigurationWrite('PUT', '/configuracion'), true);
  assert.equal(isIaConfigurationWrite('put', '/configuracion?x=1'), true);
  assert.equal(isIaConfigurationWrite('post', '/configuracion/smtp/test'), false);
  assert.equal(isIaConfigurationWrite('get', '/ia/config'), false);
  assert.equal(isIaConfigurationWrite('get', '/configuracion'), false);
  assert.equal(isIaConfigurationWrite(undefined, undefined), false);
});

test('el aviso de cambio notifica a los suscriptores y se puede darse de baja', () => {
  let llamadas = 0;
  const baja = subscribeIaConfigChanged(() => {
    llamadas += 1;
  });

  notifyIaConfigChanged();
  assert.equal(llamadas, 1, 'El suscriptor debe recibir el aviso.');

  baja();
  notifyIaConfigChanged();
  assert.equal(llamadas, 1, 'Tras darse de baja el suscriptor no debe recibir mas avisos.');
});
