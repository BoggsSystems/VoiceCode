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
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          echoCancellation: true,
          noiseSuppression: true,
          autoGainControl: true,
        },
      });

      setPermissionGranted(true);
      
      // Stop the test stream
      stream.getTracks().forEach(track => track.stop());
    } catch (error) {
      console.error('Permission denied:', error);
      dispatch(setError(`Microphone access denied`));
      setPermissionGranted(false);
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
    if (!permissionGranted) {
      await requestPermission();
      if (!permissionGranted) return;
    }

    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          echoCancellation: true,
          noiseSuppression: true,
          autoGainControl: true,
        },
      });

      streamRef.current = stream;
      
      const recorder = new MediaRecorder(stream, {
        mimeType: 'audio/webm;codecs=opus',
      });

      audioChunksRef.current = [];

      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) {
          audioChunksRef.current.push(event.data);
        }
      };

      recorder.onstop = async () => {
        if (audioChunksRef.current.length > 0) {
          const audioBlob = new Blob(audioChunksRef.current, { type: 'audio/webm' });
          
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
      recorder.start();
      
      setIsRecording(true);
      startAudioLevelMonitoring(stream);
      
    } catch (error) {
      console.error('Error starting recording:', error);
      dispatch(setError(`Failed to start recording: ${error}`));
    }
  }, [dispatch, permissionGranted, requestPermission, startAudioLevelMonitoring]);

  // Stop recording
  const stopRecording = useCallback(() => {
    if (mediaRecorderRef.current && mediaRecorderRef.current.state === 'recording') {
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