# VoiceCode Router Service

Intelligent prompt routing and intent classification service for the VoiceCode platform.

## Features

- **Intent Classification**: Multi-strategy intent detection using ML models, pattern matching, and Azure Text Analytics
- **Context Management**: Maintains conversation context and user preferences
- **Smart Routing**: Routes requests to appropriate processing queues based on intent
- **Prompt Enhancement**: Enriches prompts with context and user preferences
- **Background ML Training**: Continuously improves intent classification accuracy
- **High Performance**: Redis caching and efficient queue management

## Architecture

### Intent Classification Pipeline
1. **ML Model Classification** (if enabled and confidence > threshold)
2. **Pattern-Based Classification** (regex and keyword matching)
3. **Azure Text Analytics** (if configured)
4. **Fallback Classification** (simple keyword matching)

### Supported Intent Categories
- **Code Generation**: Creating new code components
- **Code Explanation**: Understanding existing code
- **Error Fixing**: Debugging and fixing issues
- **Code Refactoring**: Improving code quality
- **Testing**: Generating test cases
- **Documentation**: Adding code documentation
- **Project Management**: Project-level operations
- **System Commands**: System-level actions

## Prerequisites

- .NET 8 SDK
- Azure Service Bus namespace
- Redis instance
- (Optional) Azure Text Analytics resource
- (Optional) Pre-trained ML model

## Configuration

### Development

1. Set up user secrets:
```bash
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:ServiceBus" "your-service-bus-connection"
dotnet user-secrets set "IntentClassification:TextAnalyticsKey" "your-text-analytics-key"
```

2. Start Redis locally:
```bash
docker run -d -p 6379:6379 redis:latest
```

3. Use Azure Service Bus emulator or create a development namespace

### Production

Configure the following environment variables:

- `ConnectionStrings__ServiceBus`: Azure Service Bus connection string
- `ConnectionStrings__Redis`: Redis connection string
- `IntentClassification__TextAnalyticsEndpoint`: Text Analytics endpoint
- `IntentClassification__TextAnalyticsKey`: Text Analytics key
- `ApplicationInsights__ConnectionString`: Application Insights connection

## API Endpoints

### POST /api/router/route
Route a voice command request to the appropriate processing queue.

**Request:**
```json
{
  "transcript": "Create a user authentication service",
  "userId": "user123",
  "sessionId": "session456"
}
```

**Response:**
```json
{
  "route": {
    "queueName": "code-generation",
    "subject": "generate"
  },
  "intent": {
    "type": "generate_code",
    "category": "CodeGeneration",
    "confidence": 0.92
  },
  "messageId": "msg-123",
  "enhancedPrompt": "Enhanced prompt with context..."
}
```

### POST /api/router/classify
Classify the intent of a text without routing.

**Request:**
```json
{
  "text": "Explain how this function works",
  "sessionId": "session456"
}
```

### POST /api/router/analyze
Analyze text for multiple intents.

### GET /api/router/context/{userId}/{sessionId}
Get user context and conversation history.

### DELETE /api/router/context/{userId}/{sessionId}
Clear user context.

### GET /api/router/intents
Get list of supported intents with examples.

## ML Model Training

The service includes a background service that trains the intent classification model daily. To manually train:

1. Prepare training data in the format:
```csv
text,label
"create a user service",generate_code
"explain this function",explain_code
```

2. Place training data in the configured location
3. The model will be automatically trained and evaluated
4. Models with >80% accuracy are saved for use

## Queue Configuration

Default queue mappings:
- `generate_code` → `code-generation` queue
- `explain_code` → `code-generation` queue
- `fix_error` → `code-generation` queue (high priority)
- `refactor_code` → `code-generation` queue
- `create_tests` → `code-generation` queue
- `document_code` → `code-generation` queue

## Context Enhancement

The service enhances prompts with:
1. **Recent Interactions**: Relevant previous commands and results
2. **User Preferences**: Language, coding style, formatting preferences
3. **Session Context**: Current component, active errors, project info
4. **Intent-Specific Instructions**: Tailored guidance for each operation type

## Running Locally

```bash
cd services/router-service
dotnet run
```

The service will be available at https://localhost:7003 (or http://localhost:5003).

## Testing

```bash
dotnet test
```

## Docker

Build the image:
```bash
docker build -f services/router-service/Dockerfile -t voicecode-router-service .
```

Run the container:
```bash
docker run -d -p 80:80 \
  -e ConnectionStrings__ServiceBus="your-connection" \
  -e ConnectionStrings__Redis="redis-connection" \
  voicecode-router-service
```

## Monitoring

- Structured logging with Serilog
- Application Insights integration
- Health checks for Redis and Service Bus
- Intent classification metrics
- Queue routing metrics

## Performance Tuning

- **Cache TTL**: Context cached for 30 minutes by default
- **ML Model**: Disable for lower latency if pattern matching suffices
- **Text Analytics**: Optional enhancement, can be disabled
- **Queue Batching**: Supports batch message sending