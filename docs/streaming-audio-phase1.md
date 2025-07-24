# Phase 1: WebSocket Audio Streaming Implementation

## Overview

This document describes the implementation of Phase 1 of the continuous audio streaming feature for VoiceCode. This phase replaces the HTTP-based audio upload with real-time WebSocket streaming.

## Architecture Changes

### Backend Components

1. **AudioStreamHub** (`/services/dispatcher-service/Hubs/AudioStreamHub.cs`)
   - New SignalR hub for handling bidirectional audio streaming
   - Manages streaming sessions with connection lifecycle
   - Processes audio chunks and sends to STT service
   - Implements heartbeat mechanism for connection health

2. **Streaming Protocol** (`/services/dispatcher-service/Models/AudioStreamMessages.cs`)
   - Defined message types: AudioData, Control, Metadata, Heartbeat, Error, Acknowledgment
   - Audio configuration: 16kHz, 16-bit, mono PCM
   - Chunk duration: 100ms (configurable)

3. **Streaming STT Service** (`/services/stt-service/Services/StreamingSpeechToTextService.cs`)
   - Continuous recognition using Azure Speech SDK
   - Maintains session state for streaming audio
   - Returns partial and final transcription results
   - Handles multiple concurrent streaming sessions

### Frontend Components

1. **StreamingAudioService** (`/webapp/src/services/streamingAudioService.ts`)
   - WebSocket client for audio streaming
   - Manages connection lifecycle and reconnection
   - Handles audio chunk transmission with sequence numbering

2. **Audio Worklet** (`/webapp/public/audio-processor.worklet.js`)
   - Low-latency audio processing in separate thread
   - Converts Web Audio API float32 samples to int16 PCM
   - Configurable chunk size for streaming

3. **StreamingVoiceRecorder** (`/webapp/src/components/Voice/StreamingVoiceRecorder`)
   - React component for streaming UI
   - Real-time audio level visualization
   - Displays partial and final transcriptions

## Configuration

### Enable Streaming Mode

1. Set environment variable in webapp:
   ```bash
   REACT_APP_ENABLE_STREAMING_AUDIO=true
   ```

2. The feature flag controls which recorder is used in the VoiceChat page.

### Audio Settings

Default configuration (can be modified in `AudioStreamConfig`):
- Sample Rate: 16000 Hz
- Channels: 1 (mono)
- Bit Depth: 16-bit
- Chunk Duration: 100ms
- Buffer Size: 2000ms
- Heartbeat Interval: 5000ms

## API Endpoints

### SignalR Hub

- **URL**: `http://localhost:5002/hubs/audiostream`
- **Authentication**: Required (Azure AD)

### Hub Methods

1. **StartAudioStream(config)** - Start a new streaming session
2. **StopAudioStream()** - End the current streaming session
3. **SendAudioChunk(messageJson)** - Send audio data or control messages

### Events

1. **StreamSessionStarted** - Session initialized with config
2. **StreamStarted** - Audio streaming active
3. **StreamStopped** - Audio streaming ended
4. **PartialTranscription** - Intermediate transcription result
5. **AudioChunkAcknowledged** - Chunk received confirmation
6. **StreamError** - Error occurred during streaming

## Testing

### Prerequisites

1. All services running:
   ```bash
   npm run start:services  # From webapp directory
   ```

2. Azure Speech Service credentials configured in STT service

### Test Steps

1. Navigate to Voice Chat page
2. Check browser console for "Stream session started" message
3. Click microphone button to start streaming
4. Speak naturally - partial transcriptions should appear
5. Click stop - final transcription displayed
6. Check network tab for WebSocket messages

### Verification Points

- WebSocket connection established to `/hubs/audiostream`
- Audio chunks sent with ~100ms intervals
- Partial transcriptions received during speech
- Final transcription after stopping
- Heartbeat messages every 5 seconds
- Proper cleanup on disconnect

## Troubleshooting

### Common Issues

1. **No audio streaming**
   - Check microphone permissions
   - Verify REACT_APP_ENABLE_STREAMING_AUDIO=true
   - Check browser console for errors

2. **No transcriptions**
   - Verify STT service is running
   - Check Azure Speech credentials
   - Ensure audio format matches (16kHz, 16-bit)

3. **Connection drops**
   - Check heartbeat messages in network tab
   - Verify SignalR reconnection attempts
   - Check server logs for errors

## Next Steps

- Phase 2: Voice Activity Detection (VAD)
- Phase 3: Streaming STT optimizations
- Phase 4: Conversation state management
- Phase 5: Full-duplex audio support

## Performance Metrics

Target metrics for Phase 1:
- Connection establishment: <1s
- Audio chunk latency: <50ms
- Partial transcription delay: <500ms
- Memory usage: <50MB
- CPU usage: <10%