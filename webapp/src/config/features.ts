export interface FeatureFlags {
  enableStreamingAudio: boolean;
  enableVAD: boolean;
  enableFullDuplex: boolean;
  enableNaturalConversation: boolean;
}

export const features: FeatureFlags = {
  // Phase 1: WebSocket streaming (enabled for testing)
  enableStreamingAudio: process.env.REACT_APP_ENABLE_STREAMING_AUDIO === 'true' || false,
  
  // Phase 2: Voice Activity Detection (not implemented yet)
  enableVAD: false,
  
  // Phase 5: Full-duplex audio (not implemented yet)
  enableFullDuplex: false,
  
  // Phase 6: Natural conversation features (not implemented yet)
  enableNaturalConversation: false
};

export const isFeatureEnabled = (feature: keyof FeatureFlags): boolean => {
  return features[feature];
};