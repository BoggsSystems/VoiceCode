import React, { useEffect, useRef, useState, useCallback } from 'react';
import { useSelector, useDispatch } from 'react-redux';
import { RootState } from '../../store/store';
import { playNextAudio, setPlayingAudio } from '../../store/slices/voiceSlice';
import { signalrDebugger } from '../../utils/signalrDebugger';
import { remoteLogger } from '../../services/remoteLogger';

const AudioPlayer: React.FC = () => {
  const dispatch = useDispatch();
  const audioQueue = useSelector((state: RootState) => state.voice.audioQueue);
  const playingAudio = useSelector((state: RootState) => state.voice.playingAudio);
  const audioRef = useRef<HTMLAudioElement>(null);
  const [error, setError] = useState<string | null>(null);
  const [retryCount, setRetryCount] = useState(0);
  const [loadSuccess, setLoadSuccess] = useState(false);
  const [successMessage, setSuccessMessage] = useState<string>('');
  const successTimerRef = useRef<NodeJS.Timeout | null>(null);
  const [hasUserInteracted, setHasUserInteracted] = useState(false);
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
    // Detect user interaction for iOS audio playback
    const handleUserInteraction = () => {
      if (!hasUserInteracted) {
        setHasUserInteracted(true);
        remoteLogger.info('[AudioPlayer] User interaction detected');
        signalrDebugger.log('info', '[AudioPlayer] User interaction detected');
        
        // If we have audio waiting and haven't played yet, try to play
        if (audioQueue.length > 0 && !playingAudio && audioRef.current?.src) {
          playAudio();
        }
      }
    };

    document.addEventListener('click', handleUserInteraction);
    document.addEventListener('touchstart', handleUserInteraction);
    
    // Cleanup
    return () => {
      document.removeEventListener('click', handleUserInteraction);
      document.removeEventListener('touchstart', handleUserInteraction);
      if (successTimerRef.current) {
        clearTimeout(successTimerRef.current);
      }
    };
  }, [hasUserInteracted, audioQueue.length, playingAudio]);

  useEffect(() => {
    if (audioQueue.length > 0 && !playingAudio) {
      const currentAudio = audioQueue[0]; // Always play the first item in queue
      if (currentAudio && audioRef.current) {
        console.log('[AudioPlayer] Processing audio:', {
          id: currentAudio.id,
          url: currentAudio.audioUrl,
          text: currentAudio.text,
          urlType: currentAudio.audioUrl.substring(0, 50)
        });
        remoteLogger.info('[AudioPlayer] Processing audio', {
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
          remoteLogger.info('[AudioPlayer] Direct playback for blob/data URL');
          audioRef.current.src = currentAudio.audioUrl;
          playAudio();
        } else if (currentAudio.audioUrl.includes('.blob.core.windows.net')) {
          // Azure Blob Storage URL - try multiple strategies
          console.log('[AudioPlayer] Azure Blob Storage URL detected, attempting playback');
          remoteLogger.info('[AudioPlayer] Azure Blob Storage URL detected, attempting playback');
          
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
              remoteLogger.info('[AudioPlayer] Strategy 1: Direct playback');
              signalrDebugger.log('info', '[AudioPlayer] Strategy 1: Direct playback', { url: url });
              
              // iOS requires audio element to be loaded before play
              audioRef.current!.src = url;
              audioRef.current!.load(); // Explicitly load for iOS
              
              // Wait for loadeddata event
              await new Promise((resolve, reject) => {
                const timeout = setTimeout(() => reject(new Error('Load timeout')), 5000);
                audioRef.current!.addEventListener('loadeddata', () => {
                  clearTimeout(timeout);
                  resolve(true);
                }, { once: true });
                audioRef.current!.addEventListener('error', (e) => {
                  clearTimeout(timeout);
                  reject(e);
                }, { once: true });
              });
              
              await audioRef.current!.play();
              dispatch(setPlayingAudio(true));
              setError(null);
              setRetryCount(0);
              showSuccess('✅ Audio loaded successfully');
            },
            // Strategy 2: Fetch with CORS
            async (url) => {
              console.log('[AudioPlayer] Strategy 2: Fetch with CORS');
              remoteLogger.info('[AudioPlayer] Strategy 2: Fetch with CORS');
              signalrDebugger.log('info', '[AudioPlayer] Strategy 2: Fetch with CORS', { url: url });
              const authToken = localStorage.getItem('auth-token');
              const response = await fetch(url, {
                mode: 'cors',
                headers: authToken ? { 'Authorization': `Bearer ${authToken}` } : {}
              });
              if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);
              const blob = await response.blob();
              console.log('[AudioPlayer] Blob fetched:', { size: blob.size, type: blob.type });
              remoteLogger.info('[AudioPlayer] Blob fetched', { size: blob.size, type: blob.type });
              signalrDebugger.log('info', '[AudioPlayer] Blob fetched', { size: blob.size, type: blob.type });
              
              // Check if this might be an error response
              if (blob.size < 10000 && (!blob.type || !blob.type.startsWith('audio/'))) {
                const text = await blob.text();
                console.error('[AudioPlayer] Suspected error response:', text.substring(0, 500));
                remoteLogger.error('[AudioPlayer] Suspected error response', { content: text.substring(0, 500) });
                signalrDebugger.log('error', '[AudioPlayer] Suspected error response', { content: text.substring(0, 200) });
                
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
              remoteLogger.info('[AudioPlayer] Strategy 3: Convert to data URL');
              signalrDebugger.log('info', '[AudioPlayer] Strategy 3: Convert to data URL', { url: url });
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
              remoteLogger.info('[AudioPlayer] Data URL created', { length: dataUrl.length });
              signalrDebugger.log('info', '[AudioPlayer] Data URL created', { length: dataUrl.length });
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
              remoteLogger.info('[AudioPlayer] Strategy 4: Using proxy endpoint');
              const apiUrl = process.env.REACT_APP_API_BASE_URL || 'https://voicecode-router.orangewater-a2f689a8.eastus.azurecontainerapps.io';
              const proxyUrl = `${apiUrl}/api/audioproxy/fetch?url=${encodeURIComponent(url)}`;
              signalrDebugger.log('info', '[AudioPlayer] Strategy 4: Using proxy endpoint', { proxyUrl: proxyUrl });
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
          remoteLogger.info('[AudioPlayer] HTTP URL detected', { url: currentAudio.audioUrl });
          
          playAudioWithStrategies(currentAudio.audioUrl, [
            // Strategy 1: Direct fetch with auth
            async (url) => {
              console.log('[AudioPlayer] Strategy 1: Direct fetch with auth');
              remoteLogger.info('[AudioPlayer] Strategy 1: Direct fetch with auth');
              signalrDebugger.log('info', '[AudioPlayer] Strategy 1: Direct fetch with auth', { url: url });
              const authToken = localStorage.getItem('auth-token');
              const response = await fetch(url, {
                mode: 'cors',
                headers: authToken ? { 'Authorization': `Bearer ${authToken}` } : {}
              });
              if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);
              const blob = await response.blob();
              console.log('[AudioPlayer] Blob fetched:', { size: blob.size, type: blob.type });
              remoteLogger.info('[AudioPlayer] Blob fetched', { size: blob.size, type: blob.type });
              signalrDebugger.log('info', '[AudioPlayer] Blob fetched', { size: blob.size, type: blob.type });
              
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
                  remoteLogger.error('[AudioPlayer] Not an audio file', { content: text.substring(0, 500) });
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
  }, [audioQueue, playingAudio, dispatch, showSuccess]);

  const playAudioWithStrategies = async (url: string, strategies: Array<(url: string) => Promise<void>>) => {
    for (let i = 0; i < strategies.length; i++) {
      try {
        console.log(`[AudioPlayer] Trying strategy ${i + 1} of ${strategies.length}`);
        remoteLogger.info(`[AudioPlayer] Trying strategy ${i + 1} of ${strategies.length}`, { url: url });
        await strategies[i](url);
        console.log(`[AudioPlayer] Strategy ${i + 1} succeeded`);
        remoteLogger.info(`[AudioPlayer] Strategy ${i + 1} succeeded`, { url: url });
        signalrDebugger.log('info', `[AudioPlayer] Strategy ${i + 1} succeeded`, { url: url });
        return; // Success, exit
      } catch (err: any) {
        console.error(`[AudioPlayer] Strategy ${i + 1} failed:`, err);
        remoteLogger.error(`[AudioPlayer] Strategy ${i + 1} failed`, { error: err.message, url: url });
        signalrDebugger.log('warn', `Audio strategy ${i + 1} failed`, {
          error: err.message,
          url: url,
          strategy: i + 1
        });
        
        if (i === strategies.length - 1) {
          // All strategies failed
          console.error('[AudioPlayer] All strategies failed');
          remoteLogger.error('[AudioPlayer] All strategies failed', { url: url, retryCount: retryCount });
          signalrDebugger.log('error', 'All audio playback strategies failed', {
            error: err.message,
            url: url,
            retryCount: retryCount
          });
          
          if (retryCount < MAX_RETRIES) {
            console.log(`[AudioPlayer] Retrying... (attempt ${retryCount + 1} of ${MAX_RETRIES})`);
            remoteLogger.info(`[AudioPlayer] Retrying...`, { attempt: retryCount + 1, maxRetries: MAX_RETRIES });
            setRetryCount(retryCount + 1);
            setTimeout(() => {
              // Trigger re-render to retry by toggling playingAudio
              dispatch(setPlayingAudio(false));
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
    console.log('[AudioPlayer] Attempting to play audio, src:', audioRef.current?.src);
    remoteLogger.info('[AudioPlayer] Attempting to play audio', { 
      src: audioRef.current?.src,
      hasUserInteracted,
      isIOS: /iPhone|iPad|iPod/i.test(navigator.userAgent)
    });
    signalrDebugger.log('info', '[AudioPlayer] Attempting to play audio', { 
      src: audioRef.current?.src?.substring(0, 100),
      hasUserInteracted 
    });
    
    // Check if iOS and no user interaction
    if (/iPhone|iPad|iPod/i.test(navigator.userAgent) && !hasUserInteracted) {
      setError('Tap anywhere to enable audio playback');
      remoteLogger.warn('[AudioPlayer] iOS requires user interaction for audio playback');
      return;
    }
    
    // Handle autoplay policy by ensuring we have user interaction
    const playPromise = audioRef.current!.play();
    
    if (playPromise !== undefined) {
      playPromise
        .then(() => {
          console.log('[AudioPlayer] Audio playing successfully');
          remoteLogger.info('[AudioPlayer] Audio playing successfully');
          signalrDebugger.log('info', '[AudioPlayer] Audio playing successfully');
          dispatch(setPlayingAudio(true));
          setError(null);
          showSuccess('✅ Audio playing');
        })
        .catch((err) => {
          console.error('[AudioPlayer] Error playing audio:', err);
          remoteLogger.error('[AudioPlayer] Error playing audio', { error: err.message, errorName: err.name });
          signalrDebugger.log('error', 'Failed to play audio', {
            error: err.message,
            errorName: err.name
          });
          
          // If it's an autoplay policy error, show a specific message
          if (err.name === 'NotAllowedError') {
            setError('Audio blocked by browser. Click anywhere to enable audio.');
            // Add a one-time click handler to retry
            const retryPlay = () => {
              document.removeEventListener('click', retryPlay);
              playAudio();
            };
            document.addEventListener('click', retryPlay);
          } else {
            setError(`Failed to play audio: ${err.message}`);
          }
          
          setLoadSuccess(false);
          handleAudioEnded();
        });
    }
  };

  const handleAudioEnded = () => {
    console.log('[AudioPlayer] Audio ended');
    remoteLogger.info('[AudioPlayer] Audio ended');
    signalrDebugger.log('info', '[AudioPlayer] Audio ended');
    dispatch(setPlayingAudio(false));
    setLoadSuccess(false); // Hide success message when audio ends
    
    // Remove the played audio from queue
    dispatch(playNextAudio());
    
    // Don't reset index, let useEffect handle the next audio
    // The useEffect will trigger when audioQueue changes
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
    remoteLogger.error('[AudioPlayer] Audio error', errorDetails);
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
          remoteLogger.info('[AudioPlayer] Load started');
          signalrDebugger.log('info', '[AudioPlayer] Load started');
          setLoadSuccess(false);
        }}
        onLoadedMetadata={() => {
          console.log('[AudioPlayer] Metadata loaded, duration:', audioRef.current?.duration, 'volume:', audioRef.current?.volume);
          remoteLogger.info('[AudioPlayer] Metadata loaded', { 
            duration: audioRef.current?.duration, 
            volume: audioRef.current?.volume,
            muted: audioRef.current?.muted,
            readyState: audioRef.current?.readyState,
            networkState: audioRef.current?.networkState
          });
          signalrDebugger.log('info', '[AudioPlayer] Metadata loaded', { 
            duration: audioRef.current?.duration, 
            volume: audioRef.current?.volume,
            muted: audioRef.current?.muted 
          });
          if (!error) {
            showSuccess('✅ MP3 loaded successfully');
            // Ensure audio is not muted
            if (audioRef.current) {
              audioRef.current.volume = 1.0;
              audioRef.current.muted = false;
              remoteLogger.info('[AudioPlayer] Set volume to 1.0 and unmuted');
            }
          }
        }}
        onCanPlay={() => {
          console.log('[AudioPlayer] Can play');
          remoteLogger.info('[AudioPlayer] Can play');
          signalrDebugger.log('info', '[AudioPlayer] Can play');
        }}
        onCanPlayThrough={() => {
          console.log('[AudioPlayer] Can play through');
          remoteLogger.info('[AudioPlayer] Can play through');
          signalrDebugger.log('info', '[AudioPlayer] Can play through');
        }}
        onPlay={() => {
          console.log('[AudioPlayer] Play event fired');
          remoteLogger.info('[AudioPlayer] Play event fired');
          signalrDebugger.log('info', '[AudioPlayer] Play event fired');
        }}
        onPlaying={() => {
          console.log('[AudioPlayer] Playing event fired');
          remoteLogger.info('[AudioPlayer] Playing event fired');
          signalrDebugger.log('info', '[AudioPlayer] Playing event fired');
        }}
        onVolumeChange={() => {
          console.log('[AudioPlayer] Volume changed to:', audioRef.current?.volume, 'muted:', audioRef.current?.muted);
          remoteLogger.info('[AudioPlayer] Volume changed', { volume: audioRef.current?.volume, muted: audioRef.current?.muted });
          signalrDebugger.log('info', '[AudioPlayer] Volume changed', { volume: audioRef.current?.volume, muted: audioRef.current?.muted });
        }}
        style={{ display: 'none' }}
        preload="auto"
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