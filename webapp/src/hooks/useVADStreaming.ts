import { useState, useEffect, useCallback, useRef } from 'react';
import { VADStreamingAudioService } from '../services/vadStreamingAudioService';
import { VADConfig, VADState } from '../services/vadProcessor';
import { useAppDispatch, useAppSelector } from '../store/hooks';
import { 
  setIsRecording, 
  setTranscription, 
  setPartialTranscription,
  setError 
} from '../store/slices/voiceSlice';

export interface VADMetrics {
  speechSegments: number;
  totalSpeechDuration: number;
  averageSegmentDuration: number;
  lastSpeechTimestamp: number | null;
  currentState: VADState;
}

export interface UseVADStreamingResult {
  isRecording: boolean;
  isConnected: boolean;
  isVADActive: boolean;
  vadState: VADState | null;
  startRecording: () => Promise<void>;
  stopRecording: () => Promise<void>;
  error: string | null;
  audioLevel: number;
  sessionId: string | null;
  vadMetrics: VADMetrics;
  updateVADConfig: (config: Partial<VADConfig>) => void;
}

export const useVADStreaming = (enableVAD: boolean = true): UseVADStreamingResult => {
  const dispatch = useAppDispatch();
  const { isRecording } = useAppSelector(state => state.voice);
  const [isConnected, setIsConnected] = useState(false);
  const [isVADActive, setIsVADActive] = useState(false);
  const [vadState, setVADState] = useState<VADState | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [audioLevel, setAudioLevel] = useState(0);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [vadMetrics, setVADMetrics] = useState<VADMetrics>({
    speechSegments: 0,
    totalSpeechDuration: 0,
    averageSegmentDuration: 0,
    lastSpeechTimestamp: null,
    currentState: VADState.Idle
  });
  
  const serviceRef = useRef<VADStreamingAudioService | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const animationFrameRef = useRef<number | null>(null);
  const speechStartTimeRef = useRef<number | null>(null);

  // Initialize service
  useEffect(() => {
    const initService = async () => {
      try {
        const service = new VADStreamingAudioService();
        await service.initialize({
          enableVAD,
          vadConfig: {
            speechThreshold: 0.7,
            silenceThreshold: 0.3,
            minSpeechDuration: 300,
            maxSilenceDuration: 1500
          },
          autoStartOnSpeech: true,
          autoStopOnSilence: true
        });
        
        serviceRef.current = service;
        setIsConnected(true);
        
        // Setup event listeners
        const handlePartialTranscription = (event: CustomEvent) => {
          dispatch(setPartialTranscription(event.detail.text));
          if (event.detail.isFinal) {
            dispatch(setTranscription(event.detail.text));
          }
        };

        const handleStreamError = (event: CustomEvent) => {
          setError(event.detail.message);
          dispatch(setError(event.detail.message));
        };

        const handleVADStatusChanged = (event: CustomEvent) => {
          setIsVADActive(event.detail.active);
          setVADState(event.detail.state);
          setVADMetrics(prev => ({
            ...prev,
            currentState: event.detail.state
          }));
        };

        const handleVADSpeechStart = (event: CustomEvent) => {
          speechStartTimeRef.current = event.detail.timestamp;
          setVADMetrics(prev => ({
            ...prev,
            speechSegments: prev.speechSegments + 1,
            lastSpeechTimestamp: event.detail.timestamp
          }));
        };

        const handleVADSpeechEnd = (event: CustomEvent) => {
          if (speechStartTimeRef.current && event.detail.duration) {
            setVADMetrics(prev => {
              const newTotalDuration = prev.totalSpeechDuration + event.detail.duration;
              const newSegments = prev.speechSegments;
              return {
                ...prev,
                totalSpeechDuration: newTotalDuration,
                averageSegmentDuration: newTotalDuration / newSegments
              };
            });
          }
          speechStartTimeRef.current = null;
        };

        window.addEventListener('partialTranscription', handlePartialTranscription as EventListener);
        window.addEventListener('streamError', handleStreamError as EventListener);
        window.addEventListener('vadStatusChanged', handleVADStatusChanged as EventListener);
        window.addEventListener('vadSpeechStart', handleVADSpeechStart as EventListener);
        window.addEventListener('vadSpeechEnd', handleVADSpeechEnd as EventListener);

        return () => {
          window.removeEventListener('partialTranscription', handlePartialTranscription as EventListener);
          window.removeEventListener('streamError', handleStreamError as EventListener);
          window.removeEventListener('vadStatusChanged', handleVADStatusChanged as EventListener);
          window.removeEventListener('vadSpeechStart', handleVADSpeechStart as EventListener);
          window.removeEventListener('vadSpeechEnd', handleVADSpeechEnd as EventListener);
        };
      } catch (err) {
        console.error('Failed to initialize VAD streaming service:', err);
        setError('Failed to connect to audio streaming service');
        setIsConnected(false);
      }
    };

    initService();

    return () => {
      if (serviceRef.current) {
        serviceRef.current.disconnect();
      }
    };
  }, [dispatch, enableVAD]);

  // Audio level monitoring
  const startAudioLevelMonitoring = useCallback(async () => {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      audioContextRef.current = new AudioContext();
      const source = audioContextRef.current.createMediaStreamSource(stream);
      analyserRef.current = audioContextRef.current.createAnalyser();
      analyserRef.current.fftSize = 256;
      source.connect(analyserRef.current);

      const dataArray = new Uint8Array(analyserRef.current.frequencyBinCount);
      
      const updateLevel = () => {
        if (!analyserRef.current) return;
        
        analyserRef.current.getByteFrequencyData(dataArray);
        const average = dataArray.reduce((a, b) => a + b) / dataArray.length;
        setAudioLevel(average / 255);
        
        animationFrameRef.current = requestAnimationFrame(updateLevel);
      };

      updateLevel();
    } catch (err) {
      console.error('Error starting audio level monitoring:', err);
    }
  }, []);

  const stopAudioLevelMonitoring = useCallback(() => {
    if (animationFrameRef.current) {
      cancelAnimationFrame(animationFrameRef.current);
      animationFrameRef.current = null;
    }
    
    if (audioContextRef.current) {
      audioContextRef.current.close();
      audioContextRef.current = null;
    }
    
    setAudioLevel(0);
  }, []);

  const startRecording = useCallback(async () => {
    if (!serviceRef.current || isRecording) return;

    try {
      setError(null);
      dispatch(setIsRecording(true));
      dispatch(setTranscription(''));
      dispatch(setPartialTranscription(''));
      
      // Reset VAD metrics
      setVADMetrics({
        speechSegments: 0,
        totalSpeechDuration: 0,
        averageSegmentDuration: 0,
        lastSpeechTimestamp: null,
        currentState: VADState.Idle
      });
      
      await serviceRef.current.startRecording(enableVAD);
      setSessionId(serviceRef.current.getSessionId());
      
      // Start audio level monitoring
      await startAudioLevelMonitoring();
    } catch (err: any) {
      console.error('Error starting recording:', err);
      const errorMessage = err.message || 'Failed to start recording';
      setError(errorMessage);
      dispatch(setError(errorMessage));
      dispatch(setIsRecording(false));
    }
  }, [dispatch, isRecording, enableVAD, startAudioLevelMonitoring]);

  const stopRecording = useCallback(async () => {
    if (!serviceRef.current || !isRecording) return;

    try {
      await serviceRef.current.stopRecording();
      dispatch(setIsRecording(false));
      
      // Stop audio level monitoring
      stopAudioLevelMonitoring();
    } catch (err: any) {
      console.error('Error stopping recording:', err);
      const errorMessage = err.message || 'Failed to stop recording';
      setError(errorMessage);
      dispatch(setError(errorMessage));
    }
  }, [dispatch, isRecording, stopAudioLevelMonitoring]);

  const updateVADConfig = useCallback((config: Partial<VADConfig>) => {
    if (serviceRef.current) {
      serviceRef.current.updateVADConfig(config);
    }
  }, []);

  return {
    isRecording,
    isConnected,
    isVADActive,
    vadState,
    startRecording,
    stopRecording,
    error,
    audioLevel,
    sessionId,
    vadMetrics,
    updateVADConfig
  };
};