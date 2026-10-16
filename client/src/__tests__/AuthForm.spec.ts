import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import AuthForm from '@/components/auth/AuthForm.vue'
import AccountCard from '@/components/auth/AccountCard.vue'
import { useSessionStore } from '@/stores/session'

const user = { id: 'account-1', name: 'María Sol', username: 'maria_sol' }
const fetchMock = vi.fn<typeof fetch>()
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })
let wrapper: VueWrapper | undefined
let store: ReturnType<typeof useSessionStore>

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
})
afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
  store?.$dispose()
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
})

async function setup(mode: 'signup' | 'login') {
  const pinia = createPinia()
  setActivePinia(pinia)
  store = useSessionStore()
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/signup', component: AuthForm },
      { path: '/signin', component: AuthForm },
      { path: '/account', component: { template: '<p>Account destination</p>' } },
    ],
  })
  await router.push(mode === 'signup' ? '/signup' : '/signin')
  await store.initialize()
  wrapper = mount(AuthForm, {
    props: { mode },
    global: { plugins: [pinia, router] },
    attachTo: document.body,
  })
  return { wrapper, router }
}

async function signupFields(form: VueWrapper, password = 'a memorable password phrase') {
  await form.get('#name').setValue(user.name)
  await form.get('#username').setValue(user.username)
  await form.get('#email').setValue('maria@example.com')
  await form.get('#password').setValue(password)
}

describe('auth forms', () => {
  it('requires all four signup fields, explains errors, and focuses the first invalid field without a POST', async () => {
    fetchMock.mockResolvedValue(json({ user: null, csrfToken: 'token' }))
    const { wrapper: form } = await setup('signup')
    await form.get('form').trigger('submit')
    await flushPromises()
    expect(form.findAll('input[aria-invalid="true"]')).toHaveLength(4)
    expect(document.activeElement?.id).toBe('name')
    expect(form.get('#password-error').text()).toContain('15–128')
    expect(form.get('#password').attributes('aria-describedby')).toBe('password-error')
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0)
  })

  it.each([14, 15, 128, 129])('validates signup password boundary %s', async (length) => {
    fetchMock.mockImplementation(async (_, init) =>
      init?.method === 'POST' ? json(user, 201) : json({ user: null, csrfToken: 'token' }),
    )
    const { wrapper: form } = await setup('signup')
    await signupFields(form, 'p'.repeat(length))
    await form.get('form').trigger('submit')
    await flushPromises()
    const posts = fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')
    expect(posts).toHaveLength(length >= 15 && length <= 128 ? 1 : 0)
  })

  it('submits name, username, email and the exact password and navigates to the account', async () => {
    let authenticated = false
    fetchMock.mockImplementation(async (_, init) => {
      if (init?.method === 'POST') {
        authenticated = true
        return json(user, 201)
      }
      return json({
        user: authenticated ? user : null,
        csrfToken: authenticated ? 'user-token' : 'anon-token',
      })
    })
    const { wrapper: form, router } = await setup('signup')
    await signupFields(form, '  my memorable password  ')
    await form.get('form').trigger('submit')
    await flushPromises()
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/auth/signup',
      expect.objectContaining({
        body: JSON.stringify({
          ...user,
          id: undefined,
          email: 'maria@example.com',
          password: '  my memorable password  ',
        }),
      }),
    )
    expect(router.currentRoute.value.path).toBe('/account')
    expect(store.state).toMatchObject({ kind: 'authenticated', user })
    expect(form.get('#password').element).toHaveProperty('value', '')
  })

  it('disables every field and submit action while the server is processing the signup', async () => {
    let authenticated = false
    let finish: ((response: Response) => void) | undefined
    const response = new Promise<Response>((resolve) => {
      finish = resolve
    })
    fetchMock.mockImplementation(async (_, init) =>
      init?.method === 'POST'
        ? response
        : json({ user: authenticated ? user : null, csrfToken: 'token' }),
    )
    const { wrapper: form } = await setup('signup')
    await signupFields(form)
    await form.get('form').trigger('submit')
    await flushPromises()
    expect(form.findAll('input:disabled')).toHaveLength(4)
    expect(form.get('button[type="submit"]').attributes('disabled')).toBeDefined()
    expect(form.get('form').attributes('aria-busy')).toBe('true')
    authenticated = true
    finish?.(json(user, 201))
    await flushPromises()
    expect(store.pending).toBe(false)
  })

  it('renders server field errors and associates them with focused inputs', async () => {
    fetchMock.mockImplementation(async (_, init) =>
      init?.method === 'POST'
        ? json({ errors: { username: ['Choose another username.'] } }, 400)
        : json({ user: null, csrfToken: 'token' }),
    )
    const { wrapper: form } = await setup('signup')
    await signupFields(form)
    await form.get('form').trigger('submit')
    await flushPromises()
    expect(form.get('#username-error').text()).toBe('Choose another username.')
    expect(form.get('#username').attributes('aria-describedby')).toBe('username-error')
    expect(document.activeElement?.id).toBe('username')
    expect(form.get('[role="alert"]').text()).toContain('highlighted fields')
  })

  it('accepts email as the login identifier and shows a generic invalid-credentials error', async () => {
    fetchMock.mockImplementation(async (_, init) =>
      init?.method === 'POST'
        ? json({ title: 'Invalid credentials' }, 401)
        : json({ user: null, csrfToken: 'token' }),
    )
    const { wrapper: form, router } = await setup('login')
    await form.get('#identifier').setValue('maria@example.com')
    await form.get('#password').setValue('a wrong password')
    await form.get('form').trigger('submit')
    await flushPromises()
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/auth/login',
      expect.objectContaining({
        body: JSON.stringify({ identifier: 'maria@example.com', password: 'a wrong password' }),
      }),
    )
    expect(form.get('[role="alert"]').text()).toContain('username, email, or password is incorrect')
    expect(document.activeElement?.getAttribute('role')).toBe('alert')
    expect(router.currentRoute.value.path).toBe('/signin')
  })

  it('displays the account name and username as text without exposing an email', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    store = useSessionStore()
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: AccountCard }],
    })
    await router.push('/')
    wrapper = mount(AccountCard, {
      props: { user: { ...user, name: '<script>alert(1)</script>' } },
      global: { plugins: [pinia, router] },
    })
    expect(wrapper.text()).toContain('<script>alert(1)</script>')
    expect(wrapper.text()).toContain('@maria_sol')
    expect(wrapper.find('script').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('maria@example.com')
  })
})
