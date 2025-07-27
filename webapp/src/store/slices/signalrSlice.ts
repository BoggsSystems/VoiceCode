import { createSlice, createAsyncThunk, PayloadAction } from '@reduxjs/toolkit';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { apiConfig } from '../../config/authConfig';
import { signalrDebugger } from '../../utils/signalrDebugger';

export interface SignalRState {
  connection: HubConnection | null;
  connectionState: HubConnectionState;
  sessionId: string | null;
  connected: boolean;
  error: string | null;
  reconnecting: boolean;
}

const initialState: SignalRState = {
  connection: null,
  connectionState: HubConnectionState.Disconnected,
  sessionId: null,
  connected: false,
  error: null,
  reconnecting: false,
};

// Async thunks
export const initializeSignalR = createAsyncThunk(
  'signalr/initialize',
  async (_, { getState, dispatch, rejectWithValue }) => {
    try {
      signalrDebugger.log('info', 'Starting SignalR initialization', {
        hubUrl: apiConfig.signalr.hubUrl,
        timestamp: new Date().toISOString()
      });

      const connection = new HubConnectionBuilder()
        .withUrl(apiConfig.signalr.hubUrl, {
          accessTokenFactory: async () => {
            // Get access token from auth state, localStorage, or MSAL
            const state = getState() as any;
            const authToken = localStorage.getItem('auth-token');
            const token = state.auth.accessToken || authToken || '';
            
            signalrDebugger.log('info', 'Access token factory called', {
              hasAuthToken: !!authToken,
              hasStateToken: !!state.auth.accessToken,
              tokenLength: token ? token.length : 0,
              tokenPreview: token ? `${token.substring(0, 20)}...` : 'None'
            });
            
            console.log('[SignalR] Using auth token:', token ? 'Found' : 'Missing');
            return token;
          },
        })
        .configureLogging({
          log: (logLevel, message) => {
            const levelMap = {
              [LogLevel.Trace]: 'debug',
              [LogLevel.Debug]: 'debug',
              [LogLevel.Information]: 'info',
              [LogLevel.Warning]: 'warn',
              [LogLevel.Error]: 'error',
              [LogLevel.Critical]: 'error',
              [LogLevel.None]: 'info'
            } as const;
            
            signalrDebugger.log(levelMap[logLevel] || 'info', `SignalR: ${message}`);
          }
        })
        .withAutomaticReconnect({
          nextRetryDelayInMilliseconds: retryContext => {
            if (retryContext.elapsedMilliseconds < 60000) {
              return Math.random() * 10000;
            } else {
              return null; // Stop retrying after 1 minute
            }
          }
        })
        .build();

      // Set up event handlers
      connection.onreconnecting((error) => {
        signalrDebugger.log('warn', 'SignalR reconnecting', { error: error?.message });
        dispatch(setReconnecting(true));
      });

      connection.onreconnected((connectionId) => {
        signalrDebugger.log('info', 'SignalR reconnected', { connectionId });
        dispatch(setReconnecting(false));
        dispatch(setConnected(true));
      });

      connection.onclose((error) => {
        signalrDebugger.log('error', 'SignalR connection closed', { error: error?.message });
        dispatch(setConnected(false));
        dispatch(setReconnecting(false));
      });

      // Set up server event handlers
      connection.on('Connected', (data) => {
        signalrDebugger.log('info', 'Received Connected event', data);
        console.log('[SignalR] Connected event received:', data);
      });

      connection.on('SessionStarted', (session) => {
        signalrDebugger.log('info', 'Session started', { session });
        dispatch(setSessionId(session.id));
        // Update local session ID if different
        if (session.id && session.id !== sessionId) {
          localStorage.setItem('session-id', session.id);
        }
      });

      connection.on('JoinedSession', (data) => {
        signalrDebugger.log('info', 'Successfully joined session', data);
        console.log('[SignalR] JoinedSession event received:', data);
        if (data.success && data.session) {
          dispatch(setSessionId(data.session.id));
        }
      });

      connection.on('TranscriptionReceived', (result) => {
        dispatch({ type: 'voice/transcriptionReceived', payload: result });
      });

      connection.on('ClaudeResponse', (response) => {
        dispatch({ type: 'chat/messageReceived', payload: response });
      });

      connection.on('GenerationComplete', (result) => {
        dispatch({ type: 'projects/generationComplete', payload: result });
      });

      connection.on('AudioReady', (result) => {
        console.log('[SignalR] AudioReady event received:', result);
        dispatch({ type: 'voice/audioReady', payload: result });
      });

      // Also listen for AudioResponseReady (the event dispatcher uses)
      connection.on('AudioResponseReady', (result) => {
        console.log('[SignalR] AudioResponseReady event received:', result);
        signalrDebugger.log('info', 'AudioResponseReady received', result);
        
        // Dispatch the audio ready action with the proper payload
        dispatch({ type: 'voice/audioReady', payload: {
          id: result.taskId || result.SessionId || Date.now().toString(),
          audioUrl: result.audioUrl || result.AudioUrl,
          text: result.transcriptionText || result.TranscriptionText || ''
        }});
      });

      connection.on('Error', (error) => {
        dispatch(setError(error.message));
      });

      signalrDebugger.log('info', 'Starting SignalR connection', {
        url: apiConfig.signalr.hubUrl,
        state: 'Connecting'
      });
      
      console.log('[SignalR] Starting connection to:', apiConfig.signalr.hubUrl);
      
      try {
        await connection.start();
        
        signalrDebugger.log('info', 'SignalR connection established', {
          connectionId: connection.connectionId,
          state: connection.state,
          transport: (connection as any).connection?.transport?.name
        });
        
        console.log('[SignalR] Connection started successfully');
        dispatch(setConnected(true));
        
        // Generate a session ID if we don't have one
        const sessionId = localStorage.getItem('session-id') || `session-${Date.now()}-${Math.random().toString(36).substring(7)}`;
        localStorage.setItem('session-id', sessionId);
        dispatch(setSessionId(sessionId));
        
        signalrDebugger.log('info', 'Session ID configured', { sessionId });
        
        // Join the session group
        try {
          signalrDebugger.log('info', 'Attempting to join session group', { sessionId });
          await connection.invoke('JoinSession', sessionId);
          signalrDebugger.log('info', 'Successfully joined session group', { sessionId });
          console.log('[SignalR] Joined session group:', sessionId);
        } catch (error: any) {
          signalrDebugger.log('error', 'Failed to join session group', {
            sessionId,
            error: error?.message || error?.toString()
          });
          console.error('[SignalR] Failed to join session group:', error);
        }
      } catch (startError: any) {
        signalrDebugger.log('error', 'Failed to start SignalR connection', {
          error: startError?.message || startError?.toString(),
          stack: startError?.stack
        });
        throw startError;
      }
      
      return connection;
    } catch (error: any) {
      const errorMessage = error?.toString() || 'Unknown error';
      signalrDebugger.log('error', 'SignalR initialization failed', {
        error: errorMessage,
        stack: error?.stack,
        hubUrl: apiConfig.signalr.hubUrl
      });
      console.error('[SignalR] Connection failed:', error);
      return rejectWithValue(`Failed to initialize SignalR: ${errorMessage}`);
    }
  }
);

export const disconnectSignalR = createAsyncThunk(
  'signalr/disconnect',
  async (_, { getState }) => {
    const state = getState() as any;
    const connection = state.signalr.connection;
    
    if (connection) {
      await connection.stop();
    }
  }
);

export const sendAudioMessage = createAsyncThunk(
  'signalr/sendAudioMessage',
  async (audioData: ArrayBuffer, { getState, rejectWithValue }) => {
    try {
      const state = getState() as any;
      const connection = state.signalr.connection;
      
      if (!connection || connection.state !== HubConnectionState.Connected) {
        throw new Error('SignalR connection not available');
      }

      const message = {
        id: crypto.randomUUID(),
        audioData: Array.from(new Uint8Array(audioData)),
        format: 'wav',
        sampleRate: 16000,
      };

      const result = await connection.invoke('ProcessAudio', message);
      return result;
    } catch (error) {
      return rejectWithValue(`Failed to send audio: ${error}`);
    }
  }
);

export const sendTextMessage = createAsyncThunk(
  'signalr/sendTextMessage',
  async (text: string, { getState, rejectWithValue }) => {
    try {
      const state = getState() as any;
      const connection = state.signalr.connection;
      
      if (!connection || connection.state !== HubConnectionState.Connected) {
        throw new Error('SignalR connection not available');
      }

      const message = {
        id: crypto.randomUUID(),
        text,
      };

      const result = await connection.invoke('ProcessText', message);
      return result;
    } catch (error) {
      return rejectWithValue(`Failed to send text: ${error}`);
    }
  }
);

const signalrSlice = createSlice({
  name: 'signalr',
  initialState,
  reducers: {
    setConnected: (state, action: PayloadAction<boolean>) => {
      state.connected = action.payload;
      state.connectionState = action.payload 
        ? HubConnectionState.Connected 
        : HubConnectionState.Disconnected;
    },
    setReconnecting: (state, action: PayloadAction<boolean>) => {
      state.reconnecting = action.payload;
      state.connectionState = action.payload 
        ? HubConnectionState.Reconnecting 
        : state.connectionState;
    },
    setSessionId: (state, action: PayloadAction<string>) => {
      state.sessionId = action.payload;
    },
    setError: (state, action: PayloadAction<string>) => {
      state.error = action.payload;
    },
    clearError: (state) => {
      state.error = null;
    },
  },
  extraReducers: (builder) => {
    builder
      .addCase(initializeSignalR.fulfilled, (state, action) => {
        state.connection = action.payload;
        state.connected = true;
        state.connectionState = HubConnectionState.Connected;
        state.error = null;
      })
      .addCase(initializeSignalR.rejected, (state, action) => {
        state.error = action.payload as string;
        state.connected = false;
        state.connectionState = HubConnectionState.Disconnected;
      })
      .addCase(disconnectSignalR.fulfilled, (state) => {
        state.connection = null;
        state.connected = false;
        state.connectionState = HubConnectionState.Disconnected;
        state.sessionId = null;
      });
  },
});

export const {
  setConnected,
  setReconnecting,
  setSessionId,
  setError,
  clearError,
} = signalrSlice.actions;

export default signalrSlice.reducer;