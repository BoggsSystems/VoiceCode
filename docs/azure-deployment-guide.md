# Azure Deployment Guide for VoiceCode

This guide walks through deploying the VoiceCode system with repository-aware workers to Azure.

## Prerequisites

1. **Azure CLI** installed and logged in:
   ```bash
   az login
   az account set --subscription "Your-Subscription-Name"
   ```

2. **Docker** installed for building container images

3. **Required Azure Resources**:
   - Azure subscription with sufficient quota
   - Resource group created
   - Azure Container Registry (ACR)

4. **Required Secrets**:
   - GitHub Personal Access Token (with repo access)
   - Anthropic API Key
   - Azure Speech Services API Key

## Architecture Overview

The deployment includes:
- 10 repository-aware workers (each managing a different client repo)
- Orchestrator service for routing voice commands
- Supporting services (STT, TTS, Router, Claude, Dispatcher)
- Azure Container Apps with KEDA autoscaling
- Service Bus for message queuing
- Key Vault for secrets management

## Step-by-Step Deployment

### 1. Set Environment Variables

```bash
export RESOURCE_GROUP="VoiceCode-RG"
export LOCATION="eastus"
export ACR_NAME="voicecodecr"
export KEY_VAULT_NAME="voicecodevault"
```

### 2. Create Core Infrastructure

```bash
# Create resource group if not exists
az group create --name $RESOURCE_GROUP --location $LOCATION

# Create Container Registry
az acr create \
  --name $ACR_NAME \
  --resource-group $RESOURCE_GROUP \
  --sku Basic \
  --admin-enabled true

# Create Key Vault
az keyvault create \
  --name $KEY_VAULT_NAME \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION
```

### 3. Store Secrets in Key Vault

```bash
# Store GitHub Token
az keyvault secret set \
  --vault-name $KEY_VAULT_NAME \
  --name "GitHubToken" \
  --value "YOUR_GITHUB_TOKEN"

# Store Anthropic API Key
az keyvault secret set \
  --vault-name $KEY_VAULT_NAME \
  --name "AnthropicApiKey" \
  --value "YOUR_ANTHROPIC_KEY"

# Store Azure Speech API Key
az keyvault secret set \
  --vault-name $KEY_VAULT_NAME \
  --name "AzureSpeechApiKey" \
  --value "YOUR_SPEECH_KEY"

# Store Service Bus Connection String (after creating Service Bus)
az keyvault secret set \
  --vault-name $KEY_VAULT_NAME \
  --name "ServiceBusConnectionString" \
  --value "YOUR_CONNECTION_STRING"
```

### 4. Build and Push Docker Images

```bash
# Login to ACR
az acr login --name $ACR_NAME

# Get ACR login server
ACR_LOGIN_SERVER=$(az acr show --name $ACR_NAME --query loginServer -o tsv)

# Build and push all services
./infrastructure/scripts/build-and-push-images.sh $ACR_LOGIN_SERVER
```

### 5. Deploy Base Infrastructure

```bash
# Deploy Container Apps Environment and supporting resources
az deployment group create \
  --resource-group $RESOURCE_GROUP \
  --template-file infrastructure/container-apps/main.bicep \
  --parameters location=$LOCATION
```

### 6. Deploy Core Services

```bash
# Deploy orchestrator and supporting services
./infrastructure/scripts/deploy-worker-orchestrator-services.sh
```

### 7. Deploy Repository-Aware Workers

```bash
# Deploy 10 workers with repository assignments
./infrastructure/scripts/deploy-multi-repo-workers.sh

# Or with image building
BUILD_IMAGE=true ./infrastructure/scripts/deploy-multi-repo-workers.sh
```

### 8. Configure Repository Assignments

Update the `infrastructure/container-apps/repo-registry.yaml` with your actual repositories:

```yaml
repositories:
  - workerId: worker-1
    name: "Your Client 1 Project"
    repo: "https://github.com/your-org/client1-repo"
    keywords: ["client1", "specific", "keywords"]
  # ... repeat for all 10 workers
```

### 9. Verify Deployment

```bash
# Check Container Apps status
az containerapp list \
  --resource-group $RESOURCE_GROUP \
  --output table

# Check worker logs
az containerapp logs show \
  --name voicecode-worker-1 \
  --resource-group $RESOURCE_GROUP \
  --follow

# Test orchestrator health
curl https://voicecode-orchestrator.azurecontainerapps.io/health
```

## Post-Deployment Configuration

### 1. DNS Configuration (Optional)

If using custom domain:
```bash
az containerapp hostname add \
  --hostname voicecode.yourdomain.com \
  --resource-group $RESOURCE_GROUP \
  --name voicecode-webapp
```

### 2. Scale Configuration

Workers are configured to scale to 0 when idle. Adjust if needed:
```bash
az containerapp update \
  --name voicecode-worker-1 \
  --resource-group $RESOURCE_GROUP \
  --min-replicas 0 \
  --max-replicas 3
```

### 3. Monitoring Setup

```bash
# View Application Insights
az monitor app-insights component show \
  --app voicecode-insights \
  --resource-group $RESOURCE_GROUP
```

## Testing the Deployment

### 1. Test Voice Command Routing

```bash
# Send a test voice command
curl -X POST https://voicecode-orchestrator.azurecontainerapps.io/api/voice/process \
  -H "Content-Type: application/json" \
  -d '{"voicePrompt": "Update the restaurant menu pricing", "sessionId": "test-123"}'
```

### 2. Verify Worker Assignment

The command should route to `worker-1` based on the "restaurant" keyword.

### 3. Check Worker Logs

```bash
az containerapp logs show \
  --name voicecode-worker-restaurant \
  --resource-group $RESOURCE_GROUP \
  --tail 50
```

## Troubleshooting

### Common Issues

1. **Workers not starting**: Check GitHub token in Key Vault
2. **Routing failures**: Verify repo-registry.yaml is deployed
3. **Scale to zero issues**: Check Service Bus queue configuration
4. **Clone failures**: Ensure repositories are accessible with token

### Debug Commands

```bash
# Check worker environment variables
az containerapp show \
  --name voicecode-worker-1 \
  --resource-group $RESOURCE_GROUP \
  --query "properties.template.containers[0].env"

# Force worker restart
az containerapp revision restart \
  --name voicecode-worker-1 \
  --resource-group $RESOURCE_GROUP
```

## Cost Optimization

1. **Scale to Zero**: Workers automatically scale down when idle
2. **Consumption Plan**: Uses serverless Container Apps pricing
3. **Shallow Clones**: Reduces startup time and bandwidth
4. **Shared Resources**: All workers share Service Bus and Key Vault

## Security Considerations

1. **Managed Identity**: Workers use managed identity for Azure resources
2. **Key Vault**: All secrets stored in Key Vault
3. **Network Isolation**: Consider using VNet integration for production
4. **RBAC**: Apply least privilege access to resources

## Next Steps

1. Set up CI/CD pipeline for automated deployments
2. Configure alerts and monitoring dashboards
3. Implement backup and disaster recovery
4. Add custom domain with SSL certificate
5. Enable VNet integration for enhanced security