import { useCallback, useEffect, useRef } from 'react';
import { useAppDispatch, useAppSelector } from './redux';
import {
  startRecording,
  stopRecording,
  setProcessing,
  setAudioLevel,
  setError,
  setPermissionGranted,
  setPermissionRequested,
  clearAudioChunks,
  addAudioChunk,
} from '../store/slices/voiceSlice';
import { sendAudioMessage } from '../store/slices/signalrSlice';

export const useVoiceRecording = () => {
  const dispatch = useAppDispatch();
  const {
    isRecording,
    permissionGranted,
    permissionRequested,
    mediaRecorder,
  } = useAppSelector((state) => state.voice);

  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const audioChunksRef = useRef<Blob[]>([]);
  const streamRef = useRef<MediaStream | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const animationFrameRef = useRef<number>();

  // Request microphone permission
  const requestPermission = useCallback(async () => {
    if (permissionRequested) return;

    dispatch(setPermissionRequested(true));

    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          echoCancellation: true,
          noiseSuppression: true,
          autoGainControl: true,
          sampleRate: 16000,
        },
      });

      // Test if we can create a MediaRecorder
      if (!MediaRecorder.isTypeSupported('audio/webm;codecs=opus')) {
        throw new Error('Browser does not support required audio format');
      }

      dispatch(setPermissionGranted(true));
      
      // Stop the test stream
      stream.getTracks().forEach(track => track.stop());
    } catch (error) {
      console.error('Permission denied or error:', error);
      dispatch(setError(`Microphone access denied: ${error}`));
      dispatch(setPermissionGranted(false));
    }
  }, [dispatch, permissionRequested]);

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
      
      // Calculate RMS (Root Mean Square) for volume
      let sum = 0;
      for (let i = 0; i < dataArray.length; i++) {
        sum += dataArray[i] * dataArray[i];
      }
      const rms = Math.sqrt(sum / dataArray.length);
      const level = Math.min(rms / 128, 1); // Normalize to 0-1

      dispatch(setAudioLevel(level));
      
      if (isRecording) {
        animationFrameRef.current = requestAnimationFrame(updateAudioLevel);
      }
    };

    updateAudioLevel();
  }, [dispatch, isRecording]);

  // Start recording
  const handleStartRecording = useCallback(async () => {
    if (!permissionGranted) {
      await requestPermission();
      return;
    }

    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          echoCancellation: true,
          noiseSuppression: true,
          autoGainControl: true,
          sampleRate: 16000,
        },
      });

      streamRef.current = stream;
      
      const recorder = new MediaRecorder(stream, {
        mimeType: 'audio/webm;codecs=opus',
        audioBitsPerSecond: 16000,
      });

      audioChunksRef.current = [];
      dispatch(clearAudioChunks());

      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) {
          audioChunksRef.current.push(event.data);
          dispatch(addAudioChunk(event.data));
        }
      };

      recorder.onstop = async () => {
        if (audioChunksRef.current.length > 0) {
          const audioBlob = new Blob(audioChunksRef.current, { type: 'audio/webm' });
          const arrayBuffer = await audioBlob.arrayBuffer();
          
          dispatch(setProcessing(true));
          
          try {
            await dispatch(sendAudioMessage(arrayBuffer));
          } catch (error) {
            dispatch(setError(`Failed to send audio: ${error}`));
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
      recorder.start(250); // Collect data every 250ms
      
      dispatch(startRecording());
      startAudioLevelMonitoring(stream);
      
    } catch (error) {
      console.error('Error starting recording:', error);
      dispatch(setError(`Failed to start recording: ${error}`));
    }
  }, [dispatch, permissionGranted, requestPermission, startAudioLevelMonitoring]);

  // Stop recording
  const handleStopRecording = useCallback(() => {
    if (mediaRecorderRef.current && mediaRecorderRef.current.state === 'recording') {
      mediaRecorderRef.current.stop();
    }
    
    dispatch(stopRecording());
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
    startRecording: handleStartRecording,
    stopRecording: handleStopRecording,
    requestPermission,
    permissionGranted,
    isRecording,
  };
};