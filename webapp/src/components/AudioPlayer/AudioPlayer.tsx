import React, { useEffect, useRef, useState } from 'react';
import { useSelector, useDispatch } from 'react-redux';
import { RootState } from '../../store/store';
import { playNextAudio, setPlayingAudio } from '../../store/slices/voiceSlice';
import { signalrDebugger } from '../../utils/signalrDebugger';

const AudioPlayer: React.FC = () => {
  const dispatch = useDispatch();
  const audioQueue = useSelector((state: RootState) => state.voice.audioQueue);
  const playingAudio = useSelector((state: RootState) => state.voice.playingAudio);
  const audioRef = useRef<HTMLAudioElement>(null);
  const [currentAudioIndex, setCurrentAudioIndex] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [retryCount, setRetryCount] = useState(0);
  const MAX_RETRIES = 3;

  useEffect(() => {
    if (audioQueue.length > 0 && !playingAudio && currentAudioIndex < audioQueue.length) {
      const currentAudio = audioQueue[currentAudioIndex];
      if (currentAudio && audioRef.current) {
        console.log('[AudioPlayer] Processing audio:', {
          id: currentAudio.id,
          url: currentAudio.audioUrl,
          text: currentAudio.text,
          urlType: currentAudio.audioUrl.substring(0, 50)
        });
        
        signalrDebugger.log('info', 'Playing audio response', {
          id: currentAudio.id,
          url: currentAudio.audioUrl,
          text: currentAudio.text
        });

        // Check if the URL is a blob URL, data URL, or Azure Blob Storage URL
        if (currentAudio.audioUrl.startsWith('blob:') || currentAudio.audioUrl.startsWith('data:')) {
          // Direct playback for blob/data URLs
          console.log('[AudioPlayer] Direct playback for blob/data URL');
          audioRef.current.src = currentAudio.audioUrl;
          playAudio();
        } else if (currentAudio.audioUrl.includes('.blob.core.windows.net')) {
          // Azure Blob Storage URL - try multiple strategies
          console.log('[AudioPlayer] Azure Blob Storage URL detected, attempting playback');
          
          playAudioWithStrategies(currentAudio.audioUrl, [
            // Strategy 1: Direct playback
            async (url) => {
              console.log('[AudioPlayer] Strategy 1: Direct playback');
              audioRef.current!.src = url;
              await audioRef.current!.play();
              dispatch(setPlayingAudio(true));
              setError(null);
              setRetryCount(0);
            },
            // Strategy 2: Fetch with CORS
            async (url) => {
              console.log('[AudioPlayer] Strategy 2: Fetch with CORS');
              const authToken = localStorage.getItem('auth-token');
              const response = await fetch(url, {
                mode: 'cors',
                headers: authToken ? { 'Authorization': `Bearer ${authToken}` } : {}
              });
              if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);
              const blob = await response.blob();
              const blobUrl = URL.createObjectURL(blob);
              audioRef.current!.src = blobUrl;
              await audioRef.current!.play();
              dispatch(setPlayingAudio(true));
              setError(null);
              setRetryCount(0);
            },
            // Strategy 3: Use proxy endpoint
            async (url) => {
              console.log('[AudioPlayer] Strategy 3: Using proxy endpoint');
              const apiUrl = process.env.REACT_APP_API_BASE_URL || 'https://voicecode-router.orangewater-a2f689a8.eastus.azurecontainerapps.io';
              const proxyUrl = `${apiUrl}/api/audioproxy/fetch?url=${encodeURIComponent(url)}`;
              const authToken = localStorage.getItem('auth-token');
              
              const response = await fetch(proxyUrl, {
                headers: authToken ? { 'Authorization': `Bearer ${authToken}` } : {}
              });
              
              if (!response.ok) throw new Error(`Proxy failed: ${response.status}`);
              const blob = await response.blob();
              const blobUrl = URL.createObjectURL(blob);
              audioRef.current!.src = blobUrl;
              await audioRef.current!.play();
              dispatch(setPlayingAudio(true));
              setError(null);
              setRetryCount(0);
            }
          ]);
        } else {
          // Other HTTP URLs - use same strategies
          console.log('[AudioPlayer] HTTP URL detected');
          
          playAudioWithStrategies(currentAudio.audioUrl, [
            // Strategy 1: Direct fetch with auth
            async (url) => {
              console.log('[AudioPlayer] Strategy 1: Direct fetch with auth');
              const authToken = localStorage.getItem('auth-token');
              const response = await fetch(url, {
                mode: 'cors',
                headers: authToken ? { 'Authorization': `Bearer ${authToken}` } : {}
              });
              if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);
              const blob = await response.blob();
              const blobUrl = URL.createObjectURL(blob);
              audioRef.current!.src = blobUrl;
              await audioRef.current!.play();
              dispatch(setPlayingAudio(true));
              setError(null);
              setRetryCount(0);
            },
            // Strategy 2: Use proxy endpoint
            async (url) => {
              console.log('[AudioPlayer] Strategy 2: Using proxy endpoint');
              const apiUrl = process.env.REACT_APP_API_BASE_URL || 'https://voicecode-router.orangewater-a2f689a8.eastus.azurecontainerapps.io';
              const proxyUrl = `${apiUrl}/api/audioproxy/fetch?url=${encodeURIComponent(url)}`;
              const authToken = localStorage.getItem('auth-token');
              
              const response = await fetch(proxyUrl, {
                headers: authToken ? { 'Authorization': `Bearer ${authToken}` } : {}
              });
              
              if (!response.ok) throw new Error(`Proxy failed: ${response.status}`);
              const blob = await response.blob();
              const blobUrl = URL.createObjectURL(blob);
              audioRef.current!.src = blobUrl;
              await audioRef.current!.play();
              dispatch(setPlayingAudio(true));
              setError(null);
              setRetryCount(0);
            }
          ]);
        }
      }
    }
  }, [audioQueue, playingAudio, currentAudioIndex, dispatch]);

  const playAudioWithStrategies = async (url: string, strategies: Array<(url: string) => Promise<void>>) => {
    for (let i = 0; i < strategies.length; i++) {
      try {
        console.log(`[AudioPlayer] Trying strategy ${i + 1} of ${strategies.length}`);
        await strategies[i](url);
        console.log(`[AudioPlayer] Strategy ${i + 1} succeeded`);
        return; // Success, exit
      } catch (err: any) {
        console.error(`[AudioPlayer] Strategy ${i + 1} failed:`, err);
        signalrDebugger.log('warn', `Audio strategy ${i + 1} failed`, {
          error: err.message,
          url: url,
          strategy: i + 1
        });
        
        if (i === strategies.length - 1) {
          // All strategies failed
          console.error('[AudioPlayer] All strategies failed');
          signalrDebugger.log('error', 'All audio playback strategies failed', {
            error: err.message,
            url: url,
            retryCount: retryCount
          });
          
          if (retryCount < MAX_RETRIES) {
            console.log(`[AudioPlayer] Retrying... (attempt ${retryCount + 1} of ${MAX_RETRIES})`);
            setRetryCount(retryCount + 1);
            setTimeout(() => {
              // Trigger re-render to retry
              setCurrentAudioIndex(currentAudioIndex);
            }, 1000 * (retryCount + 1)); // Exponential backoff
          } else {
            setError(`Failed to play audio after ${MAX_RETRIES} retries`);
            handleAudioEnded();
          }
        }
      }
    }
  };

  const playAudio = () => {
    audioRef.current!.play()
      .then(() => {
        dispatch(setPlayingAudio(true));
        setError(null);
      })
      .catch((err) => {
        console.error('[AudioPlayer] Error playing audio:', err);
        signalrDebugger.log('error', 'Failed to play audio', {
          error: err.message
        });
        setError(`Failed to play audio: ${err.message}`);
        handleAudioEnded();
      });
  };

  const handleAudioEnded = () => {
    console.log('[AudioPlayer] Audio ended');
    dispatch(setPlayingAudio(false));
    
    // Move to next audio in queue
    if (currentAudioIndex < audioQueue.length - 1) {
      setCurrentAudioIndex(currentAudioIndex + 1);
    } else {
      // Reset when queue is exhausted
      setCurrentAudioIndex(0);
      dispatch(playNextAudio());
    }
  };

  const handleAudioError = (e: any) => {
    const audioError = e.target?.error;
    const errorDetails = {
      type: e.type,
      errorCode: audioError?.code,
      errorMessage: audioError?.message || 'Unknown error',
      mediaError: audioError ? {
        MEDIA_ERR_ABORTED: audioError.MEDIA_ERR_ABORTED,
        MEDIA_ERR_NETWORK: audioError.MEDIA_ERR_NETWORK,
        MEDIA_ERR_DECODE: audioError.MEDIA_ERR_DECODE,
        MEDIA_ERR_SRC_NOT_SUPPORTED: audioError.MEDIA_ERR_SRC_NOT_SUPPORTED,
        code: audioError.code
      } : null,
      currentSrc: e.target?.currentSrc,
      networkState: e.target?.networkState,
      readyState: e.target?.readyState
    };
    
    console.error('[AudioPlayer] Audio error:', errorDetails);
    signalrDebugger.log('error', 'Audio playback error', errorDetails);
    
    let errorMessage = 'Audio playback error';
    if (audioError?.code === 4) {
      errorMessage = 'Audio format not supported';
    } else if (audioError?.code === 3) {
      errorMessage = 'Audio decode error';
    } else if (audioError?.code === 2) {
      errorMessage = 'Network error loading audio';
    }
    
    setError(errorMessage);
    handleAudioEnded();
  };

  return (
    <>
      <audio
        ref={audioRef}
        onEnded={handleAudioEnded}
        onError={handleAudioError}
        onLoadStart={() => console.log('[AudioPlayer] Load started')}
        onLoadedMetadata={() => console.log('[AudioPlayer] Metadata loaded')}
        onCanPlay={() => console.log('[AudioPlayer] Can play')}
        crossOrigin="anonymous"
        style={{ display: 'none' }}
      />
      {error && (
        <div style={{
          position: 'fixed',
          bottom: '20px',
          right: '20px',
          background: '#ff4444',
          color: 'white',
          padding: '10px 20px',
          borderRadius: '4px',
          fontSize: '12px',
          zIndex: 1000
        }}>
          {error}
        </div>
      )}
      {playingAudio && (
        <div style={{
          position: 'fixed',
          bottom: '20px',
          right: '20px',
          background: '#4CAF50',
          color: 'white',
          padding: '10px 20px',
          borderRadius: '4px',
          fontSize: '12px',
          zIndex: 1000
        }}>
          🔊 Playing audio response...
        </div>
      )}
    </>
  );
};

export default AudioPlayer;