// Voice service using SignalR through dispatcher
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { apiConfig } from '../config/authConfig';
import { remoteLogger } from './remoteLogger';

export interface VoiceServiceOptions {
  onJourneyUpdate?: (step: string) => void;
  onTranscriptUpdate?: (transcript: string) => void;
  onResponseUpdate?: (response: string) => void;
  onError?: (error: string) => void;
}

export class VoiceService {
  private connection: HubConnection | null = null;
  private options: VoiceServiceOptions;
  private isConnected: boolean = false;

  constructor(options: VoiceServiceOptions = {}) {
    this.options = options;
  }

  async initialize(): Promise<void> {
    try {
      const hubUrl = apiConfig.signalr.hubUrl;
      this.options.onJourneyUpdate?.('🔌 Connecting to voice service...');
      
      remoteLogger.info('Initializing voice service', { hubUrl });

      this.connection = new HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Information)
        .build();

      // Setup event handlers
      this.setupEventHandlers();

      await this.connection.start();
      this.isConnected = true;
      
      this.options.onJourneyUpdate?.('✅ Connected to voice service');
      remoteLogger.info('Voice service connected');
    } catch (error) {
      const errorMsg = error instanceof Error ? error.message : String(error);
      this.options.onJourneyUpdate?.(`❌ Connection failed: ${errorMsg}`);
      this.options.onError?.(errorMsg);
      throw error;
    }
  }

  private setupEventHandlers(): void {
    if (!this.connection) return;

    this.connection.on('TranscriptionResult', (data: any) => {
      remoteLogger.info('Transcription received', data);
      this.options.onTranscriptUpdate?.(data.transcript || data.text);
      this.options.onJourneyUpdate?.(`✅ Transcribed: "${data.transcript || data.text}"`);
    });

    this.connection.on('ProcessingComplete', (data: any) => {
      remoteLogger.info('Processing complete', data);
      this.options.onResponseUpdate?.(data.response || data.message);
      this.options.onJourneyUpdate?.('✅ Response received from Claude');
    });

    this.connection.on('Error', (error: any) => {
      remoteLogger.error('Voice service error', error);
      this.options.onError?.(error.message || 'Unknown error');
      this.options.onJourneyUpdate?.(`❌ Error: ${error.message}`);
    });

    this.connection.onreconnecting(() => {
      this.options.onJourneyUpdate?.('🔄 Reconnecting...');
    });

    this.connection.onreconnected(() => {
      this.options.onJourneyUpdate?.('✅ Reconnected');
    });
  }

  async processAudio(audioBlob: Blob): Promise<void> {
    if (!this.connection || !this.isConnected) {
      throw new Error('Not connected to voice service');
    }

    const requestId = crypto.randomUUID();
    
    try {
      this.options.onJourneyUpdate?.('📦 Converting audio to base64...');
      
      // Convert blob to base64
      const reader = new FileReader();
      const base64Promise = new Promise<string>((resolve, reject) => {
        reader.onloadend = () => {
          const base64 = reader.result?.toString().split(',')[1];
          if (base64) {
            resolve(base64);
          } else {
            reject(new Error('Failed to convert audio to base64'));
          }
        };
        reader.onerror = reject;
      });
      
      reader.readAsDataURL(audioBlob);
      const base64Audio = await base64Promise;
      
      this.options.onJourneyUpdate?.(`📡 Sending audio (${(audioBlob.size / 1024).toFixed(1)}KB) via SignalR...`);
      
      // Send through SignalR
      await this.connection.invoke('ProcessVoiceRequest', {
        requestId,
        audioData: base64Audio,
        mimeType: audioBlob.type || 'audio/webm',
        language: 'en-US',
        timestamp: new Date().toISOString()
      });
      
      this.options.onJourneyUpdate?.('⏳ Processing audio...');
      
    } catch (error) {
      const errorMsg = error instanceof Error ? error.message : String(error);
      this.options.onError?.(errorMsg);
      this.options.onJourneyUpdate?.(`❌ Failed to process audio: ${errorMsg}`);
      throw error;
    }
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
      this.isConnected = false;
    }
  }

  getConnectionStatus(): boolean {
    return this.isConnected;
  }
}

export default VoiceService;