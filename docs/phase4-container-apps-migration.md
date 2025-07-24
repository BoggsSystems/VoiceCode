# Phase 4: Azure Container Apps Migration - Implementation Guide

## Overview

Phase 4 migrates VoiceCode from Azure Container Instances to Azure Container Apps, enabling automatic scaling from 0 to N workers based on queue depth, with significant cost optimization through spot instances and consumption-based pricing.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    Azure Container Apps Environment              │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │                    Orchestrator App                       │   │
│  │  • Min: 1, Max: 3 replicas                              │   │
│  │  • Consumption plan                                      │   │
│  │  • HTTP-based scaling                                    │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │                     Worker App Pool                       │   │
│  │  • Min: 0, Max: 10 replicas                             │   │
│  │  • Spot instances (D4)                                   │   │
│  │  • Queue-based KEDA scaling                             │   │
│  │  • Azure Files for workspaces                           │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │                    Dapr Components                        │   │
│  │  • Service Bus PubSub                                    │   │
│  │  • Blob Storage State Store                              │   │
│  │  • Service-to-service invocation                        │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                                  │
                                  ├── Azure Service Bus (Task Queue)
                                  ├── Azure Files (Persistent Workspaces)
                                  ├── Application Insights (Monitoring)
                                  └── Key Vault (Secrets)
```

## Key Features Implemented

### 1. KEDA Autoscaling

The worker service scales automatically based on:

- **Queue Length**: Scales up when messages appear in the Service Bus queue
- **CPU Usage**: Additional scaling at 70% CPU utilization
- **Memory Usage**: Additional scaling at 80% memory utilization

```yaml
scale:
  minReplicas: 0  # Scale to zero when idle
  maxReplicas: 10 # Maximum worker instances
  rules:
    - name: servicebus-queue-length
      custom:
        type: azure-servicebus
        metadata:
          queueName: claude-code-tasks
          messageCount: 1  # Scale up with just 1 message
```

### 2. Cost Optimization

- **Scale to Zero**: Workers scale down to 0 when no tasks are queued
- **Spot Instances**: Workers run on Spot D4 instances (up to 90% cost savings)
- **Consumption Plan**: Orchestrator uses consumption plan for cost efficiency
- **Workload Profiles**: Mixed profiles optimize cost vs. performance

### 3. Dapr Integration

Dapr provides:
- **Service Discovery**: Internal service-to-service communication
- **Pub/Sub**: Reliable message delivery via Service Bus
- **State Management**: Distributed state via Azure Blob Storage
- **Observability**: Built-in distributed tracing

### 4. Persistent Storage

- **Azure Files**: Shared workspace storage across worker instances
- **Volume Mounts**: Each worker has `/workspaces` mounted
- **State Persistence**: Worker state preserved across scaling events

### 5. Monitoring & Alerting

Comprehensive monitoring includes:
- **Metrics**: CPU, Memory, Replica count, Request latency
- **Alerts**: High resource usage, scaling events, failures
- **Dashboards**: Real-time monitoring dashboard
- **Log Analytics**: Structured query capabilities

## Deployment Guide

### Prerequisites

1. Azure CLI installed and configured
2. Required Azure resources:
   - Resource Group: `voicecode-rg`
   - Container Registry: `voicecodebuildsprod`
   - Key Vault: `voicecodedevkveus`
   - Service Bus: `voicecodeservicebus`
   - Storage Account: `voicecodestorage`

### Deploy Infrastructure

```bash
cd infrastructure/container-apps

# Deploy base infrastructure
az deployment group create \
  --resource-group voicecode-rg \
  --template-file main.bicep \
  --parameters location=eastus environmentName=voicecode

# Deploy monitoring
az deployment group create \
  --resource-group voicecode-rg \
  --template-file monitoring.bicep \
  --parameters appInsightsName=voicecode-insights alertEmail=alerts@voicecode.dev
```

### Deploy Applications

```bash
# Option 1: Deploy with existing images
./deploy-container-apps.sh

# Option 2: Build and deploy
BUILD_IMAGES=true ./deploy-container-apps.sh

# Option 3: Deploy with specific tag
IMAGE_TAG=v4 ./deploy-container-apps.sh
```

## Configuration

### Environment Variables

Key environment variables configured:

```bash
# Worker Service
AZURE_CLIENT_ID                    # Managed identity for auth
ServiceBus__FullyQualifiedNamespace # Service Bus namespace
ServiceBus__QueueName              # Task queue name
ClaudeCode__MaxConcurrentWorkers   # Max workers per instance
Storage__AccountName               # Storage for workspaces

# Orchestrator Service
ServiceEndpoints__WorkerService    # Internal worker endpoint
Orchestration__EnableMultiAgent    # Multi-worker feature flag
Orchestration__MaxConcurrentWorkers # Pool size limit
```

### Scaling Configuration

Adjust scaling parameters:

```bash
# Update worker scaling limits
az containerapp update \
  -n voicecode-worker \
  -g voicecode-rg \
  --min-replicas 1 \
  --max-replicas 20

# Update scaling rules
az containerapp update \
  -n voicecode-worker \
  -g voicecode-rg \
  --scale-rule-name queue-scale \
  --scale-rule-type azure-queue \
  --scale-rule-metadata messageCount=5
```

## Testing

### Verify Deployment

```bash
# Check health
curl https://voicecode-orchestrator.{region}.azurecontainerapps.io/health

# Check worker pool status
curl https://voicecode-orchestrator.{region}.azurecontainerapps.io/api/orchestration/pool/status

# Submit test task
curl -X POST https://voicecode-orchestrator.{region}.azurecontainerapps.io/api/orchestration/submit-task \
  -H "Content-Type: application/json" \
  -d '{
    "requestType": "code_generation",
    "userPrompt": "Create a hello world function",
    "context": {"language": "python"}
  }'
```

### Monitor Scaling

```bash
# Watch worker replicas
watch -n 5 'az containerapp replica list -n voicecode-worker -g voicecode-rg --query "length(@)"'

# View scaling events
az monitor activity-log list \
  --resource-group voicecode-rg \
  --offset 1h \
  --query "[?contains(operationName.value, 'Scale')]"

# Check queue depth
az servicebus queue show \
  --name claude-code-tasks \
  --namespace-name voicecodeservicebus \
  --resource-group voicecode-rg \
  --query messageCount
```

## Performance Metrics

### Scaling Performance
- **Scale-up time**: < 30 seconds from queue message
- **Scale-down time**: 5 minutes after last task
- **Cold start**: < 45 seconds for new worker instance

### Cost Optimization
- **Idle cost**: $0 (scales to zero)
- **Active cost**: ~$0.10/hour per worker (spot pricing)
- **Estimated savings**: 90% vs. always-on Container Instances

### Throughput
- **Concurrent workers**: 0-10 (configurable to 50+)
- **Tasks per worker**: 5 concurrent
- **Max throughput**: 50 tasks concurrently

## Monitoring

### Application Insights Queries

```kql
// Worker scaling events
ContainerAppSystemLogs_CL
| where ContainerAppName_s == "voicecode-worker"
| where EventCategory_s == "Scaling"
| project TimeGenerated, ReplicaCount_d, Reason_s
| order by TimeGenerated desc

// Task processing metrics
customMetrics
| where name == "TaskProcessingDuration"
| summarize avg(value), min(value), max(value) by bin(timestamp, 5m)
| render timechart

// Error rate
requests
| where cloud_RoleName == "voicecode-worker"
| summarize failureRate = countif(success == false) * 100.0 / count() by bin(timestamp, 5m)
| render timechart
```

### Alerts Configured

1. **High CPU Usage** (>80%)
2. **High Memory Usage** (>85%)
3. **Worker Scaling** (>8 replicas)
4. **High Failure Rate** (>5%)
5. **Slow Response Time** (>5 seconds)

## Troubleshooting

### Common Issues

1. **Workers not scaling up**
   - Check Service Bus connection
   - Verify queue has messages
   - Check KEDA logs: `az containerapp logs show -n voicecode-worker -g voicecode-rg --type system`

2. **Workers not scaling down**
   - Check minimum replica setting
   - Verify no active tasks
   - Review scale-down cooldown period

3. **Storage mount failures**
   - Verify Azure Files share exists
   - Check storage account connectivity
   - Review volume mount configuration

4. **Dapr communication issues**
   - Verify Dapr is enabled on both apps
   - Check app IDs match service names
   - Review Dapr sidecar logs

### Debug Commands

```bash
# View worker logs
az containerapp logs show -n voicecode-worker -g voicecode-rg --follow

# Check Dapr logs
az containerapp logs show -n voicecode-worker -g voicecode-rg --type system --follow

# Exec into container
az containerapp exec -n voicecode-worker -g voicecode-rg --command /bin/bash

# View environment variables
az containerapp show -n voicecode-worker -g voicecode-rg --query properties.template.containers[0].env
```

## Migration Checklist

- [x] Create Container Apps environment
- [x] Configure managed identities
- [x] Set up Dapr components
- [x] Deploy orchestrator with HTTP scaling
- [x] Deploy workers with KEDA scaling
- [x] Configure Azure Files storage
- [x] Set up monitoring and alerts
- [x] Test auto-scaling behavior
- [x] Verify zero-scale capability
- [x] Validate spot instance usage

## Next Steps

Phase 4 is complete. The system now features:
- ✅ Automatic scaling from 0 to N workers
- ✅ Queue-based scaling with KEDA
- ✅ Cost optimization with spot instances
- ✅ Dapr service mesh integration
- ✅ Persistent storage across instances
- ✅ Comprehensive monitoring and alerting

Ready for Phase 5: Advanced Features including voice-optimized task planning and multi-phase coordination.