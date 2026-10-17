<script setup lang="ts">
import { computed, shallowRef } from 'vue'
import { useRouter } from 'vue-router'
import { ArrowUpRight, LogOut, Check } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Alert, AlertDescription } from '@/components/ui/alert'
import type { Account } from '@/lib/auth-api'
import { useSessionStore } from '@/stores/session'

const props = defineProps<{ user: Account }>()
const session = useSessionStore()
const router = useRouter()
const errorMessage = shallowRef('')
const initials = computed(() =>
  props.user.name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0])
    .join('')
    .toUpperCase(),
)

async function logout() {
  errorMessage.value = ''
  try {
    await session.logout()
    await router.replace('/signin')
  } catch (error) {
    errorMessage.value =
      error instanceof Error ? error.message : 'We could not sign you out. Please try again.'
  }
}
</script>

<template>
  <div class="auth-content account-content">
    <p class="eyebrow">RIGHT WHERE YOU BELONG</p>
    <h2 class="form-heading">You’re in.</h2>
    <p class="form-description">Welcome to your little corner of Thread.</p>
    <div class="profile-card">
      <div class="profile-topline">
        <span>YOUR ACCOUNT</span><ArrowUpRight aria-hidden="true" />
      </div>
      <div class="profile-avatar" aria-hidden="true">{{ initials }}</div>
      <h3 class="profile-name">{{ user.name }}</h3>
      <p class="profile-username">@{{ user.username }}</p>
      <div class="profile-status"><Check aria-hidden="true" /> Signed in</div>
    </div>
    <Alert v-if="errorMessage" variant="destructive" class="mb-5"
      ><AlertDescription>{{ errorMessage }}</AlertDescription></Alert
    >
    <Button
      variant="outline"
      size="lg"
      class="w-full min-h-12"
      :disabled="session.pending"
      @click="logout"
    >
      <LogOut data-icon="inline-start" aria-hidden="true" />{{
        session.pending ? 'Signing out…' : 'Sign out'
      }}
    </Button>
    <p class="private-note">
      This is your account on this device. Sign out when you’re finished on a shared computer.
    </p>
  </div>
</template>
