# VoiceCode STT Service

Speech-to-Text service for the VoiceCode platform, built with .NET 8 and Azure Cognitive Services Speech SDK.

## Features

- High-accuracy speech transcription using Azure Speech Services
- Support for 30+ languages
- Real-time and batch transcription
- Word-level timing information
- Confidence scores and alternatives
- Audio file storage and retrieval
- Redis caching for improved performance
- Health checks and monitoring

## Prerequisites

- .NET 8 SDK
- Azure Speech Services subscription
- Redis instance (local or Azure Cache for Redis)
- Azure Storage Account

## Configuration

### Development

1. Set up user secrets:
```bash
dotnet user-secrets init
dotnet user-secrets set "AzureSpeech:Key" "your-speech-key"
dotnet user-secrets set "ConnectionStrings:Storage" "your-storage-connection"
```

2. Start Redis locally:
```bash
docker run -d -p 6379:6379 redis:latest
```

3. Use Azurite for local storage emulation:
```bash
npm install -g azurite
azurite --silent --location azurite --debug azurite/debug.log
```

### Production

Configure the following environment variables or Azure Key Vault secrets:

- `AzureSpeech__Key`: Azure Speech Services key
- `AzureSpeech__Region`: Azure region (e.g., "eastus")
- `ConnectionStrings__Redis`: Redis connection string
- `ConnectionStrings__Storage`: Azure Storage connection string
- `ApplicationInsights__ConnectionString`: Application Insights connection

## API Endpoints

### POST /api/transcription/transcribe
Transcribe audio file to text.

**Request:**
- Form data with audio file
- Optional language parameter (default: "en-US")

**Response:**
```json
{
  "id": "request-id",
  "transcript": "Transcribed text",
  "confidence": 0.95,
  "language": "en-US",
  "durationMs": 1500,
  "words": [...],
  "alternatives": [...]
}
```

### POST /api/transcription/transcribe-stream
Transcribe base64-encoded audio data.

**Request:**
```json
{
  "audioData": "base64-encoded-audio",
  "language": "en-US"
}
```

### GET /api/transcription/languages
Get list of supported languages.

### GET /health
Health check endpoint.

## Running Locally

```bash
cd services/stt-service
dotnet run
```

The service will be available at https://localhost:7001 (or http://localhost:5001).

## Testing

```bash
dotnet test
```

## Docker

Build the image:
```bash
docker build -f services/stt-service/Dockerfile -t voicecode-stt-service .
```

Run the container:
```bash
docker run -d -p 80:80 \
  -e AzureSpeech__Key="your-key" \
  -e AzureSpeech__Region="eastus" \
  -e ConnectionStrings__Redis="redis-connection" \
  -e ConnectionStrings__Storage="storage-connection" \
  voicecode-stt-service
```

## Monitoring

The service includes:
- Structured logging with Serilog
- Application Insights integration
- Health checks for dependencies
- Request/response logging middleware
- Performance metrics

## Security

- Azure AD authentication required for all endpoints
- Request size limits (10MB)
- Audio files stored securely in Azure Blob Storage
- Sensitive data never logged

## Performance

- Redis caching for repeated transcriptions
- Connection pooling for Azure services
- Retry policies for transient failures
- Configurable concurrent recognition limits