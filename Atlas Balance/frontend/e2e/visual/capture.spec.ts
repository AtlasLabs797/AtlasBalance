import { existsSync, mkdirSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from '@playwright/test';
import { installMockApi, setPrimerLoginMock, unknownPaths } from './mockApi';

// Arnes de capturas visuales con API mockeada.
// VISUAL_OUT  carpeta de salida (por defecto test-results/visual, ignorada por git)
// VISUAL_FAIL endpoint(s) a forzar con 500, p.ej. /dashboard/evolucion
const OUT = process.env.VISUAL_OUT
  ?? join('test-results', 'visual');
mkdirSync(OUT, { recursive: true });

const themes = ['light', 'dark'] as const;
// LOTE 8 (D-4): se anade la anchura de tablet (1024 x 768) para poder validar
// la banda 901-1023.98 px, que es justo donde el mapeo de breakpoints cambia
// el numero de columnas. Antes solo se media escritorio (1440) y movil (390).
const viewports = [{ w: 1440, h: 900 }, { w: 1024, h: 768 }, { w: 390, h: 844 }];
// LOTE 8 (T-2): cobertura de las 20 pantallas de la app (antes 9). Los ids
// de titular y cuenta son los del fixture de mockApi (TIT[0] y CTA[0]).
const routes = [
  '/login', '/dashboard', '/extractos', '/importacion', '/cuentas', '/configuracion', '/backups', '/usuarios', '/seguridad/dispositivos-mfa',
  '/titulares', '/titulares/00000000-0000-4000-8000-000000000021',
  '/auditoria', '/papelera', '/alertas', '/conciliacion',
  '/formatos-importacion', '/configuracion/paises', '/revision',
  '/dashboard/titular/00000000-0000-4000-8000-000000000021', '/dashboard/cuenta/00000000-0000-4000-8000-000000000031',
  '/cambiar-password', '/ia', '/exportaciones',
];
const slug = (r: string) => r.replace(/^\//, '').replace(/\//g, '_') || 'root';
// Slug de un texto de pestana: "General + SMTP" -> "general-smtp", "Países" -> "paises".
const slugTexto = (texto: string) => texto
  .normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
  .replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');

// El paquete @playwright/test puede esperar una revision de Chromium distinta de la
// instalada. VISUAL_CHROMIUM fuerza un ejecutable; si no, se usa el headless shell mas reciente.
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

test.describe.configure({ mode: 'serial' });
// LOTE 8 (T-1): el job e2e de CI corre esto en un runner mas lento que el
// equipo local (en local el caso mas lento tarda ~84s). 240s dejaba el margen
// justo; 300s evita que un runner lento ponga el job en rojo sin un fallo real.
// LOTE 8 (T-2): la cobertura pasa de 9 a 21 rutas por combinacion de tema y
// viewport, asi que el caso se casi duplica. 600s deja margen para el runner
// lento de CI sin convertir un timeout real en un falso verde.
test.setTimeout(600_000);

for (const theme of themes) {
  for (const vp of viewports) {
    test(`capturas ${theme} ${vp.w}`, async ({ browser }) => {
      const context = await browser.newContext({
        viewport: { width: vp.w, height: vp.h },
        colorScheme: theme,
        locale: 'es-ES',
        reducedMotion: 'reduce',
      });
      // Mismo contrato que public/theme-init.js y uiStore: localStorage 'theme'.
      await context.addInitScript((t) => {
        try { window.localStorage.setItem('theme', t); } catch { /* ignore */ }
      }, theme);
      const page = await context.newPage();
      await installMockApi(page);

      const shoot = async (name: string) => {
        await page.waitForLoadState('networkidle').catch(() => undefined);
        await page.waitForTimeout(700);
        await page.screenshot({ path: join(OUT, `${name}-${theme}-${vp.w}.png`), fullPage: false });
      };

      for (const route of routes) {
        // LOTE 8 (T-2): /cambiar-password solo es alcanzable con
        // primer_login=true (ProtectedRoute con allowPrimerLogin). La
        // bandera se enciende solo para esa navegacion y se apaga justo
        // despues, para que el resto de pantallas sigan con sesion normal.
        if (route === '/cambiar-password') setPrimerLoginMock(true);
        await page.goto(route);
        // LOTE 8 (T-2): /configuracion/paises se renderiza en el Outlet de
        // ConfiguracionPage, por debajo del pliegue. Se hace scroll hasta el
        // bloque de paises para que su tabla entre en la captura.
        if (route === '/configuracion/paises') {
          await page.locator('.paises-page').scrollIntoViewIfNeeded().catch(() => undefined);
        }
        await shoot(slug(route));
        if (route === '/cambiar-password') setPrimerLoginMock(false);
      }

      // Modal abierto: nuevo titular
      await page.goto('/titulares');
      await page.getByRole('button', { name: /Nuevo Titular/i }).click();
      await expect(page.getByRole('heading', { name: /Nuevo titular/i })).toBeVisible();
      await shoot('titulares-modal');

      // V-03.02 (LOTE 6): historial de importacion con el boton "Revertir"
      // y su dialogo de confirmacion.
      await page.goto('/importacion');
      await page.getByRole('tab', { name: 'Historial' }).click();
      const revertir = page.getByRole('button', { name: /^Revertir el lote del/ }).first();
      await expect(revertir, 'el historial debe mostrar el boton Revertir del lote confirmado').toBeVisible();
      // En movil la tabla del historial scrollea en horizontal: se lleva al
      // final para que la columna de acciones (boton Revertir) quede en la captura.
      await page.locator('.import-validation-table-wrap').evaluate((el) => { el.scrollLeft = el.scrollWidth; });
      await shoot('importacion-historial');
      await revertir.click();
      await expect(page.getByRole('dialog', { name: 'Revertir lote de importación' })).toBeVisible();
      await shoot('importacion-revertir-dialogo');
      await page.getByRole('button', { name: 'Cancelar' }).click();

      // V-03.02 (LOTE 6): dialogo de revocacion de un dispositivo MFA.
      await page.goto('/seguridad/dispositivos-mfa');
      const revocar = page.getByRole('button', { name: /^Revocar la confianza MFA de/ }).first();
      await expect(revocar, 'la tabla de dispositivos debe mostrar el boton Revocar').toBeVisible();
      // En movil la tabla scrollea en horizontal: se lleva al final para que
      // las columnas Expira y Acciones (con el aviso de vencimiento) entren en la captura.
      const scrollMfa = await page.locator('.users-table-scroll').evaluate((el) => {
        el.scrollLeft = el.scrollWidth;
        return el.scrollLeft;
      });
      if (scrollMfa > 0) {
        await shoot('dispositivos-mfa-tabla-scrolleada');
      }
      await revocar.click();
      await expect(page.getByRole('dialog', { name: 'Revocar dispositivo de confianza' })).toBeVisible();
      await shoot('dispositivos-mfa-revocar-dialogo');
      await page.getByRole('button', { name: 'Cancelar' }).click();

      // Pestanas y estados abiertos. Solo escritorio (1440) y movil (390). Cada captura
      // opcional va en try/catch: si un control no aparece, se registra y se sigue.
      const capturaOpcional = async (name: string, accion: () => Promise<void>) => {
        try {
          await accion();
          await shoot(name);
        } catch (error) {
          const motivo = error instanceof Error ? error.message.split('\n')[0] : String(error);
          console.log(`[capture] skip ${name}: ${motivo}`);
        }
      };

      if (vp.w === 1440 || vp.w === 390) {
        // Configuracion: una captura por pestana del tablist.
        await page.goto('/configuracion');
        const tabsConfig = page.getByRole('tablist', { name: 'Secciones de configuración' }).getByRole('tab');
        const totalTabsConfig = await tabsConfig.count();
        for (let i = 0; i < totalTabsConfig; i += 1) {
          const slugTab = slugTexto((await tabsConfig.nth(i).innerText()).trim());
          await capturaOpcional(`configuracion-tab-${slugTab}`, async () => {
            await tabsConfig.nth(i).click();
          });
        }

        // Revision: pestanas Comisiones y Seguros (botones dentro de .config-tabs).
        for (const nombre of ['Comisiones', 'Seguros']) {
          await capturaOpcional(`revision-tab-${slugTexto(nombre)}`, async () => {
            await page.goto('/revision');
            await page.locator('.config-tabs').getByRole('button', { name: nombre, exact: true }).click();
          });
        }

        // Extractos: desplegable abierto (AppSelect/SearchableSelect "Titular").
        await capturaOpcional('extractos-select-titular-abierto', async () => {
          await page.goto('/extractos');
          await page.getByRole('combobox', { name: 'Titular', exact: true }).click();
          await expect(page.getByRole('listbox', { name: 'Titular' })).toBeVisible();
        });

        // Extractos: selector de fecha "Desde" abierto. El trigger del DatePickerField
        // es un boton cuyo nombre accesible es "Desde" + valor ("Desde Todas"); el
        // popover es un dialog con aria-label "Fecha desde". Se espera a que el
        // trigger sea visible (count() no espera). En navegadores con input nativo
        // no hay popover propio y la captura se omite con su motivo.
        await capturaOpcional('extractos-fecha-desde-abierta', async () => {
          await page.goto('/extractos');
          const desde = page.getByRole('button', { name: /^Desde\b/ }).first();
          await desde.waitFor({ state: 'visible', timeout: 15_000 }).catch(() => {
            throw new Error('no hay selector de fecha "Desde" con popover (input nativo)');
          });
          await desde.click();
          await expect(page.getByRole('dialog', { name: 'Fecha desde' })).toBeVisible();
        });
      }

      await context.close();
    });
  }
}

test.afterAll(() => {
  if (unknownPaths.size > 0) {
    console.log('[visual] endpoints sin fixture:\n  ' + [...unknownPaths].join('\n  '));
  }
});
