import { expect, test } from '@playwright/test'

const ADMIN = { email: 'admin@kosovaradar.local', password: 'Radar-admin-dev-2026' }

test('the dashboard shows every stat tile with a number', async ({ page }) => {
  await page.goto('/admin/login')
  await page.getByLabel('Email').fill(ADMIN.email)
  await page.getByLabel('Fjalëkalimi').fill(ADMIN.password)
  await page.getByRole('button', { name: 'Hyr' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Paneli' })).toBeVisible()

  for (const label of [
    'Në pritje për shqyrtim',
    'Kërkojnë vëmendje',
    'Evente aktive',
    'Oferta aktive',
    'Burime me probleme',
  ]) {
    const tile = page.locator('dl > div').filter({ has: page.getByText(label, { exact: true }) })
    await expect(tile.locator('dd')).toHaveText(/^\d+$/)
  }
  await expect(page.getByRole('alert')).toHaveCount(0)
})
