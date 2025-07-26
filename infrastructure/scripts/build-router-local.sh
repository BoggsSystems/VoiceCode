#!/bin/bash

# Build Router service locally with extended timeout

set -e

echo "🚀 Building Router service locally with 5-minute timeout..."

# Configuration
SERVICE_NAME="router"
IMAGE_NAME="voicecode-router-service"
REGISTRY="voicecodebuildsprod.azurecr.io"
PLATFORM="linux/amd64"

# Navigate to VoiceCode root
cd /Users/jeffboggs/VoiceCode

echo "📦 Building Docker image for $PLATFORM platform..."
echo "⏱️  Build timeout set to 5 minutes (300 seconds)"

# Build with docker buildx for AMD64 platform
# Using DOCKER_BUILDKIT_PROGRESS=plain to see detailed output
export DOCKER_BUILDKIT=1
export DOCKER_BUILDKIT_PROGRESS=plain

# Build the image with extended timeout
docker buildx build \
    --platform $PLATFORM \
    --tag $REGISTRY/$IMAGE_NAME:latest \
    --file services/router-service/Dockerfile \
    --progress plain \
    --network host \
    --build-arg BUILDKIT_PROGRESS=plain \
    .

echo "✅ Build completed successfully!"
echo ""
echo "🔄 Next steps:"
echo "1. Push the image: docker push $REGISTRY/$IMAGE_NAME:latest"
echo "2. Deploy to Container App: ./infrastructure/scripts/deploy-router-to-containerapp.sh"