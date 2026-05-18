import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { MsalProvider } from '@azure/msal-react'
import './index.css'
import App from './App'
import { msalInstance } from './auth/msalConfig'

// initialize() must resolve before mounting so that handleRedirectPromise()
// runs and the authentication state is correct on first render.
msalInstance.initialize().then(() => {
  const rootEl = document.getElementById('root')
  if (!rootEl) throw new Error('Root element #root not found in index.html')

  createRoot(rootEl).render(
    <StrictMode>
      <MsalProvider instance={msalInstance}>
        <App />
      </MsalProvider>
    </StrictMode>,
  )
})
