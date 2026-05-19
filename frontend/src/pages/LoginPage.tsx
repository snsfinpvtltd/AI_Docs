import { type ReactElement, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMsal } from '@azure/msal-react'
import { InteractionStatus } from '@azure/msal-browser'
import { useGoogleLogin } from '@react-oauth/google'
import axios from 'axios'
import ClinevoLogo from '../components/ClinevoLogo'
import { useAuth } from '../auth/AuthContext'
import { storeSocialAuth } from '../auth/socialTokenStore'
import { notifySocialAuthUpdated } from '../auth/AuthContext'
import type { SocialAuthResponse } from '../types/api'

// ── Config flags ─────────────────────────────────────────────────────────────
// Computed at module level — never changes at runtime.
const GOOGLE_CLIENT_ID = (import.meta.env.VITE_GOOGLE_CLIENT_ID as string) || ''
const GOOGLE_ENABLED   = Boolean(GOOGLE_CLIENT_ID)
const GITHUB_API_BASE  = (import.meta.env.VITE_API_BASE_URL as string) || ''

// ── Google login sub-component ───────────────────────────────────────────────
// Isolated so useGoogleLogin is only called when Google is actually configured.
// This prevents initTokenClient from being called with an empty clientId.

interface GoogleBtnProps {
  disabled:  boolean
  isLoading: boolean
  onStart:   () => void
  onSuccess: (token: string) => void
  onError:   () => void
}

function GoogleLoginButton({ disabled, isLoading, onStart, onSuccess, onError }: GoogleBtnProps): ReactElement {
  const trigger = useGoogleLogin({
    scope:     'openid email profile',
    flow:      'implicit',
    onSuccess: (resp) => onSuccess(resp.access_token),
    onError:   () => onError(),
  })

  return (
    <button
      type="button"
      disabled={disabled}
      onClick={() => { onStart(); trigger() }}
      className="relative flex items-center w-full rounded-[9px] border border-[#dadce0] bg-white text-[#3c4043] font-medium text-sm hover:bg-[#f8f9fa] hover:border-[#c6c6c6] hover:shadow-sm transition-all disabled:opacity-60 disabled:cursor-not-allowed overflow-hidden"
      style={{ height: '44px' }}
    >
      <span className="flex items-center justify-center w-11 h-full border-r border-[#dadce0] flex-shrink-0">
        {isLoading
          ? <Spinner className="text-gray-400" />
          : <img src="/google-logo.svg" alt="" width={20} height={20} />}
      </span>
      <span className="flex-1 text-center pr-11">Sign in with Google</span>
    </button>
  )
}

// ── LoginPage ─────────────────────────────────────────────────────────────────

export default function LoginPage(): ReactElement {
  const { instance, inProgress } = useMsal()
  const { isAuthenticated }      = useAuth()
  const navigate                  = useNavigate()
  const [error, setError]         = useState<string | null>(null)
  const [loading, setLoading]     = useState<string | null>(null)

  // Redirect to dashboard when already authenticated.
  useEffect(() => {
    if (isAuthenticated && inProgress === InteractionStatus.None) {
      navigate('/dashboard', { replace: true })
    }
  }, [isAuthenticated, inProgress, navigate])

  // Surface auth_error from GitHub callback redirect.
  useEffect(() => {
    const params    = new URLSearchParams(window.location.search)
    const authError = params.get('auth_error')
    if (authError) setError(`GitHub sign-in failed (${authError}). Please try again.`)
  }, [])

  // ── Microsoft ──────────────────────────────────────────────────────────────
  const handleMicrosoftLogin = async (): Promise<void> => {
    setError(null)
    setLoading('microsoft')
    try {
      await instance.loginRedirect({ scopes: ['openid', 'profile', 'email'] })
    } catch {
      setError('Microsoft sign-in failed. Please try again.')
      setLoading(null)
    }
  }

  // ── Google ─────────────────────────────────────────────────────────────────
  const handleGoogleAccessToken = async (accessToken: string): Promise<void> => {
    try {
      const res = await axios.post<SocialAuthResponse>(`${GITHUB_API_BASE}/api/auth/google`, {
        token: accessToken,
      })
      storeSocialAuth(res.data.token, {
        email:    res.data.email,
        name:     res.data.name,
        provider: 'google',
      })
      notifySocialAuthUpdated()
      navigate('/dashboard', { replace: true })
    } catch {
      setError('Google sign-in failed. Please try again.')
    } finally {
      setLoading(null)
    }
  }

  // ── GitHub ─────────────────────────────────────────────────────────────────
  const handleGitHubLogin = (): void => {
    setLoading('github')
    window.location.href = `${GITHUB_API_BASE}/api/auth/github`
  }

  return (
    <div className="min-h-screen flex bg-brand-surface">

      {/* ── Left panel — Clinevo gradient ─────────────────────────────────── */}
      <div className="hidden lg:flex lg:flex-col lg:w-[46%] relative overflow-hidden bg-brand-gradient">
        <div className="absolute inset-0 opacity-10"
          style={{ backgroundImage: "url(\"data:image/svg+xml,%3Csvg viewBox='0 0 256 256' xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='4' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)' opacity='1'/%3E%3C/svg%3E\")" }}
        />
        <div className="relative flex flex-col h-full px-12 py-10">
          <div className="flex items-center gap-3">
            <div className="bg-white rounded-xl p-1.5 shadow-lg">
              <ClinevoLogo variant="icon" className="w-10 h-10" />
            </div>
            <div>
              <p className="text-white font-bold text-lg font-heading leading-tight">Clinevo</p>
              <p className="text-white/60 text-xs tracking-widest font-medium uppercase">Technologies</p>
            </div>
          </div>
          <div className="flex-1 flex flex-col justify-center">
            <p className="text-white/60 text-sm font-semibold uppercase tracking-[2px] mb-4">
              Clinical Trial Agent
            </p>
            <h2 className="text-white text-4xl font-bold font-heading leading-snug mb-6">
              Cloud-based Software<br />
              Solutions for Life<br />
              Sciences Companies
            </h2>
            <p className="text-white/70 text-base leading-relaxed max-w-sm">
              AI-powered patient data analysis and automated report generation — integrated with
              GenAI for Drug Safety, Quality Management, and Clinical Trials.
            </p>
            <div className="flex flex-wrap gap-2 mt-8">
              {['Pharmacovigilance', 'Quality Assurance', 'Clinical Trials', 'Data Warehouse'].map((f) => (
                <span key={f}
                  className="px-3 py-1 bg-white/15 border border-white/25 rounded-full text-white/90 text-xs font-medium backdrop-blur-sm">
                  {f}
                </span>
              ))}
            </div>
          </div>
          <div className="flex gap-6 text-white/40 text-[11px] font-medium">
            <span>SOC 2 Type II</span>
            <span>HIPAA-Ready</span>
            <span>Azure-Native</span>
          </div>
        </div>
      </div>

      {/* ── Right panel — sign in form ─────────────────────────────────────── */}
      <div className="flex flex-col justify-center flex-1 px-8 py-12 lg:px-14">
        <div className="w-full max-w-sm mx-auto">
          {/* Mobile logo */}
          <div className="lg:hidden flex justify-center mb-10">
            <ClinevoLogo variant="full" className="h-12" />
          </div>

          <div className="mb-8">
            <h2 className="text-2xl font-bold text-[#222222] font-heading">Welcome back</h2>
            <p className="mt-1.5 text-sm text-brand-muted">
              Sign in to access the clinical reporting platform.
            </p>
          </div>

          {/* Error banner */}
          {error && (
            <div className="mb-4 rounded-[8px] bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
              {error}
            </div>
          )}

          {/* Sign-in buttons */}
          <div className="flex flex-col gap-3">

            {/* Microsoft */}
            <button
              type="button"
              disabled={loading !== null}
              onClick={() => void handleMicrosoftLogin()}
              className="btn-brand w-full py-3 text-base gap-3 disabled:opacity-60 disabled:cursor-not-allowed"
            >
              {loading === 'microsoft' ? <Spinner /> : <MicrosoftIcon />}
              Sign in with Microsoft
            </button>

            {/* Google — only rendered when VITE_GOOGLE_CLIENT_ID is configured.
                GoogleLoginButton calls useGoogleLogin internally, so the hook is
                only invoked when this component actually mounts. */}
            {GOOGLE_ENABLED && (
              <GoogleLoginButton
                disabled={loading !== null}
                isLoading={loading === 'google'}
                onStart={() => { setError(null); setLoading('google') }}
                onSuccess={(token) => void handleGoogleAccessToken(token)}
                onError={() => { setError('Google sign-in was cancelled or failed.'); setLoading(null) }}
              />
            )}

            {/* GitHub */}
            <button
              type="button"
              disabled={loading !== null}
              onClick={handleGitHubLogin}
              className="relative flex items-center w-full rounded-[9px] bg-[#24292f] text-white font-medium text-sm hover:bg-[#1b1f23] transition-all disabled:opacity-60 disabled:cursor-not-allowed overflow-hidden"
              style={{ height: '44px' }}
            >
              <span className="flex items-center justify-center w-11 h-full border-r border-white/10 flex-shrink-0">
                {loading === 'github'
                  ? <Spinner className="text-white/70" />
                  : <img src="/github-logo.svg" alt="" width={20} height={20} />}
              </span>
              <span className="flex-1 text-center pr-11">Sign in with GitHub</span>
            </button>

          </div>

          {/* Divider */}
          <div className="relative flex items-center gap-3 my-6">
            <div className="flex-1 border-t border-brand-border" />
            <span className="text-xs text-brand-muted">Protected by Azure Entra ID &amp; OAuth 2.0</span>
            <div className="flex-1 border-t border-brand-border" />
          </div>

          {/* Trust info */}
          <div className="rounded-[9px] bg-white border border-brand-border p-4 shadow-card">
            <div className="flex items-start gap-3">
              <svg className="w-4 h-4 text-[#1EB5C7] flex-shrink-0 mt-0.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                  d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
              </svg>
              <p className="text-xs text-brand-charcoal leading-relaxed">
                This platform processes <strong>synthetic and anonymised</strong> clinical trial data only.
                All sessions are encrypted in transit.
              </p>
            </div>
          </div>

          <p className="mt-8 text-center text-[11px] text-brand-muted">
            © {new Date().getFullYear()} Clinevo Technologies. All rights reserved.
          </p>
        </div>
      </div>
    </div>
  )
}

// ── Icon components ───────────────────────────────────────────────────────────

function Spinner({ className = 'text-white' }: { className?: string }): ReactElement {
  return (
    <svg className={`animate-spin w-5 h-5 ${className}`} fill="none" viewBox="0 0 24 24">
      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
    </svg>
  )
}

function MicrosoftIcon(): ReactElement {
  return (
    <svg viewBox="0 0 21 21" width="18" height="18" aria-hidden="true">
      <rect x="1"  y="1"  width="9" height="9" fill="#f25022" />
      <rect x="11" y="1"  width="9" height="9" fill="#7fba00" />
      <rect x="1"  y="11" width="9" height="9" fill="#00a4ef" />
      <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
    </svg>
  )
}

