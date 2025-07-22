import { createSlice, createAsyncThunk, PayloadAction } from '@reduxjs/toolkit';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { apiConfig } from '../../config/authConfig';

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
      const connection = new HubConnectionBuilder()
        .withUrl(`${apiConfig.signalrUrl}/hubs/voice`, {
          accessTokenFactory: async () => {
            // Get access token from auth state or MSAL
            const state = getState() as any;
            return state.auth.accessToken || '';
          },
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
      connection.onreconnecting(() => {
        dispatch(setReconnecting(true));
      });

      connection.onreconnected(() => {
        dispatch(setReconnecting(false));
        dispatch(setConnected(true));
      });

      connection.onclose(() => {
        dispatch(setConnected(false));
        dispatch(setReconnecting(false));
      });

      // Set up server event handlers
      connection.on('SessionStarted', (session) => {
        dispatch(setSessionId(session.id));
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
        dispatch({ type: 'voice/audioReady', payload: result });
      });

      connection.on('Error', (error) => {
        dispatch(setError(error.message));
      });

      await connection.start();
      dispatch(setConnected(true));
      
      return connection;
    } catch (error) {
      return rejectWithValue(`Failed to initialize SignalR: ${error}`);
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