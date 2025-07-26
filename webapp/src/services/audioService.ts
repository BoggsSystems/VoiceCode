// Direct audio service for testing without SignalR
import { apiConfig } from '../config/authConfig';
import { remoteLogger } from './remoteLogger';

export interface TranscriptionResult {
  id: string;
  transcript: string;
  confidence: number;
  language?: string;
  timestamp: Date;
}

export class AudioService {
  private apiGatewayUrl: string;
  private dispatcherUrl: string;
  
  constructor() {
    // Use API Gateway as primary endpoint
    this.apiGatewayUrl = apiConfig.baseUrl; // This is the router at 4.157.176.121
    this.dispatcherUrl = apiConfig.services.dispatcher;
    
    console.log('[AudioService] Initialized with API Gateway:', this.apiGatewayUrl);
    remoteLogger.info('AudioService initialized', {
      apiGateway: this.apiGatewayUrl,
      dispatcher: this.dispatcherUrl
    });
  }

  async transcribeAudio(audioBlob: Blob): Promise<TranscriptionResult> {
    const startTime = Date.now();
    const requestId = crypto.randomUUID();
    
    console.log('[AudioService] 🎤 STEP 1: Starting transcription', {
      blobSize: audioBlob.size,
      requestId,
      timestamp: new Date().toISOString()
    });
    
    remoteLogger.info('Starting audio transcription', {
      step: 1,
      requestId,
      blobSize: audioBlob.size,
      blobType: audioBlob.type
    });
    
    try {
      // Convert blob to form data
      const formData = new FormData();
      // Determine filename based on blob type
      const filename = audioBlob.type.includes('wav') ? 'recording.wav' : 'recording.webm';
      formData.append('audioFile', audioBlob, filename);
      formData.append('language', 'en-US');
      formData.append('requestId', requestId);

      // Route through API Gateway proxy
      const apiUrl = `${this.apiGatewayUrl}/api/proxy/stt/transcribe`;
      console.log('[AudioService] 📡 STEP 2: Sending to STT service via API Gateway:', apiUrl);
      
      remoteLogger.info('Sending audio to API Gateway', {
        step: 2,
        requestId,
        url: apiUrl,
        method: 'POST'
      });
      
      // Get auth token from localStorage
      const authToken = localStorage.getItem('auth-token');
      
      // Call API Gateway which will route to STT service
      const response = await fetch(apiUrl, {
        method: 'POST',
        headers: {
          'Authorization': authToken ? `Bearer ${authToken}` : '',
        },
        body: formData,
      });

      const responseTime = Date.now() - startTime;
      console.log('[AudioService] 📥 STEP 3: Received response', {
        status: response.status,
        statusText: response.statusText,
        responseTime: `${responseTime}ms`,
        headers: Object.fromEntries(response.headers.entries())
      });
      
      remoteLogger.info('API Gateway response received', {
        step: 3,
        requestId,
        status: response.status,
        responseTime,
        headers: Object.fromEntries(response.headers.entries())
      });

      if (!response.ok) {
        const errorText = await response.text();
        console.error('[AudioService] ❌ STEP 3 ERROR: STT failed', {
          status: response.status,
          error: errorText
        });
        
        remoteLogger.error('STT transcription failed', {
          step: 3,
          requestId,
          status: response.status,
          error: errorText
        });
        
        throw new Error(`STT service error: ${response.status} - ${errorText}`);
      }

      const result = await response.json();
      console.log('[AudioService] ✅ STEP 4: Transcription successful', {
        transcript: result.text || result.transcript,
        confidence: result.confidence,
        processingTime: `${Date.now() - startTime}ms`
      });
      
      remoteLogger.info('Transcription completed', {
        step: 4,
        requestId,
        hasTranscript: !!(result.text || result.transcript),
        confidence: result.confidence,
        totalTime: Date.now() - startTime
      });
      
      // Transform the response to match our interface
      return {
        id: result.id || requestId,
        transcript: result.text || result.transcript || '',
        confidence: result.confidence || 0.95,
        language: result.language || 'en-US',
        timestamp: new Date(result.timestamp || Date.now())
      };
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : String(error);
      console.error('[AudioService] ❌ Transcription error:', {
        error: errorMessage,
        requestId,
        duration: `${Date.now() - startTime}ms`
      });
      
      remoteLogger.error('Transcription error', {
        requestId,
        error: errorMessage,
        duration: Date.now() - startTime
      });
      
      throw error;
    }
  }

  async processWithClaude(transcript: string): Promise<string> {
    const startTime = Date.now();
    const sessionId = sessionStorage.getItem('voiceSessionId') || crypto.randomUUID();
    
    console.log('[AudioService] 🧠 STEP 5: Processing with orchestration', {
      transcript,
      sessionId,
      timestamp: new Date().toISOString()
    });
    
    remoteLogger.info('Starting voice command processing', {
      step: 5,
      sessionId,
      transcriptLength: transcript.length
    });
    
    try {
      // Store session ID for future use
      sessionStorage.setItem('voiceSessionId', sessionId);
      
      // Send to Router for intent classification and orchestration
      const apiUrl = `${this.apiGatewayUrl}/api/voicecommand/process`;
      console.log('[AudioService] 📡 STEP 6: Sending to Router for orchestration:', apiUrl);
      
      remoteLogger.info('Sending to Router for orchestration', {
        step: 6,
        sessionId,
        url: apiUrl,
        method: 'POST'
      });
      
      // Get auth token from localStorage
      const authToken = localStorage.getItem('auth-token');
      
      // Call Router which will classify intent and forward to Dispatcher
      const response = await fetch(apiUrl, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': authToken ? `Bearer ${authToken}` : '',
        },
        body: JSON.stringify({
          transcription: transcript,
          sessionId,
          timestamp: new Date().toISOString(),
          metadata: {
            source: 'voice',
            client: 'webapp'
          }
        })
      });

      const responseTime = Date.now() - startTime;
      console.log('[AudioService] 📥 STEP 7: Received orchestration response', {
        status: response.status,
        statusText: response.statusText,
        responseTime: `${responseTime}ms`,
        headers: Object.fromEntries(response.headers.entries())
      });
      
      remoteLogger.info('Orchestration response received', {
        step: 7,
        sessionId,
        status: response.status,
        responseTime,
        headers: Object.fromEntries(response.headers.entries())
      });

      if (!response.ok) {
        const errorText = await response.text();
        console.error('[AudioService] ❌ STEP 7 ERROR: Orchestration failed', {
          status: response.status,
          error: errorText,
          sessionId
        });
        
        remoteLogger.error('Orchestration failed', {
          step: 7,
          sessionId,
          status: response.status,
          error: errorText
        });
        
        throw new Error(`Orchestration error: ${response.status} - ${errorText}`);
      }

      const result = await response.json();
      
      // Check if clarification is needed
      if (result.metadata?.clarification_needed) {
        console.log('[AudioService] 🤔 Clarification needed:', result.response);
        return result.response || "Could you please clarify what you'd like me to do?";
      }
      
      const responseText = result.response || result.message || 'Your request is being processed.';
      
      console.log('[AudioService] ✅ STEP 8: Orchestration complete', {
        taskId: result.taskId,
        intent: result.intent,
        responseLength: responseText.length,
        processingTime: `${Date.now() - startTime}ms`,
        sessionId
      });
      
      remoteLogger.info('Orchestration completed', {
        step: 8,
        sessionId,
        taskId: result.taskId,
        intent: result.intent,
        responseLength: responseText.length,
        totalTime: Date.now() - startTime
      });
      
      return responseText;
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : String(error);
      console.error('[AudioService] ❌ Orchestration error:', {
        error: errorMessage,
        sessionId,
        duration: `${Date.now() - startTime}ms`
      });
      
      remoteLogger.error('Orchestration error', {
        sessionId,
        error: errorMessage,
        duration: Date.now() - startTime
      });
      
      // Fallback to a helpful error message
      return "I'm sorry, I encountered an error processing your request. Please try again.";
    }
  }
}

export default new AudioService();