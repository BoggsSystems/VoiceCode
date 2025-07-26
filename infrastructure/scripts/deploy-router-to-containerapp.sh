#!/bin/bash

# Deploy Router Service to Container App

set -e

echo "🚀 Starting Router Service deployment to Container App..."

# Configuration
RESOURCE_GROUP="voicecode-rg"
LOCATION="eastus"
ENVIRONMENT_NAME="voicecode-env"
APP_NAME="voicecode-router"
IMAGE="voicecodebuildsprod.azurecr.io/voicecode-router-service:latest"
CPU="1.0"
MEMORY="2.0Gi"

# Get ACR credentials
ACR_NAME="voicecodebuildsprod"
echo "🔑 Getting ACR credentials..."
ACR_USERNAME=$(az acr credential show --name $ACR_NAME --query username -o tsv)
ACR_PASSWORD=$(az acr credential show --name $ACR_NAME --query passwords[0].value -o tsv)

# Get Service Bus connection string
echo "🔑 Getting Service Bus connection string..."
SERVICE_BUS_CONNECTION=$(az servicebus namespace authorization-rule keys list \
    --name RootManageSharedAccessKey \
    --namespace-name voicecodebus-0724 \
    --resource-group $RESOURCE_GROUP \
    --query primaryConnectionString -o tsv)

# Get Key Vault name
KEY_VAULT_NAME="voicecodedevkveus"
STATIC_WEB_APP_URL="https://yellow-coast-05996a50f.1.azurestaticapps.net"

echo "📦 Creating Router Container App..."
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
        "servicebus-connection=$SERVICE_BUS_CONNECTION" \
        "jwt-secret=VoiceCodeDevelopmentSecretKey123!ThisShouldBeInKeyVault" \
    --env-vars \
        "ASPNETCORE_ENVIRONMENT=Production" \
        "ASPNETCORE_URLS=http://+:80" \
        "SERVICE_NAME=router" \
        "CONTAINER_MODE=true" \
        "KeyVaultName=$KEY_VAULT_NAME" \
        "AzureAd__TenantId=2c45017b-71d8-4cd7-ae62-74745f21cf10" \
        "AzureAd__Instance=https://login.microsoftonline.com/" \
        "ApplicationInsights__ConnectionString=null" \
        "ConnectionStrings__ServiceBus=secretref:servicebus-connection" \
        "ConnectionStrings__Redis=null" \
        "Authentication__JwtSecret=secretref:jwt-secret" \
        "ServiceEndpoints__STT=https://voicecode-stt.orangewater-a2f689a8.eastus.azurecontainerapps.io" \
        "ServiceEndpoints__TTS=https://voicecode-tts.orangewater-a2f689a8.eastus.azurecontainerapps.io" \
        "ServiceEndpoints__Claude=http://4.157.96.157" \
        "ServiceEndpoints__Generator=http://52.226.90.226" \
        "ServiceEndpoints__Orchestrator=https://voicecode-orchestrator.orangewater-a2f689a8.eastus.azurecontainerapps.io" \
        "ServiceEndpoints__VoiceIntelligence=https://voicecode-voice-intelligence.orangewater-a2f689a8.eastus.azurecontainerapps.io" \
        "ServiceEndpoints__Dispatcher=https://voicecode-dispatcher.orangewater-a2f689a8.eastus.azurecontainerapps.io" \
        "Cors__AllowedOrigins__0=http://localhost:3000" \
        "Cors__AllowedOrigins__1=https://voicecode.dev" \
        "Cors__AllowedOrigins__2=$STATIC_WEB_APP_URL" \
        "Cors__AllowedOrigins__3=https://*.azurecontainerapps.io" \
    --query properties.configuration.ingress.fqdn -o tsv

# Get the new URL
echo ""
echo "⏳ Waiting for Container App to be ready..."
sleep 10

ROUTER_URL=$(az containerapp show \
    --name $APP_NAME \
    --resource-group $RESOURCE_GROUP \
    --query properties.configuration.ingress.fqdn -o tsv)

echo ""
echo "✅ Router Service deployed successfully!"
echo "🌐 New HTTPS URL: https://$ROUTER_URL"
echo ""
echo "📝 Next steps:"
echo "1. Update your webapp configuration with the new Router URL"
echo "2. Grant the Container App managed identity access to Key Vault"
echo ""
echo "To grant Key Vault access:"
echo "1. Get the identity: az containerapp identity show --name $APP_NAME --resource-group $RESOURCE_GROUP"
echo "2. Grant access: az keyvault set-policy --name $KEY_VAULT_NAME --resource-group voicecode-dev-eus-rg --object-id <principalId> --secret-permissions get list"