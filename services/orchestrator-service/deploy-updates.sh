#!/bin/bash

echo "🚀 Deploying Orchestrator Updates to Azure..."

# Set variables
RESOURCE_GROUP="voicecode-rg"
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

# Deploy using existing Bicep template
echo "🔄 Deploying orchestrator with updated configuration..."
cd ../../infrastructure/container-apps

# Run the main deployment
az deployment group create \
  --resource-group $RESOURCE_GROUP \
  --template-file orchestrator-app.bicep \
  --parameters @main.parameters.json \
  --parameters imageTag=latest

echo "✅ Orchestrator service updated with phase-aware capabilities!"
echo ""
echo "📋 New Features:"
echo "  - Phase-aware conversation flow (Ideation → Product → Technical → Architecture → Implementation)"
echo "  - Voice-optimized summaries with TTS integration"
echo "  - OpenAI-powered business and technical analysis"
echo "  - Session persistence and context management"
echo ""
echo "🔗 API Endpoints:"
echo "  - POST /api/v2/orchestrate/voice-command"
echo "  - GET  /api/v2/orchestrate/sessions"
echo "  - GET  /api/v2/orchestrate/sessions/{id}"