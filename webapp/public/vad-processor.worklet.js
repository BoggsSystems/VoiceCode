// Simple WebRTC-style VAD implementation
class VADProcessorWorklet extends AudioWorkletProcessor {
  constructor(options) {
    super();
    
    this.frameDuration = options.processorOptions?.frameDuration || 20;
    this.sampleRate = options.processorOptions?.sampleRate || 16000;
    this.frameSize = Math.floor((this.sampleRate * this.frameDuration) / 1000);
    
    // VAD parameters
    this.energyThreshold = 0.01;
    this.speechThreshold = 0.8;
    this.silenceThreshold = 0.2;
    
    // Buffers
    this.inputBuffer = new Float32Array(this.frameSize * 2);
    this.bufferIndex = 0;
    
    // Energy tracking
    this.energySmoothing = 0.9;
    this.smoothedEnergy = 0;
    
    // Simple frequency analysis for VAD
    this.fftSize = 256;
    this.hannWindow = this.createHannWindow(this.fftSize);
    
    this.port.onmessage = (event) => {
      if (event.data.type === 'configure') {
        this.updateConfig(event.data.config);
      }
    };
  }

  updateConfig(config) {
    if (config.energyThreshold !== undefined) {
      this.energyThreshold = config.energyThreshold;
    }
    if (config.speechThreshold !== undefined) {
      this.speechThreshold = config.speechThreshold;
    }
    if (config.silenceThreshold !== undefined) {
      this.silenceThreshold = config.silenceThreshold;
    }
  }

  process(inputs, outputs, parameters) {
    const input = inputs[0];
    if (!input || !input[0]) {
      return true;
    }

    const inputChannel = input[0];
    
    // Fill buffer
    for (let i = 0; i < inputChannel.length; i++) {
      this.inputBuffer[this.bufferIndex++] = inputChannel[i];
      
      if (this.bufferIndex >= this.frameSize) {
        this.processFrame();
        this.bufferIndex = 0;
      }
    }

    return true;
  }

  processFrame() {
    const frame = this.inputBuffer.slice(0, this.frameSize);
    
    // Calculate frame energy (RMS)
    const energy = this.calculateRMS(frame);
    
    // Update smoothed energy
    this.smoothedEnergy = this.energySmoothing * this.smoothedEnergy + 
                         (1 - this.energySmoothing) * energy;
    
    // Send energy data
    this.port.postMessage({
      type: 'energy',
      energy: energy,
      smoothedEnergy: this.smoothedEnergy,
      audioData: frame,
      timestamp: currentTime * 1000
    });
    
    // Perform simple VAD based on energy and spectral features
    if (energy > this.energyThreshold) {
      const vadProbability = this.calculateVADProbability(frame, energy);
      
      this.port.postMessage({
        type: 'vad',
        probability: vadProbability,
        audioData: frame,
        timestamp: currentTime * 1000
      });
    }
  }

  calculateRMS(frame) {
    let sum = 0;
    for (let i = 0; i < frame.length; i++) {
      sum += frame[i] * frame[i];
    }
    return Math.sqrt(sum / frame.length);
  }

  calculateVADProbability(frame, energy) {
    // Simple VAD based on energy and spectral characteristics
    
    // 1. Energy-based score
    const energyScore = Math.min(energy / 0.1, 1.0);
    
    // 2. Zero-crossing rate (indicates speech vs noise)
    const zcr = this.calculateZCR(frame);
    const zcrScore = 1.0 - Math.min(zcr / 50, 1.0); // Lower ZCR indicates speech
    
    // 3. Spectral centroid (speech has specific frequency characteristics)
    const centroid = this.calculateSpectralCentroid(frame);
    const centroidScore = this.scoreCentroid(centroid);
    
    // 4. High-frequency to low-frequency energy ratio
    const hflfRatio = this.calculateHFLFRatio(frame);
    const ratioScore = this.scoreHFLFRatio(hflfRatio);
    
    // Combine scores with weights
    const weights = {
      energy: 0.3,
      zcr: 0.2,
      centroid: 0.3,
      ratio: 0.2
    };
    
    const probability = weights.energy * energyScore +
                       weights.zcr * zcrScore +
                       weights.centroid * centroidScore +
                       weights.ratio * ratioScore;
    
    return Math.max(0, Math.min(1, probability));
  }

  calculateZCR(frame) {
    let zcr = 0;
    for (let i = 1; i < frame.length; i++) {
      if ((frame[i] >= 0) !== (frame[i - 1] >= 0)) {
        zcr++;
      }
    }
    return zcr;
  }

  calculateSpectralCentroid(frame) {
    // Apply window
    const windowed = new Float32Array(this.fftSize);
    for (let i = 0; i < Math.min(frame.length, this.fftSize); i++) {
      windowed[i] = frame[i] * this.hannWindow[i];
    }
    
    // Simple DFT for magnitude spectrum (real FFT would be more efficient)
    const magnitudes = new Float32Array(this.fftSize / 2);
    for (let k = 0; k < this.fftSize / 2; k++) {
      let real = 0, imag = 0;
      for (let n = 0; n < this.fftSize; n++) {
        const angle = -2 * Math.PI * k * n / this.fftSize;
        real += windowed[n] * Math.cos(angle);
        imag += windowed[n] * Math.sin(angle);
      }
      magnitudes[k] = Math.sqrt(real * real + imag * imag);
    }
    
    // Calculate centroid
    let weightedSum = 0;
    let magnitudeSum = 0;
    
    for (let i = 0; i < magnitudes.length; i++) {
      const frequency = (i * this.sampleRate) / this.fftSize;
      weightedSum += frequency * magnitudes[i];
      magnitudeSum += magnitudes[i];
    }
    
    return magnitudeSum > 0 ? weightedSum / magnitudeSum : 0;
  }

  scoreCentroid(centroid) {
    // Speech typically has centroid between 300-3000 Hz
    if (centroid >= 300 && centroid <= 3000) {
      return 1.0;
    } else if (centroid < 300) {
      return centroid / 300;
    } else {
      return Math.max(0, 1.0 - (centroid - 3000) / 3000);
    }
  }

  calculateHFLFRatio(frame) {
    // Apply window
    const windowed = new Float32Array(this.fftSize);
    for (let i = 0; i < Math.min(frame.length, this.fftSize); i++) {
      windowed[i] = frame[i] * this.hannWindow[i];
    }
    
    // Calculate energy in low and high frequency bands
    let lowEnergy = 0;
    let highEnergy = 0;
    const cutoffBin = Math.floor((1000 * this.fftSize) / this.sampleRate); // 1kHz cutoff
    
    for (let k = 0; k < this.fftSize / 2; k++) {
      let real = 0, imag = 0;
      for (let n = 0; n < this.fftSize; n++) {
        const angle = -2 * Math.PI * k * n / this.fftSize;
        real += windowed[n] * Math.cos(angle);
        imag += windowed[n] * Math.sin(angle);
      }
      const magnitude = Math.sqrt(real * real + imag * imag);
      
      if (k < cutoffBin) {
        lowEnergy += magnitude * magnitude;
      } else {
        highEnergy += magnitude * magnitude;
      }
    }
    
    return lowEnergy > 0 ? highEnergy / lowEnergy : 0;
  }

  scoreHFLFRatio(ratio) {
    // Speech typically has more low-frequency energy
    // Good ratio is between 0.1 and 0.5
    if (ratio >= 0.1 && ratio <= 0.5) {
      return 1.0;
    } else if (ratio < 0.1) {
      return ratio / 0.1;
    } else {
      return Math.max(0, 1.0 - (ratio - 0.5) / 0.5);
    }
  }

  createHannWindow(size) {
    const window = new Float32Array(size);
    for (let i = 0; i < size; i++) {
      window[i] = 0.5 - 0.5 * Math.cos((2 * Math.PI * i) / (size - 1));
    }
    return window;
  }
}

registerProcessor('vad-processor', VADProcessorWorklet);