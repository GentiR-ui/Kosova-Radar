import { expect, test } from '@playwright/test'

const ADMIN = { email: 'admin@kosovaradar.local', password: 'Radar-admin-dev-2026' }

test('an admin creates a reviewer and deactivates them', async ({ page }) => {
  const email = `flow${Date.now()}${Math.random().toString(36).slice(2, 8)}@test.local`
  await page.goto('/admin/login')
  await page.getByLabel('Email').fill(ADMIN.email)
  await page.getByLabel('Fjalëkalimi').fill(ADMIN.password)
  await page.getByRole('button', { name: 'Hyr' }).click()
  await page.getByRole('link', { name: 'Përdoruesit' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Përdoruesit' })).toBeVisible()

  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Fjalëkalimi').fill('Flow-test-password-1')
  await page.getByLabel('Roli').selectOption({ label: 'Shqyrtues' })
  await page.getByRole('button', { name: 'Krijo përdoruesin' }).click()
  const row = page.getByRole('row', { name: new RegExp(email) })
  await expect(row).toContainText('Shqyrtues')
  await expect(row).toContainText('Aktiv')

  // The same email again is refused.
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Fjalëkalimi').fill('Flow-test-password-1')
  await page.getByRole('button', { name: 'Krijo përdoruesin' }).click()
  await expect(page.getByRole('alert')).toContainText('Ky email përdoret tashmë.')

  await row.getByRole('button', { name: /Çaktivizo/ }).click()
  await expect(row).toContainText('Jo aktiv')
  await expect(row.getByRole('button')).toHaveCount(0)
})
