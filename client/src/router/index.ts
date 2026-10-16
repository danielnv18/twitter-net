import { createRouter, createWebHistory } from 'vue-router'
import { useSessionStore } from '@/stores/session'
import AuthView from '@/views/AuthView.vue'
import AccountView from '@/views/AccountView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/', redirect: '/signup' },
    { path: '/signup', component: AuthView, props: { mode: 'signup' }, meta: { guest: true } },
    { path: '/signin', component: AuthView, props: { mode: 'login' }, meta: { guest: true } },
    { path: '/account', component: AccountView, meta: { requiresAuth: true } },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})

router.beforeEach(async (to) => {
  const session = useSessionStore()
  await session.initialize()
  if (to.meta.requiresAuth && session.state.kind === 'anonymous') {
    return { path: '/signin', query: { returnTo: to.fullPath } }
  }
  if (to.meta.guest && session.state.kind === 'authenticated') return '/account'
})

export default router
