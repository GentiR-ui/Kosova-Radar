import { expect, request, test } from '@playwright/test'

const ADMIN = { email: 'admin@kosovaradar.local', password: 'Radar-admin-dev-2026' }

test('a session that ends mid-use sends the admin back to the login page', async ({
  page,
  context,
}) => {
  await page.goto('/admin/login')
  await page.getByLabel('Email').fill(ADMIN.email)
  await page.getByLabel('Fjalëkalimi').fill(ADMIN.password)
  await page.getByRole('button', { name: 'Hyr' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Paneli' })).toBeVisible()

  // End this browser's server session from elsewhere (same cookie, separate client), like a redeploy would.
  const other = await request.newContext({
    baseURL: 'http://localhost:5173',
    storageState: await context.storageState(),
  })
  expect((await other.post('/api/auth/logout')).status()).toBe(204)
  await other.dispose()

  await page.getByRole('link', { name: 'Shqyrtimi' }).click() // fetches admin data → 401 → back to login

  await expect(
    page.getByRole('heading', { level: 1, name: 'Hyrje për administratorë' }),
  ).toBeVisible()
})
