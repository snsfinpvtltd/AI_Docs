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
 * Update VITE_AZURE_CLIENT_ID to the backend API's app registration client ID
 * once it has been registered in Azure Entra ID.
 */
export const apiScopes: { scopes: string[] } = {
  scopes: [`api://${import.meta.env.VITE_AZURE_CLIENT_ID}/access_as_user`],
}
