class AudioStreamProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this.bufferSize = 4096;
    this.buffer = new Float32Array(this.bufferSize);
    this.bufferIndex = 0;
    this.sampleRate = 16000; // Target sample rate
    
    // Setup resampling if needed
    this.port.onmessage = (event) => {
      if (event.data.type === 'config') {
        this.sampleRate = event.data.sampleRate || 16000;
        this.chunkDurationMs = event.data.chunkDurationMs || 100;
        this.samplesPerChunk = Math.floor((this.sampleRate * this.chunkDurationMs) / 1000);
      }
    };
  }

  process(inputs, outputs, parameters) {
    const input = inputs[0];
    
    if (!input || !input[0]) {
      return true;
    }

    const inputChannel = input[0];
    
    // Process each sample
    for (let i = 0; i < inputChannel.length; i++) {
      this.buffer[this.bufferIndex++] = inputChannel[i];
      
      // When buffer is full, send it to main thread
      if (this.bufferIndex >= this.samplesPerChunk) {
        this.sendAudioChunk();
        this.bufferIndex = 0;
      }
    }

    return true;
  }

  sendAudioChunk() {
    // Convert float32 to int16 PCM
    const pcmData = new Int16Array(this.bufferIndex);
    for (let i = 0; i < this.bufferIndex; i++) {
      const sample = Math.max(-1, Math.min(1, this.buffer[i]));
      pcmData[i] = sample < 0 ? sample * 0x8000 : sample * 0x7FFF;
    }

    // Send PCM data to main thread
    this.port.postMessage({
      type: 'audio',
      data: pcmData.buffer,
      sampleCount: this.bufferIndex,
      timestamp: currentTime
    }, [pcmData.buffer]);
  }
}

registerProcessor('audio-stream-processor', AudioStreamProcessor);