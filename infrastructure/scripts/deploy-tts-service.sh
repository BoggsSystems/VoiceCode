#!/bin/bash

# Deploy TTS Service to Azure Container Apps

set -e

RESOURCE_GROUP="voicecode-rg"
ACR_NAME="voicecodebuildsprod"
IMAGE_TAG="tts-v1"
SERVICE_NAME="voicecode-tts"

echo "Building and deploying TTS Service..."

# Build Docker image
echo "Building Docker image..."
docker buildx build --platform linux/amd64 \
    -f services/tts-service/Dockerfile \
    -t $ACR_NAME.azurecr.io/tts-service:$IMAGE_TAG .

# Push to ACR
echo "Pushing to Azure Container Registry..."
docker push $ACR_NAME.azurecr.io/tts-service:$IMAGE_TAG

# Deploy to Container Apps
echo "Deploying to Azure Container Apps..."
az containerapp create \
    --name $SERVICE_NAME \
    --resource-group $RESOURCE_GROUP \
    --environment voicecode-env \
    --image $ACR_NAME.azurecr.io/tts-service:$IMAGE_TAG \
    --target-port 80 \
    --ingress external \
    --registry-server $ACR_NAME.azurecr.io \
    --cpu 0.5 \
    --memory 1 \
    --min-replicas 1 \
    --max-replicas 3 \
    --env-vars \
        "AZURE_SERVICE_BUS_CONNECTION_STRING=secretref:service-bus-connection-string" \
        "AZURE_SPEECH_KEY=secretref:azure-speech-key" \
        "AZURE_SPEECH_REGION=eastus" \
    --secrets \
        "service-bus-connection-string=$(az servicebus namespace authorization-rule keys list --name RootManageSharedAccessKey --namespace-name voicecodebus-0724 --resource-group $RESOURCE_GROUP --query primaryConnectionString -o tsv)" \
        "azure-speech-key=$(az keyvault secret show --vault-name voicecodedevkveus --name AzureSpeechKey --query value -o tsv)"

echo "TTS Service deployed successfully!"
echo "URL: https://$SERVICE_NAME.orangewater-a2f689a8.eastus.azurecontainerapps.io"