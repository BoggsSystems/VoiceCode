import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import { remoteLogger } from '../../services/remoteLogger';

export interface VoiceState {
  isRecording: boolean;
  isProcessing: boolean;
  currentTranscript: string | null;
  partialTranscript: string | null;
  audioLevel: number;
  error: string | null;
  permissionGranted: boolean;
  permissionRequested: boolean;
  mediaRecorder: MediaRecorder | null;
  audioChunks: Blob[];
  recognitionResults: TranscriptionResult[];
  audioQueue: AudioQueueItem[];
  playingAudio: boolean;
  playedAudioUrls: string[]; // Track URLs that have been played
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
  partialTranscript: null,
  audioLevel: 0,
  error: null,
  permissionGranted: false,
  permissionRequested: false,
  mediaRecorder: null,
  audioChunks: [],
  recognitionResults: [],
  audioQueue: [],
  playingAudio: false,
  playedAudioUrls: [], // Initialize empty array for played URLs
};

const voiceSlice = createSlice({
  name: 'voice',
  initialState,
  reducers: {
    setIsRecording: (state, action: PayloadAction<boolean>) => {
      state.isRecording = action.payload;
      if (action.payload) {
        state.error = null;
      }
    },
    setTranscription: (state, action: PayloadAction<string>) => {
      state.currentTranscript = action.payload;
    },
    setPartialTranscription: (state, action: PayloadAction<string>) => {
      state.partialTranscript = action.payload;
    },
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
      // Check if this audio URL has already been played or is in queue
      const audioUrl = action.payload.audioUrl;
      const isAlreadyPlayed = state.playedAudioUrls.includes(audioUrl);
      const isInQueue = state.audioQueue.some(item => item.audioUrl === audioUrl);
      
      if (isAlreadyPlayed || isInQueue) {
        console.log('[VoiceSlice] Skipping duplicate audio URL:', audioUrl);
        remoteLogger.warn('[VoiceSlice] Duplicate audio URL detected and skipped', {
          audioUrl,
          isAlreadyPlayed,
          isInQueue,
          playedUrlsCount: state.playedAudioUrls.length,
          queueLength: state.audioQueue.length
        });
        return; // Skip duplicate audio
      }
      
      const audioItem: AudioQueueItem = {
        id: action.payload.id,
        audioUrl: action.payload.audioUrl,
        text: action.payload.text || '',
        timestamp: new Date(),
      };
      state.audioQueue.push(audioItem);
      
      console.log('[VoiceSlice] New audio added to queue:', audioUrl);
      remoteLogger.info('[VoiceSlice] New audio added to queue', {
        audioUrl,
        id: action.payload.id,
        queueLength: state.audioQueue.length,
        playedUrlsCount: state.playedAudioUrls.length
      });
    },
    playNextAudio: (state) => {
      if (state.audioQueue.length > 0) {
        const playedItem = state.audioQueue.shift();
        if (playedItem) {
          // Add the URL to played list
          state.playedAudioUrls.push(playedItem.audioUrl);
          // Keep only last 100 played URLs to prevent memory issues
          if (state.playedAudioUrls.length > 100) {
            state.playedAudioUrls = state.playedAudioUrls.slice(-100);
          }
        }
      }
    },
    setPlayingAudio: (state, action: PayloadAction<boolean>) => {
      state.playingAudio = action.payload;
    },
    clearAudioQueue: (state) => {
      state.audioQueue = [];
      state.playingAudio = false;
    },
    clearPlayedAudioUrls: (state) => {
      state.playedAudioUrls = [];
    },
    resetVoiceState: (state) => {
      return {
        ...initialState,
        permissionGranted: state.permissionGranted,
        permissionRequested: state.permissionRequested,
        playedAudioUrls: [], // Reset played URLs on voice state reset
      };
    },
  },
});

export const {
  setIsRecording,
  setTranscription,
  setPartialTranscription,
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
  clearPlayedAudioUrls,
  resetVoiceState,
} = voiceSlice.actions;

export default voiceSlice.reducer;