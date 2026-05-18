import { type ReactElement, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMsal, useIsAuthenticated } from '@azure/msal-react'
import { apiScopes } from '../auth/msalConfig'

export default function LoginPage(): ReactElement {
  const { instance } = useMsal()
  const isAuthenticated = useIsAuthenticated()
  const navigate = useNavigate()

  // Already signed in (e.g. session storage still valid) — skip login page.
  useEffect(() => {
    if (isAuthenticated) {
      navigate('/request', { replace: true })
    }
  }, [isAuthenticated, navigate])

  const handleLogin = async (): Promise<void> => {
    await instance.loginRedirect({ ...apiScopes })
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50">
      <div className="w-full max-w-md space-y-8 rounded-xl bg-white p-8 shadow-lg">
        <div className="text-center">
          <h1 className="text-3xl font-bold tracking-tight text-gray-900">
            Clinical Trial Agent
          </h1>
          <p className="mt-2 text-sm text-gray-600">
            Sign in with your Azure account to generate patient data reports.
          </p>
        </div>

        <button
          type="button"
          onClick={handleLogin}
          className="flex w-full items-center justify-center gap-3 rounded-md bg-blue-600 px-4 py-3 text-sm font-semibold text-white shadow-sm hover:bg-blue-500 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600"
        >
          <MicrosoftIcon />
          Sign in with Microsoft
        </button>
      </div>
    </div>
  )
}

function MicrosoftIcon(): ReactElement {
  return (
    <svg viewBox="0 0 21 21" width="20" height="20" aria-hidden="true">
      <rect x="1"  y="1"  width="9" height="9" fill="#f25022" />
      <rect x="11" y="1"  width="9" height="9" fill="#7fba00" />
      <rect x="1"  y="11" width="9" height="9" fill="#00a4ef" />
      <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
    </svg>
  )
}
