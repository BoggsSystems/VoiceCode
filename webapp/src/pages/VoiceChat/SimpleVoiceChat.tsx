import React, { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAppSelector, useAppDispatch } from '../../hooks/redux';
import { useDirectVoiceRecording } from '../../hooks/useDirectVoiceRecording';
import { createConversation } from '../../store/slices/chatSlice';
import { remoteLogger, setupRemoteConsole } from '../../services/remoteLogger';
import VoiceService from '../../services/voiceService';
import SignalRDebugPanel from '../../components/SignalRDebugPanel/SignalRDebugPanel';
import AudioPlayer from '../../components/AudioPlayer/AudioPlayer';
import './SimpleVoiceChat.css';

const BUILD_VERSION = '1.0.14';
const BUILD_TIME = new Date().toISOString();

const SimpleVoiceChat: React.FC = () => {
  const navigate = useNavigate();
  const dispatch = useAppDispatch();
  const [status, setStatus] = useState<'idle' | 'listening' | 'processing' | 'speaking'>('idle');
  const [transcript, setTranscript] = useState<string>('');
  const [response, setResponse] = useState<string>('');
  const [errorMessage, setErrorMessage] = useState<string>('');
  const [journeySteps, setJourneySteps] = useState<string[]>([]);

  // Check authentication on mount
  useEffect(() => {
    const authToken = localStorage.getItem('auth-token');
    if (!authToken) {
      navigate('/login-simple');
      return;
    }
  }, [navigate]);

  // Setup remote console logging on mount
  useEffect(() => {
    setupRemoteConsole();
    remoteLogger.info('SimpleVoiceChat mounted', {
      userAgent: navigator.userAgent,
      platform: navigator.platform,
      url: window.location.href
    });
  }, []);

  const { connected } = useAppSelector((state) => state.signalr);
  const { currentTranscript } = useAppSelector((state) => state.voice);
  const { conversations, currentConversationId } = useAppSelector((state) => state.chat);
  
  const currentConversation = conversations.find(c => c.id === currentConversationId);

  console.log('[SimpleVoiceChat] Component rendered, SignalR connected:', connected);
  console.log('[SimpleVoiceChat] Current conversation ID:', currentConversationId);
  
  remoteLogger.info('Component state', {
    connected,
    currentConversationId,
    status,
    hasTranscript: !!transcript,
    hasResponse: !!response
  });

  const {
    startRecording,
    stopRecording,
    requestPermission,
    permissionGranted,
    isRecording,
    isProcessing,
  } = useDirectVoiceRecording({
    onJourneyUpdate: (step: string) => {
      setJourneySteps(prev => [...prev, step]);
    }
  });

  console.log('[SimpleVoiceChat] Voice recording state:', {
    permissionGranted,
    isRecording,
    isProcessing
  });
  
  remoteLogger.info('Voice recording state', {
    permissionGranted,
    isRecording,
    isProcessing
  });

  // Create conversation if none exists
  useEffect(() => {
    console.log('[SimpleVoiceChat] Checking conversation creation...');
    if (!currentConversationId) {
      console.log('[SimpleVoiceChat] No conversation found, creating new one');
      dispatch(createConversation({ title: 'Voice Session' }));
    }
  }, [currentConversationId, dispatch]);

  useEffect(() => {
    if (isRecording) {
      setStatus('listening');
    } else if (isProcessing) {
      setStatus('processing');
    } else {
      setStatus('idle');
    }
  }, [isRecording, isProcessing]);

  useEffect(() => {
    if (currentTranscript) {
      setTranscript(currentTranscript);
    }
  }, [currentTranscript]);

  // Watch for new messages in the conversation
  useEffect(() => {
    if (currentConversation && currentConversation.messages.length > 0) {
      const lastMessage = currentConversation.messages[currentConversation.messages.length - 1];
      if (lastMessage.type === 'assistant') {
        setResponse(lastMessage.content);
        setStatus('idle');
      }
    }
  }, [currentConversation]);

  const handleButtonClick = async () => {
    try {
      console.log('[SimpleVoiceChat] Button clicked, current state:', { 
        permissionGranted, 
        isRecording,
        connected 
      });
      
      remoteLogger.info('Button clicked', {
        permissionGranted,
        isRecording,
        connected,
        timestamp: new Date().toISOString()
      });

      setErrorMessage('');

      if (!permissionGranted) {
        console.log('[SimpleVoiceChat] Requesting microphone permission...');
        remoteLogger.info('Requesting microphone permission');
        const granted = await requestPermission();
        remoteLogger.info('Permission result', { granted });
        return;
      }

      if (isRecording) {
        console.log('[SimpleVoiceChat] Stopping recording...');
        remoteLogger.info('Stopping recording');
        stopRecording();
      } else {
        console.log('[SimpleVoiceChat] Starting recording...');
        remoteLogger.info('Starting recording');
        setTranscript('');
        setResponse('');
        setJourneySteps(['🎤 Recording started...']);
        startRecording();
      }
    } catch (error) {
      console.error('[SimpleVoiceChat] Error in handleButtonClick:', error);
      remoteLogger.error('Button click error', {
        error: error instanceof Error ? error.message : String(error),
        stack: error instanceof Error ? error.stack : undefined
      });
      setErrorMessage('An error occurred. Please try again.');
    }
  };

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

  const handleLogout = () => {
    localStorage.removeItem('auth-token');
    localStorage.removeItem('auth-user');
    localStorage.removeItem('auth-timestamp');
    navigate('/login-simple');
  };

  const authUser = localStorage.getItem('auth-user') || 'Unknown User';

  return (
    <div className="simple-voice-chat">
      <div className="auth-header">
        <span className="user-info">Logged in as: {authUser}</span>
        <button className="logout-button" onClick={handleLogout}>Logout</button>
      </div>
      <div className="voice-container">
        <button
          className={getButtonClass()}
          onClick={handleButtonClick}
          disabled={isProcessing}
        >
          <div className="button-content">
            <svg className="mic-icon" viewBox="0 0 24 24" fill="currentColor">
              <path d="M12 14c1.66 0 3-1.34 3-3V5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3z"/>
              <path d="M17 11c0 2.76-2.24 5-5 5s-5-2.24-5-5H5c0 3.53 2.61 6.43 6 6.92V21h2v-3.08c3.39-.49 6-3.39 6-6.92h-2z"/>
            </svg>
            <span className="button-text">{getButtonText()}</span>
          </div>
          {status === 'listening' && (
            <div className="pulse-ring"></div>
          )}
        </button>

        {transcript && (
          <div className="transcript-display">
            <p className="transcript-label">You said:</p>
            <p className="transcript-text">{transcript}</p>
          </div>
        )}

        {response && (
          <div className="response-display">
            <p className="response-label">Response:</p>
            <p className="response-text">{response}</p>
          </div>
        )}

        {!connected && (
          <p className="connection-status">Connecting to services...</p>
        )}

        {journeySteps.length > 0 && (
          <div className="journey-display">
            <p className="journey-label">Journey:</p>
            {journeySteps.map((step, index) => (
              <p key={index} className="journey-step">{step}</p>
            ))}
          </div>
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
          v{BUILD_VERSION} | Build: {BUILD_TIME} | {connected ? '✓' : '○'} Connected | {permissionGranted ? '✓' : '○'} Mic
        </div>
      </div>
      
      <SignalRDebugPanel />
      <AudioPlayer />
    </div>
  );
};

export default SimpleVoiceChat;