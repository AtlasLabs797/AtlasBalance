import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';
import { notaAlertaCambiada } from '../src/utils/notaAlerta.js';

// F-6: salir de la nota de alerta lanzaba un PATCH aunque el texto no hubiera
// cambiado. El test comprueba la decision (logica pura) y que el componente la
// aplique de verdad en el onBlur del campo (no hay DOM en este repo).

const source = readFileSync(new URL('../../src/components/extractos/ExtractoTable.tsx', import.meta.url), 'utf8');
const sourceFile = ts.createSourceFile('ExtractoTable.tsx', source, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TSX);

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

function nombreAtributo(prop: ts.JsxAttributeLike): string {
  return ts.isJsxAttribute(prop) && ts.isIdentifier(prop.name) ? prop.name.text : '';
}

// onBlur del input de la nota de alerta (aria-label "Nota de alerta para fila ...").
function onBlurNotaAlerta(): ts.ArrowFunction | undefined {
  let found: ts.ArrowFunction | undefined;
  const visit = (node: ts.Node) => {
    if (found) {
      return;
    }
    if (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) {
      const esNota = node.attributes.properties.some(
        (prop) => nombreAtributo(prop) === 'aria-label' && prop.getText().includes('Nota de alerta')
      );
      if (esNota) {
        const blur = node.attributes.properties.find(
          (prop): prop is ts.JsxAttribute => ts.isJsxAttribute(prop) && nombreAtributo(prop) === 'onBlur'
        );
        const init = blur?.initializer;
        if (init && ts.isJsxExpression(init) && init.expression && ts.isArrowFunction(init.expression)) {
          found = init.expression;
        }
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return found;
}

function sentencias(cuerpo: ts.ConciseBody): ts.Statement[] {
  return ts.isBlock(cuerpo) ? [...cuerpo.statements] : [];
}

test('una nota igual a la conocida no es un cambio', () => {
  assert.equal(notaAlertaCambiada('hola', 'hola'), false);
  assert.equal(notaAlertaCambiada('', ''), false);
  assert.equal(notaAlertaCambiada(null, ''), false);
  assert.equal(notaAlertaCambiada(undefined, ''), false);
});

test('una nota distinta si es un cambio', () => {
  assert.equal(notaAlertaCambiada('hola', 'hola '), true);
  assert.equal(notaAlertaCambiada('', 'revisar'), true);
  assert.equal(notaAlertaCambiada('hola', ''), true);
  assert.equal(notaAlertaCambiada(null, 'revisar'), true);
});

test('el onBlur de la nota de alerta solo guarda si la nota ha cambiado', () => {
  const onBlur = onBlurNotaAlerta();
  assert.ok(onBlur, 'No se encuentra el onBlur del campo de nota de alerta.');

  const guardas = sentencias(onBlur.body).filter(ts.isIfStatement);
  assert.equal(guardas.length, 1, 'El onBlur debe decidir con una unica guarda.');

  const guarda = guardas[0];
  assert.ok(
    calledIdentifiers(guarda.expression).includes('notaAlertaCambiada'),
    'La guarda del onBlur debe comparar la nota actual con la nota conocida.'
  );
  assert.ok(
    calledIdentifiers(guarda.thenStatement).includes('onToggleFlag'),
    'El PATCH de la nota debe salir de la rama que comprueba el cambio.'
  );
  if (guarda.elseStatement) {
    assert.equal(
      calledIdentifiers(guarda.elseStatement).filter((nombre) => nombre === 'onToggleFlag').length,
      0,
      'No debe haber un PATCH de la nota fuera de la guarda.'
    );
  }
});

test('el PATCH de la nota guarda el texto del campo, no el de la fila', () => {
  const onBlur = onBlurNotaAlerta();
  assert.ok(onBlur, 'No se encuentra el onBlur del campo de nota de alerta.');
  const guarda = sentencias(onBlur.body).find(ts.isIfStatement);
  assert.ok(guarda, 'El onBlur debe tener la guarda del cambio.');

  const llamadas: ts.CallExpression[] = [];
  const visit = (child: ts.Node) => {
    if (ts.isCallExpression(child) && ts.isIdentifier(child.expression) && child.expression.text === 'onToggleFlag') {
      llamadas.push(child);
    }
    ts.forEachChild(child, visit);
  };
  visit(guarda.thenStatement);

  assert.equal(llamadas.length, 1, 'El onBlur debe hacer un unico PATCH de la nota.');
  const argumentos = llamadas[0].arguments.map((arg) => arg.getText());
  assert.ok(argumentos.includes('note'), 'El PATCH debe enviar la nota editada.');
  assert.ok(!argumentos.includes('row.flagged_nota'), 'El PATCH no debe enviar la nota antigua de la fila.');
});
