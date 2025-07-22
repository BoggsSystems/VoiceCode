import React from 'react';
import { useMsal } from '@azure/msal-react';
import { loginRequest } from '../../config/authConfig';
import './Login.css';

const Login: React.FC = () => {
  const { instance } = useMsal();

  const handleLogin = async () => {
    try {
      await instance.loginPopup(loginRequest);
    } catch (error) {
      console.error('Login failed:', error);
    }
  };

  return (
    <div className="login-page">
      <div className="login-container">
        <div className="login-header">
          <div className="login-logo">
            <i className="bi bi-mic-fill"></i>
          </div>
          <h1 className="login-title">VoiceCode</h1>
          <p className="login-subtitle">AI-powered voice assistant for software development</p>
        </div>

        <div className="login-content">
          <div className="login-features">
            <div className="feature-item">
              <i className="bi bi-mic"></i>
              <span>Voice-to-code generation</span>
            </div>
            <div className="feature-item">
              <i className="bi bi-robot"></i>
              <span>AI-powered assistance</span>
            </div>
            <div className="feature-item">
              <i className="bi bi-code-slash"></i>
              <span>Multi-language support</span>
            </div>
            <div className="feature-item">
              <i className="bi bi-chat-dots"></i>
              <span>Natural conversation</span>
            </div>
          </div>

          <div className="login-actions">
            <button className="login-button" onClick={handleLogin}>
              <i className="bi bi-microsoft"></i>
              Sign in with Microsoft
            </button>
            
            <p className="login-help">
              Sign in to start using VoiceCode's AI-powered development tools
            </p>
          </div>
        </div>

        <div className="login-footer">
          <p>&copy; 2024 VoiceCode. Built with Claude AI.</p>
        </div>
      </div>
    </div>
  );
};

export default Login;