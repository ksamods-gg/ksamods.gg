const BACKSLASH = String.fromCharCode(92)

/**
 * Validates a post-sign-in redirect target. Only same-origin relative paths
 * into known areas are accepted: an open redirect on a sign-in page is a
 * phishing primitive, so anything unrecognised falls back to the home page.
 */
export function safeNext(value: string | null | undefined) {
  if (!value) return '/'
  // "//host" and "/\host" are protocol-relative and leave the origin.
  if (!value.startsWith('/')) return '/'
  if (value.startsWith('//') || value.startsWith(`/${BACKSLASH}`)) return '/'
  if (value.includes('://')) return '/'
  return value.startsWith('/mods/') || value === '/account' ? value : '/'
}
