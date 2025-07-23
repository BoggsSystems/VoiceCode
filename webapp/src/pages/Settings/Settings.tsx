import React from 'react';
import './Settings.css';

const Settings: React.FC = () => {
  return (
    <div className="settings">
      <div className="settings-header">
        <h1>Settings</h1>
        <p>Configure your VoiceCode experience</p>
      </div>
      <div className="settings-content">
        <div className="settings-section">
          <h3>Voice Settings</h3>
          <div className="setting-item">
            <label>
              <input type="checkbox" defaultChecked />
              Enable voice recognition
            </label>
          </div>
          <div className="setting-item">
            <label>
              <input type="checkbox" defaultChecked />
              Enable text-to-speech
            </label>
          </div>
        </div>

        <div className="settings-section">
          <h3>AI Assistant</h3>
          <div className="setting-item">
            <label>AI Model:</label>
            <select>
              <option>Claude 3.5 Sonnet</option>
              <option>GPT-4</option>
            </select>
          </div>
        </div>

        <div className="settings-section">
          <h3>Interface</h3>
          <div className="setting-item">
            <label>
              <input type="checkbox" />
              Dark mode
            </label>
          </div>
        </div>
      </div>
    </div>
  );
};

export default Settings;