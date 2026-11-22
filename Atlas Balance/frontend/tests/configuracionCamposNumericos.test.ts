import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';
import { parseOptionalNumberInput } from '../src/utils/numberInput.js';

// V-03.02 (F-8): los campos numericos de Configuracion usaban
// `Number(valor) || 0` (o `|| 24`, `|| 80`...): al vaciar el campo se
// guardaba un 0 silencioso en vez de dejarlo vacio.
//
// No hay DOM en este repo: la conversion se comprueba sobre el helper y el
// formulario sobre el fuente.

const source = readFileSync(new URL('../../src/pages/ConfiguracionPage.tsx', import.meta.url), 'utf8');
const sourceFile = ts.createSourceFile('ConfiguracionPage.tsx', source, ts.ScriptTarget.ES2020, true, ts.ScriptKind.TSX);

// Campos numericos afectados: todos los que convertian el texto con `Number(...) || fallback`.
const CLAVES = [
  'smtp_port',
  'saldo_bajo_cooldown_horas',
  'requests_por_minuto',
  'requests_por_hora',
  'requests_por_dia',
  'requests_globales_por_dia',
  'porcentaje_aviso_presupuesto',
  'max_input_tokens',
  'max_output_tokens',
  'max_context_rows',
  'app_update_auto_hour_utc',
] as const;

// Campo numerico (NumberField) y su clave.
function clavesDeCamposNumericos(): string[] {
  const claves: string[] = [];
  const visit = (node: ts.Node) => {
    if (
      ts.isJsxSelfClosingElement(node) &&
      ts.isIdentifier(node.tagName) &&
      node.tagName.text === 'NumberField'
    ) {
      for (const atributo of node.attributes.properties) {
        if (
          ts.isJsxAttribute(atributo) &&
          ts.isIdentifier(atributo.name) &&
          atributo.name.text === 'fieldKey'
        ) {
          if (atributo.initializer && ts.isStringLiteral(atributo.initializer)) {
            claves.push(atributo.initializer.text);
            continue;
          }
          if (
            atributo.initializer &&
            ts.isJsxExpression(atributo.initializer) &&
            atributo.initializer.expression &&
            ts.isStringLiteral(atributo.initializer.expression)
          ) {
            claves.push(atributo.initializer.expression.text);
          }
        }
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return claves;
}

test('un campo vacio NO se convierte en 0', () => {
  assert.equal(parseOptionalNumberInput(''), null);
  assert.equal(parseOptionalNumberInput('   '), null);
});

test('el cero sigue siendo un valor valido', () => {
  assert.equal(parseOptionalNumberInput('0'), 0);
});

test('un numero escrito se convierte y un texto no numerico no', () => {
  assert.equal(parseOptionalNumberInput(' 42 '), 42);
  assert.equal(parseOptionalNumberInput('-3'), -3);
  assert.equal(parseOptionalNumberInput('abc'), null);
});

test('ConfiguracionPage no convierte el texto del input en 0 al escribir', () => {
  assert.ok(
    !/Number\((e|event)\.target\.value\)\s*\|\|/.test(source),
    'ConfiguracionPage no debe convertir el valor del input con `Number(...) || fallback`: un campo vacio se convertiria en 0.'
  );

  for (const clave of CLAVES) {
    assert.ok(
      !source.includes(`${clave}: Number(`),
      `El campo ${clave} sigue asignando un numero conversion forzada al escribir.`
    );
  }
});

test('todos los campos numericos afectados usan el campo que admite texto vacio', () => {
  const claves = clavesDeCamposNumericos();
  for (const clave of CLAVES) {
    assert.ok(
      claves.includes(clave),
      `El campo ${clave} debe usar el campo numerico con texto local (NumberField) para poder vaciarse.`
    );
  }
});
