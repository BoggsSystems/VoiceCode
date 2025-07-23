import React from 'react';
import { NavLink } from 'react-router-dom';
import { useAppSelector } from '../../../hooks/redux';
import './Sidebar.css';

const Sidebar: React.FC = () => {
  const { sidebarOpen } = useAppSelector((state) => state.ui);

  const menuItems = [
    { path: '/dashboard', label: 'Dashboard', icon: '📊' },
    { path: '/voice', label: 'Voice Chat', icon: '🎙️' },
    { path: '/chat', label: 'Text Chat', icon: '💬' },
    { path: '/projects', label: 'Projects', icon: '📁' },
    { path: '/settings', label: 'Settings', icon: '⚙️' },
    { path: '/profile', label: 'Profile', icon: '👤' },
  ];

  return (
    <aside className={`sidebar ${!sidebarOpen ? 'sidebar--collapsed' : ''}`}>
      <div className="sidebar-header">
        <div className="sidebar-logo">
          <span className="sidebar-logo-icon">🎙️</span>
          {sidebarOpen && <span className="sidebar-logo-text">VoiceCode</span>}
        </div>
      </div>
      
      <nav className="sidebar-nav">
        {menuItems.map((item) => (
          <NavLink
            key={item.path}
            to={item.path}
            className={({ isActive }) => 
              `sidebar-nav-item ${isActive ? 'sidebar-nav-item--active' : ''}`
            }
          >
            <span className="sidebar-nav-icon">{item.icon}</span>
            {sidebarOpen && <span className="sidebar-nav-label">{item.label}</span>}
          </NavLink>
        ))}
      </nav>
    </aside>
  );
};

export default Sidebar;