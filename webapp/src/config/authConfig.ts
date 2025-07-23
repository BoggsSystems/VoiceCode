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

// API endpoints for VoiceCode services
export const apiConfig = {
  baseUrl: process.env.REACT_APP_API_BASE_URL || 'http://localhost:5000',
  services: {
    router: process.env.REACT_APP_ROUTER_SERVICE_URL || 'http://localhost:5001',
    dispatcher: process.env.REACT_APP_DISPATCHER_SERVICE_URL || 'http://localhost:5002',
    claude: process.env.REACT_APP_CLAUDE_SERVICE_URL || 'http://localhost:5003',
    stt: process.env.REACT_APP_STT_SERVICE_URL || 'http://localhost:5004',
    tts: process.env.REACT_APP_TTS_SERVICE_URL || 'http://localhost:5005',
    generator: process.env.REACT_APP_GENERATOR_SERVICE_URL || 'http://localhost:5006',
  },
  signalr: {
    hubUrl: process.env.REACT_APP_SIGNALR_HUB_URL || 'http://localhost:5002/hubs/voice',
  },
  websocket: {
    url: process.env.REACT_APP_WS_URL || 'ws://localhost:5002/ws',
  },
};