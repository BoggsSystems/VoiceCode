import { createSlice, PayloadAction } from '@reduxjs/toolkit';

interface VoiceSettings {
  enableVoiceRecognition: boolean;
  enableTextToSpeech: boolean;
  voiceModel: string;
  language: string;
}

interface AISettings {
  model: 'claude-3.5-sonnet' | 'gpt-4' | 'gpt-3.5-turbo';
  temperature: number;
  maxTokens: number;
}

interface UISettings {
  theme: 'light' | 'dark' | 'system';
  fontSize: 'small' | 'medium' | 'large';
  codeTheme: string;
}

interface SettingsState {
  voice: VoiceSettings;
  ai: AISettings;
  ui: UISettings;
  loading: boolean;
  error: string | null;
}

const initialState: SettingsState = {
  voice: {
    enableVoiceRecognition: true,
    enableTextToSpeech: true,
    voiceModel: 'default',
    language: 'en-US',
  },
  ai: {
    model: 'claude-3.5-sonnet',
    temperature: 0.7,
    maxTokens: 2000,
  },
  ui: {
    theme: 'light',
    fontSize: 'medium',
    codeTheme: 'vs-code',
  },
  loading: false,
  error: null,
};

const settingsSlice = createSlice({
  name: 'settings',
  initialState,
  reducers: {
    updateVoiceSettings: (state, action: PayloadAction<Partial<VoiceSettings>>) => {
      state.voice = { ...state.voice, ...action.payload };
    },
    updateAISettings: (state, action: PayloadAction<Partial<AISettings>>) => {
      state.ai = { ...state.ai, ...action.payload };
    },
    updateUISettings: (state, action: PayloadAction<Partial<UISettings>>) => {
      state.ui = { ...state.ui, ...action.payload };
    },
    resetSettings: (state) => {
      return initialState;
    },
    setLoading: (state, action: PayloadAction<boolean>) => {
      state.loading = action.payload;
    },
    setError: (state, action: PayloadAction<string | null>) => {
      state.error = action.payload;
    },
  },
});

export const {
  updateVoiceSettings,
  updateAISettings,
  updateUISettings,
  resetSettings,
  setLoading,
  setError,
} = settingsSlice.actions;

export default settingsSlice.reducer;