import { createSlice, PayloadAction } from '@reduxjs/toolkit';

export interface VoiceState {
  isRecording: boolean;
  isProcessing: boolean;
  currentTranscript: string | null;
  audioLevel: number;
  error: string | null;
  permissionGranted: boolean;
  permissionRequested: boolean;
  mediaRecorder: MediaRecorder | null;
  audioChunks: Blob[];
  recognitionResults: TranscriptionResult[];
  audioQueue: AudioQueueItem[];
  playingAudio: boolean;
}

export interface TranscriptionResult {
  id: string;
  transcript: string;
  confidence: number;
  timestamp: Date;
  duration?: number;
}

export interface AudioQueueItem {
  id: string;
  audioUrl: string;
  text: string;
  timestamp: Date;
}

const initialState: VoiceState = {
  isRecording: false,
  isProcessing: false,
  currentTranscript: null,
  audioLevel: 0,
  error: null,
  permissionGranted: false,
  permissionRequested: false,
  mediaRecorder: null,
  audioChunks: [],
  recognitionResults: [],
  audioQueue: [],
  playingAudio: false,
};

const voiceSlice = createSlice({
  name: 'voice',
  initialState,
  reducers: {
    startRecording: (state) => {
      state.isRecording = true;
      state.error = null;
    },
    stopRecording: (state) => {
      state.isRecording = false;
    },
    setProcessing: (state, action: PayloadAction<boolean>) => {
      state.isProcessing = action.payload;
    },
    setCurrentTranscript: (state, action: PayloadAction<string | null>) => {
      state.currentTranscript = action.payload;
    },
    setAudioLevel: (state, action: PayloadAction<number>) => {
      state.audioLevel = action.payload;
    },
    setError: (state, action: PayloadAction<string>) => {
      state.error = action.payload;
      state.isRecording = false;
      state.isProcessing = false;
    },
    clearError: (state) => {
      state.error = null;
    },
    setPermissionGranted: (state, action: PayloadAction<boolean>) => {
      state.permissionGranted = action.payload;
    },
    setPermissionRequested: (state, action: PayloadAction<boolean>) => {
      state.permissionRequested = action.payload;
    },
    setMediaRecorder: (state, action: PayloadAction<MediaRecorder | null>) => {
      // Note: MediaRecorder is not serializable, so we'll handle it differently
      state.mediaRecorder = action.payload;
    },
    addAudioChunk: (state, action: PayloadAction<Blob>) => {
      // Note: Blob is not serializable, handle separately
      state.audioChunks.push(action.payload);
    },
    clearAudioChunks: (state) => {
      state.audioChunks = [];
    },
    transcriptionReceived: (state, action: PayloadAction<TranscriptionResult>) => {
      state.recognitionResults.unshift(action.payload);
      state.currentTranscript = action.payload.transcript;
      state.isProcessing = false;
      
      // Keep only last 50 results
      if (state.recognitionResults.length > 50) {
        state.recognitionResults = state.recognitionResults.slice(0, 50);
      }
    },
    audioReady: (state, action: PayloadAction<{ id: string; audioUrl: string; text?: string }>) => {
      const audioItem: AudioQueueItem = {
        id: action.payload.id,
        audioUrl: action.payload.audioUrl,
        text: action.payload.text || '',
        timestamp: new Date(),
      };
      state.audioQueue.push(audioItem);
    },
    playNextAudio: (state) => {
      if (state.audioQueue.length > 0) {
        state.audioQueue.shift();
      }
    },
    setPlayingAudio: (state, action: PayloadAction<boolean>) => {
      state.playingAudio = action.payload;
    },
    clearAudioQueue: (state) => {
      state.audioQueue = [];
      state.playingAudio = false;
    },
    resetVoiceState: (state) => {
      return {
        ...initialState,
        permissionGranted: state.permissionGranted,
        permissionRequested: state.permissionRequested,
      };
    },
  },
});

export const {
  startRecording,
  stopRecording,
  setProcessing,
  setCurrentTranscript,
  setAudioLevel,
  setError,
  clearError,
  setPermissionGranted,
  setPermissionRequested,
  setMediaRecorder,
  addAudioChunk,
  clearAudioChunks,
  transcriptionReceived,
  audioReady,
  playNextAudio,
  setPlayingAudio,
  clearAudioQueue,
  resetVoiceState,
} = voiceSlice.actions;

export default voiceSlice.reducer;