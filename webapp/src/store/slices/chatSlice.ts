import { createSlice, PayloadAction } from '@reduxjs/toolkit';

export interface ChatMessage {
  id: string;
  type: 'user' | 'assistant';
  content: string;
  timestamp: Date;
  metadata?: {
    intent?: string;
    confidence?: number;
    codeBlocks?: CodeBlock[];
    voiceResponse?: {
      text: string;
      emotion?: string;
      audioUrl?: string;
    };
  };
}

export interface CodeBlock {
  language: string;
  content: string;
  fileName?: string;
  startLine?: number;
  endLine?: number;
}

export interface Conversation {
  id: string;
  title: string;
  messages: ChatMessage[];
  createdAt: Date;
  updatedAt: Date;
  projectId?: string;
}

export interface ChatState {
  conversations: Conversation[];
  currentConversationId: string | null;
  isTyping: boolean;
  error: string | null;
  loading: boolean;
}

const initialState: ChatState = {
  conversations: [],
  currentConversationId: null,
  isTyping: false,
  error: null,
  loading: false,
};

const chatSlice = createSlice({
  name: 'chat',
  initialState,
  reducers: {
    createConversation: (state, action: PayloadAction<{ title?: string; projectId?: string }>) => {
      const newConversation: Conversation = {
        id: crypto.randomUUID(),
        title: action.payload.title || `Conversation ${state.conversations.length + 1}`,
        messages: [],
        createdAt: new Date(),
        updatedAt: new Date(),
        projectId: action.payload.projectId,
      };
      
      state.conversations.unshift(newConversation);
      state.currentConversationId = newConversation.id;
    },
    
    setCurrentConversation: (state, action: PayloadAction<string>) => {
      state.currentConversationId = action.payload;
    },
    
    addMessage: (state, action: PayloadAction<Omit<ChatMessage, 'id' | 'timestamp'>>) => {
      if (!state.currentConversationId) {
        // Create a new conversation if none exists
        const newConversation: Conversation = {
          id: crypto.randomUUID(),
          title: 'New Conversation',
          messages: [],
          createdAt: new Date(),
          updatedAt: new Date(),
        };
        state.conversations.unshift(newConversation);
        state.currentConversationId = newConversation.id;
      }
      
      const conversation = state.conversations.find(c => c.id === state.currentConversationId);
      if (conversation) {
        const message: ChatMessage = {
          ...action.payload,
          id: crypto.randomUUID(),
          timestamp: new Date(),
        };
        
        conversation.messages.push(message);
        conversation.updatedAt = new Date();
        
        // Update title if it's the first user message
        if (conversation.messages.length === 1 && action.payload.type === 'user') {
          conversation.title = action.payload.content.substring(0, 50) + 
            (action.payload.content.length > 50 ? '...' : '');
        }
      }
    },
    
    messageReceived: (state, action: PayloadAction<{
      text: string;
      codeBlocks?: CodeBlock[];
      voiceResponse?: { text: string; emotion?: string; audioUrl?: string };
      metadata?: any;
    }>) => {
      const conversation = state.conversations.find(c => c.id === state.currentConversationId);
      if (conversation) {
        const message: ChatMessage = {
          id: crypto.randomUUID(),
          type: 'assistant',
          content: action.payload.text,
          timestamp: new Date(),
          metadata: {
            codeBlocks: action.payload.codeBlocks,
            voiceResponse: action.payload.voiceResponse,
            ...action.payload.metadata,
          },
        };
        
        conversation.messages.push(message);
        conversation.updatedAt = new Date();
        state.isTyping = false;
      }
    },
    
    updateMessage: (state, action: PayloadAction<{ id: string; updates: Partial<ChatMessage> }>) => {
      const conversation = state.conversations.find(c => c.id === state.currentConversationId);
      if (conversation) {
        const messageIndex = conversation.messages.findIndex(m => m.id === action.payload.id);
        if (messageIndex !== -1) {
          conversation.messages[messageIndex] = {
            ...conversation.messages[messageIndex],
            ...action.payload.updates,
          };
          conversation.updatedAt = new Date();
        }
      }
    },
    
    deleteMessage: (state, action: PayloadAction<string>) => {
      const conversation = state.conversations.find(c => c.id === state.currentConversationId);
      if (conversation) {
        conversation.messages = conversation.messages.filter(m => m.id !== action.payload);
        conversation.updatedAt = new Date();
      }
    },
    
    deleteConversation: (state, action: PayloadAction<string>) => {
      state.conversations = state.conversations.filter(c => c.id !== action.payload);
      if (state.currentConversationId === action.payload) {
        state.currentConversationId = state.conversations.length > 0 ? state.conversations[0].id : null;
      }
    },
    
    setIsTyping: (state, action: PayloadAction<boolean>) => {
      state.isTyping = action.payload;
    },
    
    setError: (state, action: PayloadAction<string>) => {
      state.error = action.payload;
      state.loading = false;
    },
    
    clearError: (state) => {
      state.error = null;
    },
    
    setLoading: (state, action: PayloadAction<boolean>) => {
      state.loading = action.payload;
    },
    
    loadConversations: (state, action: PayloadAction<Conversation[]>) => {
      state.conversations = action.payload;
      state.loading = false;
    },
    
    clearAllConversations: (state) => {
      state.conversations = [];
      state.currentConversationId = null;
    },
    
    exportConversation: (state, action: PayloadAction<string>) => {
      const conversation = state.conversations.find(c => c.id === action.payload);
      if (conversation) {
        const exportData = {
          title: conversation.title,
          messages: conversation.messages,
          exportedAt: new Date(),
        };
        
        const blob = new Blob([JSON.stringify(exportData, null, 2)], {
          type: 'application/json',
        });
        
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${conversation.title.replace(/[^a-z0-9]/gi, '_')}.json`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
      }
    },
  },
});

export const {
  createConversation,
  setCurrentConversation,
  addMessage,
  messageReceived,
  updateMessage,
  deleteMessage,
  deleteConversation,
  setIsTyping,
  setError,
  clearError,
  setLoading,
  loadConversations,
  clearAllConversations,
  exportConversation,
} = chatSlice.actions;

export default chatSlice.reducer;