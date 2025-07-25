#!/bin/bash

# VoiceCode Container Instances Deployment Script
# Deploys all 6 services to Azure Container Instances with proper configuration

set -e

# Configuration
RESOURCE_GROUP="voicecode-rg"
LOCATION="eastus"
KEY_VAULT_NAME="voicecodedevkveus"
ACR_NAME="voicecodebuildsprod"
ACR_SERVER="${ACR_NAME}.azurecr.io"
ENVIRONMENT="Production"

# API Keys
CLAUDE_API_KEY="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"
OPENAI_API_KEY="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"

# Get ACR credentials
echo "Getting ACR credentials..."
ACR_USERNAME=$(az acr credential show --name $ACR_NAME --query username -o tsv)
ACR_PASSWORD=$(az acr credential show --name $ACR_NAME --query passwords[0].value -o tsv)

# Get Key Vault secrets
echo "Getting Key Vault secrets..."
APP_INSIGHTS_CONN=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name ApplicationInsightsConnectionString --query value -o tsv 2>/dev/null || echo "")
SERVICE_BUS_CONN=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name ServiceBusConnectionString --query value -o tsv 2>/dev/null || echo "")
REDIS_CONN=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name RedisConnectionString --query value -o tsv 2>/dev/null || echo "")
STORAGE_CONN=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name StorageConnectionString --query value -o tsv 2>/dev/null || echo "")
COSMOS_CONN=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name CosmosDbConnectionString --query value -o tsv 2>/dev/null || echo "")
AZURE_SPEECH_KEY=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name AzureSpeechKey --query value -o tsv 2>/dev/null || echo "")

# Get tenant ID
TENANT_ID=$(az account show --query tenantId -o tsv)

# Function to deploy a service
deploy_service() {
    local SERVICE_NAME=$1
    local CPU=$2
    local MEMORY=$3
    local IMAGE_TAG=$4
    
    echo "Deploying $SERVICE_NAME service..."
    
    # Base environment variables for all services
    local ENV_VARS="ASPNETCORE_ENVIRONMENT=$ENVIRONMENT \
ASPNETCORE_URLS=http://+:80 \
SERVICE_NAME=$SERVICE_NAME \
CONTAINER_MODE=true \
KeyVaultName=$KEY_VAULT_NAME \
AzureAd__TenantId=$TENANT_ID \
AzureAd__Instance=https://login.microsoftonline.com/ \
Cors__AllowedOrigins__0=https://voicecode.dev \
Cors__AllowedOrigins__1=http://localhost:3000"

    # Add service-specific environment variables
    case $SERVICE_NAME in
        "claude")
            ENV_VARS="$ENV_VARS \
ANTHROPIC_API_KEY=$CLAUDE_API_KEY"
            ;;
        "dispatcher")
            ENV_VARS="$ENV_VARS \
ServiceEndpoints__STTService=https://voicecode-dev-eus-stt.eastus.azurecontainer.io \
ServiceEndpoints__ClaudeService=https://voicecode-dev-eus-claude.eastus.azurecontainer.io \
ServiceEndpoints__RouterService=https://voicecode-dev-eus-router.eastus.azurecontainer.io \
ServiceEndpoints__GeneratorService=https://voicecode-dev-eus-generator.eastus.azurecontainer.io \
ServiceEndpoints__TTSService=https://voicecode-dev-eus-tts.eastus.azurecontainer.io"
            ;;
        "generator")
            ENV_VARS="$ENV_VARS \
OPENAI_API_KEY=$OPENAI_API_KEY"
            ;;
        "stt" | "tts")
            ENV_VARS="$ENV_VARS \
AzureSpeech__Key=$AZURE_SPEECH_KEY \
AzureSpeech__Region=eastus"
            ;;
    esac

    # Secure environment variables (connection strings)
    local SECURE_ENV_VARS="ApplicationInsights__ConnectionString=$APP_INSIGHTS_CONN \
ConnectionStrings__ServiceBus=$SERVICE_BUS_CONN \
ConnectionStrings__Redis=$REDIS_CONN \
ConnectionStrings__Storage=$STORAGE_CONN \
ConnectionStrings__CosmosDb=$COSMOS_CONN"

    # Create the container instance
    az container create \
        --resource-group $RESOURCE_GROUP \
        --name "voicecode-dev-eus-${SERVICE_NAME}-ci" \
        --image "${ACR_SERVER}/voicecode/${SERVICE_NAME}-service:${IMAGE_TAG}" \
        --cpu $CPU \
        --memory $MEMORY \
        --registry-login-server $ACR_SERVER \
        --registry-username $ACR_USERNAME \
        --registry-password $ACR_PASSWORD \
        --dns-name-label "voicecode-dev-eus-${SERVICE_NAME}" \
        --ports 80 443 \
        --environment-variables $ENV_VARS \
        --secure-environment-variables $SECURE_ENV_VARS \
        --restart-policy Always \
        --location $LOCATION \
        --assign-identity "[system]" \
        --os-type Linux \
        --protocol TCP \
        --output table

    # Wait for container to be ready
    echo "Waiting for $SERVICE_NAME to be ready..."
    sleep 10
    
    # Check health endpoint
    FQDN=$(az container show --resource-group $RESOURCE_GROUP --name "voicecode-dev-eus-${SERVICE_NAME}-ci" --query ipAddress.fqdn -o tsv)
    echo "Service deployed at: https://$FQDN"
    
    # Test health endpoint
    echo "Testing health endpoint..."
    curl -s -o /dev/null -w "Health check status: %{http_code}\n" "http://$FQDN/health" || true
    
    echo "✓ $SERVICE_NAME service deployed successfully"
    echo "----------------------------------------"
}

# Get latest image tags from ACR
echo "Getting latest image tags from ACR..."
LATEST_TAG=$(az acr repository show-tags --name $ACR_NAME --repository voicecode/claude-service --orderby time_desc --top 1 -o tsv 2>/dev/null || echo "v3")

echo "Using image tag: $LATEST_TAG"
echo "========================================="

# Deploy services in the specified order
deploy_service "claude" "1.0" "2.0" "$LATEST_TAG"
deploy_service "stt" "0.5" "1.0" "$LATEST_TAG"
deploy_service "tts" "0.5" "1.0" "$LATEST_TAG"
deploy_service "dispatcher" "0.5" "1.0" "$LATEST_TAG"
deploy_service "generator" "1.0" "2.0" "$LATEST_TAG"
deploy_service "router" "0.5" "1.0" "$LATEST_TAG"

echo "========================================="
echo "All services deployed successfully!"
echo "========================================="

# List all deployed container instances
echo "Deployed container instances:"
az container list --resource-group $RESOURCE_GROUP --output table

# Show service URLs
echo ""
echo "Service URLs:"
echo "Claude: https://voicecode-dev-eus-claude.eastus.azurecontainer.io"
echo "STT: https://voicecode-dev-eus-stt.eastus.azurecontainer.io"
echo "TTS: https://voicecode-dev-eus-tts.eastus.azurecontainer.io"
echo "Dispatcher: https://voicecode-dev-eus-dispatcher.eastus.azurecontainer.io"
echo "Generator: https://voicecode-dev-eus-generator.eastus.azurecontainer.io"
echo "Router: https://voicecode-dev-eus-router.eastus.azurecontainer.io"