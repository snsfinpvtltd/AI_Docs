import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { MsalProvider } from '@azure/msal-react'
import { GoogleOAuthProvider } from '@react-oauth/google'
import './index.css'
import App from './App'
import { msalInstance } from './auth/msalConfig'
import { AuthProvider } from './auth/AuthContext'

// initialize() must resolve before mounting so that handleRedirectPromise()
// runs and the authentication state is correct on first render.
msalInstance.initialize()
  .then(() => {
    const rootEl = document.getElementById('root')
    if (!rootEl) throw new Error('Root element #root not found in index.html')

    // Use a non-empty placeholder so GoogleOAuthProvider never receives ''.
    // GoogleLoginButton only renders when the real ID is set, so
    // initTokenClient is never called with the placeholder value.
    const googleClientId = (import.meta.env.VITE_GOOGLE_CLIENT_ID as string) || 'not-configured'

    createRoot(rootEl).render(
      <StrictMode>
        <GoogleOAuthProvider clientId={googleClientId}>
          <MsalProvider instance={msalInstance}>
            <AuthProvider>
              <App />
            </AuthProvider>
          </MsalProvider>
        </GoogleOAuthProvider>
      </StrictMode>,
    )
  })
  .catch((error: unknown) => {
    const rootEl = document.getElementById('root')
    if (rootEl) {
      rootEl.innerHTML = `
        <div style="min-height:100vh;display:flex;align-items:center;justify-content:center;font-family:sans-serif;padding:2rem">
          <div>
            <h1 style="font-size:1.25rem;font-weight:600;color:#111">Authentication error</h1>
            <p style="margin-top:.5rem;font-size:.875rem;color:#555">
              Failed to initialize. Please refresh the page.
            </p>
            <pre style="margin-top:1rem;font-size:.75rem;color:#c00;white-space:pre-wrap">${String(error)}</pre>
          </div>
        </div>`
    }
  })
