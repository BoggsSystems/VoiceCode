import React from 'react';
import './TranscriptionDisplay.css';

interface TranscriptionDisplayProps {
  transcript: string;
  isProcessing: boolean;
  confidence?: number;
  showConfidence?: boolean;
}

const TranscriptionDisplay: React.FC<TranscriptionDisplayProps> = ({
  transcript,
  isProcessing,
  confidence,
  showConfidence = false,
}) => {
  const getConfidenceColor = (conf: number) => {
    if (conf >= 0.8) return '#28a745';
    if (conf >= 0.6) return '#ffc107';
    return '#dc3545';
  };

  const formatTranscript = (text: string) => {
    // Add some basic formatting for better readability
    return text.charAt(0).toUpperCase() + text.slice(1);
  };

  return (
    <div className="transcription-display">
      <div className="transcription-header">
        <div className="header-content">
          <i className="bi bi-chat-quote"></i>
          <span className="header-text">
            {isProcessing ? 'Processing speech...' : 'Voice Command'}
          </span>
        </div>
        
        {confidence !== undefined && showConfidence && (
          <div className="confidence-indicator">
            <span 
              className="confidence-value"
              style={{ color: getConfidenceColor(confidence) }}
            >
              {Math.round(confidence * 100)}%
            </span>
            <div className="confidence-bar">
              <div 
                className="confidence-fill"
                style={{ 
                  width: `${confidence * 100}%`,
                  backgroundColor: getConfidenceColor(confidence)
                }}
              />
            </div>
          </div>
        )}
      </div>
      
      <div className="transcription-content">
        {isProcessing ? (
          <div className="processing-indicator">
            <div className="typing-dots">
              <span></span>
              <span></span>
              <span></span>
            </div>
            <span className="processing-text">Converting speech to text...</span>
          </div>
        ) : (
          <div className="transcript-text">
            {transcript ? formatTranscript(transcript) : 'No speech detected'}
          </div>
        )}
      </div>
      
      {transcript && !isProcessing && (
        <div className="transcription-actions">
          <button
            className="action-button"
            onClick={() => navigator.clipboard?.writeText(transcript)}
            title="Copy to clipboard"
          >
            <i className="bi bi-clipboard"></i>
          </button>
          <button
            className="action-button"
            onClick={() => {
              const utterance = new SpeechSynthesisUtterance(transcript);
              speechSynthesis.speak(utterance);
            }}
            title="Read aloud"
          >
            <i className="bi bi-volume-up"></i>
          </button>
        </div>
      )}
    </div>
  );
};

export default TranscriptionDisplay;