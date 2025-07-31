# Claude Code SDK Integration Guide

## Overview

The VoiceCode platform now fully integrates the Claude Code SDK for enhanced code generation capabilities with real-time streaming feedback.

## Architecture Flow

```
Voice Command → Worker Service → Claude Code SDK → Stream Events → Observer → Narrations → TTS
                                       ↓
                                 Code Operations
```

## Key Components

### 1. Worker Service Integration

The `ClaudeCodeWorkerService` now supports two execution modes:

- **SDK Mode**: For complex code generation tasks (create, build, implement, etc.)
- **API Mode**: For simpler tasks or when SDK is unavailable

#### Routing Logic

Commands are routed to SDK when they contain keywords like:
- create
- build
- implement
- add
- update
- refactor
- generate
- scaffold

Or when explicitly requested via `useSdk: true` parameter.

### 2. Streaming Architecture

```
StreamingClaudeCodeSdkService
    ↓
Publishes to 'sdk-stream-events' queue
    ↓
Observer Service consumes messages
    ↓
Generates narrations via OpenAI
    ↓
Sends to 'tts-requests' queue
    ↓
TTS Service generates audio
```

### 3. Session Management

- Sessions are created per user/workspace
- Context persists across multiple voice commands
- Sessions are cleaned up when workspace is destroyed

## Configuration

### Worker Service (`appsettings.json`)

```json
{
  "Worker": {
    "StreamQueueName": "sdk-stream-events",
    "EnableDebugLogging": false
  },
  "ClaudeCodeSdk": {
    "Enabled": true,
    "UsePythonSdk": false,
    "ApiKey": "",
    "MaxTurns": 5,
    "DefaultTimeout": 120,
    "WorkspacePath": "/workspace",
    "EnableMockMode": false,
    "AllowedTools": ["Read", "Write", "Edit", "Bash", "Glob", "Grep"]
  }
}
```

### Observer Service (`appsettings.json`)

```json
{
  "Observer": {
    "MaxConcurrentStreams": 10,
    "NarrationThrottleMs": 2000,
    "IgnoredOperations": ["progress", "thinking"]
  },
  "ServiceBus": {
    "StreamQueueName": "sdk-stream-events",
    "NarrationQueueName": "tts-requests"
  }
}
```

## Stream Event Types

The SDK publishes various event types:

- `file_read`: Reading existing files
- `file_write`: Writing to files
- `file_create`: Creating new files
- `code_analysis`: Analyzing code structure
- `code_generation`: Generating new code
- `error`: Error occurred
- `progress`: Progress updates

## Narration Examples

The Observer Service transforms technical events into friendly narrations:

| Event | Technical Message | Narration |
|-------|------------------|-----------|
| file_create | "Creating file: components/Login.tsx" | "Creating your login component now." |
| code_analysis | "Analyzing repository structure" | "Let me look at your project setup." |
| error | "Syntax error in line 42" | "I found an issue. Let me fix that." |

## Testing

### 1. Test SDK Availability

```bash
curl http://localhost:5000/api/claudecodesdk/health
```

### 2. Test Voice Command with SDK

```bash
curl -X POST http://localhost:5000/api/worker/test \
  -H "Content-Type: application/json" \
  -d '{
    "taskId": "test-123",
    "command": "Create a React login component",
    "sessionId": "session-123"
  }'
```

### 3. Monitor SDK Streams

Use Azure Service Bus Explorer to monitor:
- Topic: `sdk-streams`
- Subscription: `observer`

### 4. Check Narration Flow

Monitor the `tts-requests` queue for narration messages.

## Deployment

### Infrastructure Setup

1. Create Service Bus resources:
```bash
./infrastructure/scripts/create-observer-resources.sh
```

2. Deploy Observer Service:
```bash
cd services/observer-service
./build-and-deploy.sh
```

3. Update Worker Service to enable SDK:
- Set `ClaudeCodeSdk__Enabled=true`
- Configure `ClaudeCodeSdk__ApiKey`

## Troubleshooting

### SDK Not Available

- Check if Claude Code SDK is installed
- Verify API key is configured
- Check SDK mock mode setting

### No Stream Events

- Verify Service Bus connection
- Check StreamTopicName configuration
- Ensure Observer Service is running

### No Narrations

- Check OpenAI configuration
- Verify TTS queue is accessible
- Check narration throttling settings

## Benefits

1. **Real-time Feedback**: Users hear progress as code is generated
2. **Better Context**: SDK maintains conversation context
3. **Structured Output**: SDK provides organized file operations
4. **Error Recovery**: Better error handling and retry logic
5. **Token Tracking**: Monitor API usage and costs

## Future Enhancements

1. **Custom Tools**: Add project-specific tools to SDK
2. **Multi-turn Conversations**: Enhanced context management
3. **Workspace Templates**: Pre-configured environments
4. **Advanced Narrations**: More contextual descriptions