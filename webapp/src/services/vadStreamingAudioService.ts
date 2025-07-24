import { HubConnection } from '@microsoft/signalr';
import { StreamingAudioService, AudioStreamConfig, AudioStreamMessageType, AudioStreamMessage } from './streamingAudioService';
import { VADProcessor, VADConfig, VADEvent, VADState } from './vadProcessor';

export interface VADStreamingConfig extends AudioStreamConfig {
  enableVAD: boolean;
  vadConfig?: Partial<VADConfig>;
  autoStartOnSpeech: boolean;
  autoStopOnSilence: boolean;
}

export class VADStreamingAudioService extends StreamingAudioService {
  private vadProcessor: VADProcessor | null = null;
  private vadEnabled: boolean = false;
  private autoStartOnSpeech: boolean = true;
  private autoStopOnSilence: boolean = true;
  private isVADActive: boolean = false;
  private audioBufferQueue: ArrayBuffer[] = [];
  private speechSegmentStart: number | null = null;
  
  constructor() {
    super();
  }

  async initialize(config?: Partial<VADStreamingConfig>): Promise<void> {
    await super.initialize();
    
    if (config?.enableVAD) {
      this.vadEnabled = true;
      this.autoStartOnSpeech = config.autoStartOnSpeech ?? true;
      this.autoStopOnSilence = config.autoStopOnSilence ?? true;
    }
  }

  async startRecording(enableVAD: boolean = true): Promise<void> {
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

      const source = this.audioContext.createMediaStreamSource(this.mediaStream);

      if (enableVAD && this.vadEnabled) {
        // Initialize VAD
        this.vadProcessor = new VADProcessor(this.audioContext, {
          sampleRate: this.config.sampleRate,
          frameDuration: 20,
          energyThreshold: 0.01,
          speechThreshold: 0.7,
          silenceThreshold: 0.3,
          minSpeechDuration: 300,
          maxSilenceDuration: 1500,
          leadingBuffer: 300,
          trailingBuffer: 500
        });

        await this.vadProcessor.initialize();

        // Setup VAD event handlers
        this.vadProcessor.on('speech_start', this.handleSpeechStart.bind(this));
        this.vadProcessor.on('speech_end', this.handleSpeechEnd.bind(this));

        // Connect audio through VAD
        source.connect(this.vadProcessor.vadWorklet!);
        
        // Also setup audio worklet for continuous processing
        await this.setupAudioWorklet(source);
        
        this.isVADActive = true;
        
        // Dispatch VAD status event
        window.dispatchEvent(new CustomEvent('vadStatusChanged', { 
          detail: { active: true, state: VADState.Idle }
        }));
      } else {
        // No VAD - traditional streaming
        await this.setupAudioWorklet(source);
        await this.connection?.invoke('StartAudioStream', this.config);
        this.isStreaming = true;
      }
    } catch (error) {
      console.error('Error starting recording:', error);
      throw error;
    }
  }

  private async setupAudioWorklet(source: AudioNode): Promise<void> {
    // Load and create audio worklet
    await this.audioContext!.audioWorklet.addModule('/audio-processor.worklet.js');
    
    this.audioWorklet = new AudioWorkletNode(this.audioContext!, 'audio-stream-processor');

    // Configure worklet
    this.audioWorklet.port.postMessage({
      type: 'config',
      sampleRate: this.config.sampleRate,
      chunkDurationMs: this.config.chunkDurationMs
    });

    // Handle audio chunks from worklet
    this.audioWorklet.port.onmessage = (event) => {
      if (event.data.type === 'audio') {
        if (this.isVADActive && !this.isStreaming) {
          // Buffer audio when VAD is active but not streaming
          this.audioBufferQueue.push(event.data.data);
          // Keep only last 2 seconds of audio
          while (this.audioBufferQueue.length > 20) {
            this.audioBufferQueue.shift();
          }
        } else if (this.isStreaming) {
          // Send audio chunk when streaming
          this.sendAudioChunk(event.data.data);
        }
      }
    };

    // Connect audio graph
    source.connect(this.audioWorklet);
    this.audioWorklet.connect(this.audioContext!.destination);
  }

  private async handleSpeechStart(event: VADEvent): Promise<void> {
    console.log('VAD: Speech started', event);
    this.speechSegmentStart = event.timestamp;
    
    // Dispatch event for UI
    window.dispatchEvent(new CustomEvent('vadSpeechStart', { detail: event }));
    window.dispatchEvent(new CustomEvent('vadStatusChanged', { 
      detail: { active: true, state: VADState.Speech }
    }));
    
    if (this.autoStartOnSpeech && !this.isStreaming) {
      // Start streaming
      await this.connection?.invoke('StartAudioStream', this.config);
      this.isStreaming = true;
      
      // Send buffered audio chunks
      if (event.audioData) {
        // Convert Float32Array to Int16Array
        const pcmData = this.float32ToInt16(event.audioData);
        await this.sendAudioChunk(pcmData.buffer);
      }
      
      // Send any buffered chunks
      for (const chunk of this.audioBufferQueue) {
        await this.sendAudioChunk(chunk);
      }
      this.audioBufferQueue = [];
    }
  }

  private async handleSpeechEnd(event: VADEvent): Promise<void> {
    console.log('VAD: Speech ended', event);
    
    // Calculate speech duration
    const duration = event.duration || (event.timestamp - (this.speechSegmentStart || event.timestamp));
    
    // Dispatch event for UI
    window.dispatchEvent(new CustomEvent('vadSpeechEnd', { 
      detail: { ...event, duration }
    }));
    window.dispatchEvent(new CustomEvent('vadStatusChanged', { 
      detail: { active: true, state: VADState.Silence }
    }));
    
    if (this.autoStopOnSilence && this.isStreaming) {
      // Send any remaining audio
      if (event.audioData) {
        const pcmData = this.float32ToInt16(event.audioData);
        await this.sendAudioChunk(pcmData.buffer);
      }
      
      // Stop streaming after a short delay
      setTimeout(async () => {
        if (this.vadProcessor?.getState() !== VADState.Speech) {
          await this.connection?.invoke('StopAudioStream');
          this.isStreaming = false;
          this.sequenceNumber = 0;
        }
      }, 500);
    }
    
    this.speechSegmentStart = null;
  }

  private float32ToInt16(float32Array: Float32Array): Int16Array {
    const int16Array = new Int16Array(float32Array.length);
    for (let i = 0; i < float32Array.length; i++) {
      const sample = Math.max(-1, Math.min(1, float32Array[i]));
      int16Array[i] = sample < 0 ? sample * 0x8000 : sample * 0x7FFF;
    }
    return int16Array;
  }

  async stopRecording(): Promise<void> {
    this.isVADActive = false;
    
    if (this.vadProcessor) {
      this.vadProcessor.destroy();
      this.vadProcessor = null;
    }
    
    // Dispatch VAD inactive event
    window.dispatchEvent(new CustomEvent('vadStatusChanged', { 
      detail: { active: false, state: VADState.Idle }
    }));
    
    await super.stopRecording();
  }

  updateVADConfig(config: Partial<VADConfig>): void {
    if (this.vadProcessor) {
      this.vadProcessor.updateConfig(config);
    }
  }

  getVADState(): VADState | null {
    return this.vadProcessor?.getState() || null;
  }

  isVADEnabled(): boolean {
    return this.vadEnabled && this.isVADActive;
  }

  // Override sendAudioChunk to add VAD metadata
  protected async sendAudioChunk(audioData: ArrayBuffer): Promise<void> {
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
        duration: (audioData.byteLength / 2) / this.config.sampleRate,
        metadata: {
          vadEnabled: this.isVADActive,
          vadState: this.vadProcessor?.getState() || VADState.Idle
        }
      }
    };

    try {
      await this.connection.invoke('SendAudioChunk', JSON.stringify(message));
    } catch (error) {
      console.error('Error sending audio chunk:', error);
    }
  }
}