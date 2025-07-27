import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import './SimpleLogin.css';

const SimpleLogin: React.FC = () => {
  const navigate = useNavigate();
  const [username, setUsername] = useState('test@voicecode.dev');
  const [password, setPassword] = useState('TestPassword123!');
  const [isLoading, setIsLoading] = useState(false);

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsLoading(true);

    try {
      // For now, use the simple-token endpoint to bypass login issues
      const apiUrl = process.env.REACT_APP_API_BASE_URL || 'https://voicecode-router.orangewater-a2f689a8.eastus.azurecontainerapps.io';
      
      // First try the simple-token endpoint (for testing)
      const response = await fetch(`${apiUrl}/api/authentication/simple-token`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
      });

      if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || 'Login failed');
      }

      const data = await response.json();
      
      // Store auth info
      localStorage.setItem('auth-token', data.token);
      localStorage.setItem('auth-user', username || 'test@voicecode.dev');
      localStorage.setItem('auth-timestamp', Date.now().toString());

      setIsLoading(false);

      // Navigate to voice chat
      navigate('/voice-simple');
    } catch (error: any) {
      console.error('Login error:', error);
      setIsLoading(false);
      alert(`Login failed: ${error?.message || 'Unknown error'}`);
    }
  };

  return (
    <div className="simple-login-container">
      <div className="login-card">
        <h1>VoiceCode Login</h1>
        <p className="login-subtitle">Development Test Login</p>
        
        <form onSubmit={handleLogin}>
          <div className="form-group">
            <label htmlFor="username">Username</label>
            <input
              type="email"
              id="username"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              placeholder="Enter username"
              required
            />
          </div>

          <div className="form-group">
            <label htmlFor="password">Password</label>
            <input
              type="password"
              id="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="Enter password"
              required
            />
          </div>

          <button 
            type="submit" 
            className="login-button"
            disabled={isLoading}
          >
            {isLoading ? 'Logging in...' : 'Login'}
          </button>
        </form>

        <div className="login-info">
          <p>🔒 This is a development login page with pre-filled test credentials</p>
          <p style={{fontSize: '11px', color: '#888', marginTop: '10px'}}>Version 1.0.9 | If you don't see updates, try Ctrl+F5 to clear cache</p>
        </div>
      </div>
    </div>
  );
};

export default SimpleLogin;