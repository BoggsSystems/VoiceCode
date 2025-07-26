import toWav from 'audiobuffer-to-wav';

/**
 * Converts a WebM audio blob to WAV format
 * @param webmBlob - The WebM audio blob from MediaRecorder
 * @returns Promise<Blob> - The converted WAV audio blob
 */
export async function convertWebMToWav(webmBlob: Blob): Promise<Blob> {
  console.log('[AudioConverter] Starting WebM to WAV conversion', {
    inputSize: webmBlob.size,
    inputType: webmBlob.type
  });

  try {
    // Create an audio context
    const audioContext = new (window.AudioContext || (window as any).webkitAudioContext)();
    
    // Convert blob to array buffer
    const arrayBuffer = await webmBlob.arrayBuffer();
    
    // Decode the audio data
    const audioBuffer = await audioContext.decodeAudioData(arrayBuffer);
    
    console.log('[AudioConverter] Audio decoded', {
      duration: audioBuffer.duration,
      sampleRate: audioBuffer.sampleRate,
      numberOfChannels: audioBuffer.numberOfChannels,
      length: audioBuffer.length
    });
    
    // Analyze audio to check if it contains actual sound
    const channelData = audioBuffer.getChannelData(0);
    let maxAmplitude = 0;
    let totalAmplitude = 0;
    for (let i = 0; i < channelData.length; i++) {
      const amplitude = Math.abs(channelData[i]);
      maxAmplitude = Math.max(maxAmplitude, amplitude);
      totalAmplitude += amplitude;
    }
    const avgAmplitude = totalAmplitude / channelData.length;
    
    console.log('[AudioConverter] Audio analysis', {
      maxAmplitude: maxAmplitude.toFixed(4),
      avgAmplitude: avgAmplitude.toFixed(6),
      isSilent: maxAmplitude < 0.01,
      hasContent: maxAmplitude > 0.1
    });
    
    // Convert to WAV - ensuring we get 16kHz mono for best Speech SDK compatibility
    const targetSampleRate = 16000;
    const wavArrayBuffer = toWav(audioBuffer, { float32: false });
    
    // Create a new blob with WAV data
    const wavBlob = new Blob([wavArrayBuffer], { type: 'audio/wav' });
    
    console.log('[AudioConverter] Conversion complete', {
      outputSize: wavBlob.size,
      outputType: wavBlob.type,
      compressionRatio: (webmBlob.size / wavBlob.size).toFixed(2)
    });
    
    // Close audio context
    await audioContext.close();
    
    return wavBlob;
  } catch (error) {
    console.error('[AudioConverter] Conversion failed:', error);
    throw new Error(`Failed to convert WebM to WAV: ${error instanceof Error ? error.message : 'Unknown error'}`);
  }
}

/**
 * Creates a WAV blob from raw PCM data (alternative approach)
 * This is useful if we want to capture PCM directly
 */
export function createWavFromPCM(pcmData: Float32Array, sampleRate: number = 16000): Blob {
  // Convert Float32Array to Int16Array
  const length = pcmData.length;
  const buffer = new ArrayBuffer(44 + length * 2);
  const view = new DataView(buffer);
  
  // WAV header
  const writeString = (offset: number, string: string) => {
    for (let i = 0; i < string.length; i++) {
      view.setUint8(offset + i, string.charCodeAt(i));
    }
  };
  
  writeString(0, 'RIFF');
  view.setUint32(4, 36 + length * 2, true);
  writeString(8, 'WAVE');
  writeString(12, 'fmt ');
  view.setUint32(16, 16, true); // fmt chunk size
  view.setUint16(20, 1, true); // PCM format
  view.setUint16(22, 1, true); // Mono
  view.setUint32(24, sampleRate, true);
  view.setUint32(28, sampleRate * 2, true); // byte rate
  view.setUint16(32, 2, true); // block align
  view.setUint16(34, 16, true); // bits per sample
  writeString(36, 'data');
  view.setUint32(40, length * 2, true);
  
  // Convert float samples to 16-bit PCM
  let offset = 44;
  for (let i = 0; i < length; i++) {
    const sample = Math.max(-1, Math.min(1, pcmData[i]));
    view.setInt16(offset, sample * 0x7FFF, true);
    offset += 2;
  }
  
  return new Blob([buffer], { type: 'audio/wav' });
}