import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactElement,
  type ReactNode,
} from 'react'
import { useIsAuthenticated, useMsal } from '@azure/msal-react'
import { InteractionStatus, InteractionRequiredAuthError } from '@azure/msal-browser'
import { msalInstance, apiScopes } from './msalConfig'
import {
  clearSocialAuth,
  getSocialToken,
  getSocialUser,
  type SocialUser,
} from './socialTokenStore'

export type AuthProvider = 'microsoft' | 'google' | 'github'

export interface AuthUser {
  email:    string
  name:     string
  provider: AuthProvider
}

interface AuthContextValue {
  isAuthenticated: boolean
  isLoading:       boolean
  user:            AuthUser | null
  getAccessToken:  () => Promise<string | null>
  logout:          () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }): ReactElement {
  const { instance, inProgress, accounts } = useMsal()
  const isMsalAuthenticated                = useIsAuthenticated()

  const [socialUser,  setSocialUser]  = useState<SocialUser  | null>(() => getSocialUser())
  const [socialToken, setSocialToken] = useState<string | null>(() => getSocialToken())

  // Re-read sessionStorage when the window regains focus (e.g. after GitHub redirect).
  useEffect(() => {
    const onFocus = (): void => {
      setSocialToken(getSocialToken())
      setSocialUser(getSocialUser())
    }
    window.addEventListener('focus', onFocus)
    return () => window.removeEventListener('focus', onFocus)
  }, [])

  // Expose a method so AuthCallbackPage can push social auth into this context.
  useEffect(() => {
    const onStorage = (): void => {
      setSocialToken(getSocialToken())
      setSocialUser(getSocialUser())
    }
    window.addEventListener('ca:social-auth-updated', onStorage)
    return () => window.removeEventListener('ca:social-auth-updated', onStorage)
  }, [])

  const isLoading = inProgress !== InteractionStatus.None

  const isAuthenticated = isMsalAuthenticated || socialToken !== null

  const user = useMemo<AuthUser | null>(() => {
    if (isMsalAuthenticated && accounts.length > 0) {
      const acc = accounts[0]
      return {
        email:    acc.username,
        name:     acc.name ?? acc.username,
        provider: 'microsoft',
      }
    }
    if (socialUser) {
      return { email: socialUser.email, name: socialUser.name, provider: socialUser.provider }
    }
    return null
  }, [isMsalAuthenticated, accounts, socialUser])

  const getAccessToken = useCallback(async (): Promise<string | null> => {
    // Microsoft — use MSAL silent/popup flow.
    if (isMsalAuthenticated && accounts.length > 0) {
      try {
        const result = await msalInstance.acquireTokenSilent({ ...apiScopes, account: accounts[0] })
        return result.accessToken
      } catch (err) {
        if (err instanceof InteractionRequiredAuthError) {
          const result = await msalInstance.acquireTokenPopup({ ...apiScopes, account: accounts[0] })
          return result.accessToken
        }
        throw err
      }
    }

    // Google / GitHub — return stored JWT from sessionStorage.
    return getSocialToken()
  }, [isMsalAuthenticated, accounts])

  const logout = useCallback(async (): Promise<void> => {
    clearSocialAuth()
    setSocialToken(null)
    setSocialUser(null)

    if (isMsalAuthenticated) {
      await instance.logoutRedirect({ postLogoutRedirectUri: window.location.origin })
    } else {
      window.location.href = '/'
    }
  }, [isMsalAuthenticated, instance])

  const value = useMemo<AuthContextValue>(
    () => ({ isAuthenticated, isLoading, user, getAccessToken, logout }),
    [isAuthenticated, isLoading, user, getAccessToken, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>')
  return ctx
}

/** Call this after storing a social JWT so AuthProvider re-reads sessionStorage. */
export function notifySocialAuthUpdated(): void {
  window.dispatchEvent(new Event('ca:social-auth-updated'))
}
