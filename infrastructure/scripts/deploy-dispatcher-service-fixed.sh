#!/bin/bash

# Deploy VoiceCode Dispatcher Service to Azure Container Instances

set -e

echo "🚀 Starting VoiceCode Dispatcher Service deployment..."

# Configuration
RESOURCE_GROUP="voicecode-rg"
CONTAINER_NAME="voicecode-dispatcher"
LOCATION="eastus"
IMAGE="voicecodebuildsprod.azurecr.io/voicecode-dispatcher-service:latest"
CPU="1.0"
MEMORY="2.0"

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

# Static Web App URL for CORS
STATIC_WEB_APP_URL="https://gentle-river-0d0d70a0f.1.azurestaticapps.net"

echo "📦 Creating Dispatcher container instance..."
az container create \
    --resource-group $RESOURCE_GROUP \
    --name $CONTAINER_NAME \
    --image $IMAGE \
    --cpu $CPU \
    --memory $MEMORY \
    --ip-address public \
    --location $LOCATION \
    --os-type Linux \
    --registry-username $ACR_USERNAME \
    --registry-password $ACR_PASSWORD \
    --assign-identity \
    --environment-variables \
        "ASPNETCORE_ENVIRONMENT=Production" \
        "ASPNETCORE_URLS=http://+:80" \
        "SERVICE_NAME=dispatcher" \
        "CONTAINER_MODE=true" \
        "KeyVaultName=$KEY_VAULT_NAME" \
        "AzureAd__TenantId=2c45017b-71d8-4cd7-ae62-74745f21cf10" \
        "AzureAd__Instance=https://login.microsoftonline.com/" \
        "ApplicationInsights__ConnectionString=null" \
        "ConnectionStrings__ServiceBus=$SERVICE_BUS_CONNECTION" \
        "ConnectionStrings__Redis=null" \
        "ConnectionStrings__Storage=null" \
        "ConnectionStrings__CosmosDb=null" \
        "ServiceEndpoints__STT=http://135.237.114.216" \
        "ServiceEndpoints__TTS=http://52.149.247.133" \
        "ServiceEndpoints__Claude=http://4.157.96.157" \
        "ServiceEndpoints__Generator=http://52.226.90.226" \
        "Cors__AllowedOrigins__0=http://localhost:3000" \
        "Cors__AllowedOrigins__1=https://voicecode.dev" \
        "Cors__AllowedOrigins__2=$STATIC_WEB_APP_URL" \
        "SignalR__EnableDetailedErrors=true" \
        "SignalR__KeepAliveInterval=00:00:15" \
        "SignalR__ClientTimeoutInterval=00:00:30" \
        "SignalR__HandshakeTimeout=00:00:15" \
        "SignalR__MaximumReceiveMessageSize=32768" \
    --ports 80 \
    --restart-policy Always

echo ""
echo "⏳ Waiting for container to be ready..."
sleep 30

# Get container status and IP
echo "🔍 Getting container information..."
CONTAINER_INFO=$(az container show \
    --resource-group $RESOURCE_GROUP \
    --name $CONTAINER_NAME \
    --query "{status:instanceView.state, ip:ipAddress.ip}" -o json)

STATUS=$(echo $CONTAINER_INFO | jq -r '.status')
IP_ADDRESS=$(echo $CONTAINER_INFO | jq -r '.ip')

echo ""
echo "📊 Container Status: $STATUS"
echo "🌐 Container IP: $IP_ADDRESS"

# Grant Key Vault access to the container's managed identity
echo ""
echo "🔐 Granting Key Vault access to container identity..."
IDENTITY_PRINCIPAL_ID=$(az container show \
    --resource-group $RESOURCE_GROUP \
    --name $CONTAINER_NAME \
    --query identity.principalId -o tsv)

if [ ! -z "$IDENTITY_PRINCIPAL_ID" ]; then
    az keyvault set-policy \
        --name $KEY_VAULT_NAME \
        --object-id $IDENTITY_PRINCIPAL_ID \
        --secret-permissions get list \
        2>/dev/null || echo "  Note: Key Vault access may already be configured"
fi

echo ""
echo "✅ Dispatcher deployment complete!"
echo ""
echo "📱 Update your webapp .env.production file with:"
echo "   REACT_APP_DISPATCHER_SERVICE_URL=http://$IP_ADDRESS"
echo "   REACT_APP_SIGNALR_HUB_URL=http://$IP_ADDRESS/hubs/voice"
echo ""
echo "🔧 The dispatcher service is now available at:"
echo "   HTTP API: http://$IP_ADDRESS"
echo "   SignalR Hub: http://$IP_ADDRESS/hubs/voice"
echo "   Health Check: http://$IP_ADDRESS/health"
echo ""
echo "📝 CORS is configured for:"
echo "   - http://localhost:3000"
echo "   - https://voicecode.dev"
echo "   - $STATIC_WEB_APP_URL"