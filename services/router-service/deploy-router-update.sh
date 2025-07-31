#!/bin/bash

echo "🚀 Deploying Router Service Update..."

# Set variables
RESOURCE_GROUP="voicecode-rg"
REGISTRY="voicecodebuildsprod"
CONTAINERAPP_NAME="voicecode-router"

# Build Docker image from root directory
echo "📦 Building Docker image..."
cd ../..
docker build -t voicecode-router:orchestrator-routing -f services/router-service/Dockerfile --platform linux/amd64 .

# Tag and push to registry
echo "⬆️ Pushing to Azure Container Registry..."
docker tag voicecode-router:orchestrator-routing $REGISTRY.azurecr.io/voicecode-router:orchestrator-routing

# Login to ACR
az acr login --name $REGISTRY

# Push image
docker push $REGISTRY.azurecr.io/voicecode-router:orchestrator-routing

# Update Container App
echo "🔄 Updating Container App..."
az containerapp update \
  --name $CONTAINERAPP_NAME \
  --resource-group $RESOURCE_GROUP \
  --image $REGISTRY.azurecr.io/voicecode-router:orchestrator-routing

echo "✅ Router Service updated!"
echo ""
echo "🎯 New Features:"
echo "  - Detects ideation commands ('thinking about', 'idea', 'what if', etc.)"
echo "  - Routes ideation commands to phase-aware orchestrator"
echo "  - Regular commands continue to workers as before"