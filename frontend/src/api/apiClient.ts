import axios, { type AxiosInstance, type InternalAxiosRequestConfig } from 'axios'
import { InteractionRequiredAuthError } from '@azure/msal-browser'
import { msalInstance, apiScopes } from '../auth/msalConfig'

const apiClient: AxiosInstance = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL,
  headers: { 'Content-Type': 'application/json' },
})

apiClient.interceptors.request.use(
  async (config: InternalAxiosRequestConfig): Promise<InternalAxiosRequestConfig> => {
    const accounts = msalInstance.getAllAccounts()

    if (accounts.length === 0) {
      // No account signed in — the ProtectedRoute will redirect to login.
      return config
    }

    try {
      // Always attempt silent acquisition first.
      const token = await msalInstance.acquireTokenSilent({
        ...apiScopes,
        account: accounts[0],
      })
      config.headers.Authorization = `Bearer ${token.accessToken}`
    } catch (err) {
      if (err instanceof InteractionRequiredAuthError) {
        // Silent failed (consent/MFA required) — fall back to interactive popup.
        const token = await msalInstance.acquireTokenPopup({
          ...apiScopes,
          account: accounts[0],
        })
        config.headers.Authorization = `Bearer ${token.accessToken}`
      } else {
        throw err
      }
    }

    return config
  },
)

export default apiClient
