import React, { useState, useEffect, useCallback } from 'react';
import { useAppSelector, useAppDispatch } from '../../hooks/redux';
import { useStreamingAudio } from '../../hooks/useStreamingAudio';
import { createConversation } from '../../store/slices/chatSlice';
import { remoteLogger } from '../../services/remoteLogger';
import './SimpleVoiceChat.css';

const StreamingVoiceChat: React.FC = () => {
  const dispatch = useAppDispatch();
  const [status, setStatus] = useState<'idle' | 'listening' | 'processing' | 'speaking'>('idle');
  const [errorMessage, setErrorMessage] = useState<string>('');

  const { connected } = useAppSelector((state) => state.signalr);
  const { currentTranscript, partialTranscript, isProcessing } = useAppSelector((state) => state.voice);
  const { conversations, currentConversationId } = useAppSelector((state) => state.chat);
  
  const currentConversation = conversations.find(c => c.id === currentConversationId);

  // Use streaming audio hook
  const {
    startRecording,
    stopRecording,
    isRecording,
    isConnected,
    error,
    audioLevel,
  } = useStreamingAudio();

  // Setup remote console logging on mount
  useEffect(() => {
    remoteLogger.info('StreamingVoiceChat mounted', {
      userAgent: navigator.userAgent,
      platform: navigator.platform,
      url: window.location.href
    });
  }, []);

  // Create conversation if none exists
  useEffect(() => {
    if (!currentConversationId) {
      remoteLogger.info('Creating new conversation');
      dispatch(createConversation({ title: 'Voice Session' }));
    }
  }, [currentConversationId, dispatch]);

  // Update status based on recording state
  useEffect(() => {
    if (isRecording) {
      setStatus('listening');
    } else if (isProcessing) {
      setStatus('processing');
    } else {
      setStatus('idle');
    }
  }, [isRecording, isProcessing]);

  // Handle errors
  useEffect(() => {
    if (error) {
      setErrorMessage(error);
      remoteLogger.error('Streaming error', { error });
    }
  }, [error]);

  const handleButtonClick = useCallback(async () => {
    try {
      remoteLogger.info('Button clicked', {
        isRecording,
        isConnected,
        connected,
        timestamp: new Date().toISOString()
      });

      setErrorMessage('');

      if (isRecording) {
        remoteLogger.info('Stopping recording');
        await stopRecording();
      } else {
        remoteLogger.info('Starting recording');
        await startRecording();
      }
    } catch (error) {
      console.error('[StreamingVoiceChat] Error in handleButtonClick:', error);
      remoteLogger.error('Button click error', {
        error: error instanceof Error ? error.message : String(error),
        stack: error instanceof Error ? error.stack : undefined
      });
      setErrorMessage('An error occurred. Please try again.');
    }
  }, [isRecording, isConnected, connected, startRecording, stopRecording]);

  const getButtonText = () => {
    switch (status) {
      case 'listening':
        return 'Listening...';
      case 'processing':
        return 'Processing...';
      case 'speaking':
        return 'Speaking...';
      default:
        return 'Press to Talk';
    }
  };

  const getButtonClass = () => {
    return `voice-button ${status !== 'idle' ? 'active' : ''} ${status}`;
  };

  // Format transcript for display
  const displayTranscript = partialTranscript || currentTranscript || '';

  // Get latest response from conversation
  const latestResponse = currentConversation?.messages
    .filter(m => m.type === 'assistant')
    .slice(-1)[0]?.content || '';

  return (
    <div className="simple-voice-chat">
      <div className="voice-container">
        <button
          className={getButtonClass()}
          onClick={handleButtonClick}
          disabled={!isConnected || isProcessing}
        >
          <div className="button-content">
            <svg className="mic-icon" viewBox="0 0 24 24" fill="currentColor">
              <path d="M12 14c1.66 0 3-1.34 3-3V5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3z"/>
              <path d="M17 11c0 2.76-2.24 5-5 5s-5-2.24-5-5H5c0 3.53 2.61 6.43 6 6.92V21h2v-3.08c3.39-.49 6-3.39 6-6.92h-2z"/>
            </svg>
            <span className="button-text">{getButtonText()}</span>
          </div>
          {status === 'listening' && (
            <>
              <div className="pulse-ring"></div>
              {/* Audio level indicator */}
              <div 
                className="audio-level-ring" 
                style={{
                  position: 'absolute',
                  top: '50%',
                  left: '50%',
                  transform: `translate(-50%, -50%) scale(${1 + audioLevel * 0.5})`,
                  width: '100%',
                  height: '100%',
                  borderRadius: '50%',
                  border: '2px solid rgba(76, 175, 80, 0.6)',
                  opacity: audioLevel,
                  transition: 'transform 0.1s, opacity 0.1s',
                  pointerEvents: 'none'
                }}
              />
            </>
          )}
        </button>

        {displayTranscript && (
          <div className="transcript-display">
            <p className="transcript-label">
              {partialTranscript ? 'Listening...' : 'You said:'}
            </p>
            <p className="transcript-text">
              {displayTranscript}
              {partialTranscript && <span className="partial-indicator">...</span>}
            </p>
          </div>
        )}

        {latestResponse && (
          <div className="response-display">
            <p className="response-label">Response:</p>
            <p className="response-text">{latestResponse}</p>
          </div>
        )}

        {!isConnected && (
          <p className="connection-status">Connecting to services...</p>
        )}
        
        {errorMessage && (
          <div className="error-message" style={{ color: 'red', marginTop: '20px' }}>
            {errorMessage}
          </div>
        )}
        
        <div className="debug-info" style={{ 
          position: 'fixed', 
          bottom: '10px', 
          left: '10px', 
          fontSize: '10px', 
          color: '#666',
          background: 'rgba(255,255,255,0.8)',
          padding: '5px',
          borderRadius: '5px'
        }}>
          v2.0.0 | {isConnected ? '✓' : '○'} Stream | {connected ? '✓' : '○'} SignalR
        </div>
      </div>
    </div>
  );
};

export default StreamingVoiceChat;