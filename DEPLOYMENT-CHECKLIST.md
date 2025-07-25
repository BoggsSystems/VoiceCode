# VoiceCode Azure Deployment Checklist

## Pre-Deployment Requirements

- [ ] Azure CLI installed and logged in
- [ ] Docker installed and running
- [ ] Azure subscription with sufficient quota
- [ ] GitHub Personal Access Token (PAT) with repo access
- [ ] Anthropic API Key
- [ ] Azure Speech Services API Key and Region

## Deployment Steps

### 1. Environment Setup
- [ ] Set environment variables:
  ```bash
  export RESOURCE_GROUP="VoiceCode-RG"
  export LOCATION="eastus"
  export ACR_NAME="voicecodecr"
  ```

### 2. Create Core Resources
- [ ] Create resource group
- [ ] Create Azure Container Registry (ACR)
- [ ] Create Key Vault
- [ ] Create Service Bus namespace

### 3. Configure Secrets
- [ ] Store GitHub token in Key Vault
- [ ] Store Anthropic API key in Key Vault
- [ ] Store Azure Speech API key in Key Vault
- [ ] Store Service Bus connection string in Key Vault

### 4. Update Repository Configuration
- [ ] Edit `infrastructure/container-apps/repo-registry.yaml`
- [ ] Set actual GitHub repository URLs for each worker
- [ ] Define appropriate keywords for routing

### 5. Build and Push Images
- [ ] Login to ACR: `az acr login --name $ACR_NAME`
- [ ] Run: `./infrastructure/scripts/build-and-push-images.sh`
- [ ] Verify all images pushed successfully

### 6. Deploy Infrastructure
- [ ] Deploy base infrastructure with Bicep
- [ ] Deploy Container Apps Environment
- [ ] Deploy Application Insights
- [ ] Create Service Bus queues

### 7. Deploy Services
- [ ] Deploy orchestrator service
- [ ] Deploy supporting services (STT, TTS, etc.)
- [ ] Deploy 10 repository-aware workers
- [ ] Verify all containers are running

### 8. Post-Deployment Verification
- [ ] Check health endpoints
- [ ] Verify worker logs show successful repo cloning
- [ ] Test voice command routing
- [ ] Confirm workers scale to zero when idle

### 9. Testing
- [ ] Test voice command: "Update the restaurant menu"
- [ ] Verify routes to worker-1
- [ ] Check Claude Code integration
- [ ] Test multiple domain keywords

### 10. Monitoring Setup
- [ ] Configure Application Insights dashboards
- [ ] Set up alerts for failures
- [ ] Enable container logs
- [ ] Configure cost alerts

## Quick Commands

```bash
# Check deployment status
az containerapp list -g $RESOURCE_GROUP -o table

# View worker logs
az containerapp logs show -n voicecode-worker-1 -g $RESOURCE_GROUP --tail 50

# Force restart a worker
az containerapp revision restart -n voicecode-worker-1 -g $RESOURCE_GROUP

# Check Service Bus queues
az servicebus queue list --namespace-name voicecodebus -g $RESOURCE_GROUP -o table
```

## Troubleshooting

If deployment fails:
1. Check Azure CLI is logged in
2. Verify sufficient quota in region
3. Ensure all secrets are set in Key Vault
4. Check Docker images built successfully
5. Review container logs for errors

## Rollback Plan

If issues occur:
1. Previous deployment revisions are retained
2. Use `az containerapp revision list` to see revisions
3. Activate previous revision if needed
4. All data is ephemeral, so no data migration needed