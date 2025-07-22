import React, { useEffect, useRef } from 'react';
import { useAppSelector, useAppDispatch } from '../../hooks/redux';
import { useVoiceRecording } from '../../hooks/useVoiceRecording';
import { useAudioPlayback } from '../../hooks/useAudioPlayback';
import VoiceRecorder from '../../components/Voice/VoiceRecorder/VoiceRecorder';
import AudioVisualizer from '../../components/Voice/AudioVisualizer/AudioVisualizer';
import TranscriptionDisplay from '../../components/Voice/TranscriptionDisplay/TranscriptionDisplay';
import ChatMessages from '../../components/Chat/ChatMessages/ChatMessages';
import { createConversation } from '../../store/slices/chatSlice';
import './VoiceChat.css';

const VoiceChat: React.FC = () => {
  const dispatch = useAppDispatch();
  const messagesEndRef = useRef<HTMLDivElement>(null);
  
  const {
    isRecording,
    isProcessing,
    audioLevel,
    currentTranscript,
    recognitionResults,
    error: voiceError,
  } = useAppSelector((state) => state.voice);
  
  const {
    conversations,
    currentConversationId,
    isTyping,
  } = useAppSelector((state) => state.chat);
  
  const { connected } = useAppSelector((state) => state.signalr);

  const {
    startRecording,
    stopRecording,
    requestPermission,
    permissionGranted,
  } = useVoiceRecording();

  const { playAudio, isPlaying } = useAudioPlayback();

  const currentConversation = conversations.find(c => c.id === currentConversationId);

  useEffect(() => {
    // Create a conversation for voice chat if none exists
    if (!currentConversationId) {
      dispatch(createConversation({ title: 'Voice Chat Session' }));
    }
  }, [currentConversationId, dispatch]);

  useEffect(() => {
    // Scroll to bottom when new messages arrive
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [currentConversation?.messages]);

  const handleStartRecording = async () => {
    if (!permissionGranted) {
      await requestPermission();
    }
    
    if (permissionGranted && connected) {
      startRecording();
    }
  };

  const handleStopRecording = () => {
    stopRecording();
  };

  const getRecorderStatus = () => {
    if (!connected) return 'disconnected';
    if (!permissionGranted) return 'permission-required';
    if (isRecording) return 'recording';
    if (isProcessing) return 'processing';
    return 'ready';
  };

  const getStatusMessage = () => {
    const status = getRecorderStatus();
    switch (status) {
      case 'disconnected':
        return 'Not connected to voice services';
      case 'permission-required':
        return 'Microphone permission required';
      case 'recording':
        return 'Listening...';
      case 'processing':
        return 'Processing your request...';
      case 'ready':
        return 'Ready to listen';
      default:
        return '';
    }
  };

  return (
    <div className="voice-chat">
      <div className="voice-chat-header">
        <h1>Voice Assistant</h1>
        <p>Speak naturally to generate code and get help</p>
      </div>

      <div className="voice-chat-content">
        {/* Voice Controls Section */}
        <div className="voice-controls">
          <div className="voice-recorder-container">
            <VoiceRecorder
              isRecording={isRecording}
              isProcessing={isProcessing}
              audioLevel={audioLevel}
              onStartRecording={handleStartRecording}
              onStopRecording={handleStopRecording}
              disabled={!connected || !permissionGranted}
              status={getRecorderStatus()}
            />
            
            <div className="voice-status">
              <p className="status-message">{getStatusMessage()}</p>
              {voiceError && (
                <p className="error-message">
                  <i className="bi bi-exclamation-triangle"></i>
                  {voiceError}
                </p>
              )}
            </div>
          </div>

          {/* Audio Visualizer */}
          {(isRecording || isProcessing) && (
            <div className="audio-visualizer-container">
              <AudioVisualizer 
                audioLevel={audioLevel} 
                isRecording={isRecording}
                isProcessing={isProcessing}
              />
            </div>
          )}

          {/* Current Transcription */}
          {currentTranscript && (
            <div className="current-transcription">
              <TranscriptionDisplay
                transcript={currentTranscript}
                isProcessing={isProcessing}
              />
            </div>
          )}
        </div>

        {/* Chat Messages */}
        <div className="voice-chat-messages">
          <div className="messages-header">
            <h3>Conversation</h3>
            {isTyping && (
              <div className="typing-indicator">
                <i className="bi bi-three-dots"></i>
                <span>AI is thinking...</span>
              </div>
            )}
          </div>
          
          <div className="messages-container">
            {currentConversation && (
              <ChatMessages
                messages={currentConversation.messages}
                onPlayAudio={playAudio}
                isAudioPlaying={isPlaying}
              />
            )}
            <div ref={messagesEndRef} />
          </div>
        </div>

        {/* Recent Transcriptions */}
        {recognitionResults.length > 0 && (
          <div className="recent-transcriptions">
            <h3>Recent Voice Commands</h3>
            <div className="transcription-list">
              {recognitionResults.slice(0, 5).map((result) => (
                <div key={result.id} className="transcription-item">
                  <span className="transcript-text">{result.transcript}</span>
                  <span className="transcript-confidence">
                    {Math.round(result.confidence * 100)}%
                  </span>
                  <span className="transcript-time">
                    {new Date(result.timestamp).toLocaleTimeString()}
                  </span>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export default VoiceChat;