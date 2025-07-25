#!/bin/bash

# VoiceCode Container Instances Update Script
# Updates existing containers with new images and configurations

set -e

# Configuration
RESOURCE_GROUP="voicecode-rg"
LOCATION="eastus"
KEY_VAULT_NAME="voicecodedevkveus"
ACR_NAME="voicecodebuildsprod"
ACR_SERVER="${ACR_NAME}.azurecr.io"
PROJECT_PREFIX="voicecode-dev-eus"
IMAGE_TAG="v3"

# API Keys
CLAUDE_API_KEY="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"
OPENAI_API_KEY="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"

# Services array
SERVICES=("claude" "stt" "tts" "dispatcher" "generator" "router")

echo "========================================="
echo "VoiceCode Container Instances Update"
echo "========================================="

# Delete existing containers if they exist
echo "Checking for existing containers..."
for SERVICE in "${SERVICES[@]}"; do
    CONTAINER_NAME="${PROJECT_PREFIX}-${SERVICE}-ci"
    if az container show --resource-group $RESOURCE_GROUP --name $CONTAINER_NAME &>/dev/null; then
        echo "Deleting existing container: $CONTAINER_NAME"
        az container delete --resource-group $RESOURCE_GROUP --name $CONTAINER_NAME --yes --output none
        echo "✓ Deleted $CONTAINER_NAME"
    else
        echo "Container $CONTAINER_NAME does not exist, skipping..."
    fi
done

echo ""
echo "All existing containers removed. Waiting 30 seconds for cleanup..."
sleep 30

# Now run the deployment script
echo "Starting fresh deployment..."
./deploy-aci-simple.sh