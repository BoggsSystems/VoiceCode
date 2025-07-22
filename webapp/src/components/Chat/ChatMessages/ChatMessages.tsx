import React from 'react';
import { Message } from '../../../store/slices/chatSlice';
import ChatMessage from '../ChatMessage/ChatMessage';
import './ChatMessages.css';

interface ChatMessagesProps {
  messages: Message[];
  onPlayAudio?: (audioData: ArrayBuffer | string, messageId?: string) => void;
  isAudioPlaying?: boolean;
  currentlyPlayingId?: string;
}

const ChatMessages: React.FC<ChatMessagesProps> = ({
  messages,
  onPlayAudio,
  isAudioPlaying = false,
  currentlyPlayingId,
}) => {
  if (messages.length === 0) {
    return (
      <div className="chat-messages-empty">
        <div className="empty-state">
          <i className="bi bi-chat-dots"></i>
          <h3>Start a conversation</h3>
          <p>Use voice commands or type to begin interacting with your AI assistant</p>
        </div>
      </div>
    );
  }

  return (
    <div className="chat-messages">
      {messages.map((message, index) => (
        <ChatMessage
          key={message.id}
          message={message}
          onPlayAudio={onPlayAudio}
          isAudioPlaying={isAudioPlaying && currentlyPlayingId === message.id}
          showAvatar={
            index === 0 || 
            messages[index - 1].role !== message.role
          }
          showTimestamp={
            index === messages.length - 1 ||
            messages[index + 1].role !== message.role ||
            (message.timestamp && messages[index + 1].timestamp &&
             new Date(message.timestamp).getTime() - new Date(messages[index + 1].timestamp).getTime() > 300000) // 5 minutes
          }
        />
      ))}
    </div>
  );
};

export default ChatMessages;