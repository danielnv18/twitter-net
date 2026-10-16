import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, createAuthApi } from '@/lib/auth-api'
import { localReturnPath } from '@/lib/navigation'
const fetchMock = vi.fn<typeof fetch>()
const json = (body: unknown, status = 200, headers = {}) =>
  new Response(JSON.stringify(body), { status, headers })

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
})
afterEach(() => vi.unstubAllGlobals())

describe('auth API boundary', () => {
  it('rejects malformed session payloads rather than displaying an unverified identity', async () => {
    fetchMock.mockResolvedValue(json({ user: { id: 'a', name: 1 }, csrfToken: 'token' }))
    await expect(createAuthApi().session()).rejects.toThrow('invalid account')
  })

  it('uses the earlier server expiry deadline', async () => {
    fetchMock.mockResolvedValue(
      json({
        user: { id: 'a', name: 'A', username: 'aaa' },
        csrfToken: 'token',
        serverNowUtc: '2026-10-16T12:00:00Z',
        expiresAtUtc: '2026-10-16T20:00:00Z',
        idleExpiresAtUtc: '2026-10-16T12:30:00Z',
      }),
    )
    const before = Date.now()
    const result = await createAuthApi().session()
    expect(result.expiresAt).toBeGreaterThanOrEqual(before + 30 * 60_000)
    expect(result.expiresAt).toBeLessThanOrEqual(Date.now() + 30 * 60_000)
  })

  it('passes server field errors through and preserves the password exactly', async () => {
    fetchMock
      .mockResolvedValueOnce(json({ user: null, csrfToken: 'token' }))
      .mockResolvedValueOnce(json({ errors: { password: ['Use a longer password.'] } }, 400))
    const api = createAuthApi()
    await api.session()
    await expect(
      api.login({ identifier: 'hello@example.com', password: ' spaces stay ' }),
    ).rejects.toMatchObject({ status: 400, errors: { password: ['Use a longer password.'] } })
    expect(fetchMock).toHaveBeenLastCalledWith(
      '/api/auth/login',
      expect.objectContaining({
        body: JSON.stringify({ identifier: 'hello@example.com', password: ' spaces stay ' }),
      }),
    )
  })

  it.each([
    [401, {}, {}, 'incorrect'],
    [409, {}, {}, 'different details'],
    [429, {}, { 'Retry-After': '20' }, '20 seconds'],
    [403, { code: 'csrf_invalid' }, {}, 'submit the form again'],
  ])(
    'explains HTTP %s without leaking server exception text',
    async (status, body, headers, message) => {
      fetchMock
        .mockResolvedValueOnce(json({ user: null, csrfToken: 'token' }))
        .mockResolvedValueOnce(json({ ...body, detail: 'private exception' }, status, headers))
      const api = createAuthApi()
      await api.session()
      const error = await api
        .login({ identifier: 'someone', password: 'password' })
        .catch((value: unknown) => value)
      expect(error).toBeInstanceOf(ApiError)
      expect(error).toMatchObject({ message: expect.stringContaining(message) })
      expect(String(error)).not.toContain('private exception')
      expect(fetchMock).toHaveBeenCalledTimes(2)
    },
  )

  it.each([
    'https://evil.example',
    '//evil.example',
    '/\\evil.example',
    '/signin?returnTo=/signup',
    null,
    ['//evil.example'],
  ])('rejects unsafe or recursive return path %s', (path) => {
    expect(localReturnPath(path)).toBe('/account')
  })

  it('retains an internal account return path', () =>
    expect(localReturnPath('/account?from=login')).toBe('/account?from=login'))
})
