import { configureStore } from '@reduxjs/toolkit';
import authReducer from './slices/authSlice';
import signalrReducer from './slices/signalrSlice';
import voiceReducer from './slices/voiceSlice';
import chatReducer from './slices/chatSlice';
import projectsReducer from './slices/projectsSlice';
import settingsReducer from './slices/settingsSlice';
import uiReducer from './slices/uiSlice';

export const store = configureStore({
  reducer: {
    auth: authReducer,
    signalr: signalrReducer,
    voice: voiceReducer,
    chat: chatReducer,
    projects: projectsReducer,
    settings: settingsReducer,
    ui: uiReducer,
  },
  middleware: (getDefaultMiddleware) =>
    getDefaultMiddleware({
      serializableCheck: {
        ignoredActions: ['signalr/initialize', 'signalr/connectionStateChanged'],
        ignoredPaths: ['signalr.connection'],
      },
    }),
  devTools: process.env.NODE_ENV !== 'production',
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;