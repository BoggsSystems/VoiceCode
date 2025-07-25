// Direct audio service for testing without SignalR
import { apiConfig } from '../config/authConfig';

export interface TranscriptionResult {
  id: string;
  transcript: string;
  confidence: number;
  language?: string;
  timestamp: Date;
}

export class AudioService {
  private sttUrl: string;
  
  constructor() {
    this.sttUrl = apiConfig.services.stt;
  }

  async transcribeAudio(audioBlob: Blob): Promise<TranscriptionResult> {
    console.log('[AudioService] transcribeAudio called with blob size:', audioBlob.size);
    try {
      // Convert blob to form data
      const formData = new FormData();
      formData.append('audioFile', audioBlob, 'recording.webm');
      formData.append('language', 'en-US');

      console.log('[AudioService] Calling STT service at:', this.sttUrl);
      
      // Call the actual STT service
      const response = await fetch(`${this.sttUrl}/api/transcription/transcribe`, {
        method: 'POST',
        body: formData,
        // No auth header for now since we're bypassing auth
      });

      console.log('[AudioService] STT response status:', response.status);

      if (!response.ok) {
        const errorText = await response.text();
        console.error('[AudioService] STT error response:', errorText);
        throw new Error(`STT service error: ${response.status} - ${errorText}`);
      }

      const result = await response.json();
      console.log('[AudioService] STT transcription result:', result);
      
      // Transform the response to match our interface
      return {
        id: result.id || crypto.randomUUID(),
        transcript: result.text || result.transcript || '',
        confidence: result.confidence || 0.95,
        language: result.language || 'en-US',
        timestamp: new Date(result.timestamp || Date.now())
      };
    } catch (error) {
      console.error('[AudioService] Transcription error:', error);
      throw error;
    }
  }

  async processWithClaude(transcript: string): Promise<string> {
    console.log('[AudioService] processWithClaude called with transcript:', transcript);
    
    try {
      const dispatcherUrl = apiConfig.services.dispatcher;
      console.log('[AudioService] Calling dispatcher at:', dispatcherUrl);
      
      // Call the dispatcher service which will route to Claude
      const response = await fetch(`${dispatcherUrl}/api/voice/process`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          // No auth header for now since we're bypassing auth
        },
        body: JSON.stringify({
          message: transcript,
          conversationId: sessionStorage.getItem('voiceConversationId') || crypto.randomUUID(),
          sessionId: sessionStorage.getItem('voiceSessionId') || crypto.randomUUID(),
        })
      });

      console.log('[AudioService] Dispatcher response status:', response.status);

      if (!response.ok) {
        const errorText = await response.text();
        console.error('[AudioService] Dispatcher error response:', errorText);
        throw new Error(`Dispatcher service error: ${response.status} - ${errorText}`);
      }

      const result = await response.json();
      console.log('[AudioService] Claude response:', result);
      
      return result.response || result.message || 'No response received';
    } catch (error) {
      console.error('[AudioService] Claude processing error:', error);
      // Fallback to a helpful error message
      return "I'm sorry, I encountered an error processing your request. Please try again.";
    }
  }
}

export default new AudioService();