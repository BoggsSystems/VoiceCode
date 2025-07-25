export interface FeatureFlags {
  enableStreamingAudio: boolean;
  enableVAD: boolean;
  enableEnhancedTranscription: boolean;
  enableFullDuplex: boolean;
  enableNaturalConversation: boolean;
}

export const features: FeatureFlags = {
  // Phase 1: WebSocket streaming (enabled for testing)
  enableStreamingAudio: process.env.REACT_APP_ENABLE_STREAMING_AUDIO === 'true' || false,
  
  // Phase 2: Voice Activity Detection (enabled for testing)
  enableVAD: process.env.REACT_APP_ENABLE_VAD === 'true' || false,
  
  // Phase 3: Enhanced transcription display (enabled for testing)
  enableEnhancedTranscription: process.env.REACT_APP_ENABLE_ENHANCED_TRANSCRIPTION === 'true' || false,
  
  // Phase 5: Full-duplex audio (not implemented yet)
  enableFullDuplex: false,
  
  // Phase 6: Natural conversation features (not implemented yet)
  enableNaturalConversation: false
};

export const isFeatureEnabled = (feature: keyof FeatureFlags): boolean => {
  return features[feature];
};