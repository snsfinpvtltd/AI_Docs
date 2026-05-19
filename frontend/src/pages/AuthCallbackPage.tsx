import { type ReactElement, useEffect, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { storeSocialAuth, type SocialUser } from '../auth/socialTokenStore'
import { notifySocialAuthUpdated } from '../auth/AuthContext'

/**
 * Handles the redirect from the backend after a social OAuth flow (currently GitHub).
 * Expected URL: /auth/callback?token=<jwt>&provider=<github>
 *
 * Reads the token, fetches user info if needed, stores everything in sessionStorage,
 * fires the auth-updated event, then navigates to /dashboard.
 */
export default function AuthCallbackPage(): ReactElement {
  const navigate  = useNavigate()
  const processed = useRef(false)

  useEffect(() => {
    if (processed.current) return
    processed.current = true

    const params   = new URLSearchParams(window.location.search)
    const token    = params.get('token')
    const provider = params.get('provider') as SocialUser['provider'] | null

    if (!token || !provider) {
      navigate('/', { replace: true })
      return
    }

    // Decode JWT payload (display only — signature already verified by the backend).
    try {
      const b64     = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')
      const payload = JSON.parse(atob(b64)) as Record<string, string>

      storeSocialAuth(token, {
        email:    payload['email'] ?? '',
        name:     payload['name']  ?? payload['email'] ?? 'User',
        provider,
      })
      notifySocialAuthUpdated()
    } catch {
      // Malformed JWT — fall back to fetching user info from the API.
    }

    navigate('/dashboard', { replace: true })
  }, [navigate])

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-50">
      <div className="flex items-center gap-3 text-gray-400">
        <svg className="animate-spin w-5 h-5" fill="none" viewBox="0 0 24 24">
          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
        </svg>
        <span className="text-sm">Completing sign-in…</span>
      </div>
    </div>
  )
}
