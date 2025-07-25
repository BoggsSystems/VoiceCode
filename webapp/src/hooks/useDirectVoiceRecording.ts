import { useCallback, useEffect, useRef, useState } from 'react';
import { useAppDispatch } from './redux';
import {
  setAudioLevel,
  setError,
  setCurrentTranscript,
  setProcessing,
  transcriptionReceived,
} from '../store/slices/voiceSlice';
import { addMessage } from '../store/slices/chatSlice';
import audioService from '../services/audioService';
import { remoteLogger } from '../services/remoteLogger';

export const useDirectVoiceRecording = () => {
  const dispatch = useAppDispatch();
  const [isRecording, setIsRecording] = useState(false);
  const [permissionGranted, setPermissionGranted] = useState(false);
  const [isProcessing, setIsProcessing] = useState(false);
  
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const audioChunksRef = useRef<Blob[]>([]);
  const streamRef = useRef<MediaStream | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const animationFrameRef = useRef<number>();

  // Request microphone permission
  const requestPermission = useCallback(async () => {
    console.log('[useDirectVoiceRecording] Requesting microphone permission...');
    remoteLogger.info('Requesting microphone permission');
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          echoCancellation: true,
          noiseSuppression: true,
          autoGainControl: true,
        },
      });

      console.log('[useDirectVoiceRecording] Permission granted, stream obtained');
      remoteLogger.info('Microphone permission granted', {
        streamActive: stream.active,
        trackCount: stream.getTracks().length
      });
      setPermissionGranted(true);
      
      // Stop the test stream
      stream.getTracks().forEach(track => track.stop());
      console.log('[useDirectVoiceRecording] Test stream stopped');
      return true;
    } catch (error) {
      console.error('[useDirectVoiceRecording] Permission denied:', error);
      remoteLogger.error('Microphone permission denied', {
        error: error instanceof Error ? error.message : String(error),
        errorName: error instanceof Error ? error.name : 'Unknown'
      });
      dispatch(setError(`Microphone access denied`));
      setPermissionGranted(false);
      return false;
    }
  }, [dispatch]);

  // Start audio level monitoring
  const startAudioLevelMonitoring = useCallback((stream: MediaStream) => {
    if (!audioContextRef.current) {
      audioContextRef.current = new (window.AudioContext || (window as any).webkitAudioContext)();
    }

    const audioContext = audioContextRef.current;
    const source = audioContext.createMediaStreamSource(stream);
    const analyser = audioContext.createAnalyser();
    
    analyser.fftSize = 256;
    analyser.smoothingTimeConstant = 0.8;
    source.connect(analyser);
    
    analyserRef.current = analyser;

    const dataArray = new Uint8Array(analyser.frequencyBinCount);

    const updateAudioLevel = () => {
      if (!analyserRef.current || !isRecording) return;

      analyser.getByteFrequencyData(dataArray);
      
      // Calculate RMS
      let sum = 0;
      for (let i = 0; i < dataArray.length; i++) {
        sum += dataArray[i] * dataArray[i];
      }
      const rms = Math.sqrt(sum / dataArray.length);
      const level = Math.min(rms / 128, 1);

      dispatch(setAudioLevel(level));
      
      if (isRecording) {
        animationFrameRef.current = requestAnimationFrame(updateAudioLevel);
      }
    };

    updateAudioLevel();
  }, [dispatch, isRecording]);

  // Start recording
  const startRecording = useCallback(async () => {
    console.log('[useDirectVoiceRecording] startRecording called');
    remoteLogger.info('startRecording called', {
      permissionGranted,
      hasNavigator: !!navigator.mediaDevices,
      hasGetUserMedia: !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia)
    });
    
    if (!permissionGranted) {
      console.log('[useDirectVoiceRecording] No permission yet, requesting...');
      remoteLogger.info('No permission, requesting');
      const granted = await requestPermission();
      if (!granted) {
        console.log('[useDirectVoiceRecording] Permission still not granted, aborting');
        remoteLogger.warn('Permission denied after request');
        return;
      }
    }

    try {
      console.log('[useDirectVoiceRecording] Getting user media stream...');
      remoteLogger.info('Getting user media stream');
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          echoCancellation: true,
          noiseSuppression: true,
          autoGainControl: true,
        },
      });

      console.log('[useDirectVoiceRecording] Stream obtained successfully');
      remoteLogger.info('Stream obtained', {
        streamActive: stream.active,
        trackCount: stream.getTracks().length,
        audioTracks: stream.getAudioTracks().length
      });
      streamRef.current = stream;
      
      console.log('[useDirectVoiceRecording] Creating MediaRecorder...');
      
      // Check supported MIME types
      const supportedTypes = [
        'audio/webm;codecs=opus',
        'audio/webm',
        'audio/ogg;codecs=opus',
        'audio/mp4'
      ];
      
      let selectedType = '';
      for (const type of supportedTypes) {
        if (MediaRecorder.isTypeSupported(type)) {
          selectedType = type;
          break;
        }
      }
      
      remoteLogger.info('MediaRecorder setup', {
        selectedType,
        supportedTypes: supportedTypes.filter(t => MediaRecorder.isTypeSupported(t))
      });
      
      const recorder = new MediaRecorder(stream, {
        mimeType: selectedType || undefined,
      });

      audioChunksRef.current = [];

      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) {
          console.log('[useDirectVoiceRecording] Audio data available, size:', event.data.size);
          remoteLogger.debug('Audio data chunk', { size: event.data.size });
          audioChunksRef.current.push(event.data);
        }
      };

      recorder.onstop = async () => {
        console.log('[useDirectVoiceRecording] Recording stopped, chunks:', audioChunksRef.current.length);
        remoteLogger.info('Recording stopped', {
          chunkCount: audioChunksRef.current.length,
          totalSize: audioChunksRef.current.reduce((sum, chunk) => sum + chunk.size, 0)
        });
        
        if (audioChunksRef.current.length > 0) {
          const audioBlob = new Blob(audioChunksRef.current, { type: 'audio/webm' });
          console.log('[useDirectVoiceRecording] Created audio blob, size:', audioBlob.size);
          remoteLogger.info('Audio blob created', { size: audioBlob.size });
          
          setIsProcessing(true);
          dispatch(setProcessing(true));
          dispatch(setCurrentTranscript('Processing audio...'));
          
          try {
            // Transcribe audio
            const transcriptionResult = await audioService.transcribeAudio(audioBlob);
            
            // Update UI with transcription
            dispatch(setCurrentTranscript(transcriptionResult.transcript));
            dispatch(transcriptionReceived({
              id: transcriptionResult.id,
              transcript: transcriptionResult.transcript,
              confidence: transcriptionResult.confidence,
              timestamp: transcriptionResult.timestamp,
            }));
            
            // Add user message
            dispatch(addMessage({
              type: 'user',
              content: transcriptionResult.transcript,
            }));
            
            // Process with Claude
            const claudeResponse = await audioService.processWithClaude(transcriptionResult.transcript);
            
            // Add AI response
            dispatch(addMessage({
              type: 'assistant',
              content: claudeResponse,
            }));
            
            dispatch(setCurrentTranscript(''));
          } catch (error) {
            dispatch(setError(`Processing failed: ${error}`));
          } finally {
            setIsProcessing(false);
            dispatch(setProcessing(false));
          }
        }
        
        // Cleanup
        if (streamRef.current) {
          streamRef.current.getTracks().forEach(track => track.stop());
          streamRef.current = null;
        }
        
        if (animationFrameRef.current) {
          cancelAnimationFrame(animationFrameRef.current);
        }
      };

      mediaRecorderRef.current = recorder;
      console.log('[useDirectVoiceRecording] Starting MediaRecorder...');
      recorder.start();
      
      console.log('[useDirectVoiceRecording] Recording started successfully');
      setIsRecording(true);
      startAudioLevelMonitoring(stream);
      
    } catch (error) {
      console.error('[useDirectVoiceRecording] Error starting recording:', error);
      dispatch(setError(`Failed to start recording: ${error}`));
    }
  }, [dispatch, permissionGranted, requestPermission, startAudioLevelMonitoring]);

  // Stop recording
  const stopRecording = useCallback(() => {
    console.log('[useDirectVoiceRecording] stopRecording called');
    if (mediaRecorderRef.current && mediaRecorderRef.current.state === 'recording') {
      console.log('[useDirectVoiceRecording] Stopping MediaRecorder...');
      mediaRecorderRef.current.stop();
    }
    
    setIsRecording(false);
    dispatch(setAudioLevel(0));
    
    if (animationFrameRef.current) {
      cancelAnimationFrame(animationFrameRef.current);
    }
  }, [dispatch]);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      if (mediaRecorderRef.current && mediaRecorderRef.current.state === 'recording') {
        mediaRecorderRef.current.stop();
      }
      
      if (streamRef.current) {
        streamRef.current.getTracks().forEach(track => track.stop());
      }
      
      if (audioContextRef.current) {
        audioContextRef.current.close();
      }
      
      if (animationFrameRef.current) {
        cancelAnimationFrame(animationFrameRef.current);
      }
    };
  }, []);

  return {
    startRecording,
    stopRecording,
    requestPermission,
    permissionGranted,
    isRecording,
    isProcessing,
  };
};