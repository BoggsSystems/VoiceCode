#!/bin/bash

# Quick deployment script for orchestrator service
# Uses existing STT service image as placeholder until proper images are built

set -e

# Configuration
RESOURCE_GROUP="voicecode-rg"
LOCATION="eastus"
ENV_NAME="voicecode-env"
REGISTRY="voicecodebuildsprod.azurecr.io"
KEY_VAULT="voicecodevault-0724"
SERVICE_BUS="voicecodebus-0724"
IDENTITY_ID="/subscriptions/42533332-e34e-4e78-b052-f051bc7d5206/resourcegroups/voicecode-rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/voicecode-identity"
IDENTITY_CLIENT_ID="015d2b8d-d732-42ae-91b2-4ed93afd3030"

# Color codes
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo -e "${YELLOW}Deploying Orchestrator Service to Container Apps...${NC}"

# Get App Insights connection string
APP_INSIGHTS_CONNECTION=$(az monitor app-insights component show \
    --app voicecode-insights \
    --resource-group $RESOURCE_GROUP \
    --query connectionString -o tsv)

# Deploy orchestrator (using STT image as placeholder for now)
echo "Creating orchestrator container app..."
az containerapp create \
    --name voicecode-orchestrator \
    --resource-group $RESOURCE_GROUP \
    --environment $ENV_NAME \
    --image $REGISTRY/voicecode/stt-service:v3 \
    --target-port 80 \
    --ingress external \
    --min-replicas 1 \
    --max-replicas 3 \
    --cpu 0.5 \
    --memory 1.0Gi \
    --user-assigned $IDENTITY_ID \
    --registry-identity $IDENTITY_ID \
    --registry-server $REGISTRY \
    --secrets \
        anthropic-key=keyvaultref:https://$KEY_VAULT.vault.azure.net/secrets/AnthropicApiKey,identityref:$IDENTITY_ID \
        servicebus-connection=keyvaultref:https://$KEY_VAULT.vault.azure.net/secrets/ServiceBusConnectionString,identityref:$IDENTITY_ID \
    --env-vars \
        ASPNETCORE_ENVIRONMENT=Production \
        AZURE_CLIENT_ID=$IDENTITY_CLIENT_ID \
        ANTHROPIC_API_KEY=secretref:anthropic-key \
        ServiceBus__FullyQualifiedNamespace=$SERVICE_BUS.servicebus.windows.net \
        ApplicationInsights__ConnectionString="$APP_INSIGHTS_CONNECTION" \
        ServiceEndpoints__WorkerService=https://voicecode-worker.orangewater-a2f689a8.eastus.azurecontainerapps.io \
    --query properties.configuration.ingress.fqdn -o tsv

echo -e "${GREEN}Orchestrator deployed successfully!${NC}"

# Deploy a sample worker
echo -e "\n${YELLOW}Deploying sample worker...${NC}"
az containerapp create \
    --name voicecode-worker-1 \
    --resource-group $RESOURCE_GROUP \
    --environment $ENV_NAME \
    --image $REGISTRY/voicecode/stt-service:v3 \
    --target-port 80 \
    --ingress internal \
    --min-replicas 0 \
    --max-replicas 1 \
    --cpu 1.0 \
    --memory 2.0Gi \
    --user-assigned $IDENTITY_ID \
    --registry-identity $IDENTITY_ID \
    --registry-server $REGISTRY \
    --secrets \
        github-token=keyvaultref:https://$KEY_VAULT.vault.azure.net/secrets/GitHubToken,identityref:$IDENTITY_ID \
        anthropic-key=keyvaultref:https://$KEY_VAULT.vault.azure.net/secrets/AnthropicApiKey,identityref:$IDENTITY_ID \
        servicebus-connection=keyvaultref:https://$KEY_VAULT.vault.azure.net/secrets/ServiceBusConnectionString,identityref:$IDENTITY_ID \
    --env-vars \
        ASPNETCORE_ENVIRONMENT=Production \
        WORKER_ID=worker-1 \
        ASSIGNED_REPO="https://github.com/restaurantchain/pos-system" \
        GITHUB_TOKEN=secretref:github-token \
        ANTHROPIC_API_KEY=secretref:anthropic-key \
        ApplicationInsights__ConnectionString="$APP_INSIGHTS_CONNECTION" \
    --scale-rule-name queue-rule \
    --scale-rule-type azure-servicebus \
    --scale-rule-metadata \
        queueName=worker-1-tasks \
        namespace=$SERVICE_BUS \
        messageCount=1 \
    --scale-rule-auth \
        connection=servicebus-connection \
    --query properties.configuration.ingress.fqdn -o tsv

echo -e "${GREEN}Sample worker deployed!${NC}"

echo -e "\n${YELLOW}Note:${NC} These deployments use placeholder images. Build and push proper images with:"
echo "  ./infrastructure/scripts/build-and-push-images.sh"
echo ""
echo "Then update the container apps with:"
echo "  az containerapp update --name voicecode-orchestrator --image $REGISTRY/voicecode/orchestrator-service:latest"