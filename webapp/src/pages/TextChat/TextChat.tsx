import React from 'react';
import './TextChat.css';

const TextChat: React.FC = () => {
  return (
    <div className="text-chat">
      <div className="text-chat-header">
        <h1>Text Chat</h1>
        <p>Chat with VoiceCode AI Assistant via text</p>
      </div>
      <div className="text-chat-content">
        <div className="chat-messages">
          <div className="message system">
            <div className="message-content">
              Welcome to VoiceCode! Type your questions or commands below.
            </div>
          </div>
        </div>
        <div className="chat-input">
          <input 
            type="text" 
            placeholder="Type your message..."
            className="message-input"
          />
          <button className="send-button">Send</button>
        </div>
      </div>
    </div>
  );
};

export default TextChat;