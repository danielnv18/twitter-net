export function localReturnPath(value: unknown): string {
  return typeof value === 'string' &&
    value.startsWith('/') &&
    !value.startsWith('//') &&
    !value.includes('\\') &&
    !value.startsWith('/signin') &&
    !value.startsWith('/signup')
    ? value
    : '/account'
}
