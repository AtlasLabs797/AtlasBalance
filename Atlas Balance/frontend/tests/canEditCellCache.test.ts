import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';
import { createCanEditCellCache } from '../src/utils/canEditCellCache.js';

// R-5: los permisos de celda se evaluaban tres veces por celda y en cada
// render de la tabla (cada cambio de foco re-renderiza la tabla). El test
// comprueba la cache (logica pura) y que el componente la use en vez de
// llamar a canEditCell en cada celda (no hay DOM en este repo).

const source = readFileSync(new URL('../../src/components/extractos/ExtractoTable.tsx', import.meta.url), 'utf8');
const sourceFile = ts.createSourceFile('ExtractoTable.tsx', source, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TSX);

interface Fila {
  id: string;
}

function llamadasCanEditCell(): ts.CallExpression[] {
  const llamadas: ts.CallExpression[] = [];
  const visit = (node: ts.Node) => {
    if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === 'canEditCell') {
      llamadas.push(node);
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return llamadas;
}

// useMemo que crea la cache de permisos.
function useMemoCache(): { deps: ts.ArrayLiteralExpression | undefined } | undefined {
  const visit = (node: ts.Node): { deps: ts.ArrayLiteralExpression | undefined } | undefined => {
    if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === 'useMemo') {
      const primero = node.arguments[0];
      if (primero && ts.isArrowFunction(primero)) {
        const usaCreate = primero.getText().includes('createCanEditCellCache');
        if (usaCreate) {
          const segundo = node.arguments[1];
          return { deps: segundo && ts.isArrayLiteralExpression(segundo) ? segundo : undefined };
        }
      }
    }
    let encontrado: { deps: ts.ArrayLiteralExpression | undefined } | undefined;
    ts.forEachChild(node, (child) => {
      if (!encontrado) {
        encontrado = visit(child);
      }
    });
    return encontrado;
  };
  return visit(sourceFile);
}

test('cada fila y columna se evalua una sola vez', () => {
  let llamadas = 0;
  const cache = createCanEditCellCache((_row: Fila, column: string) => {
    llamadas += 1;
    return column === 'monto';
  });

  const fila: Fila = { id: 'fila-1' };
  assert.equal(cache(fila, 'monto'), true);
  assert.equal(cache(fila, 'saldo'), false);
  assert.equal(cache(fila, 'monto'), true);
  assert.equal(cache(fila, 'saldo'), false);
  assert.equal(llamadas, 2, 'Cada columna de la fila debe evaluarse una sola vez.');
});

test('la cache se invalida cuando cambia el objeto fila', () => {
  let llamadas = 0;
  const cache = createCanEditCellCache((_row: Fila, column: string) => {
    llamadas += 1;
    return column === 'monto';
  });

  const fila: Fila = { id: 'fila-1' };
  assert.equal(cache(fila, 'monto'), true);
  assert.equal(cache({ ...fila }, 'monto'), true);
  assert.equal(llamadas, 2, 'Una fila nueva (mismo id, objeto nuevo) debe reevaluarse.');
});

test('la cache guarda el resultado por fila', () => {
  const cache = createCanEditCellCache((row: Fila, column: string) => row.id === 'fila-1' && column === 'monto');
  assert.equal(cache({ id: 'fila-1' }, 'monto'), true);
  assert.equal(cache({ id: 'fila-2' }, 'monto'), false);
  assert.equal(cache({ id: 'fila-2' }, 'saldo'), false);
});

test('el componente no llama a canEditCell en cada celda', () => {
  const llamadas = llamadasCanEditCell();
  assert.equal(
    llamadas.length,
    0,
    'canEditCell no debe evaluarse directamente en el render: solo a traves de la cache.'
  );

  const text = sourceFile.getFullText();
  const usos = text.match(/canEditCellCached\(/g) ?? [];
  assert.ok(
    usos.length >= 3,
    'Las tres decisiones de permiso de la celda (columna, flagged y flagged_nota) deben pasar por la cache.'
  );
});

test('la cache de permisos se invalida cuando cambia la funcion de permisos', () => {
  const memo = useMemoCache();
  assert.ok(memo, 'No se encuentra el useMemo que crea la cache de permisos.');
  assert.ok(memo.deps, 'El useMemo de la cache debe declarar sus dependencias.');
  const deps = memo.deps.elements.map((elemento) => elemento.getText());
  assert.ok(
    deps.includes('canEditCell'),
    'La cache debe depender de canEditCell: si cambian los permisos, se recalcula.'
  );
});
