import React, { useEffect } from 'react';
import { Routes, Route, Navigate } from 'react-router-dom';
// Commented out for testing - bypassing authentication
// import { useIsAuthenticated, useMsal } from '@azure/msal-react';
import { useAppDispatch } from './hooks/redux';
import { initializeAuth } from './store/slices/authSlice';
import { initializeSignalR } from './store/slices/signalrSlice';

import Layout from './components/Layout/Layout';
import Login from './pages/Login/Login';
import SimpleLogin from './pages/Login/SimpleLogin';
import Dashboard from './pages/Dashboard/Dashboard';
import SimpleVoiceChat from './pages/VoiceChat/SimpleVoiceChat';
// import StreamingVoiceChat from './pages/VoiceChat/StreamingVoiceChat';
import TextChat from './pages/TextChat/TextChat';
import Projects from './pages/Projects/Projects';
import Settings from './pages/Settings/Settings';
import Profile from './pages/Profile/Profile';
// import LoadingSpinner from './components/common/LoadingSpinner/LoadingSpinner';

import './App.css';

const App: React.FC = () => {
  // TEMPORARY: Bypass authentication for testing
  const isAuthenticated = true; // Always authenticated for testing
  // const isAuthenticated = useIsAuthenticated();
  // const { instance, accounts } = useMsal();
  const dispatch = useAppDispatch();

  useEffect(() => {
    // Mock authentication for testing
    const mockAccount = {
      homeAccountId: 'test-account-id',
      environment: 'test.microsoft.com',
      tenantId: 'test-tenant-id',
      username: 'testuser@example.com',
      localAccountId: 'test-local-id',
      name: 'Test User',
      idTokenClaims: {
        aud: 'test-audience',
        iss: 'https://test.microsoft.com',
        iat: Date.now() / 1000,
        nbf: Date.now() / 1000,
        exp: (Date.now() / 1000) + 3600,
        name: 'Test User',
        nonce: 'test-nonce',
        oid: 'test-oid',
        preferred_username: 'testuser@example.com',
        sub: 'test-sub',
        tid: 'test-tenant-id',
        ver: '2.0'
      }
    };

    // Initialize authentication state with mock data
    dispatch(initializeAuth({
      account: mockAccount as any,
      accessToken: 'mock-access-token-for-testing'
    }));

    // Initialize SignalR connection
    dispatch(initializeSignalR());
  }, [dispatch]);

  // Skip login page for testing
  // if (!isAuthenticated) {
  //   return <Login />;
  // }

  return (
    <div className="app">
      <Routes>
        <Route path="/" element={<Navigate to="/login-simple" replace />} />
        <Route path="/login-simple" element={<SimpleLogin />} />
        <Route path="/voice-simple" element={<SimpleVoiceChat />} />
        <Route path="/dashboard" element={<Layout><Dashboard /></Layout>} />
        <Route path="/voice" element={<SimpleVoiceChat />} />
        <Route path="/chat" element={<Layout><TextChat /></Layout>} />
        <Route path="/projects" element={<Layout><Projects /></Layout>} />
        <Route path="/settings" element={<Layout><Settings /></Layout>} />
        <Route path="/profile" element={<Layout><Profile /></Layout>} />
        <Route path="*" element={<Navigate to="/login-simple" replace />} />
      </Routes>
    </div>
  );
};

export default App;