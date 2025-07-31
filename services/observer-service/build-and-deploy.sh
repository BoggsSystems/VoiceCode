#!/bin/bash

# Build and deploy Observer Service

# Variables
SERVICE_NAME="observer-service"
REGISTRY_NAME="voicecoderegistry"
RESOURCE_GROUP="voicecode-rg"
IMAGE_TAG="${1:-latest}"

echo "Building Observer Service Docker image..."

# Build from repository root
cd ../..

# Build the Docker image
docker build \
    -f services/observer-service/Dockerfile \
    -t $REGISTRY_NAME.azurecr.io/voicecode-$SERVICE_NAME:$IMAGE_TAG \
    .

# Login to Azure Container Registry
echo "Logging in to Azure Container Registry..."
az acr login --name $REGISTRY_NAME

# Push the image
echo "Pushing image to registry..."
docker push $REGISTRY_NAME.azurecr.io/voicecode-$SERVICE_NAME:$IMAGE_TAG

# Deploy to Container Apps
echo "Deploying to Azure Container Apps..."
az containerapp update \
    --name voicecode-observer \
    --resource-group $RESOURCE_GROUP \
    --image $REGISTRY_NAME.azurecr.io/voicecode-$SERVICE_NAME:$IMAGE_TAG

echo "Observer Service deployed successfully!"