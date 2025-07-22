# VoiceCode Dispatcher Service

Central communication hub that manages real-time connections between clients and backend services using SignalR and Azure Service Bus.

## Features

- **Real-time Communication**: SignalR-based WebSocket connections for low-latency messaging
- **Session Management**: Tracks active user sessions with automatic cleanup
- **Service Orchestration**: Routes messages between STT, Claude, Router, Generator, and TTS services
- **Queue Processing**: Handles asynchronous message processing via Azure Service Bus
- **Redis Backplane**: Supports horizontal scaling with Redis-backed SignalR
- **Metrics & Monitoring**: Built-in metrics tracking and health checks
- **Connection Resilience**: Automatic reconnection and error handling

## Architecture

### Components

1. **VoiceHub**: SignalR hub handling real-time client connections
2. **SessionManager**: Manages user sessions and connection state
3. **QueueDispatcher**: Routes messages to appropriate Service Bus queues
4. **ServiceRouter**: Direct HTTP communication with backend services
5. **QueueProcessor**: Processes responses from backend services
6. **MetricsService**: Tracks operational metrics

### Message Flow

1. Client connects via SignalR WebSocket
2. Audio/text messages received through VoiceHub
3. Messages routed to appropriate services (STT → Router → Claude/Generator → TTS)
4. Responses delivered back to client in real-time
5. Session state maintained throughout conversation

## Prerequisites

- .NET 8 SDK
- Azure Service Bus namespace
- Redis instance
- Azure AD for authentication
- Access to all backend services

## Configuration

### Development

```bash
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:ServiceBus" "your-service-bus-connection"
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379"
```

### Production

Environment variables:
- `ConnectionStrings__ServiceBus`: Service Bus connection string
- `ConnectionStrings__Redis`: Redis connection string
- `ServiceEndpoints__STTService`: STT service URL
- `ServiceEndpoints__ClaudeService`: Claude service URL
- `ServiceEndpoints__RouterService`: Router service URL
- `ServiceEndpoints__GeneratorService`: Generator service URL
- `ServiceEndpoints__TTSService`: TTS service URL
- `ApplicationInsights__ConnectionString`: Application Insights

## SignalR Hub Methods

### Client → Server

#### ProcessAudio
Process audio data for transcription and intent.
```javascript
await connection.invoke("ProcessAudio", {
    id: "msg-123",
    audioData: audioBuffer,
    format: "wav",
    sampleRate: 16000,
    language: "en-US"
});
```

#### ProcessText
Process text input directly.
```javascript
await connection.invoke("ProcessText", {
    id: "msg-124",
    text: "Create a new React component",
    language: "en-US"
});
```

#### UpdateContext
Update session context (project info, preferences).
```javascript
await connection.invoke("UpdateContext", {
    preferences: {
        personality: "Friendly",
        outputFormat: "detailed"
    },
    currentProject: {
        name: "MyApp",
        language: "typescript",
        framework: "react"
    }
});
```

#### SendFeedback
Send user feedback about responses.
```javascript
await connection.invoke("SendFeedback", {
    messageId: "msg-123",
    type: "Positive",
    rating: 5,
    comment: "Very helpful!"
});
```

### Server → Client Events

#### SessionStarted
Fired when connection established.
```javascript
connection.on("SessionStarted", (session) => {
    console.log("Session ID:", session.id);
});
```

#### TranscriptionReceived
Audio transcription completed.
```javascript
connection.on("TranscriptionReceived", (result) => {
    console.log("Transcript:", result.transcript);
});
```

#### ClaudeResponse
Claude AI response received.
```javascript
connection.on("ClaudeResponse", (response) => {
    console.log("Claude says:", response.text);
    console.log("Code blocks:", response.codeBlocks);
});
```

#### GenerationComplete
Code generation finished.
```javascript
connection.on("GenerationComplete", (result) => {
    console.log("Generated files:", result.files);
});
```

#### AudioReady
TTS audio synthesized.
```javascript
connection.on("AudioReady", (result) => {
    console.log("Audio URL:", result.audioUrl);
});
```

#### Error
Error occurred during processing.
```javascript
connection.on("Error", (error) => {
    console.error("Error:", error.message);
});
```

## REST API Endpoints

### GET /api/metrics
Get service metrics.

### GET /api/metrics/sessions
Get session-specific metrics.

### GET /api/metrics/health
Get health metrics.

### GET /api/session/{sessionId}
Get session details.

### GET /api/session/active
List all active sessions.

### POST /api/session/{sessionId}/end
End a specific session.

### PUT /api/session/{sessionId}/context
Update session context.

## Session Management

Sessions are automatically managed with:
- Unique session ID per connection
- Automatic timeout after 30 minutes of inactivity
- Context preservation across messages
- Message history tracking
- Concurrent session limits

## Queue Processing

The service processes messages from the `dispatcher` queue with subjects:
- `claude-response`: Responses from Claude service
- `generation-complete`: Code generation results
- `synthesis-complete`: TTS audio ready
- `error`: Error notifications

## Running Locally

```bash
cd services/dispatcher-service
dotnet run
```

Service available at https://localhost:7006 (or http://localhost:5006).
SignalR hub at `/hubs/voice`.

## Testing SignalR Connection

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("https://localhost:7006/hubs/voice", {
        accessTokenFactory: () => getAccessToken()
    })
    .withAutomaticReconnect()
    .build();

await connection.start();
console.log("Connected to VoiceHub");
```

## Docker

Build:
```bash
docker build -f services/dispatcher-service/Dockerfile -t voicecode-dispatcher-service .
```

Run:
```bash
docker run -d -p 80:80 \
  -e ConnectionStrings__ServiceBus="your-connection" \
  -e ConnectionStrings__Redis="redis-connection" \
  -e ServiceEndpoints__STTService="http://stt-service" \
  voicecode-dispatcher-service
```

## Monitoring

- Health checks at `/health`
- Readiness at `/health/ready`
- Liveness at `/health/live`
- SignalR hub health monitoring
- Service dependency checks
- Application Insights integration
- Custom metrics tracking

## Security

- Azure AD authentication required
- Scoped access per connection
- Session isolation
- Message size limits
- Rate limiting per session