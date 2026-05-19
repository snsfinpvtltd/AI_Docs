/**
 * Thin wrapper around sessionStorage for social-provider (Google / GitHub) tokens.
 * MSAL manages Microsoft tokens internally; this store handles the rest.
 * sessionStorage is used — never localStorage — in line with the project security rules.
 */

const TOKEN_KEY = 'ca_social_token'
const USER_KEY  = 'ca_social_user'

export interface SocialUser {
  email:    string
  name:     string
  provider: 'google' | 'github'
}

export function storeSocialAuth(token: string, user: SocialUser): void {
  sessionStorage.setItem(TOKEN_KEY, token)
  sessionStorage.setItem(USER_KEY,  JSON.stringify(user))
}

export function getSocialToken(): string | null {
  return sessionStorage.getItem(TOKEN_KEY)
}

export function getSocialUser(): SocialUser | null {
  const raw = sessionStorage.getItem(USER_KEY)
  if (!raw) return null
  try {
    return JSON.parse(raw) as SocialUser
  } catch {
    return null
  }
}

export function clearSocialAuth(): void {
  sessionStorage.removeItem(TOKEN_KEY)
  sessionStorage.removeItem(USER_KEY)
}

/** Decode the JWT payload without verifying the signature (display purposes only). */
export function decodeJwtPayload(token: string): Record<string, string> {
  try {
    const b64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')
    return JSON.parse(atob(b64)) as Record<string, string>
  } catch {
    return {}
  }
}
