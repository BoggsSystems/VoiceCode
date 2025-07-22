import React from 'react';
import { useNavigate } from 'react-router-dom';
import { useAppSelector } from '../../../hooks/redux';
import './VoiceStatusCard.css';

const VoiceStatusCard: React.FC = () => {
  const navigate = useNavigate();
  const { connected } = useAppSelector((state) => state.signalr);
  const { permissionGranted, isRecording, isProcessing } = useAppSelector((state) => state.voice);

  const getVoiceStatus = () => {
    if (!connected) return 'disconnected';
    if (!permissionGranted) return 'permission-required';
    if (isRecording) return 'recording';
    if (isProcessing) return 'processing';
    return 'ready';
  };

  const getStatusInfo = () => {
    const status = getVoiceStatus();
    switch (status) {
      case 'disconnected':
        return {
          title: 'Disconnected',
          description: 'Voice services are not available',
          icon: 'bi-wifi-off',
          color: '#dc3545',
          actionText: 'Reconnect',
          actionIcon: 'bi-arrow-clockwise',
        };
      case 'permission-required':
        return {
          title: 'Permission Required',
          description: 'Microphone access needed for voice features',
          icon: 'bi-shield-exclamation',
          color: '#ffc107',
          actionText: 'Grant Permission',
          actionIcon: 'bi-mic',
        };
      case 'recording':
        return {
          title: 'Recording',
          description: 'Listening to your voice command',
          icon: 'bi-mic',
          color: '#dc3545',
          actionText: 'Go to Voice Chat',
          actionIcon: 'bi-arrow-right',
        };
      case 'processing':
        return {
          title: 'Processing',
          description: 'Converting speech and generating response',
          icon: 'bi-hourglass-split',
          color: '#ffc107',
          actionText: 'Go to Voice Chat',
          actionIcon: 'bi-arrow-right',
        };
      case 'ready':
        return {
          title: 'Ready',
          description: 'Voice assistant is ready to help',
          icon: 'bi-mic',
          color: '#28a745',
          actionText: 'Start Voice Chat',
          actionIcon: 'bi-mic',
        };
      default:
        return {
          title: 'Unknown',
          description: 'Voice status unknown',
          icon: 'bi-question-circle',
          color: '#6c757d',
          actionText: 'Check Status',
          actionIcon: 'bi-arrow-right',
        };
    }
  };

  const handleAction = () => {
    const status = getVoiceStatus();
    if (status === 'disconnected') {
      // Trigger reconnection logic here
      window.location.reload();
    } else {
      navigate('/voice-chat');
    }
  };

  const statusInfo = getStatusInfo();

  return (
    <div className="voice-status-card">
      <div className="status-header">
        <h3>Voice Assistant</h3>
        <div 
          className="status-indicator"
          style={{ '--status-color': statusInfo.color } as React.CSSProperties}
        >
          <div className="status-dot"></div>
          <span className="status-text">{statusInfo.title}</span>
        </div>
      </div>
      
      <div className="status-content">
        <div className="status-icon-container">
          <div 
            className="status-icon"
            style={{ '--status-color': statusInfo.color } as React.CSSProperties}
          >
            <i className={`bi ${statusInfo.icon}`}></i>
          </div>
          {(isRecording || isProcessing) && (
            <div className="pulse-ring"></div>
          )}
        </div>
        
        <div className="status-info">
          <p className="status-description">{statusInfo.description}</p>
          
          <div className="status-features">
            <div className="feature-item">
              <i className="bi bi-chat-dots"></i>
              <span>Natural conversation</span>
            </div>
            <div className="feature-item">
              <i className="bi bi-code-slash"></i>
              <span>Code generation</span>
            </div>
            <div className="feature-item">
              <i className="bi bi-lightning"></i>
              <span>Real-time responses</span>
            </div>
          </div>
        </div>
      </div>
      
      <div className="status-actions">
        <button 
          className="action-button primary"
          onClick={handleAction}
          style={{ '--action-color': statusInfo.color } as React.CSSProperties}
        >
          <i className={`bi ${statusInfo.actionIcon}`}></i>
          <span>{statusInfo.actionText}</span>
        </button>
        
        <button 
          className="action-button secondary"
          onClick={() => navigate('/settings')}
        >
          <i className="bi bi-gear"></i>
          <span>Settings</span>
        </button>
      </div>
    </div>
  );
};

export default VoiceStatusCard;