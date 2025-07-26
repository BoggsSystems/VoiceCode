import { useState, useEffect, useCallback, useRef } from 'react';
import { StreamingAudioService } from '../services/streamingAudioService';
import { useAppDispatch, useAppSelector } from '../store/hooks';
import { 
  setIsRecording, 
  setTranscription, 
  setPartialTranscription,
  setError,
  setProcessing
} from '../store/slices/voiceSlice';
import { addMessage } from '../store/slices/chatSlice';
import audioService from '../services/audioService';

export interface UseStreamingAudioResult {
  isRecording: boolean;
  isConnected: boolean;
  startRecording: () => Promise<void>;
  stopRecording: () => Promise<void>;
  error: string | null;
  audioLevel: number;
  sessionId: string | null;
}

export const useStreamingAudio = (): UseStreamingAudioResult => {
  const dispatch = useAppDispatch();
  const { isRecording } = useAppSelector(state => state.voice);
  const [isConnected, setIsConnected] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [audioLevel, setAudioLevel] = useState(0);
  const [sessionId, setSessionId] = useState<string | null>(null);
  
  const serviceRef = useRef<StreamingAudioService | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const animationFrameRef = useRef<number | null>(null);

  // Initialize service
  useEffect(() => {
    const initService = async () => {
      try {
        const service = new StreamingAudioService();
        await service.initialize();
        serviceRef.current = service;
        setIsConnected(true);
        
        // Setup event listeners
        const handlePartialTranscription = (event: CustomEvent) => {
          dispatch(setPartialTranscription(event.detail.text));
        };

        const handleFinalTranscription = async (event: CustomEvent) => {
          const transcript = event.detail.text;
          dispatch(setTranscription(transcript));
          dispatch(setPartialTranscription(''));
          
          // Add user message
          dispatch(addMessage({
            type: 'user',
            content: transcript,
          }));
          
          // Process with Claude
          dispatch(setProcessing(true));
          try {
            const response = await audioService.processWithClaude(transcript);
            
            // Add AI response
            dispatch(addMessage({
              type: 'assistant',
              content: response,
            }));
          } catch (error) {
            console.error('Error processing with Claude:', error);
            dispatch(setError('Failed to process response'));
          } finally {
            dispatch(setProcessing(false));
          }
        };

        const handleStreamError = (event: CustomEvent) => {
          setError(event.detail.message);
          dispatch(setError(event.detail.message));
        };

        window.addEventListener('partialTranscription', handlePartialTranscription as any);
        window.addEventListener('finalTranscription', handleFinalTranscription as any);
        window.addEventListener('streamError', handleStreamError as any);

        return () => {
          window.removeEventListener('partialTranscription', handlePartialTranscription as any);
          window.removeEventListener('finalTranscription', handleFinalTranscription as any);
          window.removeEventListener('streamError', handleStreamError as any);
        };
      } catch (err) {
        console.error('Failed to initialize streaming service:', err);
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
  }, [dispatch]);

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
      
      await serviceRef.current.startRecording();
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
  }, [dispatch, isRecording, startAudioLevelMonitoring]);

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

  return {
    isRecording,
    isConnected,
    startRecording,
    stopRecording,
    error,
    audioLevel,
    sessionId
  };
};