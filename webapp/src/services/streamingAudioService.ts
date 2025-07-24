import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { apiConfig } from '../config/apiConfig';

export interface AudioStreamConfig {
  sampleRate: number;
  channels: number;
  bitDepth: number;
  chunkDurationMs: number;
  bufferSizeMs: number;
  heartbeatIntervalMs: number;
  reconnectDelayMs: number;
  maxReconnectAttempts: number;
}

export enum AudioStreamMessageType {
  AudioData = 'AudioData',
  Control = 'Control',
  Metadata = 'Metadata',
  Heartbeat = 'Heartbeat',
  Error = 'Error',
  Acknowledgment = 'Acknowledgment'
}

export enum ControlCommand {
  StartStream = 'StartStream',
  StopStream = 'StopStream',
  PauseStream = 'PauseStream',
  ResumeStream = 'ResumeStream',
  ResetStream = 'ResetStream'
}

export interface AudioStreamMessage {
  type: AudioStreamMessageType;
  sessionId: string;
  timestamp: Date;
  payload: any;
}

export interface AudioDataPayload {
  audioData: ArrayBuffer;
  sampleRate: number;
  channels: number;
  bitDepth: number;
  sequenceNumber: number;
  duration: number;
}

export class StreamingAudioService {
  private connection: HubConnection | null = null;
  private audioContext: AudioContext | null = null;
  private mediaStream: MediaStream | null = null;
  private audioWorklet: AudioWorkletNode | null = null;
  private sessionId: string | null = null;
  private config: AudioStreamConfig;
  private sequenceNumber: number = 0;
  private heartbeatInterval: number | null = null;
  private isStreaming: boolean = false;
  private reconnectAttempts: number = 0;

  constructor() {
    this.config = {
      sampleRate: 16000,
      channels: 1,
      bitDepth: 16,
      chunkDurationMs: 100,
      bufferSizeMs: 2000,
      heartbeatIntervalMs: 5000,
      reconnectDelayMs: 1000,
      maxReconnectAttempts: 5
    };
  }

  async initialize(): Promise<void> {
    // Build SignalR connection
    this.connection = new HubConnectionBuilder()
      .withUrl(`${apiConfig.signalr.hubUrl.replace('/hubs/voice', '/hubs/audiostream')}`)
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          if (retryContext.previousRetryCount === this.config.maxReconnectAttempts) {
            return null;
          }
          return Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000);
        }
      })
      .configureLogging(LogLevel.Information)
      .build();

    // Setup event handlers
    this.setupEventHandlers();

    // Start connection
    await this.connection.start();
  }

  private setupEventHandlers(): void {
    if (!this.connection) return;

    this.connection.on('StreamSessionStarted', (data: any) => {
      this.sessionId = data.sessionId;
      this.config = { ...this.config, ...data.config };
      console.log('Stream session started:', this.sessionId);
    });

    this.connection.on('StreamStarted', (data: any) => {
      console.log('Audio streaming started');
      this.isStreaming = true;
      this.startHeartbeat();
    });

    this.connection.on('StreamStopped', (data: any) => {
      console.log('Audio streaming stopped');
      this.isStreaming = false;
      this.stopHeartbeat();
    });

    this.connection.on('AudioChunkAcknowledged', (ack: any) => {
      // Handle acknowledgment if needed
    });

    this.connection.on('PartialTranscription', (data: any) => {
      window.dispatchEvent(new CustomEvent('partialTranscription', { detail: data }));
    });

    this.connection.on('StreamError', (error: any) => {
      console.error('Stream error:', error);
      window.dispatchEvent(new CustomEvent('streamError', { detail: error }));
    });

    this.connection.on('Heartbeat', async (data: any) => {
      // Respond to server heartbeat
      await this.sendHeartbeatResponse();
    });

    this.connection.onreconnecting(() => {
      console.log('Reconnecting to audio stream...');
      this.reconnectAttempts++;
    });

    this.connection.onreconnected(() => {
      console.log('Reconnected to audio stream');
      this.reconnectAttempts = 0;
      if (this.isStreaming) {
        this.resumeStreaming();
      }
    });

    this.connection.onclose(() => {
      console.log('Connection closed');
      this.cleanup();
    });
  }

  async startRecording(): Promise<void> {
    try {
      // Get user media
      this.mediaStream = await navigator.mediaDevices.getUserMedia({
        audio: {
          echoCancellation: true,
          noiseSuppression: true,
          autoGainControl: true,
          sampleRate: this.config.sampleRate
        }
      });

      // Create audio context
      this.audioContext = new AudioContext({
        sampleRate: this.config.sampleRate
      });

      // Load and create audio worklet
      await this.audioContext.audioWorklet.addModule('/audio-processor.worklet.js');
      
      const source = this.audioContext.createMediaStreamSource(this.mediaStream);
      this.audioWorklet = new AudioWorkletNode(this.audioContext, 'audio-stream-processor');

      // Configure worklet
      this.audioWorklet.port.postMessage({
        type: 'config',
        sampleRate: this.config.sampleRate,
        chunkDurationMs: this.config.chunkDurationMs
      });

      // Handle audio chunks from worklet
      this.audioWorklet.port.onmessage = (event) => {
        if (event.data.type === 'audio' && this.isStreaming) {
          this.sendAudioChunk(event.data.data);
        }
      };

      // Connect audio graph
      source.connect(this.audioWorklet);
      this.audioWorklet.connect(this.audioContext.destination);

      // Start streaming
      await this.connection?.invoke('StartAudioStream', this.config);
    } catch (error) {
      console.error('Error starting recording:', error);
      throw error;
    }
  }

  async stopRecording(): Promise<void> {
    try {
      await this.connection?.invoke('StopAudioStream');
      this.cleanup();
    } catch (error) {
      console.error('Error stopping recording:', error);
      throw error;
    }
  }

  private async sendAudioChunk(audioData: ArrayBuffer): Promise<void> {
    if (!this.connection || !this.sessionId) return;

    const message: AudioStreamMessage = {
      type: AudioStreamMessageType.AudioData,
      sessionId: this.sessionId,
      timestamp: new Date(),
      payload: {
        audioData: Array.from(new Uint8Array(audioData)),
        sampleRate: this.config.sampleRate,
        channels: this.config.channels,
        bitDepth: this.config.bitDepth,
        sequenceNumber: this.sequenceNumber++,
        duration: (audioData.byteLength / 2) / this.config.sampleRate
      }
    };

    try {
      await this.connection.invoke('SendAudioChunk', JSON.stringify(message));
    } catch (error) {
      console.error('Error sending audio chunk:', error);
    }
  }

  private startHeartbeat(): void {
    this.heartbeatInterval = window.setInterval(() => {
      this.sendHeartbeat();
    }, this.config.heartbeatIntervalMs);
  }

  private stopHeartbeat(): void {
    if (this.heartbeatInterval) {
      clearInterval(this.heartbeatInterval);
      this.heartbeatInterval = null;
    }
  }

  private async sendHeartbeat(): Promise<void> {
    if (!this.connection || !this.sessionId) return;

    const message: AudioStreamMessage = {
      type: AudioStreamMessageType.Heartbeat,
      sessionId: this.sessionId,
      timestamp: new Date(),
      payload: {
        clientTimestamp: Date.now()
      }
    };

    try {
      await this.connection.invoke('SendAudioChunk', JSON.stringify(message));
    } catch (error) {
      console.error('Error sending heartbeat:', error);
    }
  }

  private async sendHeartbeatResponse(): Promise<void> {
    await this.sendHeartbeat();
  }

  private async resumeStreaming(): Promise<void> {
    if (!this.connection || !this.sessionId) return;

    const message: AudioStreamMessage = {
      type: AudioStreamMessageType.Control,
      sessionId: this.sessionId,
      timestamp: new Date(),
      payload: {
        command: ControlCommand.ResumeStream
      }
    };

    try {
      await this.connection.invoke('SendAudioChunk', JSON.stringify(message));
    } catch (error) {
      console.error('Error resuming stream:', error);
    }
  }

  private cleanup(): void {
    this.stopHeartbeat();
    
    if (this.audioWorklet) {
      this.audioWorklet.disconnect();
      this.audioWorklet = null;
    }

    if (this.audioContext) {
      this.audioContext.close();
      this.audioContext = null;
    }

    if (this.mediaStream) {
      this.mediaStream.getTracks().forEach(track => track.stop());
      this.mediaStream = null;
    }

    this.isStreaming = false;
    this.sequenceNumber = 0;
  }

  async disconnect(): Promise<void> {
    this.cleanup();
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
    }
  }

  isConnected(): boolean {
    return this.connection?.state === 'Connected';
  }

  getSessionId(): string | null {
    return this.sessionId;
  }
}