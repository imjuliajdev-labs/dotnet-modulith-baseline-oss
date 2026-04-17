import { expect, test } from '@playwright/test';

test('the live shell signs in and publishes a knowledge entry through the real backend', async ({ page }) => {
  const uniqueSuffix = `${Date.now()}`;
  const title = `Playwright knowledge ${uniqueSuffix}`;
  const slug = `playwright-knowledge-${uniqueSuffix}`;
  const body = `Published through the live browser flow ${uniqueSuffix}.`;

  await page.goto('/');

  await expect(page.getByText('Operationally honest UI for a modular monolith.')).toBeVisible({ timeout: 15000 });
  await page.getByRole('button', { name: 'Sign in with cookie auth' }).click();

  await expect(page.getByText('Signed in as')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Knowledge Base' })).toBeVisible();

  await page.getByRole('link', { name: 'Knowledge Base' }).click();

  await expect(page.getByRole('heading', { name: 'Knowledge Base' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Operator controls' })).toBeVisible();

  await page.getByLabel('Public title').fill(`Knowledge Hub ${uniqueSuffix}`);
  await page.getByLabel('Public intro').fill(`Operational settings updated live ${uniqueSuffix}.`);
  await page.getByLabel('Search placeholder').fill(`Search guided answers ${uniqueSuffix}`);
  await page.getByLabel('Management preview limit').fill('24');
  await page.getByLabel('Enable public search').uncheck();
  await page.getByRole('button', { name: 'Save settings' }).click();

  await expect(page.getByRole('heading', { name: `Knowledge Hub ${uniqueSuffix}` })).toBeVisible();
  await expect(page.locator('.dashboard-grid > section').first().getByText(`Operational settings updated live ${uniqueSuffix}.`)).toBeVisible();
  await expect(page.getByLabel('Search entries')).toHaveCount(0);

  await page.getByLabel('Title', { exact: true }).fill(title);
  await page.getByLabel('Slug').fill(slug);
  await page.getByLabel('Category').fill('Playwright');
  await page.getByLabel('Answer').fill(body);
  await page.getByLabel('Sort order').fill('77');
  await page.getByLabel('Featured').check();
  await page.getByRole('button', { name: 'Create draft' }).click();

  const managementCard = page.locator('.module-card').filter({ hasText: title }).first();
  await expect(managementCard).toBeVisible();
  await expect(managementCard).toContainText('draft');

  await managementCard.getByRole('button', { name: 'Publish' }).click();
  await expect(managementCard).toContainText('published');

  const publicEntry = page.locator('details').filter({ hasText: title }).first();
  await expect(publicEntry).toBeVisible();
  await expect(publicEntry).toContainText(body);
});