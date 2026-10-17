import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import App from '@/App.vue'
import { useSessionStore } from '@/stores/session'

const fetchMock = vi.fn<typeof fetch>()
const user = { id: 'account-1', name: 'María Sol', username: 'maria_sol' }
const json = (body: unknown) => new Response(JSON.stringify(body))
let wrapper: VueWrapper | undefined
let store: ReturnType<typeof useSessionStore>

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
})
afterEach(() => {
  wrapper?.unmount()
  store?.$dispose()
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

async function setup(path = '/account') {
  const pinia = createPinia()
  setActivePinia(pinia)
  store = useSessionStore()
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/account',
        component: { template: '<p>Protected account content</p>' },
        meta: { requiresAuth: true },
      },
      { path: '/signin', component: { template: '<p>Sign in form</p>' }, meta: { guest: true } },
      { path: '/', component: { template: '<p>Home</p>' } },
    ],
  })
  await router.push(path)
  await store.initialize()
  wrapper = mount(App, { global: { plugins: [pinia, router] } })
  return { wrapper, router }
}

describe('session shell', () => {
  it('hides protected content while unavailable and recovers with a read-only retry', async () => {
    fetchMock
      .mockRejectedValueOnce(new TypeError('offline'))
      .mockResolvedValueOnce(json({ user, csrfToken: 'token' }))
    const { wrapper: app } = await setup()
    expect(app.text()).toContain('Let’s reconnect.')
    expect(app.text()).not.toContain('Protected account content')
    await app.get('button').trigger('click')
    await flushPromises()
    expect(app.text()).toContain('Protected account content')
    expect(app.text()).toContain('@maria_sol')
    expect(fetchMock.mock.calls.every(([, init]) => !init?.method)).toBe(true)
  })

  it('refreshes on window focus and removes an expired account immediately', async () => {
    fetchMock
      .mockResolvedValueOnce(json({ user, csrfToken: 'token' }))
      .mockResolvedValueOnce(json({ user: null, csrfToken: 'anon-token' }))
    const { wrapper: app, router } = await setup()
    window.dispatchEvent(new Event('focus'))
    await flushPromises()
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(router.currentRoute.value.path).toBe('/signin')
    expect(app.text()).not.toContain('Protected account content')
  })

  it('leaves the guest form when a session refresh discovers sign-in from another tab', async () => {
    fetchMock
      .mockResolvedValueOnce(json({ user: null, csrfToken: 'anon-token' }))
      .mockResolvedValueOnce(json({ user, csrfToken: 'token' }))
    const { router } = await setup('/signin')
    await store.refresh()
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/account')
    expect(wrapper?.text()).toContain('Protected account content')
  })

  it('uses server duration despite clock skew without keepalive polling then clears expired content', async () => {
    vi.useFakeTimers()
    const now = Date.now() + 24 * 60 * 60_000
    fetchMock
      .mockResolvedValueOnce(
        json({
          user,
          csrfToken: 'token',
          serverNowUtc: new Date(now).toISOString(),
          expiresAtUtc: new Date(now + 8 * 60 * 60_000).toISOString(),
          idleExpiresAtUtc: new Date(now + 30 * 60_000).toISOString(),
        }),
      )
      .mockResolvedValueOnce(json({ user: null, csrfToken: 'anonymous-token' }))
    const { wrapper: app, router } = await setup()
    await vi.advanceTimersByTimeAsync(29 * 60_000)
    expect(fetchMock).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(60_000 + 300)
    await flushPromises()
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(router.currentRoute.value.path).toBe('/signin')
    expect(app.text()).not.toContain('Protected account content')
  })
})
