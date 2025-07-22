import { useCallback, useRef, useState } from 'react';
import { useAppDispatch, useAppSelector } from './redux';
import { setError } from '../store/slices/voiceSlice';

export const useAudioPlayback = () => {
  const dispatch = useAppDispatch();
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentAudioId, setCurrentAudioId] = useState<string | null>(null);
  const audioRef = useRef<HTMLAudioElement | null>(null);

  const playAudio = useCallback(async (audioData: ArrayBuffer | string, audioId?: string) => {
    try {
      // Stop current audio if playing
      if (audioRef.current) {
        audioRef.current.pause();
        audioRef.current = null;
      }

      let audioUrl: string;

      if (typeof audioData === 'string') {
        // Assume it's already a URL or base64
        audioUrl = audioData;
      } else {
        // Convert ArrayBuffer to blob URL
        const audioBlob = new Blob([audioData], { type: 'audio/wav' });
        audioUrl = URL.createObjectURL(audioBlob);
      }

      const audio = new Audio(audioUrl);
      audioRef.current = audio;

      audio.onloadstart = () => {
        setIsPlaying(true);
        if (audioId) setCurrentAudioId(audioId);
      };

      audio.onended = () => {
        setIsPlaying(false);
        setCurrentAudioId(null);
        if (typeof audioData !== 'string') {
          URL.revokeObjectURL(audioUrl);
        }
        audioRef.current = null;
      };

      audio.onerror = (error) => {
        console.error('Audio playback error:', error);
        dispatch(setError('Failed to play audio'));
        setIsPlaying(false);
        setCurrentAudioId(null);
        if (typeof audioData !== 'string') {
          URL.revokeObjectURL(audioUrl);
        }
        audioRef.current = null;
      };

      await audio.play();
    } catch (error) {
      console.error('Error playing audio:', error);
      dispatch(setError(`Audio playback failed: ${error}`));
      setIsPlaying(false);
      setCurrentAudioId(null);
    }
  }, [dispatch]);

  const stopAudio = useCallback(() => {
    if (audioRef.current) {
      audioRef.current.pause();
      audioRef.current.currentTime = 0;
      audioRef.current = null;
    }
    setIsPlaying(false);
    setCurrentAudioId(null);
  }, []);

  return {
    playAudio,
    stopAudio,
    isPlaying,
    currentAudioId,
  };
};