import {
  PublicClientApplication,
  type Configuration,
  LogLevel,
} from '@azure/msal-browser'

const msalConfig: Configuration = {
  auth: {
    clientId:               import.meta.env.VITE_AZURE_CLIENT_ID,
    authority:              `https://login.microsoftonline.com/${import.meta.env.VITE_AZURE_TENANT_ID}`,
    redirectUri:            window.location.origin,
    postLogoutRedirectUri:  window.location.origin,
  },
  cache: {
    // sessionStorage per CLAUDE.md — never localStorage
    cacheLocation:           'sessionStorage',
    storeAuthStateInCookie:  false,
  },
  system: {
    loggerOptions: {
      logLevel: LogLevel.Warning,
      loggerCallback: (level, message, containsPii) => {
        if (containsPii) return
        if (level === LogLevel.Error)   console.error('[MSAL]', message)
        if (level === LogLevel.Warning) console.warn('[MSAL]', message)
      },
    },
  },
}

export const msalInstance = new PublicClientApplication(msalConfig)

/**
 * Scopes requested when acquiring tokens for the backend API.
 * Set VITE_AZURE_API_SCOPE in .env.local to the full scope URI exposed by the
 * backend App Registration (e.g. api://<api-client-id>/access_as_user).
 * The App Registration must have "Expose an API" configured in Azure Entra ID
 * with that Application ID URI and scope name before this will work.
 */
export const apiScopes: { scopes: string[] } = {
  scopes: [import.meta.env.VITE_AZURE_API_SCOPE],
}
