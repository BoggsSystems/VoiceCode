#!/bin/bash

# Build and push Worker and Orchestrator service Docker images to ACR

set -e

# Configuration
ACR_NAME="voicecodebuildsprod"
ACR_SERVER="${ACR_NAME}.azurecr.io"
TAG="${1:-v3}"
PROJECT_ROOT="/Users/jeffboggs/VoiceCode"

echo "========================================="
echo "Building VoiceCode Worker and Orchestrator Services"
echo "========================================="
echo "ACR: $ACR_NAME"
echo "Tag: $TAG"
echo "========================================="

# Login to ACR
echo "Logging in to Azure Container Registry..."
az acr login --name $ACR_NAME

# Build and push orchestrator service
echo ""
echo "Building orchestrator service..."
cd $PROJECT_ROOT
docker build -f services/orchestrator-service/Dockerfile -t ${ACR_SERVER}/voicecode/orchestrator-service:${TAG} .
echo "✓ Orchestrator service built"

echo "Pushing orchestrator service to ACR..."
docker push ${ACR_SERVER}/voicecode/orchestrator-service:${TAG}
echo "✓ Orchestrator service pushed"

# Build and push worker service
echo ""
echo "Building worker service..."
docker build -f services/worker-service/Dockerfile -t ${ACR_SERVER}/voicecode/worker-service:${TAG} .
echo "✓ Worker service built"

echo "Pushing worker service to ACR..."
docker push ${ACR_SERVER}/voicecode/worker-service:${TAG}
echo "✓ Worker service pushed"

echo ""
echo "========================================="
echo "Build and push completed successfully!"
echo "========================================="
echo ""
echo "Images pushed:"
echo "- ${ACR_SERVER}/voicecode/orchestrator-service:${TAG}"
echo "- ${ACR_SERVER}/voicecode/worker-service:${TAG}"
echo ""
echo "Next steps:"
echo "1. Run ./deploy-worker-orchestrator-services.sh to deploy to Azure"
echo "2. Update dispatcher service with new endpoints"