#!/bin/bash

echo "🚀 Building and deploying Phase-Aware Orchestrator Service..."

# Set variables
RESOURCE_GROUP="voicecode-rg"
REGISTRY="voicecoderegistry"
CONTAINERAPP_ENV="voicecode-env"
CONTAINERAPP_NAME="orchestrator-service"
KEY_VAULT_NAME="voicecode-keyvault"

# Create a temporary directory for the build
BUILD_DIR=$(mktemp -d)
echo "📦 Preparing build in $BUILD_DIR..."

# Copy necessary files to build directory
mkdir -p "$BUILD_DIR/Controllers"
mkdir -p "$BUILD_DIR/Models"
mkdir -p "$BUILD_DIR/Services"
mkdir -p "$BUILD_DIR/Configuration"

# Copy Program.cs and project file
cp Services/orchestrator-service/Program.cs "$BUILD_DIR/"
cp Services/orchestrator-service/appsettings.json "$BUILD_DIR/"
cp VoiceCode.OrchestratorService.csproj "$BUILD_DIR/"

# Copy Controllers
cp Controllers/PhaseAwareOrchestrationController.cs "$BUILD_DIR/Controllers/"

# Copy Models
cp Models/ConversationModels.cs "$BUILD_DIR/Models/"
cp Models/WorkerModels.cs "$BUILD_DIR/Models/"

# Copy Services
cp Services/PhaseAwareOrchestrationService.cs "$BUILD_DIR/Services/"
cp Services/ChatGPTService.cs "$BUILD_DIR/Services/"
cp Services/SessionStorageService.cs "$BUILD_DIR/Services/"
cp Services/TTSServiceClient.cs "$BUILD_DIR/Services/"
cp Services/PromptTemplateService.cs "$BUILD_DIR/Services/"
cp Services/WorkerManagementService.cs "$BUILD_DIR/Services/"

# Create a minimal Dockerfile in the build directory
cat > "$BUILD_DIR/Dockerfile" << 'EOF'
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY *.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet build -c Release -o /app/build
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 80
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:80
ENTRYPOINT ["dotnet", "VoiceCode.OrchestratorService.dll"]
EOF

# Build Docker image
echo "🐳 Building Docker image..."
cd "$BUILD_DIR"
docker build -t orchestrator-service:phase-aware --platform linux/amd64 .

# Tag and push to Azure Container Registry
echo "⬆️ Pushing to Azure Container Registry..."
docker tag orchestrator-service:phase-aware $REGISTRY.azurecr.io/orchestrator-service:phase-aware

# Login to ACR
az acr login --name $REGISTRY

# Push image
docker push $REGISTRY.azurecr.io/orchestrator-service:phase-aware

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

# Update Container App
echo "🔄 Updating Container App..."
az containerapp update \
  --name $CONTAINERAPP_NAME \
  --resource-group $RESOURCE_GROUP \
  --image $REGISTRY.azurecr.io/orchestrator-service:phase-aware

# Clean up
rm -rf "$BUILD_DIR"

echo "✅ Phase-Aware Orchestrator Service deployed successfully!"
echo ""
echo "📋 API Endpoints:"
echo "  - POST /api/v2/orchestrate/voice-command"
echo "  - GET  /api/v2/orchestrate/sessions"
echo "  - GET  /api/v2/orchestrate/sessions/{id}"
echo ""
echo "🎯 Features:"
echo "  - Voice-optimized summaries with TTS"
echo "  - Multi-phase conversation flow"
echo "  - OpenAI API key from Key Vault"