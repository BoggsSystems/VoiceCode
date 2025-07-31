# Claude Agent - File System Tool-Based AI Assistant

A containerized Claude agent that can explore and modify local project folders using Anthropic's tool use API.

## Features

- **File System Access**: Read, write, search, and manipulate files in a mounted project directory
- **Tool-Based Interaction**: Uses Claude's tool use capability for structured file operations
- **Project Analysis**: Automatically detect project type, dependencies, and structure
- **Safe Execution**: Sandboxed environment with path traversal protection
- **Real-time Updates**: Publishes tool execution events to Azure Service Bus

## Available Tools

1. **list_dir** - List files and directories
2. **read_file** - Read file contents
3. **write_file** - Create or overwrite files
4. **append_file** - Append to existing files
5. **delete_file** - Delete files
6. **move_file** - Move or rename files
7. **create_directory** - Create directories
8. **search_codebase** - Search for text across files
9. **get_project_summary** - Get project overview
10. **get_file_tree** - Get directory structure
11. **run_shell** - Run safe shell commands (read-only)

## Quick Start

### 1. Build the Docker Image

```bash
docker build -t claude-agent .
```

### 2. Run with a Mounted Project

```bash
docker run -v $(pwd)/my-app:/project -p 3000:3000 \
  -e ANTHROPIC_API_KEY=your-api-key \
  claude-agent
```

### 3. Send a Task

```bash
curl -X POST http://localhost:3000/task \
  -H "Content-Type: application/json" \
  -d '{
    "message": "Analyze this codebase and tell me what it does. What is the main entry point?"
  }'
```

## Environment Variables

- `ANTHROPIC_API_KEY` - Your Anthropic API key (required)
- `CLAUDE_MODEL` - Model to use (default: claude-3-opus-20240229)
- `CLAUDE_MAX_TOKENS` - Max tokens for response (default: 4096)
- `CLAUDE_TEMPERATURE` - Temperature for responses (default: 0)
- `AZURE_SERVICE_BUS_CONNECTION_STRING` - For event publishing (optional)
- `SDK_STREAM_QUEUE_NAME` - Service Bus queue name (default: sdk-stream-events)
- `LOG_LEVEL` - Logging level (default: info)
- `NODE_ENV` - Environment (development/production)
- `PORT` - Server port (default: 3000)

## API Endpoints

### POST /task
Execute a natural language task.

```json
{
  "message": "Create a new React component called Button",
  "sessionId": "optional-session-id"
}
```

Response:
```json
{
  "success": true,
  "response": "I've created a new React component...",
  "toolCalls": [
    {
      "tool": "write_file",
      "input": { "path": "src/components/Button.jsx", "contents": "..." },
      "output": { "success": true, "data": "File written: src/components/Button.jsx" }
    }
  ],
  "usage": {
    "inputTokens": 1234,
    "outputTokens": 567
  }
}
```

### POST /tool/:toolName
Execute a specific tool directly (for testing).

```bash
curl -X POST http://localhost:3000/tool/read_file \
  -H "Content-Type: application/json" \
  -d '{"path": "package.json"}'
```

### GET /health
Health check endpoint.

### GET /
Service information and available tools.

## Docker Compose Example

```yaml
version: '3.8'

services:
  claude-agent:
    build: .
    volumes:
      - ./my-project:/project:ro  # Mount as read-only for safety
    ports:
      - "3000:3000"
    environment:
      - ANTHROPIC_API_KEY=${ANTHROPIC_API_KEY}
      - AZURE_SERVICE_BUS_CONNECTION_STRING=${AZURE_SERVICE_BUS_CONNECTION_STRING}
```

## Integration with Worker Service

This agent is designed to work with the VoiceCode Worker Service:

1. Worker Service receives a task
2. Worker Service calls the agent's `/task` endpoint
3. Agent uses Claude to analyze/modify the codebase
4. Agent publishes events to Service Bus for real-time updates
5. Observer Service generates narrations from the events

## Security Considerations

- Path traversal protection prevents access outside `/project`
- Shell commands are restricted to safe, read-only operations
- Runs as non-root user in container
- Can mount project as read-only if needed

## Development

```bash
# Install dependencies
npm install

# Run in development mode
npm run dev

# Run tests
npm test

# Build
npm run build
```

## Examples

### Analyze a Codebase
```bash
curl -X POST http://localhost:3000/task \
  -H "Content-Type: application/json" \
  -d '{
    "message": "Analyze the architecture of this codebase. What are the main components and how do they interact?"
  }'
```

### Refactor Code
```bash
curl -X POST http://localhost:3000/task \
  -H "Content-Type: application/json" \
  -d '{
    "message": "Refactor the UserService class to use dependency injection"
  }'
```

### Add a Feature
```bash
curl -X POST http://localhost:3000/task \
  -H "Content-Type: application/json" \
  -d '{
    "message": "Add error logging to all API endpoints"
  }'
```

### Generate Documentation
```bash
curl -X POST http://localhost:3000/task \
  -H "Content-Type: application/json" \
  -d '{
    "message": "Generate a README.md file that documents this project"
  }'
```