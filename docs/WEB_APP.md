# VoiceCode Web Application Architecture

## Overview

The VoiceCode web application provides a cross-platform, browser-based interface for voice-controlled code generation. Built with React, TypeScript, and modern web technologies, it works on any device with a web browser.

## Technology Stack

- **Framework**: React 18+ with TypeScript
- **State Management**: Redux Toolkit or Zustand
- **UI Components**: Tailwind CSS + Headless UI
- **Audio Processing**: Web Audio API + MediaRecorder API
- **Real-time Communication**: SignalR JavaScript client
- **HTTP Client**: Axios with interceptors
- **Build Tool**: Vite
- **Testing**: Jest + React Testing Library + Cypress
- **PWA**: Service Workers for offline capability

## Project Structure

```
web-app/
├── public/
│   ├── index.html
│   ├── manifest.json
│   └── service-worker.js
├── src/
│   ├── components/
│   │   ├── voice/
│   │   │   ├── VoiceRecorder.tsx
│   │   │   ├── AudioVisualizer.tsx
│   │   │   └── TranscriptDisplay.tsx
│   │   ├── code/
│   │   │   ├── CodeDisplay.tsx
│   │   │   ├── CodeEditor.tsx
│   │   │   └── FileTree.tsx
│   │   ├── common/
│   │   │   ├── Layout.tsx
│   │   │   ├── Header.tsx
│   │   │   └── ErrorBoundary.tsx
│   │   └── auth/
│   │       ├── LoginForm.tsx
│   │       └── ProtectedRoute.tsx
│   ├── hooks/
│   │   ├── useAudioRecorder.ts
│   │   ├── useWebSocket.ts
│   │   ├── useAuth.ts
│   │   └── useApi.ts
│   ├── services/
│   │   ├── api.service.ts
│   │   ├── audio.service.ts
│   │   ├── websocket.service.ts
│   │   └── auth.service.ts
│   ├── store/
│   │   ├── index.ts
│   │   ├── authSlice.ts
│   │   ├── voiceSlice.ts
│   │   └── codeSlice.ts
│   ├── types/
│   │   ├── index.ts
│   │   └── api.types.ts
│   ├── utils/
│   │   ├── audio.utils.ts
│   │   └── format.utils.ts
│   ├── App.tsx
│   └── main.tsx
├── tests/
├── package.json
├── tsconfig.json
├── vite.config.ts
└── tailwind.config.js
```

## Core Components

### 1. Main Application Component

```typescript
// src/App.tsx
import React from 'react';
import { BrowserRouter as Router, Routes, Route } from 'react-router-dom';
import { Provider } from 'react-redux';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Toaster } from 'react-hot-toast';
import { store } from './store';
import { AuthProvider } from './contexts/AuthContext';
import { WebSocketProvider } from './contexts/WebSocketContext';
import Layout from './components/common/Layout';
import VoiceInterface from './pages/VoiceInterface';
import History from './pages/History';
import Settings from './pages/Settings';
import Login from './pages/Login';
import ProtectedRoute from './components/auth/ProtectedRoute';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 5 * 60 * 1000, // 5 minutes
      cacheTime: 10 * 60 * 1000, // 10 minutes
    },
  },
});

function App() {
  return (
    <Provider store={store}>
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <WebSocketProvider>
            <Router>
              <Layout>
                <Routes>
                  <Route path="/login" element={<Login />} />
                  <Route
                    path="/"
                    element={
                      <ProtectedRoute>
                        <VoiceInterface />
                      </ProtectedRoute>
                    }
                  />
                  <Route
                    path="/history"
                    element={
                      <ProtectedRoute>
                        <History />
                      </ProtectedRoute>
                    }
                  />
                  <Route
                    path="/settings"
                    element={
                      <ProtectedRoute>
                        <Settings />
                      </ProtectedRoute>
                    }
                  />
                </Routes>
              </Layout>
            </Router>
            <Toaster position="top-right" />
          </WebSocketProvider>
        </AuthProvider>
      </QueryClientProvider>
    </Provider>
  );
}

export default App;
```

### 2. Voice Recorder Component

```typescript
// src/components/voice/VoiceRecorder.tsx
import React, { useState, useRef, useCallback } from 'react';
import { useAudioRecorder } from '../../hooks/useAudioRecorder';
import { useAppDispatch, useAppSelector } from '../../store';
import { startRecording, stopRecording, updateTranscript } from '../../store/voiceSlice';
import AudioVisualizer from './AudioVisualizer';
import { MicrophoneIcon, StopIcon } from '@heroicons/react/24/solid';
import toast from 'react-hot-toast';

const VoiceRecorder: React.FC = () => {
  const dispatch = useAppDispatch();
  const { isRecording, currentTranscript } = useAppSelector((state) => state.voice);
  const [audioLevel, setAudioLevel] = useState(0);
  
  const {
    startRecording: startAudioRecording,
    stopRecording: stopAudioRecording,
    isSupported,
  } = useAudioRecorder({
    onAudioLevel: setAudioLevel,
    onError: (error) => toast.error(error.message),
  });

  const handleToggleRecording = useCallback(async () => {
    if (isRecording) {
      const audioBlob = await stopAudioRecording();
      dispatch(stopRecording());
      
      // Send audio to API
      await processAudioCommand(audioBlob);
    } else {
      const success = await startAudioRecording();
      if (success) {
        dispatch(startRecording());
      }
    }
  }, [isRecording, startAudioRecording, stopAudioRecording, dispatch]);

  const processAudioCommand = async (audioBlob: Blob) => {
    const formData = new FormData();
    formData.append('audio', audioBlob, 'recording.wav');
    formData.append('language', 'en-US');
    
    try {
      const response = await fetch('/api/voice/process', {
        method: 'POST',
        body: formData,
        headers: {
          'Authorization': `Bearer ${localStorage.getItem('token')}`,
        },
      });
      
      if (!response.ok) {
        throw new Error('Failed to process voice command');
      }
      
      const result = await response.json();
      dispatch(updateTranscript(result.transcript));
    } catch (error) {
      toast.error('Failed to process voice command');
      console.error(error);
    }
  };

  if (!isSupported) {
    return (
      <div className="text-center p-8">
        <p className="text-red-600">Your browser doesn't support audio recording.</p>
        <p className="text-sm text-gray-600 mt-2">
          Please use a modern browser like Chrome, Firefox, or Edge.
        </p>
      </div>
    );
  }

  return (
    <div className="flex flex-col items-center space-y-8">
      <AudioVisualizer audioLevel={audioLevel} isRecording={isRecording} />
      
      <button
        onClick={handleToggleRecording}
        className={`
          relative p-8 rounded-full transition-all duration-300 transform
          ${isRecording 
            ? 'bg-red-500 hover:bg-red-600 scale-110 animate-pulse' 
            : 'bg-blue-500 hover:bg-blue-600 hover:scale-105'
          }
          text-white shadow-lg
        `}
        aria-label={isRecording ? 'Stop recording' : 'Start recording'}
      >
        {isRecording ? (
          <StopIcon className="w-12 h-12" />
        ) : (
          <MicrophoneIcon className="w-12 h-12" />
        )}
      </button>
      
      {currentTranscript && (
        <div className="w-full max-w-2xl p-4 bg-gray-100 rounded-lg">
          <h3 className="text-sm font-semibold text-gray-700 mb-2">Transcript:</h3>
          <p className="text-gray-900">{currentTranscript}</p>
        </div>
      )}
    </div>
  );
};

export default VoiceRecorder;
```

### 3. Audio Recording Hook

```typescript
// src/hooks/useAudioRecorder.ts
import { useState, useRef, useCallback } from 'react';

interface UseAudioRecorderOptions {
  onAudioLevel?: (level: number) => void;
  onError?: (error: Error) => void;
  sampleRate?: number;
  mimeType?: string;
}

interface UseAudioRecorderReturn {
  isRecording: boolean;
  isSupported: boolean;
  startRecording: () => Promise<boolean>;
  stopRecording: () => Promise<Blob>;
  audioLevel: number;
}

export const useAudioRecorder = ({
  onAudioLevel,
  onError,
  sampleRate = 16000,
  mimeType = 'audio/webm',
}: UseAudioRecorderOptions = {}): UseAudioRecorderReturn => {
  const [isRecording, setIsRecording] = useState(false);
  const [audioLevel, setAudioLevel] = useState(0);
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const audioChunksRef = useRef<Blob[]>([]);
  const audioContextRef = useRef<AudioContext | null>(null);
  const analyzerRef = useRef<AnalyserNode | null>(null);
  const animationFrameRef = useRef<number | null>(null);

  const isSupported = typeof navigator !== 'undefined' && 
    navigator.mediaDevices && 
    navigator.mediaDevices.getUserMedia;

  const updateAudioLevel = useCallback(() => {
    if (!analyzerRef.current || !isRecording) return;

    const dataArray = new Uint8Array(analyzerRef.current.frequencyBinCount);
    analyzerRef.current.getByteFrequencyData(dataArray);
    
    const average = dataArray.reduce((a, b) => a + b) / dataArray.length;
    const normalizedLevel = average / 255;
    
    setAudioLevel(normalizedLevel);
    onAudioLevel?.(normalizedLevel);
    
    animationFrameRef.current = requestAnimationFrame(updateAudioLevel);
  }, [isRecording, onAudioLevel]);

  const startRecording = useCallback(async (): Promise<boolean> => {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ 
        audio: {
          echoCancellation: true,
          noiseSuppression: true,
          sampleRate,
        } 
      });

      // Set up audio context for visualization
      audioContextRef.current = new AudioContext();
      const source = audioContextRef.current.createMediaStreamSource(stream);
      analyzerRef.current = audioContextRef.current.createAnalyser();
      analyzerRef.current.fftSize = 256;
      source.connect(analyzerRef.current);

      // Set up media recorder
      const options = { mimeType };
      if (!MediaRecorder.isTypeSupported(mimeType)) {
        options.mimeType = 'audio/webm';
      }
      
      mediaRecorderRef.current = new MediaRecorder(stream, options);
      audioChunksRef.current = [];

      mediaRecorderRef.current.ondataavailable = (event) => {
        if (event.data.size > 0) {
          audioChunksRef.current.push(event.data);
        }
      };

      mediaRecorderRef.current.start(100); // Collect data every 100ms
      setIsRecording(true);
      updateAudioLevel();
      
      return true;
    } catch (error) {
      const err = new Error(
        error instanceof Error ? error.message : 'Failed to start recording'
      );
      onError?.(err);
      return false;
    }
  }, [sampleRate, mimeType, updateAudioLevel, onError]);

  const stopRecording = useCallback(async (): Promise<Blob> => {
    return new Promise((resolve, reject) => {
      if (!mediaRecorderRef.current) {
        reject(new Error('No active recording'));
        return;
      }

      mediaRecorderRef.current.onstop = () => {
        const audioBlob = new Blob(audioChunksRef.current, { type: mimeType });
        audioChunksRef.current = [];
        resolve(audioBlob);
      };

      mediaRecorderRef.current.stop();
      mediaRecorderRef.current.stream.getTracks().forEach(track => track.stop());
      
      if (animationFrameRef.current) {
        cancelAnimationFrame(animationFrameRef.current);
      }
      
      if (audioContextRef.current) {
        audioContextRef.current.close();
      }
      
      setIsRecording(false);
      setAudioLevel(0);
    });
  }, [mimeType]);

  return {
    isRecording,
    isSupported,
    startRecording,
    stopRecording,
    audioLevel,
  };
};
```

### 4. WebSocket Service

```typescript
// src/services/websocket.service.ts
import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr';
import { store } from '../store';
import { updateStatus, setResult } from '../store/voiceSlice';
import toast from 'react-hot-toast';

class WebSocketService {
  private connection: HubConnection | null = null;
  private reconnectAttempts = 0;
  private maxReconnectAttempts = 5;

  async connect(sessionId: string): Promise<void> {
    const token = localStorage.getItem('token');
    if (!token) {
      throw new Error('No authentication token');
    }

    this.connection = new HubConnectionBuilder()
      .withUrl(`${import.meta.env.VITE_API_URL}/hubs/result`, {
        accessTokenFactory: () => token,
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Information)
      .build();

    // Set up event handlers
    this.connection.on('StatusUpdate', (data) => {
      store.dispatch(updateStatus(data));
    });

    this.connection.on('ResultReady', (data) => {
      store.dispatch(setResult(data));
      toast.success('Code generation complete!');
    });

    this.connection.on('Error', (error) => {
      toast.error(error.message);
    });

    this.connection.onreconnecting(() => {
      toast.loading('Reconnecting...');
    });

    this.connection.onreconnected(() => {
      toast.success('Reconnected');
      this.reconnectAttempts = 0;
    });

    this.connection.onclose(() => {
      if (this.reconnectAttempts < this.maxReconnectAttempts) {
        this.reconnectAttempts++;
        setTimeout(() => this.connect(sessionId), 5000);
      }
    });

    await this.connection.start();
    await this.connection.invoke('JoinSession', sessionId);
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
    }
  }

  async sendMessage(method: string, ...args: any[]): Promise<void> {
    if (!this.connection) {
      throw new Error('Not connected');
    }
    await this.connection.invoke(method, ...args);
  }
}

export default new WebSocketService();
```

### 5. API Service

```typescript
// src/services/api.service.ts
import axios, { AxiosInstance, AxiosError } from 'axios';
import { store } from '../store';
import { logout } from '../store/authSlice';
import toast from 'react-hot-toast';

class ApiService {
  private api: AxiosInstance;

  constructor() {
    this.api = axios.create({
      baseURL: import.meta.env.VITE_API_URL,
      timeout: 30000,
      headers: {
        'Content-Type': 'application/json',
      },
    });

    // Request interceptor
    this.api.interceptors.request.use(
      (config) => {
        const token = localStorage.getItem('token');
        if (token) {
          config.headers.Authorization = `Bearer ${token}`;
        }
        return config;
      },
      (error) => Promise.reject(error)
    );

    // Response interceptor
    this.api.interceptors.response.use(
      (response) => response,
      async (error: AxiosError) => {
        if (error.response?.status === 401) {
          store.dispatch(logout());
          toast.error('Session expired. Please login again.');
        } else if (error.response?.status === 429) {
          toast.error('Too many requests. Please slow down.');
        } else if (error.response?.status >= 500) {
          toast.error('Server error. Please try again later.');
        }
        return Promise.reject(error);
      }
    );
  }

  // Voice processing
  async processVoiceCommand(audioBlob: Blob, language: string = 'en-US') {
    const formData = new FormData();
    formData.append('audio', audioBlob, 'recording.wav');
    formData.append('language', language);
    formData.append('userId', store.getState().auth.user?.id || '');
    formData.append('sessionId', store.getState().voice.sessionId);

    const response = await this.api.post('/voice/process', formData, {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    });

    return response.data;
  }

  // Code generation
  async generateCode(request: {
    instruction: string;
    language: string;
    context?: string;
  }) {
    const response = await this.api.post('/code/generate', request);
    return response.data;
  }

  // Get processing status
  async getStatus(requestId: string) {
    const response = await this.api.get(`/status/${requestId}`);
    return response.data;
  }

  // Get history
  async getHistory(page: number = 1, pageSize: number = 20) {
    const response = await this.api.get('/history', {
      params: { page, pageSize },
    });
    return response.data;
  }

  // Update preferences
  async updatePreferences(preferences: any) {
    const response = await this.api.put('/user/preferences', preferences);
    return response.data;
  }
}

export default new ApiService();
```

### 6. Authentication Service

```typescript
// src/services/auth.service.ts
import { PublicClientApplication } from '@azure/msal-browser';
import { msalConfig } from '../config/auth.config';

class AuthService {
  private msalInstance: PublicClientApplication;

  constructor() {
    this.msalInstance = new PublicClientApplication(msalConfig);
  }

  async login(): Promise<string> {
    const loginRequest = {
      scopes: ['api://voicecode/access'],
    };

    try {
      const response = await this.msalInstance.loginPopup(loginRequest);
      return response.accessToken;
    } catch (error) {
      console.error('Login failed:', error);
      throw error;
    }
  }

  async logout(): Promise<void> {
    await this.msalInstance.logout();
  }

  async getToken(): Promise<string | null> {
    const accounts = this.msalInstance.getAllAccounts();
    if (accounts.length === 0) {
      return null;
    }

    const request = {
      scopes: ['api://voicecode/access'],
      account: accounts[0],
    };

    try {
      const response = await this.msalInstance.acquireTokenSilent(request);
      return response.accessToken;
    } catch (error) {
      // Token refresh failed, need to login again
      const response = await this.msalInstance.acquireTokenPopup(request);
      return response.accessToken;
    }
  }

  getUser() {
    const accounts = this.msalInstance.getAllAccounts();
    return accounts.length > 0 ? accounts[0] : null;
  }
}

export default new AuthService();
```

### 7. Progressive Web App Configuration

```javascript
// public/service-worker.js
const CACHE_NAME = 'voicecode-v1';
const urlsToCache = [
  '/',
  '/index.html',
  '/static/css/main.css',
  '/static/js/main.js',
  '/manifest.json',
];

// Install service worker
self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => {
      return cache.addAll(urlsToCache);
    })
  );
});

// Fetch event
self.addEventListener('fetch', (event) => {
  event.respondWith(
    caches.match(event.request).then((response) => {
      // Cache hit - return response
      if (response) {
        return response;
      }

      return fetch(event.request).then((response) => {
        // Check if valid response
        if (!response || response.status !== 200 || response.type !== 'basic') {
          return response;
        }

        // Clone the response
        const responseToCache = response.clone();

        caches.open(CACHE_NAME).then((cache) => {
          cache.put(event.request, responseToCache);
        });

        return response;
      });
    })
  );
});

// Activate event
self.addEventListener('activate', (event) => {
  const cacheWhitelist = [CACHE_NAME];
  event.waitUntil(
    caches.keys().then((cacheNames) => {
      return Promise.all(
        cacheNames.map((cacheName) => {
          if (cacheWhitelist.indexOf(cacheName) === -1) {
            return caches.delete(cacheName);
          }
        })
      );
    })
  );
});
```

## State Management

```typescript
// src/store/voiceSlice.ts
import { createSlice, PayloadAction } from '@reduxjs/toolkit';

interface VoiceState {
  isRecording: boolean;
  sessionId: string;
  currentTranscript: string;
  status: string;
  progress: number;
  result: CodeResult | null;
  error: string | null;
}

const initialState: VoiceState = {
  isRecording: false,
  sessionId: generateSessionId(),
  currentTranscript: '',
  status: 'idle',
  progress: 0,
  result: null,
  error: null,
};

const voiceSlice = createSlice({
  name: 'voice',
  initialState,
  reducers: {
    startRecording: (state) => {
      state.isRecording = true;
      state.currentTranscript = '';
      state.error = null;
    },
    stopRecording: (state) => {
      state.isRecording = false;
    },
    updateTranscript: (state, action: PayloadAction<string>) => {
      state.currentTranscript = action.payload;
    },
    updateStatus: (state, action: PayloadAction<StatusUpdate>) => {
      state.status = action.payload.status;
      state.progress = action.payload.progress;
    },
    setResult: (state, action: PayloadAction<CodeResult>) => {
      state.result = action.payload;
      state.status = 'completed';
      state.progress = 100;
    },
    setError: (state, action: PayloadAction<string>) => {
      state.error = action.payload;
      state.status = 'error';
    },
    resetSession: (state) => {
      state.sessionId = generateSessionId();
      state.currentTranscript = '';
      state.status = 'idle';
      state.progress = 0;
      state.result = null;
      state.error = null;
    },
  },
});

function generateSessionId(): string {
  return `session-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
}

export const {
  startRecording,
  stopRecording,
  updateTranscript,
  updateStatus,
  setResult,
  setError,
  resetSession,
} = voiceSlice.actions;

export default voiceSlice.reducer;
```

## Deployment

### Azure Static Web Apps Configuration

```json
// staticwebapp.config.json
{
  "routes": [
    {
      "route": "/api/*",
      "allowedRoles": ["authenticated"]
    },
    {
      "route": "/login",
      "allowedRoles": ["anonymous"]
    },
    {
      "route": "/*",
      "allowedRoles": ["authenticated"]
    }
  ],
  "responseOverrides": {
    "401": {
      "redirect": "/login",
      "statusCode": 302
    }
  },
  "navigationFallback": {
    "rewrite": "/index.html",
    "exclude": ["/api/*"]
  },
  "mimeTypes": {
    ".json": "application/json",
    ".js": "application/javascript",
    ".mjs": "application/javascript"
  },
  "globalHeaders": {
    "X-Frame-Options": "DENY",
    "X-Content-Type-Options": "nosniff",
    "Referrer-Policy": "strict-origin-when-cross-origin",
    "Permissions-Policy": "microphone=(self)"
  }
}
```

### Build Configuration

```javascript
// vite.config.ts
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'autoUpdate',
      includeAssets: ['favicon.ico', 'robots.txt', 'apple-touch-icon.png'],
      manifest: {
        name: 'VoiceCode',
        short_name: 'VoiceCode',
        description: 'AI-powered voice assistant for code generation',
        theme_color: '#3B82F6',
        background_color: '#ffffff',
        display: 'standalone',
        icons: [
          {
            src: 'icon-192.png',
            sizes: '192x192',
            type: 'image/png',
          },
          {
            src: 'icon-512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'any maskable',
          },
        ],
      },
    }),
  ],
  build: {
    outDir: 'dist',
    sourcemap: true,
    rollupOptions: {
      output: {
        manualChunks: {
          vendor: ['react', 'react-dom', 'react-router-dom'],
          redux: ['@reduxjs/toolkit', 'react-redux'],
          ui: ['@headlessui/react', '@heroicons/react'],
        },
      },
    },
  },
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
});
```

## Security Considerations

1. **Content Security Policy**: Strict CSP headers to prevent XSS
2. **HTTPS Only**: Enforced through Azure Static Web Apps
3. **Authentication**: Azure AD B2C integration
4. **Input Validation**: Client-side validation before sending to API
5. **Secure Storage**: No sensitive data in localStorage
6. **API Security**: All API calls require authentication tokens

## Performance Optimization

1. **Code Splitting**: Dynamic imports for route-based splitting
2. **Lazy Loading**: Components loaded on demand
3. **Image Optimization**: WebP format with fallbacks
4. **Bundle Size**: Tree shaking and minification
5. **Caching**: Service Worker for offline functionality
6. **CDN**: Azure CDN for static assets

## Browser Support

- Chrome 90+
- Firefox 88+
- Safari 14+
- Edge 90+
- Mobile browsers with WebRTC support