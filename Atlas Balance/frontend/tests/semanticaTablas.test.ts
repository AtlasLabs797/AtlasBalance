import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

// V-03.02 (LOTE 8, D-10 y cierre de D-3).
//
// D-10 (semantica de tablas y jerarquia de encabezados):
// - Ningun <th> puede ir sin `scope` (cabecera de columna o de fila).
// - Toda <table> necesita nombre accesible: <caption> (visible o `sr-only`)
//   o, en las tablas de maquetado, `aria-label` en el propio <table>.
// - La jerarquia de encabezados de cada fichero .tsx no puede saltar un nivel:
//   h1 -> h2 -> h3. Nunca h1 -> h3.
//
// Cierre de D-3: no queda ningun `<p>Cargando...</p>` suelto en src/; el
// estado de carga lo lleva el componente comun <PageSkeleton>.
//
// Test de FUENTE, no de DOM: en este repo los tests unitarios no montan React,
// asi que se comprueba el codigo fuente.

const SRC = fileURLToPath(new URL('../../src/', import.meta.url));

/** Ficheros .tsx/.ts de src/, en orden estable. */
function ficherosSrc(): string[] {
  const encontrados: string[] = [];
  const recorrer = (directorio: string) => {
    for (const entrada of readdirSync(directorio, { withFileTypes: true })) {
      const completa = path.join(directorio, entrada.name);
      if (entrada.isDirectory()) {
        recorrer(completa);
        continue;
      }
      if (entrada.isFile() && (entrada.name.endsWith('.tsx') || entrada.name.endsWith('.ts'))) {
        encontrados.push(path.relative(SRC, completa).split(path.sep).join('/'));
      }
    }
  };
  recorrer(SRC);
  return encontrados.sort();
}

function fuente(relativa: string): string {
  return readFileSync(path.join(SRC, relativa), 'utf8');
}

/**
 * Abre el elemento `<table ...>` que empieza en `indice` y devuelve el trozo de
 * codigo hasta su cierre, para poder mirar si lleva `<caption>` o `aria-label`.
 */
function bloqueDeTabla(contenido: string, indice: number): string {
  const fin = contenido.indexOf('</table>', indice);
  return fin === -1 ? contenido.slice(indice) : contenido.slice(indice, fin);
}

test('ningun <th> se queda sin scope', () => {
  const fallos: string[] = [];

  for (const fichero of ficherosSrc()) {
    const contenido = fuente(fichero);
    for (const coincidencia of contenido.matchAll(/<th\b([^>]*)>/g)) {
      if (!/\bscope\s*=/.test(coincidencia[1])) {
        const linea = contenido.slice(0, coincidencia.index).split('\n').length;
        fallos.push(`${fichero}:${linea}  <th${coincidencia[1]}>`);
      }
    }
  }

  assert.deepEqual(fallos, [], `Cabeceras de tabla sin scope:\n${fallos.join('\n')}`);
});

test('toda <table> tiene nombre accesible (caption o aria-label)', () => {
  const fallos: string[] = [];

  for (const fichero of ficherosSrc()) {
    const contenido = fuente(fichero);
    for (const coincidencia of contenido.matchAll(/<table\b([^>]*)>/g)) {
      const bloque = bloqueDeTabla(contenido, coincidencia.index);
      const tieneCaption = /<caption\b[^>]*>/.test(bloque);
      const tieneAriaLabel = /\baria-label\s*=/.test(coincidencia[1]);
      if (!tieneCaption && !tieneAriaLabel) {
        const linea = contenido.slice(0, coincidencia.index).split('\n').length;
        fallos.push(`${fichero}:${linea}  <table${coincidencia[1]}>`);
      }
    }
  }

  assert.deepEqual(fallos, [], `Tablas sin nombre accesible:\n${fallos.join('\n')}`);
});

test('no queda ningun <p>Cargando...</p> en src/', () => {
  const fallos: string[] = [];

  for (const fichero of ficherosSrc()) {
    const contenido = fuente(fichero);
    for (const coincidencia of contenido.matchAll(/<(p|div)\b[^>]*>\s*Cargando/gi)) {
      const linea = contenido.slice(0, coincidencia.index).split('\n').length;
      fallos.push(`${fichero}:${linea}  ${coincidencia[0].replace(/\s+/g, ' ')}`);
    }
  }

  assert.deepEqual(
    fallos,
    [],
    `Estados de carga sin migrar a <PageSkeleton>:\n${fallos.join('\n')}`
  );
});

test('la jerarquia de encabezados no salta niveles', () => {
  const fallos: string[] = [];

  for (const fichero of ficherosSrc().filter((nombre) => nombre.endsWith('.tsx'))) {
    const contenido = fuente(fichero);

    // Secuencia de encabezados en orden de aparicion. `<Modal` cuenta como un
    // h2 implicito: el componente comun pinta el titulo del modal en un <h2>,
    // asi que los encabezados de dentro del modal cuelgan de ese h2 y no
    // saltan desde el h1 de la pagina.
    const secuencia: { nivel: number; linea: number }[] = [];
    const marca = /<Modal\b|<h([1-6])\b/g;
    let coincidencia = marca.exec(contenido);
    while (coincidencia) {
      const linea = contenido.slice(0, coincidencia.index).split('\n').length;
      const nivel = coincidencia[1] ? Number(coincidencia[1]) : 2;
      secuencia.push({ nivel, linea });
      coincidencia = marca.exec(contenido);
    }

    for (let indice = 1; indice < secuencia.length; indice += 1) {
      const anterior = secuencia[indice - 1];
      const actual = secuencia[indice];
      if (actual.nivel > anterior.nivel + 1) {
        fallos.push(
          `${fichero}:${actual.linea}  salta de h${anterior.nivel} (linea ${anterior.linea}) a h${actual.nivel}`
        );
      }
    }
  }

  assert.deepEqual(fallos, [], `Saltos en la jerarquia de encabezados:\n${fallos.join('\n')}`);
});
