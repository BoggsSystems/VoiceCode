import { Configuration, PopupRequest } from '@azure/msal-browser';

// MSAL configuration
export const msalConfig: Configuration = {
  auth: {
    clientId: process.env.REACT_APP_CLIENT_ID || '',
    authority: `https://login.microsoftonline.com/${process.env.REACT_APP_TENANT_ID}`,
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
  },
  cache: {
    cacheLocation: 'sessionStorage',
    storeAuthStateInCookie: false,
  },
  system: {
    loggerOptions: {
      loggerCallback: (level, message, containsPii) => {
        if (containsPii) {
          return;
        }
        switch (level) {
          case 1: // Error
            console.error(message);
            return;
          case 2: // Warning
            console.warn(message);
            return;
          case 3: // Info
            console.info(message);
            return;
          case 4: // Verbose
            console.debug(message);
            return;
          default:
            return;
        }
      },
    },
  },
};

// Add here scopes for ID token to be used at MS Identity Platform endpoints.
export const loginRequest: PopupRequest = {
  scopes: ['User.Read'],
};

// Add here the endpoints for MS Graph API services you would like to use.
export const graphConfig = {
  graphMeEndpoint: 'https://graph.microsoft.com/v1.0/me',
};

// Scopes for VoiceCode API
export const voiceCodeApiScopes = {
  scopes: [`api://${process.env.REACT_APP_CLIENT_ID}/access_as_user`],
};

// API endpoints
export const apiConfig = {
  baseUrl: process.env.REACT_APP_API_URL || 'https://localhost:7000',
  signalrUrl: process.env.REACT_APP_SIGNALR_URL || 'https://localhost:7006',
  endpoints: {
    stt: '/v1/stt',
    claude: '/v1/claude',
    router: '/v1/router',
    generator: '/v1/generator',
    tts: '/v1/tts',
    dispatcher: '/v1/dispatcher',
  },
};