import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { columnasExcluidasPorAdmin, columnasPermitidas } from '../src/utils/columnasPermitidas.js';
import {
  hayPaginaAnterior,
  hayPaginaSiguiente,
  rangoMostrado,
  textoMostrando,
  totalPaginas,
} from '../src/utils/paginacion.js';

// Lote 6 (R-3 seccion 9 + S-2): el frontend consume el contrato paginado
// ({ data, total, page, page_size, total_pages }) y la regla de columnas del
// administrador (columnas_restringidas). Los helpers puros se testean aqui y
// las pantallas se comprueban sobre su codigo fuente, porque los tests
// unitarios no tienen entorno de React.

function fuentePagina(nombre: string): string {
  return readFileSync(new URL(`../../src/pages/${nombre}`, import.meta.url), 'utf8');
}

test('paginacion: totalPaginas reparte el total real en paginas', () => {
  assert.equal(totalPaginas(0, 50), 0);
  assert.equal(totalPaginas(1, 50), 1);
  assert.equal(totalPaginas(50, 50), 1);
  assert.equal(totalPaginas(51, 50), 2);
  assert.equal(totalPaginas(1000, 200), 5);
  assert.equal(totalPaginas(10, 0), 0);
});

test('paginacion: se sabe si hay pagina anterior y siguiente', () => {
  assert.equal(hayPaginaAnterior(1), false);
  assert.equal(hayPaginaAnterior(2), true);
  assert.equal(hayPaginaSiguiente(1, 3), true);
  assert.equal(hayPaginaSiguiente(3, 3), false);
  assert.equal(hayPaginaSiguiente(4, 3), false);
});

test('paginacion: el rango mostrado acota la ultima pagina', () => {
  assert.deepEqual(rangoMostrado(1, 20, 45), { desde: 1, hasta: 20 });
  assert.deepEqual(rangoMostrado(3, 20, 45), { desde: 41, hasta: 45 });
  // Pagina mas alla del final: no inventa filas.
  assert.deepEqual(rangoMostrado(5, 20, 45), { desde: 41, hasta: 45 });
  // Sin filas no hay rango.
  assert.deepEqual(rangoMostrado(1, 20, 0), { desde: 0, hasta: 0 });
});

test('paginacion: el texto del rango sigue el formato de las pantallas', () => {
  assert.equal(textoMostrando(2, 20, 45, 'movimientos'), 'Mostrando 21-40 de 45 movimientos');
  assert.equal(textoMostrando(1, 20, 0, 'conciliaciones'), 'Mostrando 0-0 de 0 conciliaciones');
});

test('columnas: sin regla del administrador se ofrecen todas las candidatas', () => {
  assert.deepEqual(columnasPermitidas(null, ['fecha', 'concepto', 'monto']), ['fecha', 'concepto', 'monto']);
});

test('columnas: la regla del administrador recorta el conjunto y descarta lo desconocido', () => {
  assert.deepEqual(columnasPermitidas(['fecha', 'monto'], ['fecha', 'concepto', 'monto']), ['fecha', 'monto']);
  // 'inventada' no es una columna conocida: no se ofrece.
  assert.deepEqual(columnasPermitidas(['fecha', 'inventada'], ['fecha', 'concepto']), ['fecha']);
  // Una whitelist vacia no permite ninguna columna.
  assert.deepEqual(columnasPermitidas([], ['fecha', 'concepto']), []);
});

test('columnas: el resultado no tiene duplicados y no distingue mayusculas', () => {
  assert.deepEqual(columnasPermitidas(null, ['fecha', 'fecha', 'concepto']), ['fecha', 'concepto']);
  assert.deepEqual(columnasPermitidas(['FECHA'], ['fecha', 'concepto']), ['fecha']);
});

test('columnas: se listan las columnas que el administrador prohibio', () => {
  assert.deepEqual(columnasExcluidasPorAdmin(null, ['fecha', 'monto']), []);
  assert.deepEqual(columnasExcluidasPorAdmin(['fecha'], ['fecha', 'monto', 'saldo']), ['monto', 'saldo']);
});

test('ConciliacionPage consume el contrato paginado del backend', () => {
  const source = fuentePagina('ConciliacionPage.tsx');
  assert.match(source, /api\.get<PaginatedResponse<MovimientoEsperado>>\(/);
  assert.match(source, /api\.get<PaginatedResponse<Conciliacion>>\(/);
  // Los totales y el numero de paginas vienen del contrato, no del array.
  assert.match(source, /\.total\b/);
  assert.match(source, /total_pages/);
  // La pagina se pide al backend y se puede cambiar desde la pantalla.
  assert.match(source, /pageSize/);
  assert.match(source, /PageSizeSelect/);
});

test('ExtractosPage y CuentaDetailPage respetan la regla de columnas del administrador', () => {
  for (const pagina of ['ExtractosPage.tsx', 'CuentaDetailPage.tsx']) {
    const source = fuentePagina(pagina);
    // Se lee el campo real del backend: columnas_restringidas.
    assert.match(source, /columnas_restringidas/, `${pagina} debe leer columnas_restringidas`);
    // Y el conjunto ofrecido se filtra con la interseccion admin ∩ usuario.
    assert.match(source, /columnasPermitidas\(/, `${pagina} debe filtrar las columnas permitidas`);
    assert.match(source, /columnasExcluidasPorAdmin\(/, `${pagina} debe excluir las columnas prohibidas`);
  }
});
