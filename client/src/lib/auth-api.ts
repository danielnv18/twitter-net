export interface Account {
  id: string
  name: string
  username: string
}

export interface SignupInput {
  name: string
  username: string
  email: string
  password: string
}

export interface LoginInput {
  identifier: string
  password: string
}

export type FieldErrors = Record<string, string[]>

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly errors: FieldErrors = {},
    public readonly code = '',
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function parseAccount(value: unknown): Account {
  if (
    !isRecord(value) ||
    typeof value.id !== 'string' ||
    typeof value.name !== 'string' ||
    typeof value.username !== 'string'
  )
    throw new Error('The server returned an invalid account. Please try again.')
  return { id: value.id, name: value.name, username: value.username }
}

async function responseError(response: Response): Promise<ApiError> {
  const body: unknown = await response.json().catch(() => null)
  const errors: FieldErrors = {}
  if (isRecord(body) && isRecord(body.errors)) {
    for (const [field, messages] of Object.entries(body.errors)) {
      if (Array.isArray(messages) && messages.every((item) => typeof item === 'string')) {
        errors[field] = messages
      }
    }
  }
  const code = isRecord(body) && typeof body.code === 'string' ? body.code : ''
  let message = 'Something went wrong. Please try again.'
  if (response.status === 401) message = 'The username, email, or password is incorrect.'
  else if (response.status === 409)
    message =
      'An account could not be created with those details. Try different details or sign in.'
  else if (response.status === 429) {
    const seconds = Number(response.headers.get('Retry-After'))
    message =
      Number.isFinite(seconds) && seconds > 0
        ? `Too many attempts. Please try again in ${Math.ceil(seconds)} seconds.`
        : 'Too many attempts. Please wait a little before trying again.'
  } else if (code.toLowerCase().includes('csrf')) {
    message = 'Your security token has expired. Please submit the form again.'
  } else if (response.status === 400 && Object.keys(errors).length) {
    message = 'Please check the highlighted fields.'
  }
  return new ApiError(response.status, message, errors, code)
}

export function createAuthApi() {
  let csrfToken: string | null = null

  async function request(path: string, init?: RequestInit): Promise<Response> {
    let response: Response
    try {
      response = await fetch(`/api/auth/${path}`, {
        credentials: 'same-origin',
        cache: 'no-store',
        ...init,
      })
    } catch {
      throw new Error('We could not reach the server. Check your connection and try again.')
    }
    if (!response.ok) throw await responseError(response)
    return response
  }

  async function session() {
    csrfToken = null
    const response = await request('session')
    const body: unknown = await response.json()
    if (!isRecord(body) || typeof body.csrfToken !== 'string' || !body.csrfToken) {
      throw new Error('We could not verify your session. Please try again.')
    }
    const user = body.user === null ? null : parseAccount(body.user)
    csrfToken = body.csrfToken
    const deadlines = [body.expiresAtUtc, body.idleExpiresAtUtc]
      .filter((value): value is string => typeof value === 'string')
      .map((value) => Date.parse(value))
      .filter(Number.isFinite)
    const serverNow = typeof body.serverNowUtc === 'string' ? Date.parse(body.serverNowUtc) : NaN
    const remaining =
      deadlines.length && Number.isFinite(serverNow)
        ? Math.max(0, Math.min(...deadlines) - serverNow)
        : 30 * 60_000
    return { user, expiresAt: Date.now() + remaining }
  }

  async function mutate(path: string, input?: SignupInput | LoginInput) {
    if (!csrfToken) throw new Error('Please reconnect before submitting the form.')
    try {
      return await request(path, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken },
        body: input ? JSON.stringify(input) : undefined,
      })
    } finally {
      csrfToken = null
    }
  }

  return {
    session,
    async signup(input: SignupInput) {
      return parseAccount(await (await mutate('signup', input)).json())
    },
    async login(input: LoginInput) {
      return parseAccount(await (await mutate('login', input)).json())
    },
    async logout() {
      await mutate('logout')
    },
  }
}
