#!/bin/bash

# Build and push all VoiceCode Docker images to Azure Container Registry

set -e

# Configuration
ACR_LOGIN_SERVER="${1:-voicecodecr.azurecr.io}"
TAG="${2:-latest}"

# Color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo -e "${BLUE}=========================================${NC}"
echo -e "${BLUE}VoiceCode Docker Build & Push${NC}"
echo -e "${BLUE}=========================================${NC}"
echo ""
echo "Registry: $ACR_LOGIN_SERVER"
echo "Tag: $TAG"
echo ""

# Navigate to repository root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/../.."

# Services to build
declare -a SERVICES=(
    "dispatcher-service"
    "stt-service"
    "tts-service"
    "router-service"
    "claude-service"
    "generator-service"
    "orchestrator-service"
    "worker-service"
)

# Build shared libraries first
echo -e "${YELLOW}Building shared libraries...${NC}"
dotnet build shared/VoiceCode.Common/VoiceCode.Common.csproj -c Release

# Build and push each service
for SERVICE in "${SERVICES[@]}"; do
    echo -e "\n${YELLOW}Building $SERVICE...${NC}"
    
    IMAGE_NAME="voicecode/$SERVICE"
    FULL_IMAGE="$ACR_LOGIN_SERVER/$IMAGE_NAME:$TAG"
    
    # Check if Dockerfile exists
    if [ ! -f "services/$SERVICE/Dockerfile" ]; then
        echo -e "${RED}Warning: Dockerfile not found for $SERVICE${NC}"
        continue
    fi
    
    # Build image
    echo "Building $FULL_IMAGE..."
    docker build \
        -f "services/$SERVICE/Dockerfile" \
        -t "$FULL_IMAGE" \
        --build-arg BUILD_CONFIGURATION=Release \
        .
    
    if [ $? -eq 0 ]; then
        echo -e "${GREEN}✓ Built $SERVICE successfully${NC}"
        
        # Push image
        echo "Pushing $FULL_IMAGE..."
        docker push "$FULL_IMAGE"
        
        if [ $? -eq 0 ]; then
            echo -e "${GREEN}✓ Pushed $SERVICE successfully${NC}"
        else
            echo -e "${RED}✗ Failed to push $SERVICE${NC}"
            exit 1
        fi
    else
        echo -e "${RED}✗ Failed to build $SERVICE${NC}"
        exit 1
    fi
done

# Build webapp separately (if needed)
if [ -d "webapp" ]; then
    echo -e "\n${YELLOW}Building webapp...${NC}"
    
    IMAGE_NAME="voicecode/webapp"
    FULL_IMAGE="$ACR_LOGIN_SERVER/$IMAGE_NAME:$TAG"
    
    if [ -f "webapp/Dockerfile" ]; then
        docker build \
            -f "webapp/Dockerfile" \
            -t "$FULL_IMAGE" \
            ./webapp
        
        if [ $? -eq 0 ]; then
            echo -e "${GREEN}✓ Built webapp successfully${NC}"
            docker push "$FULL_IMAGE"
            echo -e "${GREEN}✓ Pushed webapp successfully${NC}"
        fi
    else
        echo -e "${YELLOW}Note: No Dockerfile found for webapp${NC}"
    fi
fi

# Summary
echo -e "\n${BLUE}=========================================${NC}"
echo -e "${BLUE}Build Summary${NC}"
echo -e "${BLUE}=========================================${NC}"
echo ""
echo -e "${GREEN}Successfully built and pushed all images to:${NC}"
echo "$ACR_LOGIN_SERVER"
echo ""
echo "Images pushed:"
for SERVICE in "${SERVICES[@]}"; do
    echo "  - $ACR_LOGIN_SERVER/voicecode/$SERVICE:$TAG"
done
echo ""
echo -e "${YELLOW}Next steps:${NC}"
echo "1. Deploy infrastructure: az deployment group create ..."
echo "2. Deploy services: ./deploy-multi-repo-workers.sh"
echo ""