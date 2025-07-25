#!/bin/bash

# VoiceCode Container Instances Simple Deployment
# Direct Azure CLI commands for deployment

set -e

# Configuration
RESOURCE_GROUP="voicecode-rg"
LOCATION="eastus"
KEY_VAULT_NAME="voicecodedevkveus"
ACR_NAME="voicecodebuildsprod"
ACR_SERVER="${ACR_NAME}.azurecr.io"
PROJECT_PREFIX="voicecode-dev-eus"
IMAGE_TAG="v3"

# API Keys
CLAUDE_API_KEY="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"
OPENAI_API_KEY="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"

echo "Getting ACR credentials..."
ACR_USERNAME=$(az acr credential show --name $ACR_NAME --query username -o tsv)
ACR_PASSWORD=$(az acr credential show --name $ACR_NAME --query passwords[0].value -o tsv)

echo "Getting Key Vault secrets..."
APP_INSIGHTS_CONN=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name ApplicationInsightsConnectionString --query value -o tsv 2>/dev/null || echo "")
SERVICE_BUS_CONN=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name ServiceBusConnectionString --query value -o tsv 2>/dev/null || echo "")
REDIS_CONN=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name RedisConnectionString --query value -o tsv 2>/dev/null || echo "")
STORAGE_CONN=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name StorageConnectionString --query value -o tsv 2>/dev/null || echo "")
COSMOS_CONN=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name CosmosDbConnectionString --query value -o tsv 2>/dev/null || echo "")
AZURE_SPEECH_KEY=$(az keyvault secret show --vault-name $KEY_VAULT_NAME --name AzureSpeechKey --query value -o tsv 2>/dev/null || echo "")
TENANT_ID=$(az account show --query tenantId -o tsv)

# Deploy Claude Service
echo "Deploying Claude service..."
az container create \
    --resource-group $RESOURCE_GROUP \
    --name "${PROJECT_PREFIX}-claude-ci" \
    --image "${ACR_SERVER}/voicecode/claude-service:${IMAGE_TAG}" \
    --cpu 1.0 \
    --memory 2.0 \
    --registry-login-server $ACR_SERVER \
    --registry-username $ACR_USERNAME \
    --registry-password "$ACR_PASSWORD" \
    --dns-name-label "${PROJECT_PREFIX}-claude" \
    --ports 80 443 \
    --location $LOCATION \
    --restart-policy Always \
    --os-type Linux \
    --environment-variables \
        ASPNETCORE_ENVIRONMENT=Production \
        ASPNETCORE_URLS="http://+:80" \
        SERVICE_NAME=claude \
        CONTAINER_MODE=true \
        KeyVaultName=$KEY_VAULT_NAME \
        AzureAd__TenantId=$TENANT_ID \
        AzureAd__Instance="https://login.microsoftonline.com/" \
        Cors__AllowedOrigins__0="https://voicecode.dev" \
        Cors__AllowedOrigins__1="http://localhost:3000" \
    --secure-environment-variables \
        ANTHROPIC_API_KEY="$CLAUDE_API_KEY" \
        ApplicationInsights__ConnectionString="$APP_INSIGHTS_CONN" \
        ConnectionStrings__ServiceBus="$SERVICE_BUS_CONN" \
        ConnectionStrings__Redis="$REDIS_CONN" \
        ConnectionStrings__Storage="$STORAGE_CONN" \
        ConnectionStrings__CosmosDb="$COSMOS_CONN" \
    --output table

echo "Claude service deployed. Waiting 30 seconds..."
sleep 30

# Deploy STT Service
echo "Deploying STT service..."
az container create \
    --resource-group $RESOURCE_GROUP \
    --name "${PROJECT_PREFIX}-stt-ci" \
    --image "${ACR_SERVER}/voicecode/stt-service:${IMAGE_TAG}" \
    --cpu 0.5 \
    --memory 1.0 \
    --registry-login-server $ACR_SERVER \
    --registry-username $ACR_USERNAME \
    --registry-password "$ACR_PASSWORD" \
    --dns-name-label "${PROJECT_PREFIX}-stt" \
    --ports 80 443 \
    --location $LOCATION \
    --restart-policy Always \
    --os-type Linux \
    --environment-variables \
        ASPNETCORE_ENVIRONMENT=Production \
        ASPNETCORE_URLS="http://+:80" \
        SERVICE_NAME=stt \
        CONTAINER_MODE=true \
        KeyVaultName=$KEY_VAULT_NAME \
        AzureAd__TenantId=$TENANT_ID \
        AzureAd__Instance="https://login.microsoftonline.com/" \
        Cors__AllowedOrigins__0="https://voicecode.dev" \
        Cors__AllowedOrigins__1="http://localhost:3000" \
        AzureSpeech__Region=$LOCATION \
    --secure-environment-variables \
        AzureSpeech__Key="$AZURE_SPEECH_KEY" \
        ApplicationInsights__ConnectionString="$APP_INSIGHTS_CONN" \
        ConnectionStrings__ServiceBus="$SERVICE_BUS_CONN" \
        ConnectionStrings__Redis="$REDIS_CONN" \
        ConnectionStrings__Storage="$STORAGE_CONN" \
        ConnectionStrings__CosmosDb="$COSMOS_CONN" \
    --output table

echo "STT service deployed. Waiting 30 seconds..."
sleep 30

# Deploy TTS Service
echo "Deploying TTS service..."
az container create \
    --resource-group $RESOURCE_GROUP \
    --name "${PROJECT_PREFIX}-tts-ci" \
    --image "${ACR_SERVER}/voicecode/tts-service:${IMAGE_TAG}" \
    --cpu 0.5 \
    --memory 1.0 \
    --registry-login-server $ACR_SERVER \
    --registry-username $ACR_USERNAME \
    --registry-password "$ACR_PASSWORD" \
    --dns-name-label "${PROJECT_PREFIX}-tts" \
    --ports 80 443 \
    --location $LOCATION \
    --restart-policy Always \
    --os-type Linux \
    --environment-variables \
        ASPNETCORE_ENVIRONMENT=Production \
        ASPNETCORE_URLS="http://+:80" \
        SERVICE_NAME=tts \
        CONTAINER_MODE=true \
        KeyVaultName=$KEY_VAULT_NAME \
        AzureAd__TenantId=$TENANT_ID \
        AzureAd__Instance="https://login.microsoftonline.com/" \
        Cors__AllowedOrigins__0="https://voicecode.dev" \
        Cors__AllowedOrigins__1="http://localhost:3000" \
        AzureSpeech__Region=$LOCATION \
    --secure-environment-variables \
        AzureSpeech__Key="$AZURE_SPEECH_KEY" \
        ApplicationInsights__ConnectionString="$APP_INSIGHTS_CONN" \
        ConnectionStrings__ServiceBus="$SERVICE_BUS_CONN" \
        ConnectionStrings__Redis="$REDIS_CONN" \
        ConnectionStrings__Storage="$STORAGE_CONN" \
        ConnectionStrings__CosmosDb="$COSMOS_CONN" \
    --output table

echo "TTS service deployed. Waiting 30 seconds..."
sleep 30

# Deploy Dispatcher Service
echo "Deploying Dispatcher service..."
az container create \
    --resource-group $RESOURCE_GROUP \
    --name "${PROJECT_PREFIX}-dispatcher-ci" \
    --image "${ACR_SERVER}/voicecode/dispatcher-service:${IMAGE_TAG}" \
    --cpu 0.5 \
    --memory 1.0 \
    --registry-login-server $ACR_SERVER \
    --registry-username $ACR_USERNAME \
    --registry-password "$ACR_PASSWORD" \
    --dns-name-label "${PROJECT_PREFIX}-dispatcher" \
    --ports 80 443 \
    --location $LOCATION \
    --restart-policy Always \
    --os-type Linux \
    --environment-variables \
        ASPNETCORE_ENVIRONMENT=Production \
        ASPNETCORE_URLS="http://+:80" \
        SERVICE_NAME=dispatcher \
        CONTAINER_MODE=true \
        KeyVaultName=$KEY_VAULT_NAME \
        AzureAd__TenantId=$TENANT_ID \
        AzureAd__Instance="https://login.microsoftonline.com/" \
        Cors__AllowedOrigins__0="https://voicecode.dev" \
        Cors__AllowedOrigins__1="http://localhost:3000" \
        ServiceEndpoints__STTService="https://${PROJECT_PREFIX}-stt.${LOCATION}.azurecontainer.io" \
        ServiceEndpoints__ClaudeService="https://${PROJECT_PREFIX}-claude.${LOCATION}.azurecontainer.io" \
        ServiceEndpoints__RouterService="https://${PROJECT_PREFIX}-router.${LOCATION}.azurecontainer.io" \
        ServiceEndpoints__GeneratorService="https://${PROJECT_PREFIX}-generator.${LOCATION}.azurecontainer.io" \
        ServiceEndpoints__TTSService="https://${PROJECT_PREFIX}-tts.${LOCATION}.azurecontainer.io" \
    --secure-environment-variables \
        ApplicationInsights__ConnectionString="$APP_INSIGHTS_CONN" \
        ConnectionStrings__ServiceBus="$SERVICE_BUS_CONN" \
        ConnectionStrings__Redis="$REDIS_CONN" \
        ConnectionStrings__Storage="$STORAGE_CONN" \
        ConnectionStrings__CosmosDb="$COSMOS_CONN" \
    --output table

echo "Dispatcher service deployed. Waiting 30 seconds..."
sleep 30

# Deploy Generator Service
echo "Deploying Generator service..."
az container create \
    --resource-group $RESOURCE_GROUP \
    --name "${PROJECT_PREFIX}-generator-ci" \
    --image "${ACR_SERVER}/voicecode/generator-service:${IMAGE_TAG}" \
    --cpu 1.0 \
    --memory 2.0 \
    --registry-login-server $ACR_SERVER \
    --registry-username $ACR_USERNAME \
    --registry-password "$ACR_PASSWORD" \
    --dns-name-label "${PROJECT_PREFIX}-generator" \
    --ports 80 443 \
    --location $LOCATION \
    --restart-policy Always \
    --os-type Linux \
    --environment-variables \
        ASPNETCORE_ENVIRONMENT=Production \
        ASPNETCORE_URLS="http://+:80" \
        SERVICE_NAME=generator \
        CONTAINER_MODE=true \
        KeyVaultName=$KEY_VAULT_NAME \
        AzureAd__TenantId=$TENANT_ID \
        AzureAd__Instance="https://login.microsoftonline.com/" \
        Cors__AllowedOrigins__0="https://voicecode.dev" \
        Cors__AllowedOrigins__1="http://localhost:3000" \
    --secure-environment-variables \
        OPENAI_API_KEY="$OPENAI_API_KEY" \
        ApplicationInsights__ConnectionString="$APP_INSIGHTS_CONN" \
        ConnectionStrings__ServiceBus="$SERVICE_BUS_CONN" \
        ConnectionStrings__Redis="$REDIS_CONN" \
        ConnectionStrings__Storage="$STORAGE_CONN" \
        ConnectionStrings__CosmosDb="$COSMOS_CONN" \
    --output table

echo "Generator service deployed. Waiting 30 seconds..."
sleep 30

# Deploy Router Service
echo "Deploying Router service..."
az container create \
    --resource-group $RESOURCE_GROUP \
    --name "${PROJECT_PREFIX}-router-ci" \
    --image "${ACR_SERVER}/voicecode/router-service:${IMAGE_TAG}" \
    --cpu 0.5 \
    --memory 1.0 \
    --registry-login-server $ACR_SERVER \
    --registry-username $ACR_USERNAME \
    --registry-password "$ACR_PASSWORD" \
    --dns-name-label "${PROJECT_PREFIX}-router" \
    --ports 80 443 \
    --location $LOCATION \
    --restart-policy Always \
    --os-type Linux \
    --environment-variables \
        ASPNETCORE_ENVIRONMENT=Production \
        ASPNETCORE_URLS="http://+:80" \
        SERVICE_NAME=router \
        CONTAINER_MODE=true \
        KeyVaultName=$KEY_VAULT_NAME \
        AzureAd__TenantId=$TENANT_ID \
        AzureAd__Instance="https://login.microsoftonline.com/" \
        Cors__AllowedOrigins__0="https://voicecode.dev" \
        Cors__AllowedOrigins__1="http://localhost:3000" \
    --secure-environment-variables \
        ApplicationInsights__ConnectionString="$APP_INSIGHTS_CONN" \
        ConnectionStrings__ServiceBus="$SERVICE_BUS_CONN" \
        ConnectionStrings__Redis="$REDIS_CONN" \
        ConnectionStrings__Storage="$STORAGE_CONN" \
        ConnectionStrings__CosmosDb="$COSMOS_CONN" \
    --output table

echo "Router service deployed."

echo "========================================="
echo "All services deployed successfully!"
echo "========================================="

# List all deployed container instances
echo ""
echo "Deployed container instances:"
az container list --resource-group $RESOURCE_GROUP --output table

# Show service URLs
echo ""
echo "Service URLs:"
echo "Claude: https://${PROJECT_PREFIX}-claude.${LOCATION}.azurecontainer.io"
echo "STT: https://${PROJECT_PREFIX}-stt.${LOCATION}.azurecontainer.io"
echo "TTS: https://${PROJECT_PREFIX}-tts.${LOCATION}.azurecontainer.io"
echo "Dispatcher: https://${PROJECT_PREFIX}-dispatcher.${LOCATION}.azurecontainer.io"
echo "Generator: https://${PROJECT_PREFIX}-generator.${LOCATION}.azurecontainer.io"
echo "Router: https://${PROJECT_PREFIX}-router.${LOCATION}.azurecontainer.io"

# Test health endpoints
echo ""
echo "Testing health endpoints..."
echo "Claude health: $(curl -s -o /dev/null -w '%{http_code}' http://${PROJECT_PREFIX}-claude.${LOCATION}.azurecontainer.io/health || echo 'Not ready')"
echo "STT health: $(curl -s -o /dev/null -w '%{http_code}' http://${PROJECT_PREFIX}-stt.${LOCATION}.azurecontainer.io/health || echo 'Not ready')"
echo "TTS health: $(curl -s -o /dev/null -w '%{http_code}' http://${PROJECT_PREFIX}-tts.${LOCATION}.azurecontainer.io/health || echo 'Not ready')"
echo "Dispatcher health: $(curl -s -o /dev/null -w '%{http_code}' http://${PROJECT_PREFIX}-dispatcher.${LOCATION}.azurecontainer.io/health || echo 'Not ready')"
echo "Generator health: $(curl -s -o /dev/null -w '%{http_code}' http://${PROJECT_PREFIX}-generator.${LOCATION}.azurecontainer.io/health || echo 'Not ready')"
echo "Router health: $(curl -s -o /dev/null -w '%{http_code}' http://${PROJECT_PREFIX}-router.${LOCATION}.azurecontainer.io/health || echo 'Not ready')"