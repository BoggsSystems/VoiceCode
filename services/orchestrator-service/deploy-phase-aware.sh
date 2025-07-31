#!/bin/bash

# Deploy Phase-Aware Orchestrator Service

echo "🚀 Deploying Phase-Aware Orchestrator Service..."

# Set variables
RESOURCE_GROUP="voicecode-rg"
REGISTRY="voicecoderegistry"
CONTAINERAPP_ENV="voicecode-env"
CONTAINERAPP_NAME="orchestrator-service"

# Build and push Docker image
echo "📦 Building Docker image..."
docker build -t orchestrator-service:phase-aware -f Services/orchestrator-service/Dockerfile .

echo "🏷️ Tagging image..."
docker tag orchestrator-service:phase-aware $REGISTRY.azurecr.io/orchestrator-service:phase-aware

echo "⬆️ Pushing to Azure Container Registry..."
az acr login --name $REGISTRY
docker push $REGISTRY.azurecr.io/orchestrator-service:phase-aware

# Update Container App with new image
echo "🔄 Updating Container App..."
az containerapp update \
  --name $CONTAINERAPP_NAME \
  --resource-group $RESOURCE_GROUP \
  --image $REGISTRY.azurecr.io/orchestrator-service:phase-aware

# Set Key Vault name (adjust if different)
KEY_VAULT_NAME="voicecode-keyvault"

# Check if OpenAI API key exists in Key Vault
echo "🔐 Checking OpenAI API key in Key Vault..."
if ! az keyvault secret show --vault-name $KEY_VAULT_NAME --name openai-api-key &>/dev/null; then
  echo "OpenAI API key not found in Key Vault."
  echo "Please enter your OpenAI API key:"
  read -s OPENAI_API_KEY
  
  # Store in Key Vault
  echo "Storing OpenAI API key in Key Vault..."
  az keyvault secret set \
    --vault-name $KEY_VAULT_NAME \
    --name openai-api-key \
    --value "$OPENAI_API_KEY"
  
  echo "✅ OpenAI API key stored in Key Vault"
else
  echo "✅ OpenAI API key already exists in Key Vault"
fi

echo "✅ Phase-Aware Orchestrator Service deployed successfully!"
echo ""
echo "📋 New API Endpoints:"
echo "  - POST /api/v2/orchestrate/voice-command - Process voice commands with phase awareness"
echo "  - GET  /api/v2/orchestrate/sessions - Get user sessions"
echo "  - GET  /api/v2/orchestrate/sessions/{id} - Get session details"
echo "  - POST /api/v2/orchestrate/sessions/{id}/continue - Continue a session"
echo ""
echo "🎯 Features:"
echo "  - Multi-phase conversation flow (Ideation → Product → Technical → Architecture → Implementation)"
echo "  - Session persistence and context management"
echo "  - Structured business and technical analysis"
echo "  - Voice-optimized summaries with TTS integration"
echo "  - Intelligent routing between exploration and implementation"
echo "  - Full context passing to workers for implementation"