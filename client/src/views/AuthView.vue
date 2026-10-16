<script setup lang="ts">
import AuthForm from '@/components/auth/AuthForm.vue'
defineProps<{ mode: 'signup' | 'login' }>()
</script>

<template>
  <AuthForm :key="mode" :mode="mode" />
</template>
