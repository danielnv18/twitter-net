import { test, expect } from '@playwright/test'

test('creates an account, restores it on reload, signs out, and signs in with email and username', async ({ page }) => {
  const suffix = `${Date.now().toString(36)}${test.info().parallelIndex}`
  const username = `person_${suffix}`
  const email = `${username}@example.com`
  const password = 'a memorable phrase for this account'

  await page.goto('/account')
  await expect(page).toHaveURL(/\/signin\?returnTo=/)
  await page.getByRole('link', { name: 'Create an account', exact: true }).click()
  await page.getByLabel('Name', { exact: true }).fill('María Sol')
  await page.getByLabel('Username', { exact: true }).fill(username)
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Create account', exact: true }).click()
  await expect(page).toHaveURL(/\/account$/)
  await expect(page.getByRole('heading', { name: 'María Sol', exact: true })).toBeVisible()
  await expect(page.locator('.profile-username')).toHaveText(`@${username}`)
  await expect(page.getByText(email, { exact: true })).toHaveCount(0)
  await page.reload()
  await expect(page.getByRole('heading', { name: 'María Sol', exact: true })).toBeVisible()

  for (const identifier of [email, username]) {
    await page.getByRole('button', { name: 'Sign out', exact: true }).click()
    await expect(page).toHaveURL(/\/signin/)
    await page.goto('/account')
    await expect(page).toHaveURL(/\/signin\?returnTo=/)
    await page.getByLabel('Username or email').fill(identifier)
    await page.getByLabel('Password', { exact: true }).fill(password)
    await page.getByRole('button', { name: 'Sign in', exact: true }).click()
    await expect(page).toHaveURL(/\/account$/)
    await expect(page.locator('.profile-username')).toHaveText(`@${username}`)
  }
  await page.getByRole('button', { name: 'Sign out', exact: true }).click()
  await expect(page).toHaveURL(/\/signin/)
})

test('validates signup fields accessibly without sending a signup request', async ({ page }) => {
  let signupRequests = 0
  page.on('request', (request) => { if (request.method() === 'POST' && request.url().endsWith('/api/auth/signup')) signupRequests++ })
  await page.goto('/signup')
  await page.getByRole('button', { name: 'Create account', exact: true }).click()
  await expect(page.getByLabel('Name', { exact: true })).toBeFocused()
  await expect(page.locator('input[aria-invalid="true"]')).toHaveCount(4)
  await expect(page.locator('#password-error')).toHaveText('Use 15–128 characters. A phrase works well.')
  expect(signupRequests).toBe(0)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
})
