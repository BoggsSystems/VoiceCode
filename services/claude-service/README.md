# VoiceCode Claude Service

Claude AI integration service for the VoiceCode platform, featuring intelligent response interpretation for voice interactions.

## Features

- Integration with Claude API for code generation, explanation, fixing, and refactoring
- Voice-optimized response interpretation using GPT-3.5
- Multiple personality profiles for different interaction styles
- Intelligent caching to reduce API calls
- Rate limiting for cost control
- Comprehensive prompt templates for different code operations
- Token counting and management
- Health monitoring and diagnostics

## Key Components

### 1. Claude Integration
- Direct integration with Anthropic's Claude API
- Support for multiple operations: generate, explain, fix, refactor, review, test
- Retry policies for reliability
- Response caching for efficiency

### 2. Voice Response Interpreter
- Converts verbose technical responses to concise voice feedback
- Multiple personality profiles:
  - **Friendly Assistant**: Warm and encouraging
  - **Professional CoPilot**: Technical and direct
  - **Casual Buddy**: Laid-back and conversational
  - **Minimalist**: Ultra-concise responses
- Progress tracking and status updates
- Error summarization

### 3. Prompt Engineering
- Specialized prompts for each operation type
- Context-aware prompt building
- Language-specific optimizations

## Prerequisites

- .NET 8 SDK
- Claude API key from Anthropic
- OpenAI API key (for response interpretation)
- Redis instance
- Azure AD configuration

## Configuration

### Development

1. Set up user secrets:
```bash
dotnet user-secrets init
dotnet user-secrets set "Claude:ApiKey" "your-claude-key"
dotnet user-secrets set "ResponseInterpreter:ApiKey" "your-openai-key"
```

2. Start Redis locally:
```bash
docker run -d -p 6379:6379 redis:latest
```

### Production

Configure the following environment variables:

- `Claude__ApiKey`: Your Claude API key
- `Claude__Model`: Claude model to use (default: claude-3-opus-20240229)
- `ResponseInterpreter__ApiKey`: OpenAI API key for voice interpretation
- `ConnectionStrings__Redis`: Redis connection string
- `ApplicationInsights__ConnectionString`: Application Insights connection

## API Endpoints

### POST /api/codegeneration/generate
Generate code based on instructions.

**Request:**
```json
{
  "type": "generate",
  "instructions": "Create a user authentication service",
  "language": "csharp",
  "context": {
    "voice_response": true,
    "personality": "FriendlyAssistant"
  }
}
```

**Response:**
```json
{
  "id": "unique-id",
  "code": "generated code...",
  "explanation": "explanation...",
  "voiceResponse": {
    "text": "Created your authentication service with login and registration methods!",
    "metadata": {
      "filesCreated": 3,
      "isComplete": true
    }
  }
}
```

### POST /api/codegeneration/explain
Get explanation for code.

### POST /api/codegeneration/fix
Fix code errors.

### POST /api/codegeneration/refactor
Refactor code based on instructions.

### POST /api/codegeneration/voice/interpret
Convert technical response to voice-friendly format.

### GET /api/codegeneration/personality
Get available personality profiles.

## Voice Response Examples

### Technical Response:
"I've created a comprehensive user authentication service with the following components:
1. UserService.cs - Contains methods for user registration, login, and profile management
2. IUserRepository.cs - Interface defining data access methods
3. UserRepository.cs - Implementation using Entity Framework Core
..."

### Voice Response (Friendly Assistant):
"Great! I've created your authentication service with 3 files. It handles login, registration, and user profiles."

### Voice Response (Minimalist):
"Auth service created. 3 files."

## Rate Limiting

Default limits per minute:
- Generate: 20 requests
- Explain: 50 requests
- Fix: 30 requests
- Refactor: 30 requests

## Running Locally

```bash
cd services/claude-service
dotnet run
```

The service will be available at https://localhost:7002 (or http://localhost:5002).

## Testing

```bash
dotnet test
```

## Docker

Build the image:
```bash
docker build -f services/claude-service/Dockerfile -t voicecode-claude-service .
```

Run the container:
```bash
docker run -d -p 80:80 \
  -e Claude__ApiKey="your-claude-key" \
  -e ResponseInterpreter__ApiKey="your-openai-key" \
  -e ConnectionStrings__Redis="redis-connection" \
  voicecode-claude-service
```

## Monitoring

- Structured logging with Serilog
- Application Insights integration
- Health checks for Claude API and Redis
- Request/response logging
- Token usage tracking

## Security

- Azure AD authentication required
- API keys stored securely in Key Vault
- Rate limiting to prevent abuse
- Request validation

## Performance Optimizations

- Response caching with Redis
- Parallel processing where possible
- Token estimation before API calls
- Automatic response truncation for token limits