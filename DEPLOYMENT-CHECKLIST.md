# VoiceCode SDK Integration Deployment Checklist

## Pre-Deployment Requirements

- [ ] Azure CLI installed and logged in
- [ ] Docker installed and running
- [ ] Azure subscription with sufficient quota
- [ ] Access to Azure Container Registry
- [ ] Service Bus connection string
- [ ] OpenAI API key for Observer Service
- [ ] Claude API key for Worker Service

## Build Phase

### 1. Build Docker Images Locally
```bash
./build-docker-images.sh
```

Expected output:
- ✓ Worker Service build succeeded
- ✓ Observer Service build succeeded

### 2. Verify Images
```bash
docker images | grep voicecode
```

Should see:
- voicecode-worker:local
- voicecode-observer:local

## Deployment Phase

### 3. Run Full Deployment
```bash
./build-and-deploy-all.sh
```

This script will:
1. Login to Azure
2. Login to ACR
3. Build and tag images
4. Push to registry
5. Create Service Bus resources
6. Deploy/Update services

### 4. Monitor Deployment Progress
```bash
# Watch Worker logs
az containerapp logs tail --name voicecode-worker-1 --resource-group voicecode-rg --follow

# Watch Observer logs
az containerapp logs tail --name voicecode-observer --resource-group voicecode-rg --follow
```

## Service Bus Configuration

### 5. Verify Service Bus Resources
```bash
# Check topic exists
az servicebus topic show \
    --name sdk-streams \
    --namespace-name voicecode-servicebus \
    --resource-group voicecode-rg

# Check subscription exists
az servicebus topic subscription show \
    --name observer \
    --topic-name sdk-streams \
    --namespace-name voicecode-servicebus \
    --resource-group voicecode-rg
```

## Post-Deployment Verification

### 6. Run Verification Script
```bash
./verify-deployment.sh
```

Should show:
- ✓ Observer Service is running
- ✓ Health check passed
- ✓ Worker services running
- ✓ Service Bus resources exist

### 7. Manual Health Checks

#### Test Observer Health
```bash
OBSERVER_URL=$(az containerapp show --name voicecode-observer --resource-group voicecode-rg --query "properties.configuration.ingress.fqdn" -o tsv)
curl https://$OBSERVER_URL/health
```

#### Check Worker SDK Configuration
```bash
az containerapp show \
    --name voicecode-worker-1 \
    --resource-group voicecode-rg \
    --query "properties.template.containers[0].env[?name=='ClaudeCodeSdk__Enabled'].value | [0]"
```

## Integration Testing

### 8. Test Voice Commands with SDK

Send these commands through the VoiceCode app:
- "Worker 1, create a React login component with validation"
- "Worker 2, build a dashboard component with charts"
- "Worker 3, implement a todo list with add and delete functionality"

### 9. Monitor Real-time Feedback

1. Open Service Bus Explorer in Azure Portal
2. Navigate to Topics > sdk-streams
3. Watch for incoming stream events
4. Verify Observer processes events
5. Check TTS queue for narrations

### 10. Verify End-to-End Flow

Listen for audio feedback:
- "Looking at your project setup..."
- "Creating your login component now..."
- "I've completed the task successfully."

## Troubleshooting

### If Observer Service Fails
```bash
# Check logs
az containerapp logs show \
    --name voicecode-observer \
    --resource-group voicecode-rg \
    --tail 100

# Common issues:
# - Missing OpenAI API key
# - Service Bus connection string
# - Incorrect topic/subscription names
```

### If No Stream Events
```bash
# Verify Worker configuration
az containerapp show \
    --name voicecode-worker-1 \
    --resource-group voicecode-rg \
    --query "properties.template.containers[0].env[?contains(name, 'StreamTopicName')]"

# Check Worker logs for publishing errors
az containerapp logs show \
    --name voicecode-worker-1 \
    --resource-group voicecode-rg \
    --tail 100 | grep "SDK stream"
```

### If No Narrations
```bash
# Check Observer processing
az containerapp logs show \
    --name voicecode-observer \
    --resource-group voicecode-rg \
    --tail 100 | grep "narration"

# Verify TTS queue
az servicebus queue show \
    --name tts-requests \
    --namespace-name voicecode-servicebus \
    --resource-group voicecode-rg
```

## Rollback Plan

### Quick Disable SDK
```bash
# Disable SDK on all workers
for i in {1..3}; do
    az containerapp update \
        --name voicecode-worker-$i \
        --resource-group voicecode-rg \
        --set-env-vars "ClaudeCodeSdk__Enabled=false"
done
```

### Revert to Previous Image
```bash
# List available revisions
az containerapp revision list \
    --name voicecode-worker-1 \
    --resource-group voicecode-rg \
    -o table

# Activate previous revision
az containerapp revision activate \
    --name voicecode-worker-1 \
    --resource-group voicecode-rg \
    --revision <previous-revision-name>
```

## Success Indicators

- [ ] All services show "Running" status
- [ ] Health endpoints return 200 OK
- [ ] SDK commands route correctly (check logs for "EXECUTING WITH CLAUDE CODE SDK")
- [ ] Stream events appear in Service Bus topic
- [ ] Observer processes events and generates narrations
- [ ] TTS service receives and processes narration requests
- [ ] Users hear real-time feedback during code generation
- [ ] File operations complete successfully

## Performance Metrics

Monitor these metrics post-deployment:
- SDK execution time vs API execution time
- Stream event processing latency
- Narration generation time
- End-to-end voice command completion time

## Quick Commands Reference

```bash
# View all container apps
az containerapp list -g voicecode-rg -o table

# Check specific worker status
az containerapp show -n voicecode-worker-1 -g voicecode-rg --query "properties.runningStatus"

# Stream Worker logs
az containerapp logs tail -n voicecode-worker-1 -g voicecode-rg --follow

# Stream Observer logs
az containerapp logs tail -n voicecode-observer -g voicecode-rg --follow

# Force restart Observer
az containerapp revision restart -n voicecode-observer -g voicecode-rg

# Check Service Bus metrics
az monitor metrics list \
    --resource /subscriptions/<sub-id>/resourceGroups/voicecode-rg/providers/Microsoft.ServiceBus/namespaces/voicecode-servicebus \
    --metric "IncomingMessages" \
    --interval PT1M
```