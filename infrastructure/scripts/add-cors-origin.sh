#!/bin/bash

# Add new CORS origin to STT service

set -e

echo "🔧 Adding new CORS origin to STT service..."

RESOURCE_GROUP="voicecode-rg"
CONTAINER_NAME="voicecode-dev-eus-stt-ci"
NEW_CORS_URL="https://gentle-river-0d0d70a0f.1.azurestaticapps.net"

# Get current container configuration
echo "📌 Getting current container configuration..."
CONTAINER_CONFIG=$(az container show \
    --resource-group $RESOURCE_GROUP \
    --name $CONTAINER_NAME \
    -o json)

# Extract necessary information
IMAGE=$(echo "$CONTAINER_CONFIG" | jq -r '.containers[0].image')
CPU=$(echo "$CONTAINER_CONFIG" | jq -r '.containers[0].resources.requests.cpu')
MEMORY=$(echo "$CONTAINER_CONFIG" | jq -r '.containers[0].resources.requests.memoryInGb')
IP_ADDRESS=$(echo "$CONTAINER_CONFIG" | jq -r '.ipAddress.ip')
LOCATION=$(echo "$CONTAINER_CONFIG" | jq -r '.location')

echo "  Image: $IMAGE"
echo "  CPU: $CPU"
echo "  Memory: ${MEMORY}GB"
echo "  Location: $LOCATION"

# Get all current environment variables
ENV_VARS=$(echo "$CONTAINER_CONFIG" | jq -r '.containers[0].environmentVariables[] | "\(.name)=\(.value)"' | grep -v "^Cors__AllowedOrigins__" | tr '\n' ' ')

echo ""
echo "📝 Setting up CORS configuration..."
echo "  Existing origins:"
echo "    - https://voicecode.dev"
echo "    - http://localhost:3000"
echo "  Adding:"
echo "    - $NEW_CORS_URL"

# Get ACR credentials
ACR_NAME="voicecodebuildsprod"
ACR_USERNAME=$(az acr credential show --name $ACR_NAME --query username -o tsv)
ACR_PASSWORD=$(az acr credential show --name $ACR_NAME --query passwords[0].value -o tsv)

# Create new container with updated CORS settings
echo ""
echo "🚀 Updating container..."
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
    --environment-variables \
        $ENV_VARS \
        "Cors__AllowedOrigins__0=https://voicecode.dev" \
        "Cors__AllowedOrigins__1=http://localhost:3000" \
        "Cors__AllowedOrigins__2=$NEW_CORS_URL" \
    --ports 80 \
    --restart-policy Always

echo ""
echo "⏳ Waiting for service to be ready..."
sleep 30

# Verify the update
echo ""
echo "🔍 Verifying CORS settings..."
az container show \
    --resource-group $RESOURCE_GROUP \
    --name $CONTAINER_NAME \
    --query "containers[0].environmentVariables[?contains(name, 'Cors__AllowedOrigins')].{name:name, value:value}" \
    -o table

# Get new IP address
NEW_IP=$(az container show \
    --resource-group $RESOURCE_GROUP \
    --name $CONTAINER_NAME \
    --query "ipAddress.ip" -o tsv)

echo ""
echo "✅ CORS update complete!"
echo ""
echo "📱 Your static web app at $NEW_CORS_URL can now access the STT service"
echo "🌐 STT service is available at: http://$NEW_IP"
echo ""
echo "⚠️  Note: You may need to update your webapp's .env.production file if the IP changed:"
echo "   Old IP: $IP_ADDRESS"
echo "   New IP: $NEW_IP"