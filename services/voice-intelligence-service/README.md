# Voice Intelligence Service

## Overview

The Voice Intelligence Service is an AI-powered service that synthesizes technical outputs from multiple Claude Code workers into natural, conversational voice responses. It uses GPT-4 Turbo to transform verbose, screen-oriented outputs into concise voice-friendly responses.

## Key Features

- **Multi-Worker Result Synthesis**: Aggregates and analyzes results from multiple workers
- **Voice-Optimized Responses**: Converts technical output to natural speech patterns
- **GPT-4 Turbo Integration**: Fast, intelligent response generation
- **Service Bus Integration**: Listens to worker-results queue
- **TTS Pipeline Integration**: Sends synthesized responses to text-to-speech

## Architecture

```
Worker Results Queue → Voice Intelligence Service → TTS Queue
                              ↓
                      GPT-4 Turbo Synthesis
                              ↓
                    Natural Voice Response
```

## Configuration

### Environment Variables

- `AZURE_SERVICE_BUS_CONNECTION_STRING`: Connection string for Azure Service Bus
- `OPENAI_API_KEY`: OpenAI API key for GPT-4 Turbo

### Settings

Configure in `appsettings.json`:
- ServiceBus connection settings
- OpenAI model preferences

## Development

### Build
```bash
dotnet build
```

### Run Locally
```bash
dotnet run
```

### Docker Build
```bash
docker build -f Dockerfile -t voice-intelligence-service .
```

## Deployment

Deploy to Azure Container Apps:
```bash
./infrastructure/scripts/deploy-voice-intelligence.sh
```

## API Endpoints

- `GET /api/status` - Service health check
- `POST /api/status/test-synthesis` - Test synthesis endpoint

## Message Flow

1. Worker completes task and sends result to `worker-results` queue
2. Voice Intelligence Service picks up the result
3. Aggregates results by task ID
4. Sends to GPT-4 Turbo for synthesis
5. Receives natural language response
6. Forwards to TTS queue for voice generation