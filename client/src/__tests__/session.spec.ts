import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useSessionStore } from '@/stores/session'

const user = { id: 'account-1', name: 'María Sol', username: 'maria_sol' }
const input = {
  name: user.name,
  username: user.username,
  email: 'maria@example.com',
  password: 'my memorable password phrase',
}
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const session = (account: typeof user | null = null, csrfToken = 'anonymous-token') =>
  json({ user: account, csrfToken })
let store: ReturnType<typeof useSessionStore>
const fetchMock = vi.fn<typeof fetch>()

beforeEach(() => {
  setActivePinia(createPinia())
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
  store = useSessionStore()
})
afterEach(() => {
  store.$dispose()
  vi.unstubAllGlobals()
})

describe('session lifecycle', () => {
  it('deduplicates concurrent initialization and restores the account from the server', async () => {
    fetchMock.mockResolvedValueOnce(session(user))
    await Promise.all([store.initialize(), store.initialize(), store.initialize()])
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(store.state).toMatchObject({ kind: 'authenticated', user })
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/auth/session',
      expect.objectContaining({ credentials: 'same-origin', cache: 'no-store' }),
    )
  })

  it('keeps a network failure distinct from signed out and allows a read-only retry', async () => {
    fetchMock.mockRejectedValueOnce(new TypeError('offline')).mockResolvedValueOnce(session())
    await store.initialize()
    expect(store.state.kind).toBe('unavailable')
    await store.refresh()
    expect(store.state.kind).toBe('anonymous')
    expect(fetchMock.mock.calls.every(([, options]) => !options?.method)).toBe(true)
  })

  it('registers all four fields with CSRF and obtains a fresh token after identity changes', async () => {
    fetchMock
      .mockResolvedValueOnce(session())
      .mockResolvedValueOnce(session(null, 'signup-token'))
      .mockResolvedValueOnce(json(user, 201))
      .mockResolvedValueOnce(session(user, 'account-token'))
      .mockResolvedValueOnce(session(user, 'logout-token'))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(session(null, 'signed-out-token'))
    await store.signup(input)
    expect(store.state).toMatchObject({ kind: 'authenticated', user })
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/auth/signup',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify(input),
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': 'signup-token' },
      }),
    )
    await store.logout()
    expect(store.state.kind).toBe('anonymous')
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/auth/logout',
      expect.objectContaining({
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': 'logout-token' },
      }),
    )
  })

  it('claims submission ownership before awaiting bootstrap so double submissions send one POST', async () => {
    fetchMock
      .mockResolvedValueOnce(session())
      .mockResolvedValueOnce(session())
      .mockResolvedValueOnce(json(user, 201))
      .mockResolvedValueOnce(session(user))
    await Promise.all([store.signup(input), store.signup(input)])
    expect(fetchMock.mock.calls.filter(([, options]) => options?.method === 'POST')).toHaveLength(1)
    expect(store.pending).toBe(false)
    expect(store.state).toMatchObject({ kind: 'authenticated', user })
  })

  it('keeps the account signed in when logout fails and never retries the POST', async () => {
    fetchMock
      .mockResolvedValueOnce(session(user))
      .mockResolvedValueOnce(session(user))
      .mockRejectedValueOnce(new TypeError('offline'))
    await store.initialize()
    await expect(store.logout()).rejects.toThrow('could not reach')
    expect(store.state).toMatchObject({ kind: 'authenticated', user })
    expect(fetchMock.mock.calls.filter(([, options]) => options?.method === 'POST')).toHaveLength(1)
  })

  it('does not offer a repeated signup when its POST succeeded but the session refresh failed', async () => {
    fetchMock
      .mockResolvedValueOnce(session())
      .mockResolvedValueOnce(session())
      .mockResolvedValueOnce(json(user, 201))
      .mockRejectedValueOnce(new TypeError('offline'))
      .mockResolvedValueOnce(session(user))
    await expect(store.signup(input)).resolves.toBeUndefined()
    expect(store.state.kind).toBe('unavailable')
    await store.refresh()
    expect(store.state).toMatchObject({ kind: 'authenticated', user })
    expect(fetchMock.mock.calls.filter(([, options]) => options?.method === 'POST')).toHaveLength(1)
  })

  it('preserves a preexisting server session when a login attempt returns invalid credentials', async () => {
    fetchMock
      .mockResolvedValueOnce(session(user))
      .mockResolvedValueOnce(session(user))
      .mockResolvedValueOnce(json({ code: 'invalid_credentials' }, 401))
      .mockResolvedValueOnce(session(user))
    await store.initialize()
    await expect(
      store.login({ identifier: 'another_user', password: 'incorrect password' }),
    ).rejects.toMatchObject({ status: 401 })
    expect(store.state).toMatchObject({ kind: 'authenticated', user })
  })

  it('clears a revoked session when a mutation returns 401 and the authoritative read is anonymous', async () => {
    fetchMock
      .mockResolvedValueOnce(session(user))
      .mockResolvedValueOnce(session(user))
      .mockResolvedValueOnce(json({}, 401))
      .mockResolvedValueOnce(session())
    await store.initialize()
    await expect(store.logout()).rejects.toMatchObject({ status: 401 })
    expect(store.state.kind).toBe('anonymous')
  })

  it('clears an expired account when the server reports an anonymous session', async () => {
    fetchMock.mockResolvedValueOnce(session(user)).mockResolvedValueOnce(session())
    await store.initialize()
    await store.refresh()
    expect(store.state.kind).toBe('anonymous')
  })

  it('synchronizes across tabs using nonsensitive events without an echo loop', async () => {
    const postMessage = vi.fn<(message: string) => void>()
    const close = vi.fn<() => void>()
    let receive: ((event: { data: unknown }) => void) | undefined
    class Channel {
      postMessage = postMessage
      close = close
      set onmessage(listener: (event: { data: unknown }) => void) {
        receive = listener
      }
    }
    vi.stubGlobal('BroadcastChannel', Channel)
    fetchMock
      .mockResolvedValueOnce(session())
      .mockResolvedValueOnce(session())
      .mockResolvedValueOnce(json(user, 201))
      .mockResolvedValueOnce(session(user))
      .mockResolvedValueOnce(session())
    store.startSync()
    await store.signup(input)
    expect(postMessage).toHaveBeenCalledExactlyOnceWith('session-changed')
    receive?.({ data: 'session-changed' })
    await vi.waitFor(() => expect(store.state.kind).toBe('anonymous'))
    expect(postMessage).toHaveBeenCalledTimes(1)
    store.$dispose()
    expect(close).toHaveBeenCalledOnce()
  })
})
