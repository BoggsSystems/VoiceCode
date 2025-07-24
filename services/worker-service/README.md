# VoiceCode Worker Service

The Worker Service provides Claude Code capabilities through the Model Context Protocol (MCP), enabling multiple AI-powered workers to execute software engineering tasks in parallel.

## Overview

This service wraps Claude Code CLI as an MCP server, providing:
- File system operations (read, write, edit, search)
- Command execution (build, test, lint)
- Code generation and refactoring
- Workspace isolation for concurrent operations
- Task queue processing via Azure Service Bus

## Architecture

```
Orchestrator Service
       ↓
Service Bus Queue
       ↓
Worker Service (this)
       ↓
MCP Client → Claude Code (MCP Server)
       ↓
Isolated Workspace
```

## Key Components

### MCP Client Service
- Manages connection to Claude Code MCP server
- Handles JSON-RPC communication
- Provides tool discovery and invocation

### Claude Code Worker Service
- Executes tasks (code generation, refactoring, testing)
- Manages workspace lifecycle
- Tracks worker status and metrics

### Queue Processor
- Listens to Service Bus for tasks
- Processes tasks concurrently
- Sends results to response queue

## Configuration

```json
{
  "Worker": {
    "ClaudeApiKey": "sk-ant-...",
    "WorkspaceBasePath": "/workspaces",
    "MaxConcurrentWorkers": 5
  },
  "Mcp": {
    "TransportType": "stdio",
    "ServerExecutable": "claude-code",
    "ServerArguments": ["api", "--mode", "mcp-server"]
  }
}
```

## API Endpoints

- `GET /api/worker/status` - Get worker status
- `POST /api/worker/execute` - Execute a task directly
- `GET /api/worker/tools` - List available MCP tools
- `POST /api/worker/workspace/{id}/init` - Initialize workspace
- `DELETE /api/worker/workspace/{id}` - Cleanup workspace
- `GET /health` - Health check

## Task Types

1. **Code Generation**
   - Generate new code based on prompts
   - Support multiple languages
   - Write to specified files

2. **Refactoring**
   - Read existing code
   - Apply refactoring patterns
   - Update files in place

3. **Testing**
   - Run test commands
   - Capture output and results
   - Support various test frameworks

4. **File Operations**
   - Read, write, search files
   - Navigate project structure
   - Manage dependencies

5. **Command Execution**
   - Run build commands
   - Execute linters
   - Custom shell commands

## Deployment

### Local Development
```bash
dotnet run
```

### Docker
```bash
docker build -t voicecode-worker-service .
docker run -p 5006:80 voicecode-worker-service
```

### Azure Container Apps
- Scales from 0 to N based on queue depth
- Automatic workspace cleanup
- Integrated with Service Bus

## Security

- Workspace isolation per task
- Resource limits (CPU, memory, timeout)
- Sandboxed command execution
- Path validation to prevent traversal

## Monitoring

- Application Insights integration
- Structured logging with Serilog
- Task execution metrics
- Worker health monitoring