import React from 'react';
import { useAppSelector } from '../../../hooks/redux';
import './StatusBar.css';

const StatusBar: React.FC = () => {
  const { connectionState } = useAppSelector((state) => state.signalr);
  const { isRecording } = useAppSelector((state) => state.voice);
  const { currentProject } = useAppSelector((state) => state.projects);

  const getConnectionIcon = () => {
    switch (connectionState) {
      case 'Connected':
        return '🟢';
      case 'Connecting':
        return '🟡';
      case 'Disconnected':
      default:
        return '🔴';
    }
  };

  return (
    <div className="status-bar">
      <div className="status-bar-section">
        <span className="status-item">
          {getConnectionIcon()} {connectionState || 'Disconnected'}
        </span>
      </div>
      
      <div className="status-bar-section">
        {currentProject && (
          <span className="status-item">
            📁 {currentProject.name}
          </span>
        )}
      </div>
      
      <div className="status-bar-section">
        {isRecording && (
          <span className="status-item recording">
            🎙️ Recording...
          </span>
        )}
      </div>
      
      <div className="status-bar-section">
        <span className="status-item">
          VoiceCode v1.0.0
        </span>
      </div>
    </div>
  );
};

export default StatusBar;