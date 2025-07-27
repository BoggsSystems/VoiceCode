#!/bin/bash

# Update Container Apps with new images that have session ID propagation fixes

echo "Updating Router service..."
az containerapp update \
  --name voicecode-router \
  --resource-group voicecode-rg \
  --image voicecodebuildsprod.azurecr.io/voicecode-router:latest \
  --set-env-vars AZURE_CONTAINER_APPS_ENVIRONMENT="true"

echo "Updating Dispatcher service..."
az containerapp update \
  --name voicecode-dispatcher \
  --resource-group voicecode-rg \
  --image voicecodebuildsprod.azurecr.io/voicecode-dispatcher:latest \
  --set-env-vars AZURE_CONTAINER_APPS_ENVIRONMENT="true"

echo "Updating Voice Intelligence service..."
az containerapp update \
  --name voicecode-voice-intelligence \
  --resource-group voicecode-rg \
  --image voicecodebuildsprod.azurecr.io/voice-intelligence-service:latest \
  --set-env-vars AZURE_CONTAINER_APPS_ENVIRONMENT="true"

echo "Updating TTS service..."
az containerapp update \
  --name voicecode-tts \
  --resource-group voicecode-rg \
  --image voicecodebuildsprod.azurecr.io/voicecode-tts:latest \
  --set-env-vars AZURE_CONTAINER_APPS_ENVIRONMENT="true"

echo "All services updated successfully!"