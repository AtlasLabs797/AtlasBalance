import { expect, test } from '@playwright/test';

const adminEmail = process.env.E2E_ADMIN_EMAIL ?? 'admin@atlasbalance.local';
const adminPassword = process.env.E2E_ADMIN_PASSWORD;
const apiHealthUrl = process.env.E2E_API_HEALTH_URL ?? 'http://localhost:5000/api/health';

const adminRoutes = [
  '/dashboard',
  '/titulares',
  '/cuentas',
  '/extractos',
  '/importacion',
  '/formatos-importacion',
  '/alertas',
  '/exportaciones',
  '/usuarios',
  '/auditoria',
  '/configuracion',
  '/backups',
  '/papelera',
];

test.beforeAll(() => {
  if (!adminPassword) {
    throw new Error(
      'Set E2E_ADMIN_PASSWORD before running E2E tests. Refusing to guess a password and risk locking the admin account.'
    );
  }
});

test('admin can log in, navigate core screens, keep account filters, and avoid secret leaks', async ({ page, request }) => {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  const failedApiResponses: string[] = [];

  const health = await request.get(apiHealthUrl);
  expect(health.status(), `Backend health check failed at ${apiHealthUrl}`).toBe(200);

  page.on('console', (message) => {
    if (message.type() !== 'error') {
      return;
    }

    const text = message.text();
    if (!text.includes('favicon')) {
      consoleErrors.push(text);
    }
  });

  page.on('pageerror', (error) => {
    pageErrors.push(error.message);
  });

  page.on('response', (response) => {
    if (response.url().includes('/api/') && response.status() >= 500) {
      failedApiResponses.push(`${response.status()} ${response.url()}`);
    }
  });

  await page.goto('/login');
  await expect(page.getByRole('heading', { name: /Iniciar/i })).toBeVisible();
  await page.locator('#email').fill(adminEmail);
  await page.locator('#password').fill(adminPassword);
  await Promise.all([
    page.waitForURL(/\/(dashboard|cambiar-password)/, { timeout: 15_000 }),
    page.getByRole('button', { name: /Entrar/i }).click(),
  ]);

  expect(page.url(), 'E2E admin must not be in first-login password-change flow').toContain('/dashboard');

  for (const route of adminRoutes) {
    await page.goto(route);
    await expect(page.locator('body')).not.toContainText(/Algo sali[oó] mal|Cannot read|undefined is not/i);
  }

  const firstAccountId = await page.evaluate(async () => {
    const response = await fetch('/api/extractos/titulares-resumen', { credentials: 'include' });
    if (!response.ok) {
      return null;
    }

    const titulares = await response.json();
    for (const titular of titulares) {
      const cuenta = titular?.cuentas?.[0];
      if (cuenta?.cuenta_id) {
        return cuenta.cuenta_id as string;
      }
    }

    return null;
  });

  if (firstAccountId) {
    await page.goto(`/extractos?cuentaId=${firstAccountId}`);
    await expect(page.locator('select').nth(1)).toHaveValue(firstAccountId);
  }

  await page.goto('/configuracion');
  const configResponse = await page.evaluate(async () => {
    const response = await fetch('/api/configuracion', { credentials: 'include' });
    const payload = await response.json();
    return {
      ok: response.ok,
      text: JSON.stringify(payload),
      smtpPassword: payload?.smtp?.password,
    };
  });

  expect(configResponse.ok).toBe(true);
  expect(configResponse.smtpPassword).toBe('');
  expect(configResponse.text).not.toContain('smtp_password');

  expect(consoleErrors).toEqual([]);
  expect(pageErrors).toEqual([]);
  expect(failedApiResponses).toEqual([]);

  await page.getByRole('button', { name: /Cerrar sesion/i }).click();
  await expect(page).toHaveURL(/\/login/);
});
