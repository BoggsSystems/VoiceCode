#!/bin/bash

# Deploy Voice Intelligence Service to Azure Container Apps

set -e

RESOURCE_GROUP="voicecode-rg"
ACR_NAME="voicecodebuildsprod"
IMAGE_TAG="voice-intelligence-v1"
SERVICE_NAME="voicecode-voice-intelligence"

echo "Building and deploying Voice Intelligence Service..."

# Build Docker image
echo "Building Docker image..."
docker buildx build --platform linux/amd64 \
    -f services/voice-intelligence-service/Dockerfile \
    -t $ACR_NAME.azurecr.io/voice-intelligence-service:$IMAGE_TAG .

# Push to ACR
echo "Pushing to Azure Container Registry..."
docker push $ACR_NAME.azurecr.io/voice-intelligence-service:$IMAGE_TAG

# Deploy to Container Apps
echo "Deploying to Azure Container Apps..."
az containerapp create \
    --name $SERVICE_NAME \
    --resource-group $RESOURCE_GROUP \
    --environment voicecode-env \
    --image $ACR_NAME.azurecr.io/voice-intelligence-service:$IMAGE_TAG \
    --target-port 80 \
    --ingress external \
    --registry-server $ACR_NAME.azurecr.io \
    --cpu 0.5 \
    --memory 1 \
    --min-replicas 1 \
    --max-replicas 3 \
    --env-vars \
        "AZURE_SERVICE_BUS_CONNECTION_STRING=secretref:service-bus-connection-string" \
        "OPENAI_API_KEY=secretref:openai-api-key" \
    --secrets \
        "service-bus-connection-string=$(az servicebus namespace authorization-rule keys list --name RootManageSharedAccessKey --namespace-name voicecodebus-0724 --resource-group $RESOURCE_GROUP --query primaryConnectionString -o tsv)" \
        "openai-api-key=$(az keyvault secret show --vault-name voicecodedevkveus --name OpenAIApiKey --query value -o tsv)"

echo "Voice Intelligence Service deployed successfully!"
echo "URL: https://$SERVICE_NAME.orangewater-a2f689a8.eastus.azurecontainerapps.io"