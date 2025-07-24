# MCP Worker Testing Guide

This guide covers testing the MCP (Model Context Protocol) integration with Claude Code workers in VoiceCode.

## Prerequisites

1. **Install Claude Code CLI**
   ```bash
   curl -fsSL https://storage.googleapis.com/anthropic-public/claude-code/install.sh | sh
   ```

2. **Set API Key**
   ```bash
   export ANTHROPIC_API_KEY=your-api-key
   ```

3. **Install .NET 8 SDK**
   - Download from https://dotnet.microsoft.com/download/dotnet/8.0

4. **Azure Service Bus (Optional)**
   - For queue-based task distribution
   - Set connection string: `export AZURE_SERVICE_BUS_CONNECTION_STRING=...`

## Local Testing

### 1. Test MCP Connection Directly

```bash
# Test Claude Code MCP server mode
claude-code api --mode mcp-server

# In another terminal, send a test request
echo '{"jsonrpc":"2.0","id":"1","method":"initialize","params":{"protocolVersion":"1.0"}}' | claude-code api --mode mcp-server
```

### 2. Run Worker Service Locally

```bash
cd services/worker-service
dotnet run
```

The service will start on http://localhost:5006

### 3. Test Worker Endpoints

```bash
# Check health
curl http://localhost:5006/health

# Get available tools
curl http://localhost:5006/api/worker/tools

# Execute a simple task
curl -X POST http://localhost:5006/api/worker/execute \
  -H "Content-Type: application/json" \
  -d '{
    "type": "file_operation",
    "description": "Read a file",
    "workspaceId": "test",
    "parameters": {
      "operation": "read",
      "path": "/tmp/test.txt"
    }
  }'
```

### 4. Run Orchestrator with Worker

```bash
# Terminal 1: Start worker
cd services/worker-service
dotnet run

# Terminal 2: Start orchestrator
cd services/orchestrator-service
dotnet run

# Terminal 3: Test orchestration
curl -X POST http://localhost:5010/api/orchestration/submit-task \
  -H "Content-Type: application/json" \
  -d '{
    "requestType": "code_generation",
    "userPrompt": "Create a simple TypeScript function to calculate fibonacci",
    "sessionId": "test-session"
  }'
```

## Docker Testing

### 1. Build and Run with Docker Compose

```bash
# Build images
docker-compose -f docker-compose.mcp.yml build

# Start services
docker-compose -f docker-compose.mcp.yml up

# In another terminal, test the services
./scripts/test-mcp-worker.sh
```

### 2. Scale Workers

```bash
# Scale to 3 workers
docker-compose -f docker-compose.mcp.yml up --scale worker=3
```

## Integration Testing

### 1. Test File Operations

```python
import requests
import json

# Create a file
task = {
    "type": "file_operation",
    "description": "Create test file",
    "workspaceId": "integration-test",
    "parameters": {
        "operation": "write",
        "path": "/workspace/test.py",
        "content": "def hello():\n    return 'Hello, MCP!'"
    }
}

response = requests.post("http://localhost:5006/api/worker/execute", json=task)
print(json.dumps(response.json(), indent=2))
```

### 2. Test Code Generation

```python
# Generate code
task = {
    "type": "code_generation",
    "description": "Generate a REST API",
    "workspaceId": "integration-test",
    "parameters": {
        "prompt": "Create a simple Express.js REST API for a todo list",
        "language": "javascript",
        "filePath": "/workspace/api.js"
    }
}

response = requests.post("http://localhost:5006/api/worker/execute", json=task)
print(json.dumps(response.json(), indent=2))
```

### 3. Test Command Execution

```python
# Run tests
task = {
    "type": "command_execution",
    "description": "Run npm test",
    "workspaceId": "integration-test",
    "parameters": {
        "command": "npm test",
        "workingDirectory": "/workspace"
    }
}

response = requests.post("http://localhost:5006/api/worker/execute", json=task)
print(json.dumps(response.json(), indent=2))
```

## Troubleshooting

### MCP Connection Issues

1. **Check Claude Code Installation**
   ```bash
   which claude-code
   claude-code --version
   ```

2. **Verify API Key**
   ```bash
   echo $ANTHROPIC_API_KEY
   ```

3. **Test MCP Server Directly**
   ```bash
   claude-code api --mode mcp-server --debug
   ```

### Worker Service Issues

1. **Check Logs**
   ```bash
   # In docker
   docker-compose -f docker-compose.mcp.yml logs worker

   # Local
   dotnet run --verbosity detailed
   ```

2. **Verify Workspace Permissions**
   ```bash
   ls -la /workspaces
   # Should be writable
   ```

3. **Test Without Service Bus**
   - Comment out queue processor in Program.cs
   - Use direct API calls

### Common Errors

1. **"Failed to start MCP server process"**
   - Ensure Claude Code is in PATH
   - Check file permissions

2. **"Timeout waiting for task result"**
   - Increase timeout in configuration
   - Check Service Bus connectivity

3. **"No pending result found for task"**
   - Ensure Service Bus queues exist
   - Check connection string

## Performance Testing

### Load Test with Multiple Workers

```bash
# Start multiple workers
for i in {1..5}; do
    PORT=$((5006 + $i)) dotnet run --urls="http://localhost:$PORT" &
done

# Run load test
ab -n 100 -c 10 -p task.json -T application/json http://localhost:5010/api/orchestration/submit-task
```

### Monitor Resources

```bash
# Watch CPU and memory
docker stats

# Monitor MCP processes
ps aux | grep claude-code
```

## Next Steps

1. **Deploy to Azure Container Apps**
   - See Phase 4 documentation

2. **Implement Worker Specialization**
   - Frontend workers
   - Backend workers
   - Testing workers

3. **Add Monitoring**
   - Application Insights
   - Custom metrics
   - Distributed tracing