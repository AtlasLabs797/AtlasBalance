import { expect, test } from '@playwright/test';
import { existsSync } from 'node:fs';
import { installMockApi } from './mockApi';

// Prueba funcional con API simulada; no requiere credenciales ni modifica datos reales.
const windowsChrome = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
test.use({ launchOptions: { executablePath: process.env.VISUAL_CHROMIUM ?? (existsSync(windowsChrome) ? windowsChrome : undefined) } });

for (const theme of ['light', 'dark'] as const) {
  test(`extractos: alerta y barras sincronizadas (${theme})`, async ({ browser }, testInfo) => {
    const context = await browser.newContext({ viewport: { width: 1000, height: 800 }, colorScheme: theme });
    await context.addInitScript((value) => localStorage.setItem('theme', value), theme);
    const page = await context.newPage();
    await installMockApi(page);
    let isFlagged = true;
    const rowId = '00000000-0000-4000-8000-000000001000';
    await page.route('**/api/extractos**', async (route) => {
      const url = new URL(route.request().url());
      if (url.pathname === `/api/extractos/${rowId}/flag`) {
        isFlagged = route.request().postDataJSON().flagged;
        await route.fulfill({ json: {} });
      } else if (url.pathname === '/api/extractos') {
        await route.fulfill({ json: {
          data: [{ id: rowId, cuenta_id: '00000000-0000-4000-8000-000000000031', titular_id: '00000000-0000-4000-8000-000000000021', pais_id: '00000000-0000-4000-8000-000000000011', fecha: '2026-11-14', concepto: 'Prueba', comentarios: '', monto: 100, saldo: 100, fila_numero: 60, checked: false, flagged: isFlagged, flagged_nota: null, divisa: 'EUR', cuenta_nombre: 'Cuenta', banco_nombre: 'Banco', titular_nombre: 'Titular', columnas_extra: {} }],
          total: 1, page: 1, page_size: 200, total_pages: 1, columnas_disponibles: [],
        } });
      } else {
        await route.fallback();
      }
    });
    await page.goto('/extractos');
    const grid = page.locator('.extracto-table-viewport');
    const top = page.getByRole('region', { name: 'Desplazamiento horizontal superior de extractos' });
    const flagged = page.locator('.extracto-row').first();
    await expect(flagged).toBeVisible();
    await expect(flagged).toHaveClass(/flagged/);
    const colors = await flagged.evaluate((row) => ({
      actual: getComputedStyle(row).backgroundColor,
      expected: getComputedStyle(row).getPropertyValue('--status-warning-bg').trim(),
      cell: getComputedStyle(row.querySelector('.cell')!).backgroundColor,
    }));
    expect(colors.actual).not.toBe('rgba(0, 0, 0, 0)');
    expect(colors.cell).toBe(colors.actual);
    expect(colors.expected).not.toBe('');
    await flagged.scrollIntoViewIfNeeded();
    await page.screenshot({ path: testInfo.outputPath(`extractos-${theme}.png`) });
    await top.evaluate((element) => { element.scrollLeft = 300; });
    await expect.poll(() => grid.evaluate((element) => element.scrollLeft)).toBe(300);
    await grid.evaluate((element) => { element.scrollLeft = 500; });
    await expect.poll(() => top.evaluate((element) => element.scrollLeft)).toBe(500);
    await page.getByRole('button', { name: 'Edición avanzada' }).click();
    const checkbox = flagged.getByRole('checkbox', { name: /con alerta/ });
    await expect(checkbox).toBeChecked();
    await checkbox.click();
    await expect(flagged).not.toHaveClass(/flagged/);
    await expect.poll(() => flagged.evaluate((row) => getComputedStyle(row).backgroundColor)).not.toBe(colors.actual);
    await context.close();
  });
}

test('detalle de cuenta: columnas, filtros y preferencias persistidas', async ({ browser }) => {
  const accountId = '00000000-0000-4000-8000-000000000031';
  const holderId = '00000000-0000-4000-8000-000000000021';
  const countryId = '00000000-0000-4000-8000-000000000011';
  const context = await browser.newContext({ viewport: { width: 1100, height: 900 } });
  const page = await context.newPage();
  await installMockApi(page);
  let preferences: { columnas_visibles: string[] | null; orden_columnas: string[] | null } = { columnas_visibles: null, orden_columnas: null };
  const fixtureRows = ['Cobro proveedor', 'Pago alquiler'].map((concepto, index) => ({
    id: `00000000-0000-4000-8000-${String(1000 + index).padStart(12, '0')}`, cuenta_id: accountId, titular_id: holderId, pais_id: countryId,
    fecha: '2026-11-14', concepto, comentarios: '', monto: 100, saldo: 100, fila_numero: 60 - index,
    checked: false, flagged: index === 0, flagged_nota: null, divisa: 'EUR', cuenta_nombre: 'Cuenta', banco_nombre: 'Banco', titular_nombre: 'Titular',
    columnas_extra: { referencia_2: `REF-${index}` }, desglose_count: 0, desglose_total: 0, desglose_estado: 'sin_desglose',
  }));
  await page.route('**/api/extractos**', async (route) => {
    const url = new URL(route.request().url());
    const method = route.request().method();
    if (url.pathname === '/api/extractos/columnas-visibles') {
      if (method === 'PUT') {
        const body = route.request().postDataJSON();
        preferences = { columnas_visibles: body.columnas_visibles ?? null, orden_columnas: body.orden_columnas ?? null };
      }
      await route.fulfill({ json: preferences });
    } else if (url.pathname === `/api/extractos/cuentas/${accountId}/resumen`) {
      await route.fulfill({ json: { cuenta_id: accountId, cuenta_nombre: 'Cuenta', titular_id: holderId, titular_nombre: 'Titular', pais_id: countryId, pais_nombre: 'España', banco_nombre: 'Banco', divisa: 'EUR', tipo_cuenta: 'NORMAL', saldo_actual: 200, ingresos_mes: 100, egresos_mes: 100, notas: null, ultima_actualizacion: null } });
    } else if (url.pathname === '/api/extractos') {
      expect(url.searchParams.get('cuentaId')).toBe(accountId);
      const search = url.searchParams.get('search')?.toLowerCase() ?? '';
      const rows = fixtureRows.filter((row) => row.concepto.toLowerCase().includes(search));
      await route.fulfill({ json: { data: rows, total: rows.length, page: 1, page_size: 200, total_pages: 1, columnas_disponibles: ['referencia_2'] } });
    } else {
      await route.fallback();
    }
  });
  await page.goto(`/cuentas/${accountId}`);
  const grid = page.locator('.extracto-table-viewport');
  await expect(grid.getByRole('columnheader', { name: 'Concepto' })).toBeVisible();
  for (const label of ['Cuenta', 'Banco', 'Titular']) {
    await expect(grid.getByRole('columnheader', { name: label, exact: true })).toHaveCount(0);
  }
  await expect(grid.getByText('REF-0')).toBeVisible();
  await page.getByRole('button', { name: 'Columnas' }).click();
  const panel = page.getByRole('group', { name: 'Columnas visibles' });
  for (const label of ['Cuenta', 'Banco', 'Titular']) {
    await expect(panel.getByRole('checkbox', { name: label, exact: true })).toHaveCount(0);
  }
  const changed = () => page.waitForResponse((response) => response.url().includes('/extractos/columnas-visibles') && response.request().method() === 'PUT');
  const orderSave = changed();
  await panel.getByRole('button', { name: 'Subir Concepto' }).click();
  await orderSave;
  const hideSave = changed();
  await panel.getByRole('checkbox', { name: 'Concepto', exact: true }).uncheck();
  await hideSave;
  await page.reload();
  await expect(grid.getByRole('columnheader', { name: 'Concepto' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Columnas' }).click();
  const showSave = changed();
  await panel.getByRole('checkbox', { name: 'Concepto', exact: true }).check();
  await showSave;
  const headers = grid.locator('.extracto-table-head-row [role="columnheader"]');
  await expect.poll(async () => headers.allTextContents()).toEqual(expect.arrayContaining([expect.stringContaining('Concepto')]));
  const headerText = await headers.allTextContents();
  expect(headerText.findIndex((text) => text.includes('Concepto'))).toBeLessThan(headerText.findIndex((text) => text.includes('Divisa')));
  await page.getByRole('searchbox', { name: /Buscar por concepto o comentario/i }).fill('alquiler');
  await expect(grid.getByText('Pago alquiler')).toBeVisible();
  await expect(grid.getByText('Cobro proveedor')).toHaveCount(0);
  await page.getByRole('searchbox', { name: /Buscar por concepto o comentario/i }).fill('');
  await expect(grid.getByText('Cobro proveedor')).toBeVisible();
  const selected = grid.getByRole('checkbox', { name: 'Seleccionar movimiento 59' });
  await selected.check();
  await expect(selected).toBeChecked();
  await grid.getByLabel('Filtrar por Concepto').fill('proveedor');
  await expect(grid.getByText('Pago alquiler')).toHaveCount(0);
  await expect(grid.getByText('Cobro proveedor')).toBeVisible();
  await grid.getByLabel('Filtrar por Concepto').fill('');
  await expect(grid.getByText('Pago alquiler')).toBeVisible();
  await expect(grid.getByRole('checkbox', { name: 'Seleccionar movimiento 59' })).not.toBeChecked();
  const top = page.getByRole('region', { name: 'Desplazamiento horizontal superior de extractos' });
  await top.evaluate((element) => { element.scrollLeft = 200; });
  await expect.poll(() => grid.evaluate((element) => element.scrollLeft)).toBe(200);
  await grid.evaluate((element) => { element.scrollLeft = 400; });
  await expect.poll(() => top.evaluate((element) => element.scrollLeft)).toBe(400);
  await context.close();
});

for (const view of ['extractos', 'cuenta'] as const) {
  test(`${view}: bloquea cambios simultáneos mientras guarda preferencias`, async ({ browser }) => {
    const accountId = '00000000-0000-4000-8000-000000000031';
    const holderId = '00000000-0000-4000-8000-000000000021';
    const countryId = '00000000-0000-4000-8000-000000000011';
    const context = await browser.newContext({ viewport: { width: 1100, height: 900 } });
    const page = await context.newPage();
    await installMockApi(page);
    let preferences: { columnas_visibles: string[] | null; orden_columnas: string[] | null } = { columnas_visibles: null, orden_columnas: null };
    let requests = 0;
    let release!: () => void;
    const delayed = new Promise<void>((resolve) => { release = resolve; });
    await page.route('**/api/extractos**', async (route) => {
      const url = new URL(route.request().url());
      if (url.pathname === '/api/extractos/columnas-visibles') {
        if (route.request().method() === 'PUT') {
          requests += 1;
          const body = route.request().postDataJSON();
          if (requests === 1) await delayed;
          preferences = { columnas_visibles: body.columnas_visibles ?? null, orden_columnas: body.orden_columnas ?? null };
        }
        await route.fulfill({ json: preferences });
      } else if (url.pathname === `/api/extractos/cuentas/${accountId}/resumen`) {
        await route.fulfill({ json: { cuenta_id: accountId, cuenta_nombre: 'Cuenta', titular_id: holderId, titular_nombre: 'Titular', pais_id: countryId, pais_nombre: 'España', banco_nombre: 'Banco', divisa: 'EUR', tipo_cuenta: 'NORMAL', saldo_actual: 200, ingresos_mes: 100, egresos_mes: 100, notas: null, ultima_actualizacion: null } });
      } else {
        await route.fallback();
      }
    });
    try {
      await page.goto(view === 'cuenta' ? `/cuentas/${accountId}` : '/extractos');
      await page.getByRole('button', { name: 'Columnas' }).click();
      const panel = page.getByRole('group', { name: 'Columnas visibles' });
      const concept = panel.getByRole('checkbox', { name: 'Concepto', exact: true });
      await expect(concept).toBeEnabled();
      await concept.uncheck();
      await expect.poll(() => requests).toBe(1);
      await expect(panel.getByRole('checkbox', { name: 'Fecha', exact: true })).toBeDisabled();
      await expect(panel.getByRole('button', { name: 'Subir Concepto' })).toBeDisabled();
      expect(requests).toBe(1);
      release();
      await expect(panel.getByRole('checkbox', { name: 'Fecha', exact: true })).toBeEnabled();
      const nextResponse = page.waitForResponse((response) => response.url().includes('/extractos/columnas-visibles') && response.request().method() === 'PUT');
      await panel.getByRole('checkbox', { name: 'Fecha', exact: true }).uncheck();
      await nextResponse;
      await expect.poll(() => requests).toBe(2);
      await page.reload();
      await page.getByRole('button', { name: 'Columnas' }).click();
      await expect(panel.getByRole('checkbox', { name: 'Concepto', exact: true })).not.toBeChecked();
      await expect(panel.getByRole('checkbox', { name: 'Fecha', exact: true })).not.toBeChecked();
    } finally {
      release();
      await context.close();
    }
  });
}
