import { expect, test } from '@playwright/test';

test('the shell bootstraps, authenticates against the real backend, and reveals the platform route', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByText('Operationally honest UI for a modular monolith.')).toBeVisible({ timeout: 15000 });
  await expect(page.getByRole('button', { name: 'Sign in with cookie auth' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Platform Console' })).toHaveCount(0);

  await page.getByRole('button', { name: 'Sign in with cookie auth' }).click();

  await expect(page.getByText('Signed in as')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Platform Console' })).toBeVisible();

  await page.getByRole('link', { name: 'Platform Console' }).click();

  await expect(page.getByRole('heading', { name: 'Platform console' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Module transitions' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Audit trail' })).toBeVisible();
});
