import React from 'react';
import './VoiceRecorder.css';

interface VoiceRecorderProps {
  isRecording: boolean;
  isProcessing: boolean;
  audioLevel: number;
  onStartRecording: () => void;
  onStopRecording: () => void;
  disabled?: boolean;
  status: 'ready' | 'recording' | 'processing' | 'disconnected' | 'permission-required';
}

const VoiceRecorder: React.FC<VoiceRecorderProps> = ({
  isRecording,
  isProcessing,
  audioLevel,
  onStartRecording,
  onStopRecording,
  disabled = false,
  status,
}) => {
  const handleClick = () => {
    if (disabled) return;
    
    if (isRecording) {
      onStopRecording();
    } else {
      onStartRecording();
    }
  };

  const getButtonClass = () => {
    const baseClass = 'voice-recorder-button';
    const classes = [baseClass];
    
    if (disabled) classes.push('disabled');
    if (isRecording) classes.push('recording');
    if (isProcessing) classes.push('processing');
    
    return classes.join(' ');
  };

  const getButtonIcon = () => {
    if (isProcessing) return 'bi-hourglass-split';
    if (isRecording) return 'bi-stop-circle';
    if (status === 'permission-required') return 'bi-shield-exclamation';
    if (status === 'disconnected') return 'bi-wifi-off';
    return 'bi-mic';
  };

  const getButtonText = () => {
    if (isProcessing) return 'Processing...';
    if (isRecording) return 'Stop Recording';
    if (status === 'permission-required') return 'Allow Microphone';
    if (status === 'disconnected') return 'Disconnected';
    return 'Start Recording';
  };

  return (
    <div className="voice-recorder">
      <div className="recorder-container">
        <button
          className={getButtonClass()}
          onClick={handleClick}
          disabled={disabled && status !== 'permission-required'}
          style={{
            '--audio-level': audioLevel,
          } as React.CSSProperties}
        >
          <div className="button-content">
            <i className={`bi ${getButtonIcon()}`}></i>
            <span className="button-text">{getButtonText()}</span>
          </div>
          
          {isRecording && (
            <div className="audio-level-indicator">
              <div 
                className="audio-level-bar"
                style={{ transform: `scaleY(${audioLevel})` }}
              />
            </div>
          )}
        </button>
        
        {isRecording && (
          <div className="recording-pulse" />
        )}
      </div>
    </div>
  );
};

export default VoiceRecorder;