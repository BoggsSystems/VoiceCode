export interface VADConfig {
  // Energy-based pre-filtering
  energyThreshold: number;         // RMS energy threshold (0-1)
  energyHistorySize: number;       // Number of frames for energy averaging
  
  // VAD sensitivity
  speechThreshold: number;         // Probability threshold for speech (0-1)
  silenceThreshold: number;        // Probability threshold for silence (0-1)
  
  // Timing parameters
  minSpeechDuration: number;       // Minimum speech duration in ms
  maxSilenceDuration: number;      // Maximum silence before stopping in ms
  leadingBuffer: number;           // Audio to capture before speech in ms
  trailingBuffer: number;          // Audio to capture after speech in ms
  
  // Frame settings
  frameDuration: number;           // Frame duration in ms (10, 20, or 30)
  sampleRate: number;              // Sample rate (must be 8000, 16000, 32000, or 48000)
}

export enum VADState {
  Idle = 'idle',
  MaybeSpeech = 'maybe_speech',
  Speech = 'speech',
  MaybeSilence = 'maybe_silence',
  Silence = 'silence'
}

export interface VADEvent {
  type: 'speech_start' | 'speech_end' | 'speech_segment';
  timestamp: number;
  duration?: number;
  confidence?: number;
  audioData?: Float32Array;
}

export class VADProcessor {
  private config: VADConfig;
  private audioContext: AudioContext;
  private vadWorklet: AudioWorkletNode | null = null;
  private state: VADState = VADState.Idle;
  private energyHistory: number[] = [];
  private audioBuffer: Float32Array[] = [];
  private speechStartTime: number | null = null;
  private silenceStartTime: number | null = null;
  private listeners: Map<string, ((event: VADEvent) => void)[]> = new Map();
  
  constructor(audioContext: AudioContext, config?: Partial<VADConfig>) {
    this.audioContext = audioContext;
    this.config = {
      energyThreshold: 0.01,
      energyHistorySize: 50,
      speechThreshold: 0.8,
      silenceThreshold: 0.2,
      minSpeechDuration: 250,
      maxSilenceDuration: 1500,
      leadingBuffer: 500,
      trailingBuffer: 500,
      frameDuration: 20,
      sampleRate: 16000,
      ...config
    };
  }

  async initialize(): Promise<void> {
    // Load VAD worklet
    await this.audioContext.audioWorklet.addModule('/vad-processor.worklet.js');
    
    this.vadWorklet = new AudioWorkletNode(this.audioContext, 'vad-processor', {
      processorOptions: {
        frameDuration: this.config.frameDuration,
        sampleRate: this.config.sampleRate
      }
    });

    // Setup message handling
    this.vadWorklet.port.onmessage = (event) => {
      this.handleVADMessage(event.data);
    };

    // Configure VAD
    this.vadWorklet.port.postMessage({
      type: 'configure',
      config: this.config
    });
  }

  connect(source: AudioNode): void {
    if (!this.vadWorklet) {
      throw new Error('VAD not initialized');
    }
    source.connect(this.vadWorklet);
  }

  disconnect(): void {
    if (this.vadWorklet) {
      this.vadWorklet.disconnect();
    }
  }

  private handleVADMessage(data: any): void {
    switch (data.type) {
      case 'energy':
        this.processEnergyFrame(data.energy, data.audioData);
        break;
      case 'vad':
        this.processVADResult(data.probability, data.audioData, data.timestamp);
        break;
      case 'error':
        console.error('VAD error:', data.error);
        break;
    }
  }

  private processEnergyFrame(energy: number, audioData: Float32Array): void {
    // Update energy history
    this.energyHistory.push(energy);
    if (this.energyHistory.length > this.config.energyHistorySize) {
      this.energyHistory.shift();
    }

    // Calculate average energy
    const avgEnergy = this.energyHistory.reduce((a, b) => a + b, 0) / this.energyHistory.length;

    // Energy-based pre-filtering
    if (energy < this.config.energyThreshold * avgEnergy) {
      // Too quiet, skip VAD processing
      if (this.state === VADState.Speech || this.state === VADState.MaybeSpeech) {
        this.transitionToState(VADState.MaybeSilence, audioData);
      }
      return;
    }

    // Add to buffer for leading/trailing audio
    this.updateAudioBuffer(audioData);
  }

  private processVADResult(probability: number, audioData: Float32Array, timestamp: number): void {
    const isSpeech = probability > this.config.speechThreshold;
    const isSilence = probability < this.config.silenceThreshold;

    switch (this.state) {
      case VADState.Idle:
      case VADState.Silence:
        if (isSpeech) {
          this.transitionToState(VADState.MaybeSpeech, audioData, timestamp);
        }
        break;

      case VADState.MaybeSpeech:
        if (isSpeech) {
          const duration = timestamp - (this.speechStartTime || timestamp);
          if (duration >= this.config.minSpeechDuration) {
            this.transitionToState(VADState.Speech, audioData, timestamp);
          }
        } else if (isSilence) {
          this.transitionToState(VADState.Idle, audioData, timestamp);
        }
        break;

      case VADState.Speech:
        if (isSilence) {
          this.transitionToState(VADState.MaybeSilence, audioData, timestamp);
        }
        break;

      case VADState.MaybeSilence:
        if (isSpeech) {
          this.transitionToState(VADState.Speech, audioData, timestamp);
        } else if (isSilence) {
          const silenceDuration = timestamp - (this.silenceStartTime || timestamp);
          if (silenceDuration >= this.config.maxSilenceDuration) {
            this.transitionToState(VADState.Silence, audioData, timestamp);
          }
        }
        break;
    }
  }

  private transitionToState(newState: VADState, audioData: Float32Array, timestamp: number = Date.now()): void {
    const oldState = this.state;
    this.state = newState;

    switch (newState) {
      case VADState.MaybeSpeech:
        this.speechStartTime = timestamp;
        break;

      case VADState.Speech:
        if (oldState !== VADState.MaybeSpeech) {
          this.speechStartTime = timestamp;
        }
        // Emit speech start event
        this.emit('speech_start', {
          type: 'speech_start',
          timestamp: this.speechStartTime!,
          audioData: this.getLeadingBuffer()
        });
        break;

      case VADState.MaybeSilence:
        this.silenceStartTime = timestamp;
        break;

      case VADState.Silence:
      case VADState.Idle:
        if (oldState === VADState.Speech || oldState === VADState.MaybeSilence) {
          // Emit speech end event
          const duration = timestamp - this.speechStartTime!;
          this.emit('speech_end', {
            type: 'speech_end',
            timestamp,
            duration,
            audioData: this.getTrailingBuffer()
          });
        }
        this.speechStartTime = null;
        this.silenceStartTime = null;
        break;
    }
  }

  private updateAudioBuffer(audioData: Float32Array): void {
    this.audioBuffer.push(audioData);
    
    // Keep buffer size limited
    const maxBufferSize = Math.ceil(
      (this.config.leadingBuffer + this.config.trailingBuffer) / 
      this.config.frameDuration
    );
    
    while (this.audioBuffer.length > maxBufferSize) {
      this.audioBuffer.shift();
    }
  }

  private getLeadingBuffer(): Float32Array {
    const leadingFrames = Math.ceil(this.config.leadingBuffer / this.config.frameDuration);
    const startIndex = Math.max(0, this.audioBuffer.length - leadingFrames);
    return this.concatenateAudioBuffers(this.audioBuffer.slice(startIndex));
  }

  private getTrailingBuffer(): Float32Array {
    return this.concatenateAudioBuffers(this.audioBuffer);
  }

  private concatenateAudioBuffers(buffers: Float32Array[]): Float32Array {
    const totalLength = buffers.reduce((sum, buf) => sum + buf.length, 0);
    const result = new Float32Array(totalLength);
    let offset = 0;
    
    for (const buffer of buffers) {
      result.set(buffer, offset);
      offset += buffer.length;
    }
    
    return result;
  }

  on(event: string, callback: (event: VADEvent) => void): void {
    if (!this.listeners.has(event)) {
      this.listeners.set(event, []);
    }
    this.listeners.get(event)!.push(callback);
  }

  off(event: string, callback: (event: VADEvent) => void): void {
    const callbacks = this.listeners.get(event);
    if (callbacks) {
      const index = callbacks.indexOf(callback);
      if (index !== -1) {
        callbacks.splice(index, 1);
      }
    }
  }

  private emit(event: string, data: VADEvent): void {
    const callbacks = this.listeners.get(event);
    if (callbacks) {
      callbacks.forEach(callback => callback(data));
    }
  }

  updateConfig(config: Partial<VADConfig>): void {
    this.config = { ...this.config, ...config };
    if (this.vadWorklet) {
      this.vadWorklet.port.postMessage({
        type: 'configure',
        config: this.config
      });
    }
  }

  getState(): VADState {
    return this.state;
  }

  reset(): void {
    this.state = VADState.Idle;
    this.energyHistory = [];
    this.audioBuffer = [];
    this.speechStartTime = null;
    this.silenceStartTime = null;
  }

  destroy(): void {
    this.disconnect();
    this.vadWorklet = null;
    this.listeners.clear();
    this.reset();
  }
}