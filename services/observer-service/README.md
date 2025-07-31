# VoiceCode Observer Service

The Observer Service monitors Claude Code SDK streaming events and generates real-time narrations for a more interactive user experience.

## Architecture

The Observer Service implements the following flow:

1. **SDK Stream Monitoring**: Subscribes to `sdk-streams` topic from Service Bus
2. **Event Processing**: Processes SDK events (file operations, code generation, errors)
3. **Narration Generation**: Uses OpenAI to generate human-friendly narrations
4. **Narration Publishing**: Publishes narration requests to `narration-tasks` queue

## Configuration

### Environment Variables

```json
{
  "Observer": {
    "MaxConcurrentStreams": 10,
    "NarrationThrottleMs": 2000,
    "IgnoredOperations": ["progress", "thinking"]
  },
  "ServiceBus": {
    "ConnectionString": "Endpoint=sb://...",
    "StreamTopicName": "sdk-streams",
    "StreamSubscriptionName": "observer",
    "NarrationQueueName": "narration-tasks"
  },
  "OpenAI": {
    "ApiKey": "your-api-key",
    "Endpoint": "https://your-resource.openai.azure.com/",
    "DeploymentName": "gpt-4"
  }
}
```

## SDK Event Types

The service processes the following event types:

- `file_read`: File reading operations
- `file_write`: File writing operations
- `file_create`: New file creation
- `file_delete`: File deletion
- `directory_create`: Directory creation
- `code_analysis`: Code analysis in progress
- `code_generation`: Code generation in progress
- `command_execution`: Command execution
- `error`: Error occurred
- `progress`: Progress updates
- `thinking`: AI thinking/processing

## Narration Examples

The Observer Service transforms technical events into user-friendly narrations:

- Technical: "Executing file write operation on index.tsx"
- Narration: "Creating your React component now."

- Technical: "Analyzing repository structure"
- Narration: "Let me look at your project setup."

- Technical: "Error in parsing syntax"
- Narration: "I found an issue. Let me fix that."

## Integration with Worker Service

The Worker Service publishes SDK stream events by:

1. Using `StreamingClaudeCodeSdkService` wrapper
2. Publishing events to `sdk-streams` topic
3. Including session and worker context

## Deployment

### Local Development

```bash
dotnet run
```

### Docker

```bash
docker build -f Dockerfile -t voicecode-observer .
docker run -p 8080:8080 voicecode-observer
```

### Azure Container Apps

```bash
./build-and-deploy.sh
```

## Health Check

The service exposes health endpoints:
- `/health` - Basic health check
- `/api/health/status` - Detailed status

## Monitoring

The service integrates with Application Insights for:
- Event processing metrics
- Narration generation latency
- Error tracking
- Custom metrics for throttling and processing rates