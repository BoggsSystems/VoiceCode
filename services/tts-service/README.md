# VoiceCode TTS Service

Text-to-Speech service that converts text responses into natural-sounding speech with personality-aware voice synthesis.

## Features

- **Azure Cognitive Services Integration**: Leverages Azure Speech Services for high-quality synthesis
- **Multiple Voice Personalities**: Friendly, Professional, Casual, and Minimalist profiles
- **SSML Support**: Rich speech synthesis markup for natural-sounding output
- **Emotion Support**: Dynamic emotion expression in synthesized speech
- **Audio Caching**: Redis-based caching for frequently used phrases
- **Audio Storage**: Azure Blob Storage for persistent audio files
- **Streaming Synthesis**: Real-time audio streaming for low-latency responses
- **Voice Customization**: Configurable prosody settings (rate, pitch, volume)

## Architecture

### Components

1. **TextToSpeechService**: Core synthesis engine with retry policies
2. **VoicePersonalityService**: Maps personalities to voice configurations
3. **SSMLBuilderService**: Constructs SSML for natural speech patterns
4. **AudioStorageService**: Manages audio file storage in Azure Blob
5. **RedisCacheService**: Caches synthesized audio for performance

### Voice Profiles

- **Friendly**: Warm, approachable tone with slight pitch increase
- **Professional**: Clear, businesslike delivery
- **Casual**: Relaxed, conversational style
- **Minimalist**: Brief, calm responses with reduced prosody

## Prerequisites

- .NET 8 SDK
- Azure Speech Services subscription
- Azure Storage account
- Redis instance
- Azure AD for authentication

## Configuration

### Development

```bash
dotnet user-secrets init
dotnet user-secrets set "AzureSpeech:Key" "your-speech-key"
dotnet user-secrets set "AzureSpeech:Region" "your-region"
dotnet user-secrets set "ConnectionStrings:Storage" "your-storage-connection"
```

### Production

Environment variables:
- `AzureSpeech__Key`: Azure Speech Services key
- `AzureSpeech__Region`: Azure region (e.g., eastus)
- `ConnectionStrings__Redis`: Redis connection string
- `ConnectionStrings__Storage`: Azure Storage connection
- `ApplicationInsights__ConnectionString`: Application Insights

## API Endpoints

### POST /api/speech/synthesize
Synthesize text to speech with options.

**Request:**
```json
{
  "text": "Hello, I've found 3 errors in your code",
  "voiceProfile": "friendly",
  "emotion": "concerned",
  "storeAudio": true,
  "returnAudioData": true,
  "sessionId": "session-123"
}
```

**Response:**
```json
{
  "id": "synth-123",
  "audioData": "base64-encoded-audio",
  "audioUrl": "https://storage.blob.core.windows.net/audio/file.mp3",
  "format": "audio16khz32kbitratemonopm3",
  "duration": "00:00:02.5",
  "voiceUsed": "en-US-JennyNeural",
  "sessionId": "session-123"
}
```

### POST /api/speech/synthesize/personality
Synthesize with specific personality profile.

### POST /api/speech/synthesize/stream
Stream synthesized audio in real-time.

### GET /api/speech/audio/{sessionId}
List all audio files for a session.

### DELETE /api/speech/audio?audioUrl={url}
Delete a stored audio file.

### GET /api/speech/voices
Get available voice profiles.

### GET /api/speech/personalities
Get personality descriptions.

## SSML Features

The service automatically enhances speech with:
- Smart pauses after punctuation
- Emphasis on important words
- Natural prosody adjustments
- Emotion expression
- Speaking style variations

Example SSML generation:
```xml
<speak version="1.0" xml:lang="en-US">
  <voice name="en-US-JennyNeural">
    <mstts:express-as style="friendly" styledegree="1.2">
      <prosody rate="1.05" pitch="+5%" volume="105">
        I've completed the task.<break time="300ms"/>
        Three files were updated successfully.
      </prosody>
    </mstts:express-as>
  </voice>
</speak>
```

## Voice Selection

Voices are selected based on:
1. Personality profile mapping
2. Language requirements
3. Emotion expression capabilities
4. Gender preferences
5. Speaking style support

## Performance Optimization

- **Caching**: Frequently used phrases cached for 24 hours
- **Connection Pooling**: Reuses Speech SDK connections
- **Concurrent Synthesis**: Supports up to 50 concurrent operations
- **Audio Compression**: Optimized formats for different use cases

## Running Locally

```bash
cd services/tts-service
dotnet run
```

Service available at https://localhost:7005 (or http://localhost:5005).

## Testing

```bash
dotnet test
```

## Docker

Build:
```bash
docker build -f services/tts-service/Dockerfile -t voicecode-tts-service .
```

Run:
```bash
docker run -d -p 80:80 \
  -e AzureSpeech__Key="your-key" \
  -e AzureSpeech__Region="your-region" \
  -e ConnectionStrings__Redis="redis-connection" \
  -e ConnectionStrings__Storage="storage-connection" \
  voicecode-tts-service
```

## Monitoring

- Health checks at `/health`
- Readiness at `/health/ready`
- Liveness at `/health/live`
- Application Insights integration
- Structured logging with Serilog

## Security

- Azure AD authentication required
- Scoped access to speech synthesis
- Secure audio storage with SAS tokens
- No PII logging