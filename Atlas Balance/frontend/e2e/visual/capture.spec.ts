import { existsSync, mkdirSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from '@playwright/test';
import { installMockApi, unknownPaths } from './mockApi';

// Arnes de capturas visuales con API mockeada.
// VISUAL_OUT  carpeta de salida (por defecto test-results/visual, ignorada por git)
// VISUAL_FAIL endpoint(s) a forzar con 500, p.ej. /dashboard/evolucion
const OUT = process.env.VISUAL_OUT
  ?? join('test-results', 'visual');
mkdirSync(OUT, { recursive: true });

const themes = ['light', 'dark'] as const;
const viewports = [{ w: 1440, h: 900 }, { w: 390, h: 844 }];
const routes = ['/login', '/dashboard', '/extractos', '/importacion', '/cuentas', '/configuracion', '/backups', '/usuarios', '/seguridad/dispositivos-mfa'];
const slug = (r: string) => r.replace(/^\//, '').replace(/\//g, '_') || 'root';

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
test.setTimeout(240_000);

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
        await page.goto(route);
        await shoot(slug(route));
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

      await context.close();
    });
  }
}

test.afterAll(() => {
  if (unknownPaths.size > 0) {
    console.log('[visual] endpoints sin fixture:\n  ' + [...unknownPaths].join('\n  '));
  }
});
