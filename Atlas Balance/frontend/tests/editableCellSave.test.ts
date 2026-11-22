import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';
import { applyEditableCellSaveResult } from '../src/utils/editableCellSave.js';

// F-9: si el guardado de una celda falla, lo escrito por el usuario se perdia
// (el campo volvia al valor anterior) sin aviso util. En este repo no hay DOM,
// asi que el componente se comprueba sobre el fuente con la API del compilador
// de TypeScript y la regla de estado sobre el helper puro.

const source = readFileSync(new URL('../../src/components/extractos/EditableCell.tsx', import.meta.url), 'utf8');
const sourceFile = ts.createSourceFile('EditableCell.tsx', source, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TSX);

interface CallInfo {
  name: string;
  args: readonly ts.Expression[];
}

function componentStatements(): ts.NodeArray<ts.Statement> {
  const fn = sourceFile.statements.find(
    (stmt): stmt is ts.FunctionDeclaration => ts.isFunctionDeclaration(stmt) && stmt.name?.text === 'EditableCell'
  );
  assert.ok(fn?.body, 'No se encuentra el componente EditableCell.');
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

// Bloques try/catch de commit(): lo que se hace al guardar y lo que se hace
// cuando el guardado falla.
function commitTryCatch(): { tryBlock: ts.Block; catchBlock: ts.Block } {
  const commit = findArrowFunction('commit');
  assert.ok(commit, 'No se encuentra la funcion commit.');
  assert.ok(ts.isBlock(commit.body), 'commit debe ser un bloque.');
  const tryStatement = commit.body.statements.find((stmt): stmt is ts.TryStatement => ts.isTryStatement(stmt));
  assert.ok(tryStatement, 'commit debe envolver la llamada a onSave en un try/catch.');
  const catchClause = tryStatement.catchClause;
  assert.ok(catchClause, 'commit debe tener bloque catch.');
  return { tryBlock: tryStatement.tryBlock, catchBlock: catchClause.block };
}

function callsIn(node: ts.Node): CallInfo[] {
  const calls: CallInfo[] = [];
  const visit = (child: ts.Node) => {
    if (ts.isCallExpression(child) && ts.isIdentifier(child.expression)) {
      calls.push({ name: child.expression.text, args: child.arguments });
    }
    ts.forEachChild(child, visit);
  };
  visit(node);
  return calls;
}

// Rama que se renderiza mientras la celda esta en edicion.
function editingBranch(): ts.Node {
  const stmt = componentStatements().find(
    (s): s is ts.IfStatement => ts.isIfStatement(s) && ts.isIdentifier(s.expression) && s.expression.text === 'isEditing'
  );
  assert.ok(stmt, 'No se encuentra la rama de edicion de la celda.');
  return stmt.thenStatement;
}

function hasText(node: ts.Node, needle: string): boolean {
  let found = false;
  const visit = (child: ts.Node) => {
    if (found) {
      return;
    }
    if ((ts.isStringLiteral(child) || ts.isJsxText(child)) && child.text.includes(needle)) {
      found = true;
      return;
    }
    ts.forEachChild(child, visit);
  };
  visit(node);
  return found;
}

function hasRoleAlert(node: ts.Node): boolean {
  let found = false;
  const visit = (child: ts.Node) => {
    if (found) {
      return;
    }
    if (
      ts.isJsxAttribute(child) &&
      ts.isIdentifier(child.name) &&
      child.name.text === 'role' &&
      child.initializer &&
      ts.isStringLiteral(child.initializer) &&
      child.initializer.text === 'alert'
    ) {
      found = true;
      return;
    }
    ts.forEachChild(child, visit);
  };
  visit(node);
  return found;
}

test('el estado de celda conserva el borrador y deja el editor abierto cuando el guardado falla', () => {
  const state = { draft: '12,50', isEditing: true, saveState: 'idle' as const };

  const failed = applyEditableCellSaveResult(state, 'failed');

  assert.equal(failed.draft, '12,50');
  assert.equal(failed.isEditing, true);
  assert.equal(failed.saveState, 'error');
});

test('el estado de celda cierra el editor y avisa de guardado cuando el guardado va bien', () => {
  const state = { draft: '12,50', isEditing: true, saveState: 'idle' as const };

  const saved = applyEditableCellSaveResult(state, 'saved');

  assert.equal(saved.draft, '12,50');
  assert.equal(saved.isEditing, false);
  assert.equal(saved.saveState, 'saved');
});

test('el fallo del guardado no sustituye el borrador por el valor del servidor', () => {
  const { catchBlock } = commitTryCatch();

  const setDraftCalls = callsIn(catchBlock).filter((call) => call.name === 'setDraft');
  assert.ok(setDraftCalls.length > 0, 'El catch debe fijar el borrador que se conserva.');
  for (const call of setDraftCalls) {
    const arg = call.args[0];
    assert.ok(
      !(ts.isIdentifier(arg) && arg.text === 'value'),
      'El fallo no debe resetear el borrador al valor del servidor: se perderia lo escrito.',
    );
  }
});

test('el fallo del guardado se resuelve con el helper en modo failed y el editor sigue abierto', () => {
  const { catchBlock } = commitTryCatch();

  const helperCalls = callsIn(catchBlock).filter((call) => call.name === 'applyEditableCellSaveResult');
  assert.equal(helperCalls.length, 1, 'El catch debe aplicar el helper de estado del guardado.');
  const result = helperCalls[0].args[1];
  assert.ok(
    result && ts.isStringLiteral(result) && result.text === 'failed',
    'El catch debe aplicar el resultado "failed" (borrador conservado, editor abierto, estado de error).',
  );

  const names = callsIn(catchBlock).map((call) => call.name);
  assert.ok(names.includes('setIsEditing'), 'El catch debe propagar el editor que deja el helper.');
  assert.ok(names.includes('setSaveState'), 'El catch debe propagar el estado de error que deja el helper.');
});

test('mientras se edita, el fallo se avisa junto al valor escrito', () => {
  const branch = editingBranch();

  assert.ok(hasRoleAlert(branch), 'La rama de edicion debe avisar del fallo con role="alert".');
  assert.ok(hasText(branch, 'cell-save-state--error'), 'La rama de edicion debe usar el estilo de error ya existente.');
  assert.ok(hasText(branch, 'No guardado'), 'La rama de edicion debe explicar que no se ha guardado.');
});

test('el guardado correcto mantiene el comportamiento actual: cierra el editor y avisa', () => {
  const { tryBlock } = commitTryCatch();

  const helperCalls = callsIn(tryBlock).filter((call) => call.name === 'applyEditableCellSaveResult');
  assert.equal(helperCalls.length, 1, 'El try debe aplicar el helper de estado del guardado.');
  const result = helperCalls[0].args[1];
  assert.ok(
    result && ts.isStringLiteral(result) && result.text === 'saved',
    'El try debe aplicar el resultado "saved" (editor cerrado, estado de guardado correcto).',
  );
});
