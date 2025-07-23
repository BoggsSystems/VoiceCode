import React, { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAppSelector } from '../../hooks/redux';
import QuickActions from '../../components/Dashboard/QuickActions/QuickActions';
import RecentActivity from '../../components/Dashboard/RecentActivity/RecentActivity';
import ProjectStats from '../../components/Dashboard/ProjectStats/ProjectStats';
import VoiceStatusCard from '../../components/Dashboard/VoiceStatusCard/VoiceStatusCard';
import './Dashboard.css';

const Dashboard: React.FC = () => {
  const navigate = useNavigate();
  const { conversations } = useAppSelector((state) => state.chat);
  const { projects } = useAppSelector((state) => state.projects);
  const { connected } = useAppSelector((state) => state.signalr);

  const recentConversations = conversations.slice(0, 5);
  const recentProjects = projects.slice(0, 3);

  const stats = {
    totalConversations: conversations.length,
    totalProjects: projects.length,
    codeFilesGenerated: 0, // Would be calculated from actual generated files in a real app
    voiceCommandsToday: conversations.filter(conv => {
      const today = new Date().toDateString();
      return conv.createdAt && new Date(conv.createdAt).toDateString() === today;
    }).length,
  };

  return (
    <div className="dashboard">
      <div className="dashboard-header">
        <h1>Dashboard</h1>
        <p>Welcome back! Here's what's happening with your projects.</p>
      </div>

      <div className="dashboard-grid">
        {/* Quick Actions */}
        <div className="dashboard-section">
          <QuickActions />
        </div>

        {/* Voice Status */}
        <div className="dashboard-section">
          <VoiceStatusCard />
        </div>

        {/* Stats */}
        <div className="dashboard-section full-width">
          <ProjectStats stats={stats} />
        </div>

        {/* Recent Activity */}
        <div className="dashboard-section">
          <RecentActivity
            conversations={recentConversations}
            projects={recentProjects}
          />
        </div>

        {/* System Status */}
        <div className="dashboard-section">
          <div className="status-card">
            <h3>System Status</h3>
            <div className="status-items">
              <div className={`status-item ${connected ? 'online' : 'offline'}`}>
                <i className={`bi ${connected ? 'bi-check-circle-fill' : 'bi-x-circle-fill'}`}></i>
                <span>Real-time Connection</span>
                <span className="status-text">{connected ? 'Connected' : 'Disconnected'}</span>
              </div>
              
              <div className="status-item online">
                <i className="bi bi-check-circle-fill"></i>
                <span>Speech Services</span>
                <span className="status-text">Online</span>
              </div>
              
              <div className="status-item online">
                <i className="bi bi-check-circle-fill"></i>
                <span>AI Services</span>
                <span className="status-text">Online</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default Dashboard;