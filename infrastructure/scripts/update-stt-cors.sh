#!/bin/bash

# Update STT service CORS to allow all origins (temporary for testing)

RESOURCE_GROUP="voicecode-rg"
CONTAINER_NAME="voicecode-dev-eus-stt-ci"
STATIC_WEB_APP_URL="https://gentle-river-0d0d70a0f.1.azurestaticapps.net"

echo "🔧 Updating STT service CORS settings..."

# Delete existing container
echo "Deleting existing container..."
az container delete --resource-group $RESOURCE_GROUP --name $CONTAINER_NAME --yes

# Wait a bit
sleep 10

# Get ACR credentials
ACR_NAME="voicecodebuildsprod"
ACR_USERNAME=$(az acr credential show --name $ACR_NAME --query username -o tsv)
ACR_PASSWORD=$(az acr credential show --name $ACR_NAME --query passwords[0].value -o tsv)

# Recreate with updated CORS
echo "Creating container with updated CORS..."
az container create \
    --resource-group $RESOURCE_GROUP \
    --name $CONTAINER_NAME \
    --image voicecodebuildsprod.azurecr.io/voicecode-stt-service:latest \
    --cpu 2.0 \
    --memory 4.0 \
    --ip-address public \
    --location eastus \
    --os-type Linux \
    --registry-username $ACR_USERNAME \
    --registry-password $ACR_PASSWORD \
    --assign-identity \
    --environment-variables \
        "ASPNETCORE_ENVIRONMENT=Production" \
        "ASPNETCORE_URLS=http://+:80" \
        "SERVICE_NAME=stt" \
        "CONTAINER_MODE=true" \
        "KeyVaultName=voicecodedevkveus" \
        "AzureAd__TenantId=2c45017b-71d8-4cd7-ae62-74745f21cf10" \
        "AzureAd__Instance=https://login.microsoftonline.com/" \
        "ServiceEndpoints__Dispatcher=http://57.151.42.14" \
        "Cors__AllowedOrigins__0=*" \
        "Cors__AllowedMethods__0=*" \
        "Cors__AllowedHeaders__0=*" \
        "Cors__AllowCredentials=false" \
    --ports 80 \
    --restart-policy Always

echo "⏳ Waiting for container to be ready..."
sleep 30

# Get new IP
NEW_IP=$(az container show --resource-group $RESOURCE_GROUP --name $CONTAINER_NAME --query ipAddress.ip -o tsv)
echo "✅ STT service updated with new IP: $NEW_IP"
echo "📝 Update your webapp configuration with the new IP if it changed"