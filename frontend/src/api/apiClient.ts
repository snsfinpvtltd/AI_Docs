import axios, { type AxiosInstance, type InternalAxiosRequestConfig } from 'axios'
import { InteractionRequiredAuthError } from '@azure/msal-browser'
import { msalInstance, apiScopes } from '../auth/msalConfig'
import { getSocialToken } from '../auth/socialTokenStore'

const apiClient: AxiosInstance = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL,
})

apiClient.interceptors.request.use(
  async (config: InternalAxiosRequestConfig): Promise<InternalAxiosRequestConfig> => {
    // Social sign-in (Google / GitHub) — JWT stored in sessionStorage.
    const socialToken = getSocialToken()
    if (socialToken) {
      config.headers.Authorization = `Bearer ${socialToken}`
      if (!(config.data instanceof FormData)) {
        config.headers['Content-Type'] = 'application/json'
      }
      return config
    }

    // Microsoft — acquire via MSAL silent/popup.
    const accounts = msalInstance.getAllAccounts()
    if (accounts.length === 0) return config

    try {
      const result = await msalInstance.acquireTokenSilent({ ...apiScopes, account: accounts[0] })
      config.headers.Authorization = `Bearer ${result.accessToken}`
    } catch (err) {
      if (err instanceof InteractionRequiredAuthError) {
        const result = await msalInstance.acquireTokenPopup({ ...apiScopes, account: accounts[0] })
        config.headers.Authorization = `Bearer ${result.accessToken}`
      } else {
        throw err
      }
    }

    if (!(config.data instanceof FormData)) {
      config.headers['Content-Type'] = 'application/json'
    }

    return config
  },
)

export default apiClient
