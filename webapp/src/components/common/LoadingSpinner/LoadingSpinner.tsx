import React from 'react';
import './LoadingSpinner.css';

interface LoadingSpinnerProps {
  size?: 'small' | 'medium' | 'large';
  color?: string;
}

const LoadingSpinner: React.FC<LoadingSpinnerProps> = ({ 
  size = 'medium',
  color = '#667eea'
}) => {
  return (
    <div className={`loading-spinner loading-spinner--${size}`}>
      <div 
        className="loading-spinner__circle"
        style={{ borderTopColor: color }}
      />
    </div>
  );
};

export default LoadingSpinner;