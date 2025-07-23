import React from 'react';
import { useNavigate } from 'react-router-dom';
import { Conversation } from '../../../store/slices/chatSlice';
import { Project } from '../../../store/slices/projectsSlice';
import './RecentActivity.css';

interface RecentActivityProps {
  conversations: Conversation[];
  projects: Project[];
}

const RecentActivity: React.FC<RecentActivityProps> = ({
  conversations,
  projects,
}) => {
  const navigate = useNavigate();

  const formatTimeAgo = (timestamp: string | Date) => {
    const now = new Date();
    const time = typeof timestamp === 'string' ? new Date(timestamp) : timestamp;
    const diffInMinutes = Math.floor((now.getTime() - time.getTime()) / (1000 * 60));
    
    if (diffInMinutes < 1) return 'Just now';
    if (diffInMinutes < 60) return `${diffInMinutes}m ago`;
    if (diffInMinutes < 1440) return `${Math.floor(diffInMinutes / 60)}h ago`;
    return `${Math.floor(diffInMinutes / 1440)}d ago`;
  };

  const getConversationPreview = (conversation: Conversation) => {
    if (conversation.messages.length === 0) return 'New conversation';
    const lastMessage = conversation.messages[conversation.messages.length - 1];
    return lastMessage.content.slice(0, 100) + (lastMessage.content.length > 100 ? '...' : '');
  };

  const handleConversationClick = (conversationId: string) => {
    navigate('/voice-chat', { state: { conversationId } });
  };

  const handleProjectClick = (projectId: string) => {
    navigate('/projects', { state: { projectId } });
  };

  return (
    <div className="recent-activity">
      <div className="recent-activity-header">
        <h3>Recent Activity</h3>
        <p>Your latest conversations and projects</p>
      </div>
      
      <div className="activity-sections">
        {/* Recent Conversations */}
        <div className="activity-section">
          <div className="section-header">
            <h4>
              <i className="bi bi-chat-dots"></i>
              Recent Conversations
            </h4>
            {conversations.length > 0 && (
              <button 
                className="view-all-button"
                onClick={() => navigate('/conversations')}
              >
                View all
              </button>
            )}
          </div>
          
          <div className="activity-list">
            {conversations.length === 0 ? (
              <div className="empty-state">
                <i className="bi bi-chat"></i>
                <span>No conversations yet</span>
              </div>
            ) : (
              conversations.map((conversation) => (
                <div
                  key={conversation.id}
                  className="activity-item conversation-item"
                  onClick={() => handleConversationClick(conversation.id)}
                >
                  <div className="item-icon">
                    <i className="bi bi-chat-fill"></i>
                  </div>
                  <div className="item-content">
                    <h5 className="item-title">{conversation.title}</h5>
                    <p className="item-description">
                      {getConversationPreview(conversation)}
                    </p>
                    <span className="item-time">
                      {conversation.updatedAt && formatTimeAgo(conversation.updatedAt)}
                    </span>
                  </div>
                  <div className="item-arrow">
                    <i className="bi bi-chevron-right"></i>
                  </div>
                </div>
              ))
            )}
          </div>
        </div>

        {/* Recent Projects */}
        <div className="activity-section">
          <div className="section-header">
            <h4>
              <i className="bi bi-folder"></i>
              Recent Projects
            </h4>
            {projects.length > 0 && (
              <button 
                className="view-all-button"
                onClick={() => navigate('/projects')}
              >
                View all
              </button>
            )}
          </div>
          
          <div className="activity-list">
            {projects.length === 0 ? (
              <div className="empty-state">
                <i className="bi bi-folder"></i>
                <span>No projects yet</span>
              </div>
            ) : (
              projects.map((project) => (
                <div
                  key={project.id}
                  className="activity-item project-item"
                  onClick={() => handleProjectClick(project.id)}
                >
                  <div className="item-icon">
                    <i className="bi bi-folder-fill"></i>
                  </div>
                  <div className="item-content">
                    <h5 className="item-title">{project.name}</h5>
                    <p className="item-description">
                      {project.description || 'No description'}
                    </p>
                    <div className="project-meta">
                      <span className="project-language">{project.language}</span>
                      <span className="item-time">
                        {project.lastAccessed && formatTimeAgo(project.lastAccessed)}
                      </span>
                    </div>
                  </div>
                  <div className="item-arrow">
                    <i className="bi bi-chevron-right"></i>
                  </div>
                </div>
              ))
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

export default RecentActivity;