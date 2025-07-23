import React from 'react';
import './Projects.css';

const Projects: React.FC = () => {
  return (
    <div className="projects">
      <div className="projects-header">
        <h1>Projects</h1>
        <p>Manage your development projects with VoiceCode</p>
      </div>
      <div className="projects-content">
        <div className="project-card">
          <h3>No Projects Yet</h3>
          <p>Connect VoiceCode to your development projects to get started with AI-powered assistance.</p>
          <button className="add-project-button">Add Project</button>
        </div>
      </div>
    </div>
  );
};

export default Projects;