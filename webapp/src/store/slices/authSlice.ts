import { createSlice, createAsyncThunk, PayloadAction } from '@reduxjs/toolkit';
import { AccountInfo } from '@azure/msal-browser';

export interface AuthState {
  isAuthenticated: boolean;
  account: AccountInfo | null;
  accessToken: string | null;
  loading: boolean;
  error: string | null;
}

const initialState: AuthState = {
  isAuthenticated: false,
  account: null,
  accessToken: null,
  loading: false,
  error: null,
};

// Async thunks
export const acquireAccessToken = createAsyncThunk(
  'auth/acquireAccessToken',
  async (scopes: string[], { rejectWithValue }) => {
    try {
      // This would be handled by MSAL in the component
      // For now, return a placeholder
      return 'placeholder-token';
    } catch (error) {
      return rejectWithValue('Failed to acquire access token');
    }
  }
);

const authSlice = createSlice({
  name: 'auth',
  initialState,
  reducers: {
    initializeAuth: (state, action: PayloadAction<{ account: AccountInfo; accessToken: string | null }>) => {
      state.isAuthenticated = true;
      state.account = action.payload.account;
      state.accessToken = action.payload.accessToken;
      state.error = null;
    },
    setAccessToken: (state, action: PayloadAction<string>) => {
      state.accessToken = action.payload;
    },
    clearAuth: (state) => {
      state.isAuthenticated = false;
      state.account = null;
      state.accessToken = null;
      state.error = null;
    },
    setAuthError: (state, action: PayloadAction<string>) => {
      state.error = action.payload;
      state.loading = false;
    },
    clearAuthError: (state) => {
      state.error = null;
    },
  },
  extraReducers: (builder) => {
    builder
      .addCase(acquireAccessToken.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(acquireAccessToken.fulfilled, (state, action) => {
        state.loading = false;
        state.accessToken = action.payload;
      })
      .addCase(acquireAccessToken.rejected, (state, action) => {
        state.loading = false;
        state.error = action.payload as string;
      });
  },
});

export const {
  initializeAuth,
  setAccessToken,
  clearAuth,
  setAuthError,
  clearAuthError,
} = authSlice.actions;

export default authSlice.reducer;