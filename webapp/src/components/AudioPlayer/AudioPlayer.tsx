import React, { useEffect, useRef, useState, useCallback } from 'react';
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
  const [loadSuccess, setLoadSuccess] = useState(false);
  const [successMessage, setSuccessMessage] = useState<string>('');
  const successTimerRef = useRef<NodeJS.Timeout | null>(null);
  const MAX_RETRIES = 3;

  const showSuccess = useCallback((message: string) => {
    setLoadSuccess(true);
    setSuccessMessage(message);
    
    // Clear any existing timer
    if (successTimerRef.current) {
      clearTimeout(successTimerRef.current);
    }
    
    // Hide success message after 5 seconds
    successTimerRef.current = setTimeout(() => {
      setLoadSuccess(false);
    }, 5000);
  }, []);

  useEffect(() => {
    // Cleanup timer on unmount
    return () => {
      if (successTimerRef.current) {
        clearTimeout(successTimerRef.current);
      }
    };
  }, []);

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
          
          // Log the exact URL for debugging
          signalrDebugger.log('info', 'Attempting to play Azure Blob audio', {
            url: currentAudio.audioUrl,
            id: currentAudio.id,
            text: currentAudio.text
          });
          
          playAudioWithStrategies(currentAudio.audioUrl, [
            // Strategy 1: Direct playback
            async (url) => {
              console.log('[AudioPlayer] Strategy 1: Direct playback');
              audioRef.current!.src = url;
              await audioRef.current!.play();
              dispatch(setPlayingAudio(true));
              setError(null);
              setRetryCount(0);
              showSuccess('✅ Audio loaded successfully');
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
              console.log('[AudioPlayer] Blob fetched:', { size: blob.size, type: blob.type });
              
              // Check if this might be an error response
              if (blob.size < 10000 && (!blob.type || !blob.type.startsWith('audio/'))) {
                const text = await blob.text();
                console.error('[AudioPlayer] Suspected error response:', text.substring(0, 500));
                
                if (text.includes('{') || text.includes('<')) {
                  throw new Error('Received error response instead of audio: ' + text.substring(0, 200));
                }
              }
              
              // Check blob type and create appropriate URL
              if (blob.type && blob.type.startsWith('audio/')) {
                const blobUrl = URL.createObjectURL(blob);
                audioRef.current!.src = blobUrl;
              } else {
                // Try to force audio/mpeg type
                const audioBlob = new Blob([blob], { type: 'audio/mpeg' });
                const blobUrl = URL.createObjectURL(audioBlob);
                audioRef.current!.src = blobUrl;
              }
              await audioRef.current!.play();
              dispatch(setPlayingAudio(true));
              setError(null);
              setRetryCount(0);
              showSuccess('✅ Audio loaded successfully');
            },
            // Strategy 3: Convert to data URL
            async (url) => {
              console.log('[AudioPlayer] Strategy 3: Convert to data URL');
              const authToken = localStorage.getItem('auth-token');
              const response = await fetch(url, {
                mode: 'cors',
                headers: authToken ? { 'Authorization': `Bearer ${authToken}` } : {}
              });
              if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);
              const blob = await response.blob();
              
              // Force audio/mpeg type if not set
              const audioBlob = blob.type && blob.type.startsWith('audio/') 
                ? blob 
                : new Blob([blob], { type: 'audio/mpeg' });
              
              const dataUrl = await createDataUrlFromBlob(audioBlob);
              console.log('[AudioPlayer] Data URL created, length:', dataUrl.length);
              audioRef.current!.src = dataUrl;
              await audioRef.current!.play();
              dispatch(setPlayingAudio(true));
              setError(null);
              setRetryCount(0);
              showSuccess('✅ Audio loaded successfully');
            },
            // Strategy 4: Use proxy endpoint
            async (url) => {
              console.log('[AudioPlayer] Strategy 4: Using proxy endpoint');
              const apiUrl = process.env.REACT_APP_API_BASE_URL || 'https://voicecode-router.orangewater-a2f689a8.eastus.azurecontainerapps.io';
              const proxyUrl = `${apiUrl}/api/audioproxy/fetch?url=${encodeURIComponent(url)}`;
              const authToken = localStorage.getItem('auth-token');
              
              const response = await fetch(proxyUrl, {
                headers: authToken ? { 'Authorization': `Bearer ${authToken}` } : {}
              });
              
              if (!response.ok) throw new Error(`Proxy failed: ${response.status}`);
              const blob = await response.blob();
              const audioBlob = new Blob([blob], { type: 'audio/mpeg' });
              const dataUrl = await createDataUrlFromBlob(audioBlob);
              audioRef.current!.src = dataUrl;
              await audioRef.current!.play();
              dispatch(setPlayingAudio(true));
              setError(null);
              setRetryCount(0);
              showSuccess('✅ Audio loaded successfully');
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
              console.log('[AudioPlayer] Blob fetched:', { size: blob.size, type: blob.type });
              
              // Check for error response
              if (blob.size < 10000) {
                const arrayBuffer = await blob.arrayBuffer();
                const bytes = new Uint8Array(arrayBuffer);
                
                // Check file signature
                const isMP3 = (bytes[0] === 0xFF && (bytes[1] & 0xE0) === 0xE0) || 
                             (bytes[0] === 0x49 && bytes[1] === 0x44 && bytes[2] === 0x33);
                const isWAV = bytes[0] === 0x52 && bytes[1] === 0x49 && bytes[2] === 0x46 && bytes[3] === 0x46;
                
                if (!isMP3 && !isWAV) {
                  const text = new TextDecoder().decode(bytes);
                  console.error('[AudioPlayer] Not an audio file. Content:', text.substring(0, 500));
                  signalrDebugger.log('error', 'Received non-audio response', {
                    size: blob.size,
                    type: blob.type,
                    content: text.substring(0, 200)
                  });
                  throw new Error('Server returned non-audio content: ' + text.substring(0, 100));
                }
                
                // Reconstruct blob from arrayBuffer
                const audioBlob = new Blob([arrayBuffer], { type: 'audio/mpeg' });
                const blobUrl = URL.createObjectURL(audioBlob);
                audioRef.current!.src = blobUrl;
              } else {
                // Large file, assume it's audio
                const audioBlob = blob.type && blob.type.startsWith('audio/') 
                  ? blob 
                  : new Blob([blob], { type: 'audio/mpeg' });
                
                const blobUrl = URL.createObjectURL(audioBlob);
                audioRef.current!.src = blobUrl;
              }
              
              await audioRef.current!.play();
              dispatch(setPlayingAudio(true));
              setError(null);
              setRetryCount(0);
              showSuccess('✅ Audio loaded successfully');
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
              const audioBlob = new Blob([blob], { type: 'audio/mpeg' });
              const dataUrl = await createDataUrlFromBlob(audioBlob);
              audioRef.current!.src = dataUrl;
              await audioRef.current!.play();
              dispatch(setPlayingAudio(true));
              setError(null);
              setRetryCount(0);
              showSuccess('✅ Audio loaded successfully');
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

  const createDataUrlFromBlob = async (blob: Blob): Promise<string> => {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onloadend = () => {
        if (typeof reader.result === 'string') {
          resolve(reader.result);
        } else {
          reject(new Error('Failed to convert blob to data URL'));
        }
      };
      reader.onerror = () => reject(new Error('Failed to read blob'));
      reader.readAsDataURL(blob);
    });
  };

  const playAudio = () => {
    audioRef.current!.play()
      .then(() => {
        dispatch(setPlayingAudio(true));
        setError(null);
        showSuccess('✅ Audio loaded successfully');
      })
      .catch((err) => {
        console.error('[AudioPlayer] Error playing audio:', err);
        signalrDebugger.log('error', 'Failed to play audio', {
          error: err.message
        });
        setError(`Failed to play audio: ${err.message}`);
        setLoadSuccess(false);
        handleAudioEnded();
      });
  };

  const handleAudioEnded = () => {
    console.log('[AudioPlayer] Audio ended');
    dispatch(setPlayingAudio(false));
    setLoadSuccess(false); // Hide success message when audio ends
    
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
      errorMessage = `Audio format not supported (URL: ${e.target?.currentSrc?.substring(0, 100)}...)`;
    } else if (audioError?.code === 3) {
      errorMessage = 'Audio decode error - file may be corrupted';
    } else if (audioError?.code === 2) {
      errorMessage = 'Network error loading audio - check connectivity';
    } else if (audioError?.code === 1) {
      errorMessage = 'Audio loading aborted';
    }
    
    setError(errorMessage);
    setLoadSuccess(false);
    handleAudioEnded();
  };

  return (
    <>
      <style>{`
        @keyframes slideInLeft {
          from {
            transform: translateX(-100%);
            opacity: 0;
          }
          to {
            transform: translateX(0);
            opacity: 1;
          }
        }
        @keyframes pulse {
          0% {
            transform: scale(1);
          }
          50% {
            transform: scale(1.1);
          }
          100% {
            transform: scale(1);
          }
        }
      `}</style>
      <audio
        ref={audioRef}
        onEnded={handleAudioEnded}
        onError={handleAudioError}
        onLoadStart={() => {
          console.log('[AudioPlayer] Load started');
          setLoadSuccess(false);
        }}
        onLoadedMetadata={() => {
          console.log('[AudioPlayer] Metadata loaded');
          if (!error) {
            showSuccess('✅ MP3 loaded successfully');
          }
        }}
        onCanPlay={() => console.log('[AudioPlayer] Can play')}
        onCanPlayThrough={() => console.log('[AudioPlayer] Can play through')}
        style={{ display: 'none' }}
      />
      {error && (
        <div style={{
          position: 'fixed',
          bottom: '20px',
          left: '20px',
          background: '#ff4444',
          color: 'white',
          padding: '10px 20px',
          borderRadius: '4px',
          fontSize: '12px',
          zIndex: 1000,
          maxWidth: '300px',
          wordWrap: 'break-word'
        }}>
          {error}
        </div>
      )}
      {loadSuccess && (
        <div style={{
          position: 'fixed',
          bottom: error ? '70px' : '20px',
          left: '20px',
          background: '#4CAF50',
          color: 'white',
          padding: '12px 24px',
          borderRadius: '8px',
          fontSize: '14px',
          fontWeight: '500',
          zIndex: 1000,
          boxShadow: '0 4px 6px rgba(0, 0, 0, 0.1)',
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          animation: 'slideInLeft 0.3s ease-out'
        }}>
          <span style={{ fontSize: '16px' }}>✅</span>
          <span>{successMessage || 'Audio loaded successfully'}</span>
        </div>
      )}
      {playingAudio && (
        <div style={{
          position: 'fixed',
          bottom: loadSuccess ? '80px' : (error ? '70px' : '20px'),
          left: '20px',
          background: '#2196F3',
          color: 'white',
          padding: '10px 20px',
          borderRadius: '4px',
          fontSize: '12px',
          zIndex: 999,
          display: 'flex',
          alignItems: 'center',
          gap: '8px'
        }}>
          <span style={{ animation: 'pulse 1.5s infinite' }}>🔊</span>
          <span>Playing audio response...</span>
        </div>
      )}
    </>
  );
};

export default AudioPlayer;