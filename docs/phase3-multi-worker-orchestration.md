# Phase 3: Multi-Worker Orchestration - Implementation Guide

## Overview

Phase 3 enhances the orchestrator service with advanced multi-worker management capabilities, including worker pool management, intelligent load balancing, health monitoring, and auto-scaling.

## Architecture

```
                        ┌─────────────────────┐
                        │   Orchestrator      │
                        │  ┌───────────────┐  │
                        │  │ Worker Pool   │  │
                        │  │   Service     │  │
                        │  └───────────────┘  │
                        │         │           │
                        │  ┌──────┴────────┐  │
                        │  │ Load Balancer │  │
                        │  └───────────────┘  │
                        └─────────┬───────────┘
                                  │
                ┌─────────────────┼─────────────────┐
                │                 │                 │
        ┌───────▼──────┐ ┌───────▼──────┐ ┌───────▼──────┐
        │   Worker 1   │ │   Worker 2   │ │   Worker N   │
        │ (Claude Code)│ │ (Claude Code)│ │ (Claude Code)│
        └──────────────┘ └──────────────┘ └──────────────┘
```

## Key Features Implemented

### 1. Worker Pool Management

- **Dynamic Registration**: Workers can register/unregister dynamically
- **Capability Discovery**: Each worker declares supported languages, frameworks, and task types
- **State Tracking**: Real-time tracking of worker state, load, and availability
- **Metrics Collection**: Performance metrics for each worker

### 2. Load Balancing Strategies

#### Round Robin
- Distributes tasks evenly across all available workers
- Simple and fair distribution
- Best for homogeneous workloads

#### Least Loaded
- Assigns tasks to the worker with the lowest current load
- Optimizes resource utilization
- Default strategy

#### Capability-Based
- Matches task requirements with worker capabilities
- Scores workers based on language/framework support
- Optimal for specialized tasks

#### Performance-Based
- Routes tasks to workers with the best success rate
- Considers average task duration
- Rewards high-performing workers

#### Adaptive
- Combines multiple factors: load, capabilities, performance, idle time
- Dynamic scoring algorithm
- Best overall strategy for mixed workloads

### 3. Health Monitoring

- **Active Health Checks**: Configurable interval health probes
- **Failure Detection**: Tracks consecutive failures
- **Auto-Recovery**: Automatic restart of unhealthy workers
- **Circuit Breaker**: Prevents routing to failing workers

### 4. Parallel Task Execution

- **Concurrent Processing**: Multiple tasks processed simultaneously
- **Queue Management**: Azure Service Bus for reliable task delivery
- **Task Monitoring**: Active monitoring with timeout handling
- **Retry Logic**: Configurable retry policies for failed tasks

### 5. Auto-Scaling

- **Utilization-Based**: Scale up/down based on pool utilization
- **Configurable Thresholds**: 
  - Scale up: > 80% utilization
  - Scale down: < 20% utilization
- **Min/Max Limits**: Prevents over/under-provisioning

## API Endpoints

### Worker Pool Management

```bash
# Get pool status
GET /api/orchestration/pool/status

# Register new worker
POST /api/orchestration/pool/register
{
  "endpoint": "http://worker-5:80",
  "capabilities": {
    "supportedLanguages": ["python", "javascript"],
    "supportedTaskTypes": ["code_generation", "testing"],
    "maxConcurrentTasks": 5
  }
}

# Unregister worker
DELETE /api/orchestration/pool/worker/{workerId}

# Get worker metrics
GET /api/orchestration/pool/metrics

# Get task assignments
GET /api/orchestration/pool/assignments
```

### Load Balancing Configuration

```bash
# Get current policy
GET /api/orchestration/pool/policy

# Update policy
PUT /api/orchestration/pool/policy
{
  "strategy": "Adaptive",
  "enableAutoScaling": true,
  "minWorkers": 2,
  "maxWorkers": 10,
  "scaleUpThreshold": 80,
  "scaleDownThreshold": 20,
  "healthCheckInterval": "00:00:30",
  "maxRetries": 3
}
```

### Batch Operations

```bash
# Submit multiple tasks
POST /api/orchestration/submit-batch
[
  {
    "requestType": "code_generation",
    "userPrompt": "Create a Python API",
    "context": {"language": "python"}
  },
  {
    "requestType": "testing",
    "userPrompt": "Write unit tests",
    "context": {"language": "javascript"}
  }
]
```

## Configuration

### appsettings.json

```json
{
  "Orchestration": {
    "WorkerEndpoints": [
      "http://worker-1:80",
      "http://worker-2:80",
      "http://worker-3:80"
    ],
    "MaxConcurrentWorkers": 10,
    "TaskTimeoutMinutes": 30,
    "EnableMultiAgent": true
  }
}
```

### Environment Variables

```bash
# Service Bus for task queue
AZURE_SERVICE_BUS_CONNECTION_STRING=...

# Application Insights for monitoring
APPLICATIONINSIGHTS_CONNECTION_STRING=...

# Redis for distributed state (optional)
REDIS_CONNECTION_STRING=...
```

## Testing

### Local Testing

```bash
# Start services locally
docker-compose -f docker-compose.mcp.yml up -d

# Run multi-worker tests
./scripts/test-multi-worker-orchestration.sh
```

### Load Testing

```bash
# Simulate concurrent users
ab -n 100 -c 20 -p task.json -T application/json \
  http://localhost:5010/api/orchestration/submit-task

# Monitor pool status during load
watch -n 2 'curl -s http://localhost:5010/api/orchestration/pool/status | jq .'
```

### Performance Benchmarks

Target metrics for Phase 3:
- Support 5+ concurrent workers ✓
- Task distribution latency: < 100ms
- Health check cycle: 30 seconds
- Auto-scaling response: < 60 seconds
- Load balancing fairness: ±10% distribution

## Monitoring and Observability

### Metrics Tracked

1. **Pool Metrics**
   - Total/Healthy/Available workers
   - Pool utilization percentage
   - Task queue depth

2. **Worker Metrics**
   - Tasks completed/failed
   - Average task duration
   - Success rate
   - Task type breakdown

3. **Task Metrics**
   - Submission rate
   - Completion rate
   - Average latency
   - Retry count

### Health Checks

```bash
# Orchestrator health
curl http://localhost:5010/health

# Detailed health with worker pool status
curl http://localhost:5010/health/ready
```

## Troubleshooting

### Common Issues

1. **Workers not registering**
   - Check network connectivity
   - Verify worker endpoints are accessible
   - Review orchestrator logs

2. **Tasks not distributed evenly**
   - Check load balancing strategy
   - Verify worker capabilities match tasks
   - Review worker health status

3. **High task failure rate**
   - Check worker health metrics
   - Review task timeout settings
   - Verify Service Bus connectivity

4. **Auto-scaling not working**
   - Ensure auto-scaling is enabled
   - Check utilization thresholds
   - Verify scaling permissions

### Debug Commands

```bash
# Check worker registrations
curl http://localhost:5010/api/orchestration/pool/status | jq '.workers'

# View task assignments
curl http://localhost:5010/api/orchestration/pool/assignments | jq '.'

# Check worker metrics
curl http://localhost:5010/api/orchestration/pool/metrics | jq '.'

# View orchestrator logs
docker logs voicecode-orchestrator-1 --tail 100 -f
```

## Next Steps

Phase 3 implementation is complete. The system now supports:
- ✅ Worker pool management
- ✅ Multiple load balancing strategies
- ✅ Health monitoring and auto-recovery
- ✅ Parallel task execution
- ✅ Auto-scaling capabilities

Ready for Phase 4: Azure Container Apps migration for production scaling.