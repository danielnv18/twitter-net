import { onScopeDispose, shallowRef } from 'vue'
import { defineStore } from 'pinia'
import {
  ApiError,
  createAuthApi,
  type Account,
  type LoginInput,
  type SignupInput,
} from '@/lib/auth-api'

export type SessionState =
  | { kind: 'loading' }
  | { kind: 'anonymous' }
  | { kind: 'authenticated'; user: Account; expiresAt: number }
  | { kind: 'unavailable'; message: string }

export const useSessionStore = defineStore('session', () => {
  const state = shallowRef<SessionState>({ kind: 'loading' })
  const pending = shallowRef(false)
  const api = createAuthApi()
  let initialization: Promise<void> | undefined
  let refreshRequest: Promise<void> | undefined
  let channel: BroadcastChannel | undefined
  let refreshQueued = false

  function readSession(): Promise<void> {
    if (refreshRequest) return refreshRequest
    refreshRequest = (async () => {
      try {
        const { user, expiresAt } = await api.session()
        state.value = user ? { kind: 'authenticated', user, expiresAt } : { kind: 'anonymous' }
      } catch (error) {
        state.value = {
          kind: 'unavailable',
          message: error instanceof Error ? error.message : 'We could not verify your session.',
        }
      } finally {
        refreshRequest = undefined
      }
    })()
    return refreshRequest
  }

  function refresh(): Promise<void> {
    if (pending.value) {
      refreshQueued = true
      return Promise.resolve()
    }
    return readSession()
  }

  function initialize() {
    initialization ??= readSession()
    return initialization
  }

  async function changeSession(action: () => Promise<unknown>) {
    if (pending.value) return
    pending.value = true
    try {
      await initialize()
      await readSession()
      if (state.value.kind === 'unavailable') throw new Error(state.value.message)
      await action()
      channel?.postMessage('session-changed')
      await readSession()
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) await readSession()
      throw error
    } finally {
      pending.value = false
      if (refreshQueued) {
        refreshQueued = false
        void readSession()
      }
    }
  }

  function startSync() {
    if (channel || typeof BroadcastChannel === 'undefined') return
    channel = new BroadcastChannel('twitter-net-session')
    channel.onmessage = (event: MessageEvent<unknown>) => {
      if (event.data === 'session-changed') void refresh()
    }
  }

  onScopeDispose(() => channel?.close())

  return {
    state,
    pending,
    initialize,
    refresh,
    startSync,
    signup: (input: SignupInput) => changeSession(() => api.signup(input)),
    login: (input: LoginInput) => changeSession(() => api.login(input)),
    logout: () => changeSession(() => api.logout()),
  }
})
