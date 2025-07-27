#!/bin/bash

# Deploy Dispatcher Service with Auth Fix to Azure Container Apps
# This script includes the authentication fix for SignalR connections

set -e

echo "========================================="
echo "Deploying Dispatcher Service with Auth Fix to Container Apps"
echo "========================================="

# Configuration
RESOURCE_GROUP="voicecode-rg"
LOCATION="eastus"
REGISTRY_NAME="voicecodebuildsprod"
IMAGE_NAME="dispatcher-service"
IMAGE_TAG="session-fix-$(date +%Y%m%d-%H%M%S)"
CONTAINER_APP_NAME="voicecode-dispatcher"
CONTAINER_APP_ENV="voicecode-env-eus"

# Service Bus configuration
SERVICE_BUS_NAMESPACE="voicecodebus-0724"

# Build and push Docker image
echo "Building Docker image..."
cd ../..  # Go to repository root

# Build the Docker image from repository root for linux/amd64
docker build --platform linux/amd64 -t $IMAGE_NAME:$IMAGE_TAG -f services/dispatcher-service/Dockerfile .

# Tag for ACR
docker tag $IMAGE_NAME:$IMAGE_TAG $REGISTRY_NAME.azurecr.io/$IMAGE_NAME:$IMAGE_TAG

# Login to ACR
echo "Logging in to Azure Container Registry..."
az acr login --name $REGISTRY_NAME

# Push to ACR
echo "Pushing image to registry..."
docker push $REGISTRY_NAME.azurecr.io/$IMAGE_NAME:$IMAGE_TAG

# Get Service Bus connection string
SERVICE_BUS_CONNECTION=$(az servicebus namespace authorization-rule keys list \
    --resource-group $RESOURCE_GROUP \
    --namespace-name $SERVICE_BUS_NAMESPACE \
    --name RootManageSharedAccessKey \
    --query primaryConnectionString -o tsv)

# Get Application Insights connection string
APP_INSIGHTS_CONNECTION=$(az monitor app-insights component show \
    --app voicecode-insights \
    --resource-group $RESOURCE_GROUP \
    --query connectionString -o tsv)

# Update the container app with new image
echo "Updating Container App..."
az containerapp update \
    --name $CONTAINER_APP_NAME \
    --resource-group $RESOURCE_GROUP \
    --image $REGISTRY_NAME.azurecr.io/$IMAGE_NAME:$IMAGE_TAG \
    --set-env-vars \
        ASPNETCORE_ENVIRONMENT=Production \
        ASPNETCORE_URLS="http://+:80" \
        ConnectionStrings__ServiceBus="$SERVICE_BUS_CONNECTION" \
        ConnectionStrings__Redis="" \
        ApplicationInsights__ConnectionString="$APP_INSIGHTS_CONNECTION" \
        ServiceEndpoints__STTService="https://voicecode-stt.orangewater-a2f689a8.eastus.azurecontainerapps.io" \
        ServiceEndpoints__ClaudeService="http://4.157.96.157" \
        ServiceEndpoints__RouterService="https://voicecode-router.orangewater-a2f689a8.eastus.azurecontainerapps.io" \
        ServiceEndpoints__GeneratorService="http://52.226.90.226" \
        ServiceEndpoints__TTSService="https://voicecode-tts.orangewater-a2f689a8.eastus.azurecontainerapps.io" \
        Cors__AllowedOrigins__0="http://localhost:3000" \
        Cors__AllowedOrigins__1="https://localhost:3000" \
        Cors__AllowedOrigins__2="https://yellow-coast-05996a50f.1.azurestaticapps.net" \
        Cors__AllowedOrigins__3="https://voicecode-webapp.azurewebsites.net" \
        Logging__LogLevel__Default="Information" \
        Logging__LogLevel__Microsoft.AspNetCore.SignalR="Debug" \
        Logging__LogLevel__VoiceCode.DispatcherService.Middleware="Debug" \
    --min-replicas 1 \
    --max-replicas 1

# Get container app details
FQDN=$(az containerapp show \
    --resource-group $RESOURCE_GROUP \
    --name $CONTAINER_APP_NAME \
    --query properties.configuration.ingress.fqdn -o tsv)

echo "========================================="
echo "Dispatcher Service deployed successfully!"
echo "URL: https://$FQDN"
echo "Image: $REGISTRY_NAME.azurecr.io/$IMAGE_NAME:$IMAGE_TAG"
echo "========================================="

# Test the health endpoint
echo "Testing health endpoint..."
curl -s https://$FQDN/health || echo "Health check endpoint"

# Test the auth check endpoint
echo "Testing auth check endpoint..."
curl -s https://$FQDN/api/authtest/check || echo "Auth test endpoint"