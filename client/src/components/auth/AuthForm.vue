<script setup lang="ts">
import { computed, nextTick, reactive, shallowRef, useTemplateRef } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { ArrowRight, ArrowUpRight } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Field, FieldDescription, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { ApiError, type FieldErrors } from '@/lib/auth-api'
import { localReturnPath } from '@/lib/navigation'
import { useSessionStore } from '@/stores/session'

const props = defineProps<{ mode: 'signup' | 'login' }>()
const route = useRoute()
const router = useRouter()
const session = useSessionStore()
const formElement = useTemplateRef<HTMLFormElement>('formElement')
const values = reactive({ name: '', username: '', email: '', identifier: '', password: '' })
const errors = shallowRef<FieldErrors>({})
const errorMessage = shallowRef('')
const isSignup = computed(() => props.mode === 'signup')
const fields = computed(() =>
  isSignup.value
    ? ([
        {
          key: 'name',
          label: 'Name',
          autocomplete: 'name',
          type: 'text',
          max: 100,
          min: 1,
          placeholder: 'What should we call you?',
        },
        {
          key: 'username',
          label: 'Username',
          autocomplete: 'username',
          type: 'text',
          max: 30,
          min: 3,
          placeholder: 'Your unique handle',
        },
        {
          key: 'email',
          label: 'Email',
          autocomplete: 'email',
          type: 'email',
          max: 254,
          min: 1,
          placeholder: 'you@example.com',
        },
        {
          key: 'password',
          label: 'Password',
          autocomplete: 'new-password',
          type: 'password',
          max: 128,
          min: 15,
          placeholder: 'Make it a memorable phrase',
        },
      ] as const)
    : ([
        {
          key: 'identifier',
          label: 'Username or email',
          autocomplete: 'username',
          type: 'text',
          max: 254,
          min: 1,
          placeholder: 'Your username or email address',
        },
        {
          key: 'password',
          label: 'Password',
          autocomplete: 'current-password',
          type: 'password',
          max: 128,
          min: 1,
          placeholder: 'Enter your password',
        },
      ] as const),
)

async function focusError() {
  await nextTick()
  formElement.value?.querySelector<HTMLInputElement>('[aria-invalid="true"]')?.focus()
  if (!Object.keys(errors.value).length) {
    formElement.value?.querySelector<HTMLElement>('[role="alert"]')?.focus()
  }
}

async function submit() {
  if (session.pending) return
  const invalid: FieldErrors = {}
  if (isSignup.value) {
    if (!values.name.trim() || values.name.length > 100 || /\p{Cc}/u.test(values.name))
      invalid.name = ['Enter a name between 1 and 100 characters.']
    if (!/^[a-zA-Z0-9_]{3,30}$/.test(values.username.trim()))
      invalid.username = ['Use 3–30 letters, numbers, or underscores.']
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(values.email.trim()) || values.email.trim().length > 254)
      invalid.email = ['Enter a valid email address.']
    if (values.password.length < 15 || values.password.length > 128)
      invalid.password = ['Use 15–128 characters. A phrase works well.']
  } else {
    if (!values.identifier.trim()) invalid.identifier = ['Enter your username or email.']
    if (!values.password) invalid.password = ['Enter your password.']
  }
  errors.value = invalid
  errorMessage.value = ''
  if (Object.keys(invalid).length) {
    await focusError()
    return
  }
  try {
    if (isSignup.value) {
      await session.signup({
        name: values.name.trim(),
        username: values.username.trim(),
        email: values.email.trim(),
        password: values.password,
      })
    } else {
      await session.login({ identifier: values.identifier.trim(), password: values.password })
    }
    values.password = ''
    await router.replace(localReturnPath(route.query.returnTo))
  } catch (error) {
    errors.value = error instanceof ApiError ? error.errors : {}
    errorMessage.value =
      error instanceof Error ? error.message : 'Something went wrong. Please try again.'
    await focusError()
  }
}
</script>

<template>
  <div class="auth-content">
    <p class="eyebrow">{{ isSignup ? 'YOUR NEXT CHAPTER' : 'GOOD TO SEE YOU AGAIN' }}</p>
    <h2 class="form-heading">{{ isSignup ? 'Find your thread.' : 'Welcome back.' }}</h2>
    <p class="form-description">
      {{
        isSignup
          ? 'Create an account. Make yourself at home.'
          : 'Sign in and pick up where you left off.'
      }}
    </p>

    <form
      ref="formElement"
      class="auth-form"
      novalidate
      :aria-busy="session.pending"
      @submit.prevent="submit"
    >
      <Alert v-if="errorMessage" variant="destructive" tabindex="-1" class="form-alert">
        <AlertDescription>{{ errorMessage }}</AlertDescription>
      </Alert>
      <p v-else-if="route.query.expired" class="session-notice" role="status">
        Your session has ended. Sign in to continue.
      </p>
      <FieldGroup class="gap-5">
        <Field
          v-for="field in fields"
          :key="field.key"
          :data-invalid="Boolean(errors[field.key]?.length) || undefined"
          :data-disabled="session.pending || undefined"
        >
          <FieldLabel :for="field.key">{{ field.label }}</FieldLabel>
          <Input
            :id="field.key"
            v-model="values[field.key]"
            :name="field.key"
            :type="field.type"
            :autocomplete="field.autocomplete"
            :minlength="field.min"
            :maxlength="field.max"
            :placeholder="field.placeholder"
            :disabled="session.pending"
            :spellcheck="false"
            :autocapitalize="field.key === 'name' ? 'words' : 'none'"
            :aria-invalid="Boolean(errors[field.key]?.length)"
            :aria-describedby="
              errors[field.key]?.length
                ? `${field.key}-error`
                : isSignup && ['username', 'password'].includes(field.key)
                  ? `${field.key}-hint`
                  : undefined
            "
            required
          />
          <FieldDescription
            v-if="errors[field.key]?.length"
            :id="`${field.key}-error`"
            class="field-error"
            >{{ errors[field.key]?.join(' ') }}</FieldDescription
          >
          <FieldDescription v-else-if="isSignup && field.key === 'username'" id="username-hint"
            >3–30 letters, numbers, or underscores.</FieldDescription
          >
          <FieldDescription v-else-if="isSignup && field.key === 'password'" id="password-hint"
            >At least 15 characters. Try a memorable phrase.</FieldDescription
          >
        </Field>
      </FieldGroup>
      <Button type="submit" size="lg" class="w-full min-h-12 mt-7" :disabled="session.pending">
        {{
          session.pending
            ? isSignup
              ? 'Creating your account…'
              : 'Signing in…'
            : isSignup
              ? 'Create account'
              : 'Sign in'
        }}
        <ArrowRight v-if="!session.pending" data-icon="inline-end" aria-hidden="true" />
      </Button>
    </form>
    <p class="form-switch">
      {{ isSignup ? 'Already found your people?' : 'New around here?' }}
      <RouterLink
        :to="{
          path: isSignup ? '/signin' : '/signup',
          query: route.query.returnTo ? { returnTo: localReturnPath(route.query.returnTo) } : {},
        }"
      >
        {{ isSignup ? 'Sign in' : 'Create an account' }} <ArrowUpRight aria-hidden="true" />
      </RouterLink>
    </p>
    <p v-if="isSignup" class="private-note">
      Your email stays private. Your name and username are how you’ll be seen.
    </p>
  </div>
</template>
