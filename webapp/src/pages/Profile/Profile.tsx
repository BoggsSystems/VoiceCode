import React from 'react';
import './Profile.css';

const Profile: React.FC = () => {
  return (
    <div className="profile">
      <div className="profile-header">
        <h1>Profile</h1>
        <p>Manage your VoiceCode account</p>
      </div>
      <div className="profile-content">
        <div className="profile-section">
          <h3>Account Information</h3>
          <div className="profile-item">
            <label>Name:</label>
            <span>Demo User</span>
          </div>
          <div className="profile-item">
            <label>Email:</label>
            <span>demo@voicecode.ai</span>
          </div>
          <div className="profile-item">
            <label>Plan:</label>
            <span>Developer</span>
          </div>
        </div>

        <div className="profile-section">
          <h3>Usage Statistics</h3>
          <div className="profile-item">
            <label>Voice Sessions:</label>
            <span>0</span>
          </div>
          <div className="profile-item">
            <label>Code Generated:</label>
            <span>0 lines</span>
          </div>
          <div className="profile-item">
            <label>Projects:</label>
            <span>0</span>
          </div>
        </div>
      </div>
    </div>
  );
};

export default Profile;