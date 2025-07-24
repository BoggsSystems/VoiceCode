# Phase 2: Single Worker Prototype - Deployment Guide

## Overview

This guide covers the deployment of the Worker and Orchestrator services to Azure Container Instances for Phase 2 of the VoiceCode MCP Claude Code Workers implementation.

## Prerequisites

1. Azure CLI installed and configured
2. Docker installed locally
3. Access to the VoiceCode Azure subscription
4. ANTHROPIC_API_KEY environment variable set

## Architecture

```
Voice Input → STT Service → Dispatcher → Orchestrator → Worker (Claude Code) → Response
                                              ↓
                                        Service Bus Queue
```

## Deployment Steps

### 1. Build and Push Docker Images

First, build and push the Docker images to Azure Container Registry:

```bash
cd /Users/jeffboggs/VoiceCode/infrastructure/scripts
./build-worker-orchestrator-images.sh v3
```

This will:
- Build orchestrator-service Docker image
- Build worker-service Docker image
- Push both images to ACR with tag v3

### 2. Local Testing (Optional)

Test the services locally before deploying to Azure:

```bash
cd /Users/jeffboggs/VoiceCode/scripts
./test-worker-orchestrator-local.sh
```

This will:
- Start orchestrator and worker services using docker-compose
- Test health endpoints
- Submit a test task
- Show service logs

### 3. Deploy to Azure Container Instances

Deploy the services to Azure:

```bash
cd /Users/jeffboggs/VoiceCode/infrastructure/scripts
./deploy-worker-orchestrator-services.sh
```

When prompted about Azure Files, choose:
- `y` - For persistent workspace storage across container restarts
- `n` - For ephemeral workspaces (faster, but lost on restart)

### 4. Update Dispatcher Service

After deployment, update the dispatcher service to include the new endpoints:

```bash
az container create --resource-group voicecode-rg --name voicecode-dev-eus-dispatcher-ci \
  --environment-variables \
    ServiceEndpoints__OrchestratorService=https://voicecode-dev-eus-orchestrator.eastus.azurecontainer.io \
    ServiceEndpoints__WorkerService=https://voicecode-dev-eus-worker.eastus.azurecontainer.io
```

## Service Configuration

### Orchestrator Service
- **CPU**: 0.5 cores
- **Memory**: 1.0 GB
- **Endpoints**: 
  - Health: `/health`
  - Ready: `/health/ready`
  - Submit Task: `/api/orchestration/submit-task`
  - Task Status: `/api/orchestration/task/{taskId}/status`

### Worker Service
- **CPU**: 1.0 cores
- **Memory**: 2.0 GB
- **Claude Code CLI**: Pre-installed in container
- **Workspace**: `/workspaces` (isolated per task)
- **Max Concurrent Workers**: 5
- **Task Timeout**: 30 minutes

## Testing the Deployment

### 1. Health Check

```bash
# Check orchestrator health
curl https://voicecode-dev-eus-orchestrator.eastus.azurecontainer.io/health

# Check worker health
curl https://voicecode-dev-eus-worker.eastus.azurecontainer.io/health
```

### 2. Submit a Test Task

```bash
curl -X POST https://voicecode-dev-eus-orchestrator.eastus.azurecontainer.io/api/orchestration/submit-task \
  -H "Content-Type: application/json" \
  -d '{
    "type": "code_generation",
    "prompt": "Create a REST API endpoint in C# that returns the current time",
    "context": {
      "language": "csharp",
      "framework": "aspnetcore"
    }
  }'
```

### 3. Voice Pipeline Integration

Test the complete voice pipeline:

1. Use the web app at https://voicecode.dev
2. Say: "Create a Python function that calculates fibonacci numbers"
3. Verify the response includes generated code

## Monitoring

### View Logs

```bash
# Orchestrator logs
az container logs --resource-group voicecode-rg --name voicecode-dev-eus-orchestrator-ci --follow

# Worker logs
az container logs --resource-group voicecode-rg --name voicecode-dev-eus-worker-ci --follow
```

### Application Insights

Check Application Insights for:
- Request traces
- Performance metrics
- Error logs
- Custom events (task submissions, completions)

## Troubleshooting

### Common Issues

1. **Container fails to start**
   - Check logs for startup errors
   - Verify managed identity has Key Vault access
   - Ensure ACR credentials are valid

2. **Health check failing**
   - Verify service is running: `az container show -g voicecode-rg -n voicecode-dev-eus-worker-ci`
   - Check for port conflicts
   - Review application logs

3. **Tasks not processing**
   - Verify Service Bus connection
   - Check queue exists: `claude-code-tasks`
   - Ensure worker queue processor is running

4. **Claude Code CLI errors**
   - Verify ANTHROPIC_API_KEY is set
   - Check Claude Code CLI installation in container
   - Review worker service logs

### Restart Services

```bash
# Restart a specific service
az container restart --resource-group voicecode-rg --name voicecode-dev-eus-worker-ci

# Restart all services
cd /Users/jeffboggs/VoiceCode/infrastructure/scripts
./restart-containers.sh
```

## Performance Benchmarks

Target metrics for Phase 2:
- Worker startup time: < 30 seconds
- Task processing latency: < 5 seconds
- Code generation time: < 20 seconds
- End-to-end voice command: < 30 seconds

## Next Steps

After successful deployment and testing:

1. Run performance benchmarks
2. Test with 10+ different coding tasks
3. Verify integration with voice pipeline
4. Document any issues or improvements
5. Prepare for Phase 3: Multi-Worker Orchestration

## Security Considerations

- Worker containers run with managed identities
- No direct access to production resources
- Workspaces are isolated per task
- API keys stored in Key Vault
- Network isolation between services

## Cost Optimization

Current deployment (Phase 2):
- Orchestrator: ~$15/month (0.5 CPU, 1GB)
- Worker: ~$30/month (1.0 CPU, 2GB)
- Total: ~$45/month

Future optimization (Phase 4):
- Container Apps with scale-to-zero
- Estimated 90% cost reduction
- Pay only for actual usage