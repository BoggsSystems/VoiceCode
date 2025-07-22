import React from 'react';
import { useMsal } from '@azure/msal-react';
import { useAppSelector, useAppDispatch } from '../../../hooks/redux';
import { toggleSidebar } from '../../../store/slices/uiSlice';
import './Header.css';

const Header: React.FC = () => {
  const { instance, accounts } = useMsal();
  const dispatch = useAppDispatch();
  const { connected, reconnecting } = useAppSelector((state) => state.signalr);
  const { sidebarCollapsed } = useAppSelector((state) => state.ui);

  const handleLogout = () => {
    instance.logoutPopup({
      postLogoutRedirectUri: window.location.origin,
    });
  };

  const handleToggleSidebar = () => {
    dispatch(toggleSidebar());
  };

  const getConnectionStatus = () => {
    if (reconnecting) return { status: 'reconnecting', text: 'Reconnecting...', icon: 'bi-arrow-repeat' };
    if (connected) return { status: 'connected', text: 'Connected', icon: 'bi-circle-fill' };
    return { status: 'disconnected', text: 'Disconnected', icon: 'bi-circle' };
  };

  const connectionStatus = getConnectionStatus();

  return (
    <header className="header">
      <div className="header-left">
        <button
          className="sidebar-toggle"
          onClick={handleToggleSidebar}
          aria-label={sidebarCollapsed ? 'Expand sidebar' : 'Collapse sidebar'}
        >
          <i className="bi bi-list"></i>
        </button>
        
        <div className="header-brand">
          <div className="brand-logo">
            <i className="bi bi-mic-fill"></i>
          </div>
          <h1 className="brand-title">VoiceCode</h1>
        </div>
      </div>

      <div className="header-center">
        <div className={`connection-status ${connectionStatus.status}`}>
          <i className={`bi ${connectionStatus.icon}`}></i>
          <span>{connectionStatus.text}</span>
        </div>
      </div>

      <div className="header-right">
        <div className="user-menu">
          <div className="user-info">
            <span className="user-name">
              {accounts[0]?.name || accounts[0]?.username || 'User'}
            </span>
          </div>
          
          <div className="user-avatar">
            <img
              src={`https://ui-avatars.com/api/?name=${encodeURIComponent(
                accounts[0]?.name || accounts[0]?.username || 'User'
              )}&background=0078d4&color=fff&size=32`}
              alt="User avatar"
              className="avatar-image"
            />
          </div>
          
          <div className="user-dropdown">
            <button className="dropdown-toggle" aria-label="User menu">
              <i className="bi bi-chevron-down"></i>
            </button>
            
            <div className="dropdown-menu">
              <a href="/profile" className="dropdown-item">
                <i className="bi bi-person"></i>
                Profile
              </a>
              <a href="/settings" className="dropdown-item">
                <i className="bi bi-gear"></i>
                Settings
              </a>
              <div className="dropdown-divider"></div>
              <button onClick={handleLogout} className="dropdown-item">
                <i className="bi bi-box-arrow-right"></i>
                Sign Out
              </button>
            </div>
          </div>
        </div>
      </div>
    </header>
  );
};

export default Header;