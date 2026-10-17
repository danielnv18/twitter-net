<script setup lang="ts">
import { onMounted, onUnmounted, watch } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { ArrowUpRight } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { useSessionStore } from '@/stores/session'

const session = useSessionStore()
const route = useRoute()
const router = useRouter()
let timer: ReturnType<typeof setTimeout> | undefined

function refreshVisibleSession() {
  if (document.visibilityState === 'visible') void session.refresh()
}

watch(
  () => [session.state, session.pending] as const,
  ([state, pending]) => {
    clearTimeout(timer)
    if (state.kind === 'authenticated' && !pending) {
      timer = setTimeout(
        () => void session.refresh(),
        Math.max(0, state.expiresAt - Date.now()) + 250,
      )
    }
    const kind = state.kind
    if (kind === 'authenticated' && route.meta.guest) void router.replace('/account')
    if (kind === 'anonymous' && route.meta.requiresAuth) {
      void router.replace({ path: '/signin', query: { returnTo: route.fullPath, expired: '1' } })
    }
  },
  { immediate: true },
)

onMounted(() => {
  session.startSync()
  window.addEventListener('focus', refreshVisibleSession)
  document.addEventListener('visibilitychange', refreshVisibleSession)
})

onUnmounted(() => {
  window.removeEventListener('focus', refreshVisibleSession)
  document.removeEventListener('visibilitychange', refreshVisibleSession)
  clearTimeout(timer)
})
</script>

<template>
  <div class="site-shell">
    <header class="site-header">
      <RouterLink class="wordmark" to="/" aria-label="Thread home">
        <span class="brand-symbol" aria-hidden="true">t</span> thread<span class="brand-period"
          >.</span
        >
      </RouterLink>
      <span class="header-caption">A little closer to your people.</span>
      <RouterLink
        v-if="session.state.kind === 'anonymous'"
        class="header-link"
        :to="route.path === '/signin' ? '/signup' : '/signin'"
      >
        {{ route.path === '/signin' ? 'Join the conversation' : 'Sign in' }}
        <ArrowUpRight aria-hidden="true" />
      </RouterLink>
      <span v-else-if="session.state.kind === 'authenticated'" class="header-account"
        >@{{ session.state.user.username }}</span
      >
    </header>

    <main id="main" class="main-layout">
      <aside class="intro-panel" aria-labelledby="intro-title">
        <p class="eyebrow"><span aria-hidden="true"></span> GOOD CONVERSATIONS START HERE</p>
        <h1 id="intro-title">Your people.<br />Your perspective.<br /><span>Your place.</span></h1>
        <p class="intro-description">
          A space for the thoughts, ideas, and everyday moments that bring us together.
        </p>
        <div class="conversation-art" aria-hidden="true">
          <div class="art-orbit"></div>
          <div class="art-line art-line-one"></div>
          <div class="art-line art-line-two"></div>
          <div class="art-bubble art-bubble-back"><span></span><span></span></div>
          <div class="art-bubble art-bubble-front"><span></span><span></span><span></span></div>
          <div class="art-spark">✳</div>
          <p>Every connection begins<br />with a conversation.</p>
        </div>
        <span class="intro-index">01 / MADE FOR CONNECTION</span>
      </aside>

      <section class="content-panel" aria-label="Your account">
        <div v-if="session.state.kind === 'loading'" class="status-panel" role="status">
          <p class="eyebrow">ONE MOMENT</p>
          <h2>Finding your place.</h2>
          <p>Checking your session…</p>
        </div>
        <div v-else-if="session.state.kind === 'unavailable'" class="status-panel" role="alert">
          <p class="eyebrow">CONNECTION INTERRUPTED</p>
          <h2>Let’s reconnect.</h2>
          <p>{{ session.state.message }}</p>
          <Button size="lg" @click="session.refresh()">Try again</Button>
        </div>
        <RouterView v-else />
      </section>
    </main>

    <footer class="site-footer">
      <span>Small moments. Real connections.</span>
      <span>THREAD © {{ new Date().getFullYear() }}</span>
    </footer>
  </div>
</template>
