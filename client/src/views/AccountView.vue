<script setup lang="ts">
import AccountCard from '@/components/auth/AccountCard.vue'
import { useSessionStore } from '@/stores/session'
const session = useSessionStore()
</script>

<template>
  <AccountCard v-if="session.state.kind === 'authenticated'" :user="session.state.user" />
</template>
