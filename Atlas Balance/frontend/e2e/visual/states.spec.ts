import { appendFileSync, existsSync, mkdirSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Browser, type Locator, type Page } from '@playwright/test';
import { dashboardPrincipalVacio, installMockApi, type MockApiOptions } from './mockApi';

// Estados e interacciones (dialogos de confirmacion, modales, densidad, tablas
// apiladas en movil, errores de dashboard, contraste del boton primario).
// VISUAL_OUT  carpeta de salida de capturas + metrics.log (por defecto test-results/visual-states)
const OUT = process.env.VISUAL_OUT ?? join('test-results', 'visual-states');
mkdirSync(OUT, { recursive: true });
const METRICS = join(OUT, 'metrics.log');

function metric(name: string, value: unknown) {
  const line = `[states] ${name}: ${typeof value === 'string' ? value : JSON.stringify(value)}`;
  console.log(line);
  appendFileSync(METRICS, line + '\n');
}

function findChromium(): string | undefined {
  if (process.env.VISUAL_CHROMIUM) return process.env.VISUAL_CHROMIUM;
  const base = join(process.env.LOCALAPPDATA ?? '', 'ms-playwright');
  if (!existsSync(base)) return undefined;
  const dirs = readdirSync(base).filter((d) => d.startsWith('chromium_headless_shell-')).sort().reverse();
  for (const d of dirs) {
    const exe = join(base, d, 'chrome-headless-shell-win64', 'chrome-headless-shell.exe');
    if (existsSync(exe)) return exe;
  }
  return undefined;
}
test.use({ launchOptions: { executablePath: findChromium() } });
test.setTimeout(120_000);

interface CtxOptions {
  theme?: 'light' | 'dark';
  w?: number;
  h?: number;
  density?: 'comodo' | 'compacto';
  api?: MockApiOptions;
}

async function open(browser: Browser, opts: CtxOptions = {}) {
  const { theme = 'light', w = 1440, h = 900, density, api } = opts;
  const context = await browser.newContext({
    viewport: { width: w, height: h },
    colorScheme: theme,
    locale: 'es-ES',
    reducedMotion: 'reduce',
  });
  await context.addInitScript(([t, d]) => {
    try {
      window.localStorage.setItem('theme', t as string);
      if (d) window.localStorage.setItem('density', d as string);
    } catch { /* ignore */ }
  }, [theme, density ?? null]);
  const page = await context.newPage();
  await installMockApi(page, api);
  return { context, page };
}

async function shoot(page: Page, name: string) {
  await page.waitForLoadState('networkidle').catch(() => undefined);
  await page.waitForTimeout(500);
  await page.screenshot({ path: join(OUT, `${name}.png`), fullPage: false });
}

async function openIntegraciones(page: Page) {
  await page.goto('/configuracion');
  await page.locator('#config-tab-integraciones').click();
  await expect(page.getByRole('heading', { name: 'Tokens existentes' })).toBeVisible();
}

// ---------------------------------------------------------------------------
// 1) Dialogos de confirmacion por nivel
// ---------------------------------------------------------------------------

type Tone = 'normal' | 'reversible' | 'irreversible';

async function exerciseConfirm(
  page: Page,
  trigger: Locator,
  spec: { tone: Tone; title: string; shot: string },
) {
  const dialog = page.getByRole('dialog', { name: spec.title });
  const irreversibleText = dialog.getByText('Esta acción no se puede deshacer');

  await trigger.click();
  await expect(dialog, `[${spec.tone}] debe abrirse role=dialog "${spec.title}"`).toBeVisible();
  await expect(dialog, `[${spec.tone}] nombre accesible del dialogo`).toHaveAccessibleName(spec.title);
  await expect(dialog).toHaveAttribute('aria-modal', 'true');
  if (spec.tone === 'irreversible') {
    await expect(irreversibleText, 'irreversible debe mostrar "Esta acción no se puede deshacer"').toHaveCount(1);
  } else {
    await expect(irreversibleText, `${spec.tone} NO debe mostrar "Esta acción no se puede deshacer"`).toHaveCount(0);
  }
  const confirmBtn = dialog.locator('.confirm-action');
  const confirmClass = await confirmBtn.getAttribute('class');
  metric(`confirm ${spec.tone} boton`, confirmClass ?? '');
  await shoot(page, spec.shot);

  // Escape cierra y el foco vuelve al disparador
  await page.keyboard.press('Escape');
  await expect(dialog, 'Escape debe cerrar el dialogo').toBeHidden();
  await expect(trigger, 'tras Escape el foco debe volver al disparador').toBeFocused();

  // Clic en el fondo cierra y devuelve el foco
  await trigger.click();
  await expect(dialog).toBeVisible();
  await page.locator('.modal-backdrop').last().click({ position: { x: 4, y: 4 } });
  await expect(dialog, 'clic en el fondo debe cerrar el dialogo').toBeHidden();
  await expect(trigger, 'tras clic en el fondo el foco debe volver al disparador').toBeFocused();
}

test('1a dialogo normal (Conciliacion > Conciliar)', async ({ browser }) => {
  const { context, page } = await open(browser);
  await page.goto('/conciliacion');
  const trigger = page.getByRole('button', { name: 'Conciliar', exact: true });
  await expect(trigger, 'fixture de conciliacion debe mostrar el boton Conciliar').toBeVisible();
  await exerciseConfirm(page, trigger, { tone: 'normal', title: 'Conciliar movimiento', shot: 'confirm-normal-light-1440' });
  await context.close();
});

test('1b dialogo reversible (Backups > Desvincular Google Drive)', async ({ browser }) => {
  const { context, page } = await open(browser);
  await page.goto('/backups');
  const trigger = page.getByRole('button', { name: 'Desvincular', exact: true });
  await expect(trigger).toBeVisible();
  await expect(trigger, 'el fixture marca Drive como conectado: Desvincular debe estar habilitado').toBeEnabled();
  await exerciseConfirm(page, trigger, { tone: 'reversible', title: 'Desvincular Google Drive', shot: 'confirm-reversible-light-1440' });
  await context.close();
});

test('1c dialogo irreversible, Rotar token (Configuracion > Integraciones)', async ({ browser }) => {
  // Rotar usa tone irreversible en ConfiguracionPage.rotateToken.
  const { context, page } = await open(browser);
  await openIntegraciones(page);
  const trigger = page.getByRole('button', { name: 'Rotar token Token lectura demo' });
  await expect(trigger).toBeVisible();
  await exerciseConfirm(page, trigger, { tone: 'irreversible', title: 'Rotar token', shot: 'confirm-rotar-token-light-1440' });
  await context.close();
});

test('1d dialogo irreversible, Revocar token (claro 1440 y 390)', async ({ browser }) => {
  const { context, page } = await open(browser);
  await openIntegraciones(page);
  const trigger = page.getByRole('button', { name: 'Revocar token Token lectura demo' });
  await expect(trigger).toBeVisible();
  await exerciseConfirm(page, trigger, { tone: 'irreversible', title: 'Revocar token', shot: 'confirm-irreversible-revocar-light-1440' });
  await context.close();

  const m = await open(browser, { w: 390, h: 844 });
  await openIntegraciones(m.page);
  const mTrigger = m.page.getByRole('button', { name: 'Revocar token Token lectura demo' });
  await mTrigger.scrollIntoViewIfNeeded();
  await mTrigger.click();
  const dialog = m.page.getByRole('dialog', { name: 'Revocar token' });
  await expect(dialog).toBeVisible();
  await shoot(m.page, 'confirm-irreversible-revocar-light-390');
  const box = await dialog.boundingBox();
  metric('confirm irreversible 390 caja dialogo', box);
  expect(box && box.x >= 0 && box.x + box.width <= 390 + 0.5, 'el dialogo debe caber en 390px de ancho').toBeTruthy();
  await m.context.close();
});

test('1e dialogo irreversible, Eliminar token (claro)', async ({ browser }) => {
  const { context, page } = await open(browser);
  await openIntegraciones(page);
  const trigger = page.getByRole('button', { name: 'Eliminar token Token lectura demo' });
  await exerciseConfirm(page, trigger, { tone: 'irreversible', title: 'Eliminar token', shot: 'confirm-irreversible-eliminar-light-1440' });
  await context.close();
});

test('1f Revocar token: una sola confirmacion (sin doble dialogo) y no cierra mientras carga', async ({ browser }) => {
  // TokenList abre su propio ConfirmDialog y, al confirmar, onRevocar vuelve a pedir
  // confirmacion en ConfiguracionPage.revokeToken. Esperado: UN dialogo.
  const { context, page } = await open(browser);
  await openIntegraciones(page);
  // Retrasa el POST para observar el estado "cargando"
  await page.route('**/integraciones/tokens/*/revocar', async (route) => {
    await new Promise((r) => setTimeout(r, 2500));
    await route.fallback();
  });
  await page.getByRole('button', { name: 'Revocar token Token lectura demo' }).click();
  const first = page.getByRole('dialog', { name: 'Revocar token' });
  await expect(first).toBeVisible();
  await first.locator('.confirm-action').click();
  await page.waitForTimeout(600);
  const dialogsAfterConfirm = await page.getByRole('dialog').count();
  const confirmBtnsVisible = await page.locator('.confirm-action').count();
  metric('revocar tras primer Confirmar: dialogos abiertos', dialogsAfterConfirm);
  metric('revocar tras primer Confirmar: botones .confirm-action', confirmBtnsVisible);
  await shoot(page, 'confirm-revocar-tras-confirmar-light-1440');
  // Una sola confirmacion: tras confirmar no queda ningun dialogo "Revocar token" abierto
  // (TokenList pide la confirmacion y ConfiguracionPage.revokeToken ya no la repite).
  await expect(page.getByRole('dialog', { name: 'Revocar token' }), 'DEFECTO APP: doble confirmacion al revocar token').toHaveCount(0, { timeout: 5000 });
  await expect(page.getByText('Token revocado.')).toBeVisible({ timeout: 8000 });
  await context.close();
});

// FIXME: el retraso del POST no llega a aplicarse con el mock actual (la revocacion es inmediata), asi que no se llega al estado busy; ConfirmDialog/Modal no cambian ese comportamiento (loading bloquea Escape y fondo). Pendiente de un mock con latencia.
test.fixme('1g Revocar token: Escape y clic en fondo NO cierran mientras carga (busy)', async ({ browser }) => {
  const { context, page } = await open(browser);
  await openIntegraciones(page);
  await page.route('**/integraciones/tokens/*/revocar', async (route) => {
    await new Promise((r) => setTimeout(r, 3000));
    await route.fallback();
  });
  await page.getByRole('button', { name: 'Revocar token Token lectura demo' }).click();
  const dialogs = page.getByRole('dialog', { name: 'Revocar token' });
  await dialogs.first().locator('.confirm-action').click();
  // Si la app pide una segunda confirmacion (ver 1f), la aceptamos para llegar al estado busy.
  await page.waitForTimeout(400);
  const secondConfirm = page.locator('.modal-backdrop').last().locator('.confirm-action');
  if ((await dialogs.count()) > 0 && (await secondConfirm.count()) > 0) {
    const txt = (await secondConfirm.first().textContent())?.trim();
    if (txt === 'Revocar token') await secondConfirm.first().click();
  }
  // Ahora la peticion esta en vuelo: busy=true => el dialogo de TokenList sigue abierto con loadingLabel
  const loadingBtn = page.getByRole('button', { name: 'Revocando...' });
  await expect(loadingBtn, 'mientras carga el boton debe mostrar "Revocando..."').toBeVisible({ timeout: 4000 });
  await shoot(page, 'confirm-irreversible-cargando-light-1440');
  await page.keyboard.press('Escape');
  await page.waitForTimeout(200);
  await expect(loadingBtn, 'Escape NO debe cerrar el dialogo mientras carga').toBeVisible();
  await page.locator('.modal-backdrop').last().click({ position: { x: 4, y: 4 } });
  await page.waitForTimeout(200);
  await expect(loadingBtn, 'clic en el fondo NO debe cerrar el dialogo mientras carga').toBeVisible();
  await context.close();
});

// ---------------------------------------------------------------------------
// 2) Modales migrados: Desglose y CreateTokenModal
// ---------------------------------------------------------------------------

test('2a CreateTokenModal: captura, Escape, foco y clic en fondo', async ({ browser }) => {
  const { context, page } = await open(browser);
  await openIntegraciones(page);
  const trigger = page.getByRole('button', { name: 'Crear token', exact: true });
  await trigger.click();
  const dialog = page.getByRole('dialog', { name: 'Crear token OpenClaw' });
  await expect(dialog).toBeVisible();
  await expect(dialog).toHaveAccessibleName('Crear token OpenClaw');
  await shoot(page, 'modal-crear-token-light-1440');
  await page.keyboard.press('Escape');
  await expect(dialog, 'Escape debe cerrar CreateTokenModal').toBeHidden();
  await expect(trigger, 'foco vuelve a "Crear token" tras Escape').toBeFocused();
  await trigger.click();
  await expect(dialog).toBeVisible();
  await page.locator('.modal-backdrop').last().click({ position: { x: 4, y: 4 } });
  await expect(dialog, 'clic en fondo cierra CreateTokenModal').toBeHidden();
  await expect(trigger, 'foco vuelve a "Crear token" tras clic en fondo').toBeFocused();
  await context.close();

  const m = await open(browser, { w: 390, h: 844 });
  await openIntegraciones(m.page);
  await m.page.getByRole('button', { name: 'Crear token', exact: true }).click();
  await expect(m.page.getByRole('dialog', { name: 'Crear token OpenClaw' })).toBeVisible();
  await shoot(m.page, 'modal-crear-token-light-390');
  await m.context.close();
});

test('2b Desglose: captura, Escape, foco, clic en fondo', async ({ browser }) => {
  const { context, page } = await open(browser);
  await page.goto('/extractos');
  const trigger = page.getByRole('button', { name: /Abrir desglose de fila/ }).first();
  await expect(trigger, 'la tabla de extractos debe mostrar botones de desglose').toBeVisible();
  await trigger.click();
  const dialog = page.getByRole('dialog', { name: 'Desglose del extracto' });
  await expect(dialog).toBeVisible();
  await expect(dialog).toHaveAccessibleName('Desglose del extracto');
  await shoot(page, 'modal-desglose-light-1440');
  await page.keyboard.press('Escape');
  await expect(dialog, 'Escape (sin cambios) cierra el Desglose').toBeHidden();
  await expect(trigger, 'foco vuelve al boton de desglose tras Escape').toBeFocused();
  await trigger.click();
  await expect(dialog).toBeVisible();
  await page.locator('.modal-backdrop').last().click({ position: { x: 4, y: 4 } });
  await expect(dialog, 'clic en fondo (sin cambios) cierra el Desglose').toBeHidden();
  await expect(trigger, 'foco vuelve al boton de desglose tras clic en fondo').toBeFocused();
  await context.close();
});

test('2c Desglose con cambios: apilado Escape -> ConfirmDialog -> Escape', async ({ browser }) => {
  const { context, page } = await open(browser);
  await page.goto('/extractos');
  // El desglose solo es editable en modo "Edición avanzada" (ExtractosPage.canEditCell).
  await page.getByRole('button', { name: 'Edición avanzada' }).click();
  const trigger = page.getByRole('button', { name: /Abrir desglose de fila/ }).first();
  await expect(trigger).toBeVisible();
  await trigger.click();
  const desglose = page.getByRole('dialog', { name: 'Desglose del extracto' });
  const confirmar = page.getByRole('dialog', { name: 'Cerrar desglose' });
  await expect(desglose).toBeVisible();
  const addLine = page.getByRole('button', { name: 'Añadir línea' });
  await expect(addLine, 'el usuario admin debe poder editar el desglose').toBeEnabled();
  await addLine.click();
  await page.getByLabel('Persona de la linea 1').fill('Persona Demo');

  await page.keyboard.press('Escape');
  await expect(confirmar, 'con cambios, Escape abre el ConfirmDialog "Cerrar desglose"').toBeVisible();
  await expect(desglose, 'el Desglose sigue abierto debajo').toBeVisible();
  await shoot(page, 'modal-desglose-confirmar-cierre-light-1440');
  metric('apilado tras 1er Escape', { dialogos: await page.getByRole('dialog').count() });

  await page.keyboard.press('Escape');
  await page.waitForTimeout(300);
  const confirmarVisible = await confirmar.isVisible();
  const desgloseVisible = await desglose.isVisible();
  metric('apilado tras 2o Escape', { confirmarVisible, desgloseVisible, dialogos: await page.getByRole('dialog').count() });
  await shoot(page, 'modal-desglose-tras-segundo-escape-light-1440');
  expect(confirmarVisible, 'el 2o Escape debe cerrar el ConfirmDialog').toBe(false);
  expect(desgloseVisible, 'DEFECTO APP: el 2o Escape cerro tambien el Desglose (cambios perdidos)').toBe(true);

  // El Desglose debe seguir operativo: otro Escape vuelve a pedir confirmacion
  await page.keyboard.press('Escape');
  await expect(confirmar, 'otro Escape vuelve a pedir confirmacion (estado coherente)').toBeVisible();
  // Clic en el fondo del ConfirmDialog cierra solo el ConfirmDialog
  await page.locator('.modal-backdrop').last().click({ position: { x: 4, y: 4 } });
  await expect(confirmar, 'clic en el fondo cierra el ConfirmDialog').toBeHidden();
  await expect(desglose, 'clic en el fondo del ConfirmDialog no cierra el Desglose').toBeVisible();
  // Foco tras cerrar el ConfirmDialog
  const active = await page.evaluate(() => {
    const a = document.activeElement as HTMLElement | null;
    return a ? `${a.tagName.toLowerCase()}${a.getAttribute('aria-label') ? `[aria-label=${a.getAttribute('aria-label')}]` : ''}:${(a.textContent ?? '').trim().slice(0, 30)}` : 'null';
  });
  metric('foco tras cerrar ConfirmDialog apilado', active);
  const insideDesglose = await desglose.evaluate((el) => el.contains(document.activeElement));
  expect(insideDesglose, 'el foco debe quedar dentro del Desglose tras cerrar el ConfirmDialog').toBe(true);
  // "Cerrar sin guardar" cierra ambos y devuelve foco al disparador
  await page.keyboard.press('Escape');
  await expect(confirmar).toBeVisible();
  await confirmar.getByRole('button', { name: 'Cerrar sin guardar' }).click();
  await expect(desglose).toBeHidden();
  await expect(trigger, 'foco vuelve al boton de desglose tras descartar').toBeFocused();
  await context.close();
});

// ---------------------------------------------------------------------------
// 3) Densidad
// ---------------------------------------------------------------------------

async function measureRows(page: Page) {
  return page.evaluate(async () => {
    const vp = document.querySelector<HTMLElement>('.extracto-table-viewport');
    if (!vp) return { error: 'sin .extracto-table-viewport' };
    const snap = () => {
      const items = [...vp.querySelectorAll<HTMLElement>('.extracto-virtual-item')]
        .map((el) => el.getBoundingClientRect())
        .sort((a, b) => a.top - b.top);
      const rows = [...vp.querySelectorAll<HTMLElement>('.extracto-row')].map((el) => el.getBoundingClientRect().height);
      const issues: string[] = [];
      for (let i = 1; i < items.length; i += 1) {
        const delta = items[i].top - items[i - 1].bottom;
        if (Math.abs(delta) > 0.75) issues.push(`#${i} delta=${delta.toFixed(2)}`);
      }
      return { n: items.length, rowAvg: rows.reduce((a, b) => a + b, 0) / Math.max(rows.length, 1), rowMin: Math.min(...rows), rowMax: Math.max(...rows), itemH: items.length ? items[0].height : 0, issues };
    };
    const out: Record<string, unknown> = { scrollHeight: vp.scrollHeight, clientHeight: vp.clientHeight };
    const positions: Array<[string, number]> = [['top', 0], ['mid', Math.max(0, (vp.scrollHeight - vp.clientHeight) / 2)], ['end', vp.scrollHeight]];
    for (const [label, y] of positions) {
      vp.scrollTop = y;
      await new Promise((r) => setTimeout(r, 400));
      out[label] = { scrollTop: Math.round(vp.scrollTop), ...snap() };
    }
    vp.scrollTop = 0;
    return out;
  });
}

for (const density of ['compacto', 'comodo'] as const) {
  test(`3 densidad ${density}: altura de fila y virtualizacion sin solapes`, async ({ browser }) => {
    const { context, page } = await open(browser, { density });
    await page.goto('/extractos');
    await expect(page.locator('.extracto-row').first(), 'la tabla de extractos debe renderizar filas').toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('data-density', density);
    await shoot(page, `extractos-${density}-light-1440`);
    const expected = density === 'compacto' ? 36 : 44;
    const m = (await measureRows(page)) as Record<string, unknown>;
    metric(`densidad ${density} medidas`, m);
    for (const pos of ['top', 'mid', 'end']) {
      const s = m[pos];
      expect(s.n, `${density}/${pos}: debe haber filas visibles`).toBeGreaterThan(0);
      expect(Math.abs(s.rowAvg - expected), `${density}/${pos}: altura media de .extracto-row=${s.rowAvg} (esperado ~${expected})`).toBeLessThanOrEqual(2);
      expect(s.issues, `${density}/${pos}: filas solapadas o con huecos: ${JSON.stringify(s.issues)}`).toEqual([]);
    }
    // Scroll real con la rueda (no solo scrollTop)
    const vpBox = await page.locator('.extracto-table-viewport').boundingBox();
    if (vpBox) {
      const wheelY = Math.min(vpBox.y + vpBox.height / 2, 800);
      metric(`densidad ${density} caja viewport`, vpBox);
      await page.mouse.move(vpBox.x + vpBox.width / 2, Math.max(wheelY, vpBox.y + 150));
      await page.mouse.wheel(0, 700);
      await page.waitForTimeout(500);
      const afterWheel = (await measureRowsNoScroll(page));
      metric(`densidad ${density} tras rueda`, afterWheel);
      expect(afterWheel.scrollTop, `${density}: la rueda sobre la rejilla debe desplazar el viewport virtualizado`).toBeGreaterThan(0);
      expect(afterWheel.issues, `${density} tras rueda: solapes/huecos ${JSON.stringify(afterWheel.issues)}`).toEqual([]);
      await shoot(page, `extractos-${density}-scrolled-light-1440`);
    }
    await context.close();
  });
}

async function measureRowsNoScroll(page: Page) {
  return page.evaluate(() => {
    const vp = document.querySelector<HTMLElement>('.extracto-table-viewport')!;
    const items = [...vp.querySelectorAll<HTMLElement>('.extracto-virtual-item')].map((el) => el.getBoundingClientRect()).sort((a, b) => a.top - b.top);
    const issues: string[] = [];
    for (let i = 1; i < items.length; i += 1) {
      const delta = items[i].top - items[i - 1].bottom;
      if (Math.abs(delta) > 0.75) issues.push(`#${i} delta=${delta.toFixed(2)}`);
    }
    return { scrollTop: Math.round(vp.scrollTop), n: items.length, issues };
  });
}

test('3b selector Comodo/Compacto cambia data-density y la altura de fila', async ({ browser }) => {
  const { context, page } = await open(browser);
  await page.goto('/extractos');
  await expect(page.locator('.extracto-row').first()).toBeVisible();
  const html = page.locator('html');
  await expect(html).toHaveAttribute('data-density', 'comodo');
  const avg = () => page.evaluate(() => {
    const rows = [...document.querySelectorAll<HTMLElement>('.extracto-row')].map((el) => el.getBoundingClientRect().height);
    return rows.reduce((a, b) => a + b, 0) / Math.max(rows.length, 1);
  });
  const antes = await avg();
  await page.getByRole('radio', { name: 'Compacto' }).click();
  await expect(html, 'clic en Compacto debe poner data-density=compacto en <html>').toHaveAttribute('data-density', 'compacto');
  await page.waitForTimeout(400);
  const compacto = await avg();
  const stored = await page.evaluate(() => window.localStorage.getItem('density'));
  await page.getByRole('radio', { name: 'Cómodo' }).click();
  await expect(html, 'clic en Cómodo debe volver a data-density=comodo').toHaveAttribute('data-density', 'comodo');
  await page.waitForTimeout(400);
  const comodo = await avg();
  metric('selector densidad: alto medio fila comodo/compacto/comodo', { antes, compacto, comodo, localStorage: stored });
  expect(stored, 'localStorage density tras elegir Compacto').toBe('compacto');
  expect(Math.abs(compacto - 36), `alto medio compacto=${compacto}`).toBeLessThanOrEqual(2);
  expect(Math.abs(comodo - 44), `alto medio comodo=${comodo}`).toBeLessThanOrEqual(2);
  await context.close();
});

// ---------------------------------------------------------------------------
// 4) Tablas apiladas en movil: semantica real (CDP) vs. getByRole
// ---------------------------------------------------------------------------

async function axCounts(page: Page) {
  const client = await page.context().newCDPSession(page);
  await client.send('Accessibility.enable');
  const { nodes } = await client.send('Accessibility.getFullAXTree');
  const wanted = ['table', 'row', 'cell', 'columnheader', 'rowheader', 'gridcell', 'rowgroup'];
  const counts: Record<string, number> = Object.fromEntries(wanted.map((r) => [r, 0]));
  for (const n of nodes) {
    const role = (n.role as { value?: string } | undefined)?.value;
    if (!n.ignored && role && role in counts) counts[role] += 1;
  }
  await client.detach();
  return counts;
}

const stackedRoutes: Array<{ route: string; ready: (page: Page) => Locator }> = [
  { route: '/alertas', ready: (p) => p.locator('table.table-stack-mobile').first() },
  { route: '/exportaciones', ready: (p) => p.locator('table.table-stack-mobile').first() },
  { route: '/papelera', ready: (p) => p.locator('table.table-stack-mobile').first() },
];

for (const { route, ready } of stackedRoutes) {
  test(`4 tabla apilada movil ${route}`, async ({ browser }) => {
    const slug = route.slice(1);
    const desktop = await open(browser, { w: 1440, h: 900 });
    await desktop.page.goto(route);
    await expect(ready(desktop.page), `${route}: el fixture debe renderizar la tabla`).toBeVisible();
    const dCounts = {
      playwright: {
        row: await desktop.page.getByRole('row').count(),
        cell: await desktop.page.getByRole('cell').count(),
        columnheader: await desktop.page.getByRole('columnheader').count(),
      },
      ax: await axCounts(desktop.page),
    };
    await shoot(desktop.page, `tabla-apilada-${slug}-light-1440`);
    await desktop.context.close();

    const mob = await open(browser, { w: 390, h: 844 });
    await mob.page.goto(route);
    await expect(ready(mob.page)).toBeVisible();
    const display = await mob.page.locator('table.table-stack-mobile').first().evaluate((el) => getComputedStyle(el).display);
    const mCounts = {
      display,
      playwright: {
        row: await mob.page.getByRole('row').count(),
        cell: await mob.page.getByRole('cell').count(),
        columnheader: await mob.page.getByRole('columnheader').count(),
      },
      ax: await axCounts(mob.page),
    };
    await ready(mob.page).scrollIntoViewIfNeeded();
    await shoot(mob.page, `tabla-apilada-${slug}-light-390`);
    await mob.page.screenshot({ path: join(OUT, `tabla-apilada-${slug}-light-390-full.png`), fullPage: true });
    metric(`tabla apilada ${route} 1440`, dCounts);
    metric(`tabla apilada ${route} 390`, mCounts);
    expect(mCounts.display, `${route}: a 390px la tabla debe apilarse (display:block)`).toBe('block');
    expect(
      mCounts.ax.cell,
      `SEMANTICA PERDIDA ${route}: arbol de accesibilidad real a 390px tiene ${mCounts.ax.cell} cell / ${mCounts.ax.row} row / ${mCounts.ax.columnheader} columnheader (1440px: ${dCounts.ax.cell}/${dCounts.ax.row}/${dCounts.ax.columnheader})`,
    ).toBe(dCounts.ax.cell);
    expect(mCounts.ax.columnheader, `${route}: columnheader en AX a 390px vs 1440px`).toBe(dCounts.ax.columnheader);
    await mob.context.close();
  });
}

// ---------------------------------------------------------------------------
// 5) Dashboard: errores por bloque y estado vacio
// ---------------------------------------------------------------------------

async function checkBarsVisible(page: Page, where: string) {
  await expect(page.getByRole('radiogroup', { name: 'Periodo' }), `${where}: la barra de periodo debe seguir visible`).toBeVisible();
  await expect(page.getByRole('radiogroup', { name: 'Divisa principal' }), `${where}: el selector de divisa debe seguir visible`).toBeVisible();
}

test('5a dashboard con /dashboard/principal en 500', async ({ browser }) => {
  const { context, page } = await open(browser, { api: { fail: ['/dashboard/principal'] } });
  await page.goto('/dashboard');
  const title = page.getByText('No se pudo cargar el saldo consolidado.');
  await expect(title, 'bloque de saldo consolidado debe mostrar el error').toBeVisible();
  const block = page.locator('.empty-state--error', { has: title });
  await expect(block.getByRole('button', { name: 'Reintentar' }), 'el bloque afectado debe tener el boton Reintentar').toBeVisible();
  await checkBarsVisible(page, 'principal 500');
  const nErrores = await page.locator('.empty-state--error').count();
  const nReintentar = await page.getByRole('button', { name: 'Reintentar' }).count();
  metric('dashboard principal 500: bloques de error / botones Reintentar', { nErrores, nReintentar });
  await shoot(page, 'dashboard-error-principal-light-1440');
  await context.close();
});

test('5b dashboard con /dashboard/saldos-divisa en 500', async ({ browser }) => {
  const { context, page } = await open(browser, { api: { fail: ['/dashboard/saldos-divisa'] } });
  await page.goto('/dashboard');
  const title = page.getByText('No se pudieron cargar los saldos por divisa.');
  await expect(title, 'bloque de saldos por divisa debe mostrar el error').toBeVisible();
  const block = page.locator('.empty-state--error', { has: title });
  await expect(block.getByRole('button', { name: 'Reintentar' }), 'el bloque afectado debe tener el boton Reintentar').toBeVisible();
  await checkBarsVisible(page, 'saldos-divisa 500');
  // El resto del dashboard debe seguir mostrando datos
  await expect(page.getByText('Saldo consolidado', { exact: true }), 'el saldo consolidado debe seguir visible').toBeVisible();
  await shoot(page, 'dashboard-error-saldos-divisa-light-1440');
  await context.close();
});

test('5c dashboard sin cuentas: EmptyState de setup (no error)', async ({ browser }) => {
  const { context, page } = await open(browser, {
    api: {
      overrides: {
        '/dashboard/principal': dashboardPrincipalVacio,
        '/dashboard/saldos-divisa': { divisa_principal: 'EUR', total_convertido: 0, divisas: [] },
      },
    },
  });
  await page.goto('/dashboard');
  await expect(page.getByRole('heading', { name: 'Todavía no hay datos para mostrar.' })).toBeVisible();
  await expect(page.locator('.empty-state--setup'), 'debe usarse la variante setup').toHaveCount(1);
  await expect(page.locator('.empty-state--error'), 'no debe haber EmptyState de error').toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Ir a extractos' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Reintentar' })).toHaveCount(0);
  await checkBarsVisible(page, 'sin cuentas');
  await shoot(page, 'dashboard-sin-cuentas-light-1440');
  await context.close();
});

// ---------------------------------------------------------------------------
// 6) Boton primario oscuro: contraste WCAG
// ---------------------------------------------------------------------------

async function contrast(locator: Locator) {
  return locator.evaluate((el) => {
    const toRgba = (css: string): [number, number, number, number] => {
      const c = document.createElement('canvas');
      c.width = 1; c.height = 1;
      const ctx = c.getContext('2d', { willReadFrequently: true })!;
      ctx.clearRect(0, 0, 1, 1);
      ctx.fillStyle = '#000';
      ctx.fillStyle = css;
      ctx.fillRect(0, 0, 1, 1);
      const d = ctx.getImageData(0, 0, 1, 1).data;
      return [d[0], d[1], d[2], d[3] / 255];
    };
    const lum = ([r, g, b]: number[]) => {
      const f = (v: number) => { const s = v / 255; return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4; };
      return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
    };
    const cs = getComputedStyle(el);
    const fg = toRgba(cs.color);
    const bg = toRgba(cs.backgroundColor);
    const l1 = lum(fg); const l2 = lum(bg);
    const ratio = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
    return { color: cs.color, background: cs.backgroundColor, fg, bg, ratio: Math.round(ratio * 100) / 100, backgroundImage: cs.backgroundImage, className: (el as HTMLElement).className };
  });
}

test('6a /backups tema oscuro: contraste .button-primary (reposo y hover)', async ({ browser }) => {
  const { context, page } = await open(browser, { theme: 'dark' });
  await page.goto('/backups');
  const btn = page.locator('.button-primary').first();
  await expect(btn, 'debe existir un .button-primary en /backups').toBeVisible();
  metric('backups dark: html data-theme', (await page.locator('html').getAttribute('data-theme')) ?? 'null');
  await page.mouse.move(0, 0);
  await page.waitForTimeout(300);
  const rest = await contrast(btn);
  await shoot(page, 'backups-dark-1440-reposo');
  await btn.hover();
  await page.waitForTimeout(400);
  const hover = await contrast(btn);
  await shoot(page, 'backups-dark-1440-hover');
  metric('contraste /backups dark reposo', rest);
  metric('contraste /backups dark hover', hover);
  expect(rest.ratio, `contraste en reposo ${rest.ratio}:1 (${rest.color} sobre ${rest.background})`).toBeGreaterThanOrEqual(4.5);
  expect(hover.ratio, `contraste en hover ${hover.ratio}:1 (${hover.color} sobre ${hover.background})`).toBeGreaterThanOrEqual(4.5);
  await context.close();
});

test('6b /login tema oscuro: contraste del boton de envio (reposo y hover)', async ({ browser }) => {
  const { context, page } = await open(browser, { theme: 'dark' });
  await page.goto('/login');
  const btn = page.locator('button[type="submit"]').first();
  await expect(btn, 'debe existir el boton de envio en /login').toBeVisible();
  const nPrimary = await page.locator('.button-primary').count();
  metric('login: elementos .button-primary', nPrimary);
  await page.mouse.move(0, 0);
  await page.waitForTimeout(300);
  const rest = await contrast(btn);
  await shoot(page, 'login-dark-1440-reposo');
  await btn.hover();
  await page.waitForTimeout(400);
  const hover = await contrast(btn);
  await shoot(page, 'login-dark-1440-hover');
  metric('contraste /login dark reposo', rest);
  metric('contraste /login dark hover', hover);
  expect(rest.ratio, `contraste login reposo ${rest.ratio}:1 (${rest.color} sobre ${rest.background})`).toBeGreaterThanOrEqual(4.5);
  expect(hover.ratio, `contraste login hover ${hover.ratio}:1 (${hover.color} sobre ${hover.background})`).toBeGreaterThanOrEqual(4.5);
  await context.close();
});
