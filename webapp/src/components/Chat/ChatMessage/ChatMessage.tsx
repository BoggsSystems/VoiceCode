import React, { useState } from 'react';
import { ChatMessage as ChatMessageType } from '../../../store/slices/chatSlice';
import CodeBlock from '../CodeBlock/CodeBlock';
import './ChatMessage.css';

interface ChatMessageProps {
  message: ChatMessageType;
  onPlayAudio?: (audioData: ArrayBuffer | string, messageId?: string) => void;
  isAudioPlaying?: boolean;
  showAvatar?: boolean;
  showTimestamp?: boolean;
}

const ChatMessage: React.FC<ChatMessageProps> = ({
  message,
  onPlayAudio,
  isAudioPlaying = false,
  showAvatar = true,
  showTimestamp = true,
}) => {
  const [isExpanded, setIsExpanded] = useState(false);

  const formatTimestamp = (timestamp: Date) => {
    return timestamp.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  };

  const renderMessageContent = (content: string) => {
    // Simple code block detection (in a real app, you'd use a proper markdown parser)
    const codeBlockRegex = /```(\w+)?\n?([\s\S]*?)```/g;
    const parts = [];
    let lastIndex = 0;
    let match;

    while ((match = codeBlockRegex.exec(content)) !== null) {
      // Add text before code block
      if (match.index > lastIndex) {
        parts.push(
          <span key={`text-${lastIndex}`}>
            {content.slice(lastIndex, match.index)}
          </span>
        );
      }

      // Add code block
      parts.push(
        <CodeBlock
          key={`code-${match.index}`}
          code={match[2]}
          language={match[1] || 'text'}
        />
      );

      lastIndex = match.index + match[0].length;
    }

    // Add remaining text
    if (lastIndex < content.length) {
      parts.push(
        <span key={`text-${lastIndex}`}>
          {content.slice(lastIndex)}
        </span>
      );
    }

    return parts.length > 0 ? parts : content;
  };

  const isLongMessage = message.content.length > 500;
  const shouldTruncate = isLongMessage && !isExpanded;
  const displayContent = shouldTruncate 
    ? message.content.slice(0, 500) + '...' 
    : message.content;

  return (
    <div className={`chat-message ${message.type}`}>
      {showAvatar && (
        <div className="message-avatar">
          {message.type === 'user' ? (
            <i className="bi bi-person-circle"></i>
          ) : (
            <i className="bi bi-robot"></i>
          )}
        </div>
      )}
      
      <div className="message-content">
        <div className="message-header">
          <span className="message-sender">
            {message.type === 'user' ? 'You' : 'VoiceCode AI'}
          </span>
          {message.metadata?.intent && (
            <span className="message-type">
              {message.metadata.intent === 'voice' && <i className="bi bi-mic-fill"></i>}
              {message.metadata.intent === 'code' && <i className="bi bi-code-slash"></i>}
            </span>
          )}
        </div>
        
        <div className="message-text">
          {renderMessageContent(displayContent)}
        </div>
        
        {isLongMessage && (
          <button
            className="expand-button"
            onClick={() => setIsExpanded(!isExpanded)}
          >
            {isExpanded ? 'Show less' : 'Show more'}
          </button>
        )}
        
        <div className="message-footer">
          {showTimestamp && message.timestamp && (
            <span className="message-timestamp">
              {formatTimestamp(message.timestamp)}
            </span>
          )}
          
          <div className="message-actions">
            {message.metadata?.voiceResponse?.audioUrl && onPlayAudio && (
              <button
                className={`action-button ${isAudioPlaying ? 'playing' : ''}`}
                onClick={() => onPlayAudio(message.metadata!.voiceResponse!.audioUrl!, message.id)}
                title={isAudioPlaying ? 'Stop audio' : 'Play audio'}
              >
                <i className={`bi ${isAudioPlaying ? 'bi-pause-fill' : 'bi-play-fill'}`}></i>
              </button>
            )}
            
            <button
              className="action-button"
              onClick={() => navigator.clipboard?.writeText(message.content)}
              title="Copy message"
            >
              <i className="bi bi-clipboard"></i>
            </button>
            
            {message.type === 'assistant' && (
              <button
                className="action-button"
                onClick={() => {
                  const utterance = new SpeechSynthesisUtterance(message.content);
                  speechSynthesis.speak(utterance);
                }}
                title="Read aloud"
              >
                <i className="bi bi-volume-up"></i>
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

export default ChatMessage;