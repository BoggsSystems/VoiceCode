#!/bin/bash

# Migrate STT Service from Container Instance to Container App

set -e

echo "🚀 Starting STT Service migration to Container App..."

# Configuration
RESOURCE_GROUP="voicecode-rg"
LOCATION="eastus"
ENVIRONMENT_NAME="voicecode-env"
APP_NAME="voicecode-stt"
IMAGE="voicecodebuildsprod.azurecr.io/voicecode-stt-service:latest"
CPU="1.0"
MEMORY="2.0Gi"

# Get ACR credentials
ACR_NAME="voicecodebuildsprod"
echo "🔑 Getting ACR credentials..."
ACR_USERNAME=$(az acr credential show --name $ACR_NAME --query username -o tsv)
ACR_PASSWORD=$(az acr credential show --name $ACR_NAME --query passwords[0].value -o tsv)

# Check if environment exists
echo "🔍 Checking Container Apps environment..."
ENV_EXISTS=$(az containerapp env show --name $ENVIRONMENT_NAME --resource-group $RESOURCE_GROUP --query name -o tsv 2>/dev/null || echo "")

if [ -z "$ENV_EXISTS" ]; then
    echo "❌ Container Apps environment not found. Creating one..."
    az containerapp env create \
        --name $ENVIRONMENT_NAME \
        --resource-group $RESOURCE_GROUP \
        --location $LOCATION
else
    echo "✅ Using existing Container Apps environment: $ENVIRONMENT_NAME"
fi

# Get Key Vault name
KEY_VAULT_NAME="voicecodedevkveus"
STATIC_WEB_APP_URL="https://gentle-river-0d0d70a0f.1.azurestaticapps.net"

echo "📦 Creating STT Container App..."
az containerapp create \
    --name $APP_NAME \
    --resource-group $RESOURCE_GROUP \
    --environment $ENVIRONMENT_NAME \
    --image $IMAGE \
    --cpu $CPU \
    --memory $MEMORY \
    --min-replicas 1 \
    --max-replicas 3 \
    --ingress external \
    --target-port 80 \
    --registry-server voicecodebuildsprod.azurecr.io \
    --registry-username $ACR_USERNAME \
    --registry-password $ACR_PASSWORD \
    --secrets \
        "acr-password=$ACR_PASSWORD" \
    --env-vars \
        "ASPNETCORE_ENVIRONMENT=Production" \
        "ASPNETCORE_URLS=http://+:80" \
        "SERVICE_NAME=stt" \
        "CONTAINER_MODE=true" \
        "KeyVaultName=$KEY_VAULT_NAME" \
        "AzureAd__TenantId=2c45017b-71d8-4cd7-ae62-74745f21cf10" \
        "AzureAd__Instance=https://login.microsoftonline.com/" \
        "ServiceEndpoints__Dispatcher=https://voicecode-dispatcher.orangewater-a2f689a8.eastus.azurecontainerapps.io" \
        "Cors__AllowedOrigins__0=https://voicecode.dev" \
        "Cors__AllowedOrigins__1=http://localhost:3000" \
        "Cors__AllowedOrigins__2=$STATIC_WEB_APP_URL" \
        "Cors__AllowedOrigins__3=https://*.azurecontainerapps.io" \
    --query properties.configuration.ingress.fqdn -o tsv

# Get the new URL
echo ""
echo "⏳ Waiting for Container App to be ready..."
sleep 10

STT_URL=$(az containerapp show \
    --name $APP_NAME \
    --resource-group $RESOURCE_GROUP \
    --query properties.configuration.ingress.fqdn -o tsv)

echo ""
echo "✅ STT Service migrated successfully!"
echo "🌐 New HTTPS URL: https://$STT_URL"
echo ""
echo "📝 Next steps:"
echo "1. Update your webapp configuration with the new STT URL"
echo "2. Delete the old Container Instance when ready"
echo ""
echo "To delete the old Container Instance:"
echo "az container delete --resource-group $RESOURCE_GROUP --name voicecode-dev-eus-stt-ci --yes"