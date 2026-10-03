import { expect, test } from '@playwright/test'
import { ADMIN, token } from './helpers'

test('REQ-P10 admin signs in at /admin/login and sees the dashboard "Paneli"', async ({ page }) => {
  await page.goto('/admin/login')
  await expect(page.getByRole('heading', { level: 1, name: 'Hyrje për administratorë' })).toBeVisible()

  await page.getByLabel('Email').fill(ADMIN.email)
  await page.getByLabel('Fjalëkalimi').fill(ADMIN.password)
  await page.getByRole('button', { name: 'Hyr' }).click()

  await expect(page.getByRole('heading', { level: 1, name: 'Paneli' })).toBeVisible()
  for (const label of ['Në pritje për shqyrtim', 'Kërkojnë vëmendje', 'Evente aktive', 'Oferta aktive', 'Burime me probleme']) {
    await expect(page.getByText(label, { exact: true }).first()).toBeVisible()
  }
  // Admin navigation stays visible on every screen size (no hamburger menu).
  for (const link of ['Paneli', 'Shqyrtimi', 'Të publikuarat', 'Burimet', 'Përdoruesit']) {
    await expect(page.getByRole('link', { name: link, exact: true })).toBeVisible()
  }
})

test('REQ-P02 wrong credentials show the generic login error', async ({ page }) => {
  await page.goto('/admin/login')
  await page.getByLabel('Email').fill(`u${token()}@test.local`)
  await page.getByLabel('Fjalëkalimi').fill('Wrong-password-123')
  await page.getByRole('button', { name: 'Hyr' }).click()

  await expect(page.getByText('Email ose fjalëkalim i pasaktë')).toBeVisible()
  await expect(page.getByRole('heading', { level: 1, name: 'Paneli' })).toHaveCount(0)
})

test('REQ-P11 "Dil" ends the session', async ({ page }) => {
  await page.goto('/admin/login')
  await page.getByLabel('Email').fill(ADMIN.email)
  await page.getByLabel('Fjalëkalimi').fill(ADMIN.password)
  await page.getByRole('button', { name: 'Hyr' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Paneli' })).toBeVisible()

  await page.getByRole('button', { name: 'Dil' }).click()

  await expect(page.getByRole('heading', { level: 1, name: 'Hyrje për administratorë' })).toBeVisible()
  expect((await page.request.get('/api/me')).status()).toBe(401)
  expect((await page.request.get('/api/admin/dashboard')).status()).toBe(401)
  await page.goto('/admin')
  await expect(page.getByRole('heading', { level: 1, name: 'Paneli' })).toHaveCount(0)
  await expect(page.getByRole('heading', { level: 1, name: 'Hyrje për administratorë' })).toBeVisible()
})
