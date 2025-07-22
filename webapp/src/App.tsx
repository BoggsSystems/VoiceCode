import React, { useEffect } from 'react';
import { Routes, Route, Navigate } from 'react-router-dom';
import { useIsAuthenticated, useMsal } from '@azure/msal-react';
import { useAppDispatch } from './hooks/redux';
import { initializeAuth } from './store/slices/authSlice';
import { initializeSignalR } from './store/slices/signalrSlice';

import Layout from './components/Layout/Layout';
import Login from './pages/Login/Login';
import Dashboard from './pages/Dashboard/Dashboard';
import VoiceChat from './pages/VoiceChat/VoiceChat';
import TextChat from './pages/TextChat/TextChat';
import Projects from './pages/Projects/Projects';
import Settings from './pages/Settings/Settings';
import Profile from './pages/Profile/Profile';
import LoadingSpinner from './components/common/LoadingSpinner/LoadingSpinner';

import './App.css';

const App: React.FC = () => {
  const isAuthenticated = useIsAuthenticated();
  const { instance, accounts } = useMsal();
  const dispatch = useAppDispatch();

  useEffect(() => {
    if (isAuthenticated && accounts.length > 0) {
      // Initialize authentication state
      dispatch(initializeAuth({
        account: accounts[0],
        accessToken: null // Will be acquired when needed
      }));

      // Initialize SignalR connection
      dispatch(initializeSignalR());
    }
  }, [isAuthenticated, accounts, dispatch]);

  if (!isAuthenticated) {
    return <Login />;
  }

  return (
    <div className="app">
      <Layout>
        <Routes>
          <Route path="/" element={<Navigate to="/dashboard" replace />} />
          <Route path="/dashboard" element={<Dashboard />} />
          <Route path="/voice" element={<VoiceChat />} />
          <Route path="/chat" element={<TextChat />} />
          <Route path="/projects" element={<Projects />} />
          <Route path="/settings" element={<Settings />} />
          <Route path="/profile" element={<Profile />} />
          <Route path="*" element={<Navigate to="/dashboard" replace />} />
        </Routes>
      </Layout>
    </div>
  );
};

export default App;