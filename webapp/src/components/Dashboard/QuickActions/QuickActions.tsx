import React from 'react';
import { useNavigate } from 'react-router-dom';
import { useAppDispatch } from '../../../hooks/redux';
import { createConversation } from '../../../store/slices/chatSlice';
import { createProject } from '../../../store/slices/projectsSlice';
import './QuickActions.css';

const QuickActions: React.FC = () => {
  const navigate = useNavigate();
  const dispatch = useAppDispatch();

  const handleStartVoiceChat = () => {
    dispatch(createConversation({ title: 'Voice Chat Session' }));
    navigate('/voice-chat');
  };

  const handleCreateProject = () => {
    const projectName = prompt('Enter project name:');
    if (projectName) {
      dispatch(createProject({ 
        name: projectName,
        description: `New project: ${projectName}`,
        language: 'typescript'
      }));
    }
  };

  const handleOpenProjects = () => {
    navigate('/projects');
  };

  const handleOpenSettings = () => {
    navigate('/settings');
  };

  const actions = [
    {
      id: 'voice-chat',
      title: 'Start Voice Chat',
      description: 'Begin a new voice conversation',
      icon: 'bi-mic',
      color: '#007acc',
      onClick: handleStartVoiceChat,
    },
    {
      id: 'new-project',
      title: 'New Project',
      description: 'Create a new coding project',
      icon: 'bi-folder-plus',
      color: '#28a745',
      onClick: handleCreateProject,
    },
    {
      id: 'view-projects',
      title: 'View Projects',
      description: 'Browse existing projects',
      icon: 'bi-collection',
      color: '#6f42c1',
      onClick: handleOpenProjects,
    },
    {
      id: 'settings',
      title: 'Settings',
      description: 'Configure your preferences',
      icon: 'bi-gear',
      color: '#6c757d',
      onClick: handleOpenSettings,
    },
  ];

  return (
    <div className="quick-actions">
      <div className="quick-actions-header">
        <h3>Quick Actions</h3>
        <p>Get started with common tasks</p>
      </div>
      
      <div className="actions-grid">
        {actions.map((action) => (
          <button
            key={action.id}
            className="action-card"
            onClick={action.onClick}
            style={{ '--action-color': action.color } as React.CSSProperties}
          >
            <div className="action-icon">
              <i className={`bi ${action.icon}`}></i>
            </div>
            <div className="action-content">
              <h4 className="action-title">{action.title}</h4>
              <p className="action-description">{action.description}</p>
            </div>
            <div className="action-arrow">
              <i className="bi bi-arrow-right"></i>
            </div>
          </button>
        ))}
      </div>
    </div>
  );
};

export default QuickActions;